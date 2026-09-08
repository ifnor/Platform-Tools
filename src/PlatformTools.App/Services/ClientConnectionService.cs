using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

public sealed class ClientConnectionService : IAsyncDisposable
{
    private Process? _process;
    public event EventHandler<string>? LogReceived;

    public async Task ConnectAsync(ShareProfile profile, int localPort)
    {
        await StopAsync();
        if (profile.Protocol is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(profile.Hostname) { UseShellExecute = true });
            return;
        }

        EnsurePortAvailable(localPort);
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到 cloudflared。", executable);
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("access");
        info.ArgumentList.Add(profile.Protocol == "rdp" ? "rdp" : "tcp");
        info.ArgumentList.Add("--hostname"); info.ArgumentList.Add(profile.Hostname);
        info.ArgumentList.Add("--url"); info.ArgumentList.Add($"localhost:{localPort}");
        _process = new Process { StartInfo = info };
        _process.OutputDataReceived += OnOutput; _process.ErrorDataReceived += OnOutput;
        if (!_process.Start()) throw new InvalidOperationException("无法启动客户端连接。");
        _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
    }

    public async Task StopAsync()
    {
        var process = _process; _process = null;
        if (process is null) return;
        try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(this, e.Data); }
    private static void EnsurePortAvailable(int port)
    {
        TcpListener? listener = null;
        try { listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException($"本地端口 {port} 已被占用，请选择其他端口。"); }
        finally { listener?.Stop(); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
