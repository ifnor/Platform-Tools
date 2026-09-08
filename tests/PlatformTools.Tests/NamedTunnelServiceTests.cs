using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class NamedTunnelServiceTests : IDisposable
{
    private const string TunnelId = "11111111-2222-3333-4444-555555555555";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingCredentials_AreRecoveredForExistingTunnel()
    {
        await NamedTunnelService.EnsureCredentialsAsync(TunnelId, _directory, async arguments =>
        {
            Assert.Equal(new[] { "tunnel", "token", "--cred-file" }, arguments[..3]);
            Assert.Equal(TunnelId, arguments[4]);
            await File.WriteAllTextAsync(arguments[3], $"{{\"TunnelID\":\"{TunnelId}\",\"AccountTag\":\"account\",\"TunnelSecret\":\"test-secret\"}}");
            return "";
        });

        Assert.True(File.Exists(Path.Combine(_directory, TunnelId + ".json")));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ExistingCredentials_ArePreservedWithoutNetworkRequest()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, TunnelId + ".json");
        await File.WriteAllTextAsync(path, "existing credentials");

        await NamedTunnelService.EnsureCredentialsAsync(TunnelId, _directory,
            _ => throw new Xunit.Sdk.XunitException("Must not fetch existing credentials"));

        Assert.Equal("existing credentials", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedRecovery_RemovesPartialFileAndDoesNotExposeSecrets(bool commandFails)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamedTunnelService.EnsureCredentialsAsync(TunnelId, _directory, async arguments =>
            {
                await File.WriteAllTextAsync(arguments[3], "{\"TunnelSecret\":\"sensitive-token\"}");
                if (commandFails) throw new InvalidOperationException("sensitive-token");
                return "sensitive-token";
            }));

        Assert.DoesNotContain("sensitive-token", error.ToString());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task ExistingRouteToSameTunnel_IsAcceptedWithoutOverwrite()
    {
        await NamedTunnelService.EnsureDnsRouteAsync(TunnelId, "app.example.com", arguments =>
        {
            Assert.Equal(new[] { "tunnel", "route", "dns", TunnelId, "app.example.com" }, arguments);
            return Task.FromResult("app.example.com is already configured to route to your tunnel");
        });
    }

    [Fact]
    public async Task ConflictingDnsRecord_ExplainsRecoveryWithoutRetryingOrOverwriting()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamedTunnelService.EnsureDnsRouteAsync(TunnelId, "app.example.com", _ =>
            {
                calls++;
                throw new InvalidOperationException("Failed to add route: 1003: An A, AAAA, or CNAME record with that host already exists.");
            }));
        Assert.Equal(1, calls);
        Assert.Contains("app.example.com", error.Message);
        Assert.Contains(TunnelId + ".cfargotunnel.com", error.Message);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact]
    public async Task OtherRouteFailures_ArePreserved()
    {
        var original = new InvalidOperationException("Network unavailable");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamedTunnelService.EnsureDnsRouteAsync(TunnelId, "app.example.com", _ => throw original));
        Assert.Same(original, error);
    }
}
