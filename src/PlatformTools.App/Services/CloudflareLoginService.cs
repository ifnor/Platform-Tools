using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal sealed class CloudflareLoginService
{
    private readonly AppPaths _paths;
    private readonly string _executable;
    private readonly Func<ProcessStartInfo, Action<string>, CancellationToken, Task<int>> _run;
    private readonly Action<Uri> _openBrowser;

    internal CloudflareLoginService(AppPaths paths, string executable,
        Func<ProcessStartInfo, Action<string>, CancellationToken, Task<int>>? run = null,
        Action<Uri>? openBrowser = null)
    {
        _paths = paths;
        _executable = executable;
        _run = run ?? RunAsync;
        _openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
    }

    public async Task LoginAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        var staging = Path.Combine(_paths.ConfigDirectory, "login-" + Guid.NewGuid().ToString("N"));
        var certificate = Path.Combine(staging, ".cloudflared", "cert.pem");
        Directory.CreateDirectory(Path.GetDirectoryName(certificate)!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var info = new ProcessStartInfo(_executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            _paths.ConfigureCloudflared(info, staging);
            // On Windows cloudflared opens URLs through cmd.exe. Disable that
            // lookup for this child only; the app opens the emitted URL itself.
            if (OperatingSystem.IsWindows()) info.Environment["PATH"] = "";
            info.ArgumentList.Add("tunnel");
            info.ArgumentList.Add("login");
            var opened = 0;
            var exitCode = await _run(info, line =>
            {
                log(line);
                if (!OperatingSystem.IsWindows() || !TryGetAuthorizationUrl(line, out var url) ||
                    Interlocked.Exchange(ref opened, 1) != 0) return;
                try { _openBrowser(url!); }
                catch (Exception) { log("无法自动打开浏览器，请复制上方授权链接到浏览器完成登录。"); }
            }, timeout.Token);
            if (exitCode != 0 || !File.Exists(certificate) || new FileInfo(certificate).Length == 0)
                throw new InvalidOperationException("Cloudflare 授权未完成，原有凭据已保留。请查看上方日志后重试。");
            File.Move(certificate, _paths.CertificatePath, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Cloudflare 授权超时，原有凭据已保留，请重试。");
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    internal static bool TryGetAuthorizationUrl(string line, out Uri? uri)
    {
        return Uri.TryCreate(line.Trim(), UriKind.Absolute, out uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("dash.cloudflare.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath == "/argotunnel" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort &&
            uri.Query.Contains("callback=", StringComparison.Ordinal);
    }

    private static async Task<int> RunAsync(ProcessStartInfo info, Action<string> receive, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("无法启动 cloudflared login。");
        static async Task ReadAsync(StreamReader reader, Action<string> callback)
        {
            while (await reader.ReadLineAsync() is { } line)
                if (!string.IsNullOrWhiteSpace(line)) callback(line);
        }
        var stdout = ReadAsync(process.StandardOutput, receive);
        var stderr = ReadAsync(process.StandardError, receive);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
    }
}
