using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class PublishedServiceManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_root, "services.json");
    private readonly List<FakeRuntime> _runtimes = [];
    private PublishedServiceManager Manager(bool pending = false) => new(new PublishedServiceStore(StorePath), () =>
    {
        var runtime = new FakeRuntime(pending); _runtimes.Add(runtime); return runtime;
    }, action => action());
    private static PublishedServiceDefinition Definition(string name) => new() { Name = name };

    [Fact]
    public async Task MultipleServices_RunAndStopIndependently()
    {
        var manager = Manager();
        var first = manager.Save(Definition("web"));
        var second = manager.Save(Definition("api") with { LocalAddress = "http://localhost:3000" });
        await Task.WhenAll(manager.StartAsync(first), manager.StartAsync(second));
        Assert.Equal(2, manager.RunningCount);
        Assert.NotEqual(first.PublicAddress, second.PublicAddress);
        _runtimes[0].Log("first log"); _runtimes[1].Log("second log");
        Assert.DoesNotContain("first log", second.Log);
        await manager.StopAsync(first);
        Assert.Equal(PublishedServiceState.Stopped, first.State);
        Assert.Equal(PublishedServiceState.Running, second.State);
        Assert.True(_runtimes[0].Disposed);
        Assert.False(_runtimes[1].Disposed);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task UnexpectedExit_OnlyFailsItsOwnServiceAndCanRestart()
    {
        var manager = Manager();
        var first = manager.Save(Definition("web")); var second = manager.Save(Definition("api"));
        await manager.StartAsync(first); await manager.StartAsync(second);
        _runtimes[0].Exit(7);
        Assert.Equal(PublishedServiceState.Failed, first.State);
        Assert.False(first.CanShare);
        Assert.Equal(PublishedServiceState.Running, second.State);
        await manager.StartAsync(first);
        Assert.True(_runtimes[0].Disposed);
        Assert.Equal(2, manager.RunningCount);
        _runtimes[0].Log("stale log"); _runtimes[0].Exit(1);
        Assert.DoesNotContain("stale log", first.Log);
        Assert.Equal(PublishedServiceState.Running, first.State);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task StopDuringStartup_CancelsAndDisposesWithoutASecondLaunch()
    {
        var manager = Manager(pending: true);
        var item = manager.Save(Definition("pending"));
        var start = manager.StartAsync(item);
        Assert.Equal(PublishedServiceState.Starting, item.State);
        await manager.StartAsync(item);
        Assert.Single(_runtimes);
        await manager.StopAsync(item).WaitAsync(TimeSpan.FromSeconds(2));
        await start;
        Assert.True(_runtimes[0].Disposed);
        Assert.Equal(PublishedServiceState.Stopped, item.State);
    }

    [Fact]
    public async Task EditingRunningServiceIsBlocked_DeletingStopsOnlyTheSelectedService()
    {
        var manager = Manager();
        var first = manager.Save(Definition("first")); var second = manager.Save(Definition("second"));
        await manager.StartAsync(first); await manager.StartAsync(second);
        Assert.Throws<InvalidOperationException>(() => manager.Save(first.Definition with { Name = "changed" }));
        await manager.DeleteAsync(first);
        Assert.Single(manager.Items);
        Assert.Equal(PublishedServiceState.Running, second.State);
        Assert.True(_runtimes[0].Disposed);
        Assert.Single(new PublishedServiceStore(StorePath).Load());
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task ReloadPreservesDefinitionsButNeverRestartsServicesOrStoresTokens()
    {
        var manager = Manager();
        var item = manager.Save(Definition("protected") with { IsQuick = false, Hostname = "api.example.com", TunnelName = "pt-api", ProtectedAccess = true, AllowedEmails = ["a@example.com"] });
        await manager.StartAsync(item, "private-api-token");
        Assert.DoesNotContain("private-api-token", File.ReadAllText(StorePath));
        var reloaded = Manager();
        Assert.Single(reloaded.Items);
        Assert.Equal(PublishedServiceState.Stopped, reloaded.Items[0].State);
        Assert.Equal("api.example.com", reloaded.Items[0].Definition.Hostname);
        await manager.ShutdownAsync();
    }

    [Fact]
    public void CorruptFileIsRetainedAndCannotBeSilentlyOverwritten()
    {
        Directory.CreateDirectory(_root); File.WriteAllText(StorePath, "{broken file");
        var manager = Manager();
        Assert.NotNull(manager.LoadError);
        Assert.Throws<InvalidOperationException>(() => manager.Save(Definition("new")));
        Assert.Equal("{broken file", File.ReadAllText(StorePath));
    }

    [Fact]
    public void DuplicateDomainsAndTunnelNamesAreRejectedAfterNormalization()
    {
        var manager = Manager();
        manager.Save(Definition("one") with { IsQuick = false, Hostname = "api.example.com", TunnelName = "pt-one" });
        Assert.Throws<ArgumentException>(() => manager.Save(Definition("two") with { IsQuick = false, Hostname = " API.EXAMPLE.COM. ", TunnelName = "pt-two" }));
        Assert.Throws<ArgumentException>(() => manager.Save(Definition("two") with { IsQuick = false, Hostname = "two.example.com", TunnelName = "PT-ONE" }));
    }

    [Fact]
    public async Task ShutdownCancelsAllPendingStarts()
    {
        var manager = Manager(pending: true);
        var one = manager.Save(Definition("one")); var two = manager.Save(Definition("two"));
        var starts = Task.WhenAll(manager.StartAsync(one), manager.StartAsync(two));
        await manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await starts;
        Assert.False(manager.HasActive);
        Assert.All(_runtimes, runtime => Assert.True(runtime.Disposed));
    }

    [Fact]
    public async Task FailedRemoteDeletionKeepsRecordAcrossRestartAndBlocksStartAndEdit()
    {
        var fail = true;
        var manager = new PublishedServiceManager(new PublishedServiceStore(StorePath), () => new FakeRuntime(false), action => action(),
            (_, _) => fail ? Task.FromException(new InvalidOperationException("cleanup failed")) : Task.CompletedTask);
        var item = manager.Save(Definition("web"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync(item));
        Assert.Single(manager.Items);
        Assert.True(item.Definition.DeletionPending);
        Assert.False(item.CanStart); Assert.False(item.CanEdit); Assert.True(item.CanDelete);
        Assert.Throws<InvalidOperationException>(() => manager.Save(item.Definition with { Name = "changed" }));
        Assert.False(Manager().Items[0].CanStart);
        fail = false;
        await manager.DeleteAsync(item);
        Assert.Empty(manager.Items);
        Assert.Empty(new PublishedServiceStore(StorePath).Load());
    }

    [Fact]
    public async Task PendingDeletionCannotRunTwiceOrRestartWhileCleanupAwaits()
    {
        var release = new TaskCompletionSource(); var deletes = 0;
        var manager = new PublishedServiceManager(new PublishedServiceStore(StorePath), () => new FakeRuntime(false), action => action(),
            async (_, _) => { deletes++; await release.Task; });
        var item = manager.Save(Definition("web"));
        var deletion = manager.DeleteAsync(item);
        Assert.Equal(PublishedServiceState.Deleting, item.State);
        Assert.False(item.CanDelete); Assert.False(item.CanStart);
        await manager.DeleteAsync(item); await manager.StartAsync(item);
        Assert.Equal(1, deletes);
        release.SetResult(); await deletion;
        Assert.Empty(manager.Items);
    }
    private sealed class FakeRuntime(bool pending) : IPublishedServiceRuntime
    {
        public event EventHandler<string>? LogReceived;
        public event EventHandler<int>? Exited;
        public bool Disposed { get; private set; }
        public async Task<string> StartAsync(PublishedServiceDefinition definition, string? apiToken, CancellationToken cancellationToken)
        {
            if (pending) await Task.Delay(Timeout.Infinite, cancellationToken);
            return "https://" + definition.Id.ToString("N") + ".trycloudflare.com";
        }
        public void Log(string line) => LogReceived?.Invoke(this, line);
        public void Exit(int code) => Exited?.Invoke(this, code);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
