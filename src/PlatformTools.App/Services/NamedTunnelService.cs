using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

public sealed record NamedTunnelResult(string TunnelId, string ConfigPath, string AccountId);

public sealed class NamedTunnelService : IAsyncDisposable
{
    private static readonly Regex TunnelIdPattern = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private Process? _process;
    public event EventHandler<string>? LogReceived;
    public bool IsRunning => _process is { HasExited: false };

    public async Task<NamedTunnelResult> CreateAndStartAsync(string name, string hostname, string serviceUrl)
    {
        await StopAsync();
        var tunnelId = await FindTunnelIdAsync(name);
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            var createOutput = await RunCommandAsync(["tunnel", "create", name]);
            tunnelId = TunnelIdPattern.Match(createOutput).Value;
            if (string.IsNullOrWhiteSpace(tunnelId)) throw new InvalidOperationException("隧道已创建，但没有找到隧道 ID。");
        }

        await EnsureCredentialsAsync(tunnelId,
            AppPaths.Current.CredentialsDirectory,
            arguments => RunCommandAsync(arguments, logOutput: false));
        var (configPath, accountId) = CreateConfig(tunnelId, hostname, serviceUrl);
        await RunCommandAsync(["tunnel", "route", "dns", "--overwrite-dns", tunnelId, hostname]);
        StartLongRunning(["tunnel", "--config", configPath, "run", tunnelId]);
        return new NamedTunnelResult(tunnelId, configPath, accountId);
    }

    private async Task<string?> FindTunnelIdAsync(string name)
    {
        try
        {
            var output = await RunCommandAsync(["tunnel", "list", "--output", "json"]);
            using var json = JsonDocument.Parse(output);
            foreach (var item in json.RootElement.EnumerateArray())
                if (item.TryGetProperty("name", out var tunnelName) && string.Equals(tunnelName.GetString(), name, StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("id", out var id)) return id.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    public async Task StopAsync()
    {
        var process = _process; _process = null;
        if (process is null) return;
        try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    internal static async Task EnsureCredentialsAsync(string tunnelId, string directory, Func<string[], Task<string>> runCommand)
    {
        if (!Guid.TryParse(tunnelId, out _)) throw new InvalidDataException("隧道 ID 无效。");
        var credentials = Path.Combine(directory, tunnelId + ".json");
        if (File.Exists(credentials)) return;

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, tunnelId + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await runCommand(["tunnel", "token", "--cred-file", temporaryPath, tunnelId]);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(temporaryPath));
            var root = json.RootElement;
            if (!root.TryGetProperty("TunnelID", out var id) ||
                !Guid.TryParse(id.GetString(), out var recoveredId) || recoveredId != Guid.Parse(tunnelId) ||
                !root.TryGetProperty("AccountTag", out var account) || string.IsNullOrWhiteSpace(account.GetString()) ||
                !root.TryGetProperty("TunnelSecret", out var secret) || string.IsNullOrWhiteSpace(secret.GetString()))
                throw new InvalidDataException("隧道凭据无效。");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporaryPath, credentials, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            // Do not propagate command output: it may contain a tunnel token.
            throw new InvalidOperationException("无法获取此隧道的运行凭据。请确认当前 Cloudflare 登录账户拥有该隧道；也可从原设备恢复凭据 JSON，或使用新的隧道名称创建隧道。");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<string> RunCommandAsync(string[] arguments, bool logOutput = true)
    {
        var info = CreateStartInfo(arguments);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("无法启动 cloudflared。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (logOutput) { Receive(stdout); Receive(stderr); }
        if (process.ExitCode != 0) throw new InvalidOperationException((stdout + stderr).Trim());
        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    private void StartLongRunning(string[] arguments)
    {
        _process = new Process { StartInfo = CreateStartInfo(arguments), EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Receive(e.Data); _process.ErrorDataReceived += (_, e) => Receive(e.Data);
        if (!_process.Start()) throw new InvalidOperationException("无法运行命名隧道。");
        _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
    }

    private ProcessStartInfo CreateStartInfo(string[] arguments)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到 cloudflared。", executable);
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        AppPaths.Current.ConfigureCloudflared(info);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static (string ConfigPath, string AccountId) CreateConfig(string tunnelId, string hostname, string serviceUrl)
    {
        var credentials = Path.Combine(AppPaths.Current.CredentialsDirectory, tunnelId + ".json");
        if (!File.Exists(credentials)) throw new FileNotFoundException("没有找到隧道凭据，请重试发布以获取凭据。", credentials);
        using var credentialJson = JsonDocument.Parse(File.ReadAllText(credentials));
        var accountId = credentialJson.RootElement.TryGetProperty("AccountTag", out var tag) ? tag.GetString() : null;
        if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidDataException("隧道凭据中缺少 Cloudflare Account ID。");
        var directory = Path.Combine(AppPaths.Current.TunnelsDirectory, tunnelId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.yml");
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        File.WriteAllText(path, $"tunnel: {tunnelId}\ncredentials-file: {Quote(credentials)}\ningress:\n  - hostname: {Quote(hostname)}\n    service: {Quote(serviceUrl)}\n  - service: http_status:404\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return (path, accountId);
    }

    private void Receive(string? line, StringBuilder? output = null) { if (string.IsNullOrWhiteSpace(line)) return; output?.AppendLine(line); LogReceived?.Invoke(this, line); }
    public async ValueTask DisposeAsync() => await StopAsync();
}
