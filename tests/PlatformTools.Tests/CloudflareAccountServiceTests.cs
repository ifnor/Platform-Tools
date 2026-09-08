using System.Security.Cryptography;
using System.Text;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class CloudflareAccountServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, Path.Combine(_root, "legacy"), Path.Combine(_root, "appdata"));

    [Fact]
    public async Task NoCertificate_ShowsSignedOutWithoutNetworkRequest()
    {
        using var account = new CloudflareAccountService(Paths, verify: _ => throw new Exception("Must not verify"));
        await account.RefreshAsync();
        Assert.Equal(AccountState.SignedOut, account.State);
        Assert.False(account.HasCredentials);
    }

    [Fact]
    public async Task InvalidCertificate_DoesNotShowSignedIn()
    {
        var paths = Paths;
        File.WriteAllText(paths.CertificatePath, "broken credential");
        using var account = new CloudflareAccountService(paths);
        await account.RefreshAsync();
        Assert.Equal(AccountState.Invalid, account.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedCredentials_AreVerifiedBeforeClaimingSignedIn(bool valid)
    {
        var paths = Paths;
        WriteCertificate(paths);
        using var account = new CloudflareAccountService(paths, verify: _ => Task.FromResult(valid));
        Assert.Equal(AccountState.Saved, account.State);
        await account.RefreshAsync();
        Assert.Equal(valid ? AccountState.SignedIn : AccountState.Unavailable, account.State);
        Assert.Equal("test-account", account.AccountId);
        Assert.False(account.IsBusy);
    }

    [Fact]
    public async Task Login_ExposesPendingStateAndPreventsDuplicateOperations()
    {
        var paths = Paths;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var account = new CloudflareAccountService(paths, async (log, token) =>
        {
            calls++;
            log("browser authorization link");
            await completion.Task;
            WriteCertificate(paths);
        });
        var login = account.LoginAsync();
        Assert.Equal(AccountState.SigningIn, account.State);
        Assert.True(account.IsBusy);
        await account.LoginAsync();
        await account.RefreshAsync();
        Assert.Equal(1, calls);
        Assert.Contains("browser authorization link", account.LogText);
        completion.SetResult();
        await login;
        Assert.Equal(AccountState.SignedIn, account.State);
        Assert.False(account.IsBusy);
    }

    [Fact]
    public async Task FailedReauthorization_KeepsPreviousSignedInStateAndReportsFailure()
    {
        var paths = Paths;
        WriteCertificate(paths);
        using var account = new CloudflareAccountService(paths,
            (_, _) => throw new InvalidOperationException("Authorization failed"), _ => Task.FromResult(true));
        await account.RefreshAsync();
        await account.LoginAsync();
        Assert.Equal(AccountState.SignedIn, account.State);
        Assert.Equal("Authorization failed", account.Error);
        Assert.True(account.HasCredentials);
        Assert.False(account.IsBusy);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotClaimCredentialsWereRemoved()
    {
        var paths = Paths;
        WriteCertificate(paths);
        using var account = new CloudflareAccountService(paths, verify: _ => throw new IOException("offline"));
        await account.RefreshAsync();
        Assert.Equal(AccountState.Unavailable, account.State);
        Assert.True(account.HasCredentials);
    }

    private static void WriteCertificate(AppPaths paths)
    {
        File.WriteAllText(paths.CertificatePath, PemEncoding.WriteString("ARGO TUNNEL TOKEN",
            Encoding.UTF8.GetBytes("{\"accountID\":\"test-account\",\"apiToken\":\"test-token\",\"zoneID\":\"test-zone\"}")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
