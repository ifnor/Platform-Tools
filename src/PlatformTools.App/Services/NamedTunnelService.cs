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

    public Task LoginAsync() => RunCommandAsync(["tunnel", "login"]);

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

        await RunCommandAsync(["tunnel", "route", "dns", "--overwrite-dns", tunnelId, hostname]);
        var (configPath, accountId) = CreateConfig(tunnelId, hostname, serviceUrl);
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

    private async Task<string> RunCommandAsync(string[] arguments)
    {
        var info = CreateStartInfo(arguments);
        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Receive(e.Data, output);
        process.ErrorDataReceived += (_, e) => Receive(e.Data, output);
        if (!process.Start()) throw new InvalidOperationException("无法启动 cloudflared。");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(output.ToString().Trim());
        return output.ToString();
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
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static (string ConfigPath, string AccountId) CreateConfig(string tunnelId, string hostname, string serviceUrl)
    {
        var credentials = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared", tunnelId + ".json");
        if (!File.Exists(credentials)) throw new FileNotFoundException("没有找到隧道凭据，请重新登录 Cloudflare。", credentials);
        using var credentialJson = JsonDocument.Parse(File.ReadAllText(credentials));
        var accountId = credentialJson.RootElement.TryGetProperty("AccountTag", out var tag) ? tag.GetString() : null;
        if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidDataException("隧道凭据中缺少 Cloudflare Account ID。");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatformTools", "tunnels", tunnelId);
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
