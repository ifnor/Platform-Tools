using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class CloudflareCommandThrottleTests
{
    [Fact]
    public async Task RateLimitAllowsImmediateManualRetryWithoutAutomaticRetry()
    {
        var throttle = new CloudflareCommandThrottle();
        var calls = 0;
        var error = await Assert.ThrowsAsync<CloudflareRateLimitException>(() => throttle.RunAsync<string>(() =>
        { calls++; throw new InvalidOperationException("Error Parsing page 1: API call to list tunnels failed with status 429: Too Many Requests"); }, default));
        Assert.Contains("429", error.Message);
        Assert.Equal(1, calls);
        Assert.Equal("ok", await throttle.RunAsync(() => { calls++; return Task.FromResult("ok"); }, default));
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task ConcurrentRequestsAreSerializedAndCancelledWaitersDoNotRun()
    {
        var throttle = new CloudflareCommandThrottle();
        var release = new TaskCompletionSource<string>();
        var first = throttle.RunAsync(() => release.Task, default);
        using var cancel = new CancellationTokenSource();
        var ran = false;
        var second = throttle.RunAsync(() => { ran = true; return Task.FromResult("second"); }, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(ran);
        release.SetResult("first"); await first;
        Assert.Equal("third", await throttle.RunAsync(() => Task.FromResult("third"), default));
    }
    [Fact]
    public async Task OtherErrorsArePreservedAndAllowSubsequentCalls()
    {
        var throttle = new CloudflareCommandThrottle();
        var original = new InvalidOperationException("DNS conflict");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => throttle.RunAsync<string>(() => throw original, default)));
        Assert.True(await throttle.RunAsync(() => Task.FromResult(true), default));
    }
    [Fact]
    public async Task LookupUsesServerSideNameFilterAndDoesNotTreatBadJsonAsMissingTunnel()
    {
        var id = await NamedTunnelService.FindTunnelIdAsync("web", args =>
        {
            Assert.Equal(new[] { "tunnel", "list", "--name", "web", "--output", "json" }, args);
            return Task.FromResult("[{\"name\":\"web\",\"id\":\"11111111-2222-3333-4444-555555555555\"}]");
        });
        Assert.Equal("11111111-2222-3333-4444-555555555555", id);
        await Assert.ThrowsAsync<InvalidDataException>(() => NamedTunnelService.FindTunnelIdAsync("web", _ => Task.FromResult("broken")));
    }
    [Fact]
    public async Task CredentialRecoveryPreservesRateLimitError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new CloudflareRateLimitException("HTTP 429");
            Assert.Same(expected, await Assert.ThrowsAsync<CloudflareRateLimitException>(() => NamedTunnelService.EnsureCredentialsAsync("11111111-2222-3333-4444-555555555555", directory, _ => throw expected)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData(" \n null \n")]
    public async Task SuccessfulEmptyLookupAllowsNewTunnelAndAccountVerification(string output)
    {
        Assert.Empty(NamedTunnelService.ParseTunnelList(output));
        Assert.Null(await NamedTunnelService.FindTunnelIdAsync("web", _ => Task.FromResult(output)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"web\",\"id\":null}]")]
    public async Task InvalidListShapeDoesNotBecomeMissingTunnel(string output)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => NamedTunnelService.FindTunnelIdAsync("web", _ => Task.FromResult(output)));
    }

    [Fact]
    public async Task FailedLookupDoesNotBecomeEmptyList()
    {
        var failure = new CloudflareRateLimitException("HTTP 429");
        Assert.Same(failure, await Assert.ThrowsAsync<CloudflareRateLimitException>(() =>
            NamedTunnelService.FindTunnelIdAsync("web", _ => throw failure)));
    }
}
