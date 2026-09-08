using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

internal sealed class PublishedServiceManager
{
    private static readonly Lazy<PublishedServiceManager> Default = new(() => new(
        new PublishedServiceStore(Path.Combine(AppPaths.Current.ConfigDirectory, "services.json")),
        () => new PublishedServiceRuntime(), action => Dispatcher.UIThread.Post(action)));
    public static PublishedServiceManager Current => Default.Value;
    private readonly PublishedServiceStore _store;
    private readonly Func<IPublishedServiceRuntime> _factory;
    private readonly Action<Action> _dispatch;
    private readonly Func<PublishedServiceDefinition, string?, Task> _deleteRemote;
    private bool _shuttingDown;
    public ObservableCollection<PublishedService> Items { get; } = [];
    public string? LoadError { get; }
    public event EventHandler? Changed;
    public bool HasActive => Items.Any(x => x.IsActive);
    public int RunningCount => Items.Count(x => x.State == PublishedServiceState.Running);
    public int FailedCount => Items.Count(x => x.State == PublishedServiceState.Failed);
    public string Summary => LocalizationService.T($"{Items.Count} 个服务 · {RunningCount} 个运行中 · {FailedCount} 个异常", $"{Items.Count} services · {RunningCount} running · {FailedCount} failed");

    internal PublishedServiceManager(PublishedServiceStore store, Func<IPublishedServiceRuntime> factory, Action<Action> dispatch, Func<PublishedServiceDefinition, string?, Task>? deleteRemote = null)
    {
        _store = store; _factory = factory; _dispatch = dispatch;
        _deleteRemote = deleteRemote ?? ((definition, token) => new CloudflareServiceDeletion(AppPaths.Current).DeleteAsync(definition, token));
        try { foreach (var definition in store.Load()) Items.Add(new(definition)); }
        catch (Exception ex) { LoadError = "无法读取 config/services.json，已保留原文件：" + ex.Message; }
    }

    public PublishedService Save(PublishedServiceDefinition definition)
    {
        if (LoadError is not null) throw new InvalidOperationException(LoadError);
        if (_shuttingDown) throw new InvalidOperationException("程序正在退出。");
        var existing = Items.FirstOrDefault(x => x.Definition.Id == definition.Id);
        if (existing?.IsActive == true) throw new InvalidOperationException("请先停止服务，再修改配置。");
        if (existing?.Definition.DeletionPending == true) throw new InvalidOperationException("此服务尚未完成删除，请先重试删除。");
        definition = definition.NormalizeAndValidate(Items.Select(x => x.Definition));
        _store.Save(Items.Where(x => x != existing).Select(x => x.Definition).Append(definition));
        if (existing is null) { existing = new(definition); Items.Add(existing); }
        else { existing.Definition = definition; existing.PublicAddress = ""; existing.Error = ""; existing.State = PublishedServiceState.Stopped; }
        Notify(existing);
        return existing;
    }

