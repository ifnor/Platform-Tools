using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal enum AccountState { SignedOut, Saved, Checking, SigningIn, SignedIn, Invalid, Unavailable }

internal sealed class CloudflareAccountService : IDisposable
{
    private static readonly Lazy<CloudflareAccountService> Default = new(() => new(AppPaths.Current));
    public static CloudflareAccountService Current => Default.Value;
    private readonly AppPaths _paths;
    private readonly Func<Action<string>, CancellationToken, Task> _login;
    private readonly Func<CancellationToken, Task<bool>> _verify;
    private CancellationTokenSource? _operation;
    private readonly object _logLock = new();
    private string _logText = "";
    public string LogText { get { lock (_logLock) return _logText; } }
    public event EventHandler? Changed;
    public event EventHandler<string>? LogReceived;
    public AccountState State { get; private set; }
    public string? AccountId { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy => _operation is not null;
    public bool HasCredentials => AccountId is not null;

    internal CloudflareAccountService(AppPaths paths,
        Func<Action<string>, CancellationToken, Task>? login = null,
        Func<CancellationToken, Task<bool>>? verify = null)
    {
        _paths = paths;
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");
        _login = login ?? ((log, token) => new CloudflareLoginService(paths, executable).LoginAsync(log, token));
        _verify = verify ?? VerifyAsync;
        ReadLocalState();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        Error = null;
        ReadLocalState();
        if (!HasCredentials) { Changed?.Invoke(this, EventArgs.Empty); return; }
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _operation = operation;
        State = AccountState.Checking;
        Changed?.Invoke(this, EventArgs.Empty);
        try { State = await _verify(operation.Token) ? AccountState.SignedIn : AccountState.Unavailable; }
        catch (Exception) { State = AccountState.Unavailable; }
        finally { _operation = null; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public async Task LoginAsync()
    {
        if (IsBusy) return;
        var previousState = State;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        Error = null;
        lock (_logLock) _logText = "";
        State = AccountState.SigningIn;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _login(line =>
            {
                lock (_logLock)
                {
                    _logText += line + Environment.NewLine;
                    if (_logText.Length > 20000) _logText = _logText[^20000..];
                }
                LogReceived?.Invoke(this, line);
            }, operation.Token);
            ReadLocalState();
            if (State != AccountState.Saved) throw new InvalidDataException(LocalizationService.T("授权结束，但未找到有效登录凭据。", "Authorization ended without valid credentials."));
            State = AccountState.SignedIn;
        }
        catch (Exception ex)
        {
            ReadLocalState();
            if (HasCredentials && previousState == AccountState.SignedIn) State = AccountState.SignedIn;
            Error = ex is OperationCanceledException
                ? LocalizationService.T("已取消授权，原有凭据已保留。", "Authorization cancelled. Existing credentials were kept.")
                : ex.Message;
        }
        finally { _operation = null; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void Cancel() => _operation?.Cancel();

    private void ReadLocalState()
    {
        AccountId = null;
        if (!File.Exists(_paths.CertificatePath)) { State = AccountState.SignedOut; return; }
        try
        {
            var pem = File.ReadAllText(_paths.CertificatePath);
            if (!PemEncoding.TryFind(pem, out var fields) || pem[fields.Label] != "ARGO TUNNEL TOKEN")
                throw new InvalidDataException();
            using var json = JsonDocument.Parse(Convert.FromBase64String(pem[fields.Base64Data]));
            var id = json.RootElement.GetProperty("accountID").GetString();
            var token = json.RootElement.GetProperty("apiToken").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token)) throw new InvalidDataException();
            AccountId = id;
            State = AccountState.Saved;
        }
        catch (Exception) { State = AccountState.Invalid; }
    }

    private async Task<bool> VerifyAsync(CancellationToken token)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");
        var info = new ProcessStartInfo(executable) { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        _paths.ConfigureCloudflared(info);
        foreach (var argument in new[] { "tunnel", "list", "--output", "json" }) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) return false;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) return false;
            using var json = JsonDocument.Parse(await stdout);
            return json.RootElement.ValueKind == JsonValueKind.Array;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
    }

    public string StatusText => State switch
    {
        AccountState.SignedOut => LocalizationService.T("未登录", "Not signed in"),
        AccountState.Saved => LocalizationService.T("已保存凭据，待验证", "Credentials saved; not verified"),
        AccountState.Checking => LocalizationService.T("正在验证登录状态…", "Checking sign-in…"),
        AccountState.SigningIn => LocalizationService.T("正在授权，等待浏览器确认…", "Waiting for browser authorization…"),
        AccountState.SignedIn => LocalizationService.T("已登录", "Signed in"),
        AccountState.Invalid => LocalizationService.T("登录凭据无效", "Invalid credentials"),
        _ => LocalizationService.T("无法验证登录状态", "Unable to verify sign-in")
    };

    public string StatusColor => State == AccountState.SignedIn ? "#36B58B" : State == AccountState.SignedOut ? "#8B99AD" : "#ECA34E";

    public string Description => State switch
    {
        AccountState.SignedOut => LocalizationService.T("使用自己的域名前，请先登录 Cloudflare。临时地址无需登录。", "Sign in to use your own domain. Temporary URLs do not require sign-in."),
        AccountState.SigningIn => LocalizationService.T("请在浏览器完成授权。未自动打开时，可从下方日志复制授权链接。", "Complete authorization in your browser. If it did not open, copy the link from the log below."),
        AccountState.SignedIn => LocalizationService.T("Cloudflare 授权已确认，可以发布自己的域名。", "Cloudflare authorization is confirmed. You can publish to your own domain."),
        AccountState.Invalid => LocalizationService.T("本地凭据无法读取，请重新授权。", "Local credentials could not be read. Please reauthorize."),
        AccountState.Unavailable => LocalizationService.T("本地凭据仍在。请检查网络后刷新，或重新授权。", "Local credentials are still saved. Check your network and refresh, or reauthorize."),
        _ => LocalizationService.T("正在确认已有凭据是否可用。", "Checking whether the saved credentials are usable.")
    };

    public void Dispose() => Cancel();
}
