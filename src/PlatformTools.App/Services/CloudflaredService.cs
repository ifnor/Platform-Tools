using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

public sealed class CloudflaredService : IAsyncDisposable
{
    private static readonly Regex PublicUrlPattern = new(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private Process? _process;

    public event EventHandler<string>? LogReceived;
    public event EventHandler<string>? PublicUrlReceived;
    public bool IsRunning => _process is { HasExited: false };

    public async Task StartQuickTunnelAsync(string localUrl, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        var executable = ResolveExecutable();
        if (!File.Exists(executable))
            throw new FileNotFoundException("未找到 cloudflared。请将官方程序放入应用的 tools 目录。", executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("tunnel");
        startInfo.ArgumentList.Add("--no-autoupdate");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add(localUrl);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += HandleOutput;
        _process.ErrorDataReceived += HandleOutput;
        if (!_process.Start()) throw new InvalidOperationException("无法启动 cloudflared。 ");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    private void HandleOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        LogReceived?.Invoke(this, e.Data);
        var match = PublicUrlPattern.Match(e.Data);
        if (match.Success) PublicUrlReceived?.Invoke(this, match.Value);
    }

    private static string ResolveExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared";
        return Path.Combine(AppContext.BaseDirectory, "tools", name);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
