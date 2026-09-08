using System;
using System.Threading;
using System.Threading.Tasks;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

internal interface IPublishedServiceRuntime : IAsyncDisposable
{
    event EventHandler<string>? LogReceived;
    event EventHandler<int>? Exited;
    Task<string> StartAsync(PublishedServiceDefinition definition, string? apiToken, CancellationToken cancellationToken);
}

internal sealed class PublishedServiceRuntime : IPublishedServiceRuntime
{
    private readonly CloudflaredService _quick = new();
    private readonly NamedTunnelService _named = new();
    private readonly TaskCompletionSource<string> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();
    private bool _connected;
    private string? _address;
    private bool _stopping;
    public event EventHandler<string>? LogReceived;
    public event EventHandler<int>? Exited;

    public PublishedServiceRuntime()
    {
        _quick.LogReceived += OnLog;
        _named.LogReceived += OnLog;
        _quick.PublicUrlReceived += (_, address) => { lock (_sync) { _address = address; CompleteIfReady(); } };
        _quick.Exited += OnExited;
        _named.Exited += OnExited;
    }

    public async Task<string> StartAsync(PublishedServiceDefinition definition, string? apiToken, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            if (definition.IsQuick) await _quick.StartQuickTunnelAsync(definition.LocalAddress, timeout.Token);
            else
            {
                var result = await _named.PrepareAsync(definition.TunnelName, definition.Hostname, definition.LocalAddress, timeout.Token);
                if (definition.ProtectedAccess)
                {
                    LogReceived?.Invoke(this, "正在配置身份保护…");
                    await new CloudflareAccessService().ConfigureEmailProtectionAsync(
                        string.IsNullOrWhiteSpace(definition.AccountId) ? result.AccountId : definition.AccountId,
                        apiToken ?? "", definition.Name, definition.Hostname, definition.AllowedEmails, timeout.Token);
                }
                timeout.Token.ThrowIfCancellationRequested();
                lock (_sync) _address = definition.Protocol is "http" or "https" ? "https://" + definition.Hostname : definition.Hostname;
                _named.StartPrepared(result);
            }
            return await _ready.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("90 秒内未能建立隧道连接，请检查网络并查看日志。");
        }
    }

    private void OnLog(object? sender, string line)
    {
        LogReceived?.Invoke(this, line);
        if (line.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
            lock (_sync) { _connected = true; CompleteIfReady(); }
    }
    private void CompleteIfReady() { if (_connected && _address is not null) _ready.TrySetResult(_address); }
    private void OnExited(object? sender, int code)
    {
        if (_stopping) return;
        _ready.TrySetException(new InvalidOperationException($"隧道进程启动失败（退出码 {code}）。"));
        Exited?.Invoke(this, code);
    }
    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        await _quick.DisposeAsync();
        await _named.DisposeAsync();
    }
}