    public async Task StartAsync(PublishedService item, string? apiToken = null)
    {
        string? activeToken = apiToken;
        if (_shuttingDown || !Items.Contains(item) || !item.CanStart) return;
        await item.Gate.WaitAsync();
        try
        {
            if (_shuttingDown || !Items.Contains(item) || !item.CanStart) return;
            if (item.Definition.ProtectedAccess && string.IsNullOrWhiteSpace(apiToken)) throw new InvalidOperationException("启动受保护服务需要 API Token，Token 不会保存。");
            await ReleaseRuntimeAsync(item);
            item.Cancellation = new CancellationTokenSource();
            item.State = PublishedServiceState.Starting; item.Error = ""; item.PublicAddress = ""; item.Log = "";
            var runtime = _factory(); item.Runtime = runtime;
            runtime.LogReceived += (_, line) => _dispatch(() =>
            {
                if (item.Runtime != runtime) return;
                item.Log += (string.IsNullOrEmpty(activeToken) ? line : line.Replace(activeToken, "[redacted]", StringComparison.Ordinal)) + Environment.NewLine;
                if (item.Log.Length > 64000) item.Log = item.Log[^64000..];
                item.Notify();
            });
            runtime.Exited += (_, code) => _dispatch(() =>
            {
                if (item.Runtime != runtime || item.State is not (PublishedServiceState.Starting or PublishedServiceState.Running)) return;
                item.State = PublishedServiceState.Failed; item.PublicAddress = "";
                item.Error = $"隧道进程已退出（{code}），请查看日志后重试。";
                Notify(item);
            });
            Notify(item);
            var address = await runtime.StartAsync(item.Definition, apiToken, item.Cancellation.Token);
            item.Cancellation.Token.ThrowIfCancellationRequested();
            if (item.State == PublishedServiceState.Failed) throw new InvalidOperationException(item.Error);
            item.PublicAddress = address; item.State = PublishedServiceState.Running;
            Notify(item);
        }
        catch (OperationCanceledException) { await ReleaseRuntimeAsync(item); item.State = PublishedServiceState.Stopped; item.PublicAddress = ""; Notify(item); }
        catch (Exception ex)
        {
            await ReleaseRuntimeAsync(item);
            item.State = PublishedServiceState.Failed; item.PublicAddress = "";
            item.Error = string.IsNullOrEmpty(apiToken) ? ex.Message : ex.Message.Replace(apiToken, "[redacted]", StringComparison.Ordinal);
            Notify(item);
        }
        finally { activeToken = null; item.Cancellation?.Dispose(); item.Cancellation = null; item.Gate.Release(); }
    }

    public async Task StopAsync(PublishedService item)
    {
        item.Cancellation?.Cancel();
        await item.Gate.WaitAsync();
        try
        {
            item.State = PublishedServiceState.Stopping; Notify(item);
            await ReleaseRuntimeAsync(item);
            item.State = PublishedServiceState.Stopped; item.PublicAddress = ""; item.Error = ""; Notify(item);
        }
        catch (Exception ex) { item.State = PublishedServiceState.Failed; item.Error = "停止失败：" + ex.Message; Notify(item); throw; }
        finally { item.Gate.Release(); }
    }

    private static async Task ReleaseRuntimeAsync(PublishedService item)
    {
        var runtime = item.Runtime;
        if (runtime is not null) await runtime.DisposeAsync();
        item.Runtime = null;
    }

    public async Task DeleteAsync(PublishedService item, string? apiToken = null)
    {
        if (LoadError is not null) throw new InvalidOperationException(LoadError);
        if (_shuttingDown || !Items.Contains(item) || !item.CanDelete) return;
        // Mark before yielding so edit/start cannot race with a pending cleanup.
        var pending = item.Definition with { DeletionPending = true };
        _store.Save(Items.Select(x => x == item ? pending : x.Definition));
        item.Definition = pending;
        item.IsDeleting = true;
        item.Cancellation?.Cancel();
        item.State = PublishedServiceState.Deleting; Notify(item);
        await item.Gate.WaitAsync();
        try
        {
            item.State = PublishedServiceState.Deleting; Notify(item);
            await ReleaseRuntimeAsync(item);
            item.PublicAddress = "";
            await _deleteRemote(item.Definition, apiToken);
            _store.Save(Items.Where(x => x != item).Select(x => x.Definition));
            Items.Remove(item); Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            item.State = PublishedServiceState.Failed;
            item.Error = "删除未完成，请重试删除：" + (string.IsNullOrEmpty(apiToken) ? ex.Message : ex.Message.Replace(apiToken, "[redacted]", StringComparison.Ordinal));
            Notify(item); throw;
        }
        finally { item.IsDeleting = false; item.Gate.Release(); Notify(item); }
    }

    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        try { await Task.WhenAll(Items.ToArray().Select(StopAsync)); }
        catch { _shuttingDown = false; throw; }
    }
    public void RefreshLabels() { foreach (var item in Items) item.Notify(); Changed?.Invoke(this, EventArgs.Empty); }
    private void Notify(PublishedService item) { item.Notify(); Changed?.Invoke(this, EventArgs.Empty); }
}
