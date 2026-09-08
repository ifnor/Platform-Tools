using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class CloudflareLoginServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    private const string AuthUrl = "https://dash.cloudflare.com/argotunnel?callback=https%3A%2F%2Flogin.cloudflareaccess.org%2Ftest";

    [Fact]
    public async Task Reauthorization_IsHiddenAndReplacesCertificateOnlyAfterSuccess()
    {
        var paths = new AppPaths(_root, Path.Combine(_root, "legacy"), Path.Combine(_root, "appdata"));
        File.WriteAllText(paths.CertificatePath, "old certificate");
        var opened = new List<Uri>();
        var service = new CloudflareLoginService(paths, "cloudflared.exe", async (info, receive, token) =>
        {
            Assert.False(info.UseShellExecute);
            Assert.True(info.CreateNoWindow);
            Assert.True(info.RedirectStandardError);
            if (OperatingSystem.IsWindows()) Assert.Equal("", info.Environment["PATH"]);
            Assert.Equal("old certificate", File.ReadAllText(paths.CertificatePath));
            Assert.False(File.Exists(info.Environment["TUNNEL_ORIGIN_CERT"]));
            receive(AuthUrl);
            receive(AuthUrl);
            await File.WriteAllTextAsync(info.Environment["TUNNEL_ORIGIN_CERT"]!, "new certificate", token);
            return 0;
        }, opened.Add);

        await service.LoginAsync(_ => { });

        Assert.Equal("new certificate", File.ReadAllText(paths.CertificatePath));
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, opened.Count);
        Assert.Empty(Directory.GetDirectories(paths.ConfigDirectory, "login-*"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task FailedOrIncompleteLogin_PreservesExistingCertificate(int exitCode)
    {
        var paths = new AppPaths(_root, Path.Combine(_root, "legacy"), Path.Combine(_root, "appdata"));
        File.WriteAllText(paths.CertificatePath, "old certificate");
        var service = new CloudflareLoginService(paths, "cloudflared.exe", (_, _, _) => Task.FromResult(exitCode));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync(_ => { }));

        Assert.Equal("old certificate", File.ReadAllText(paths.CertificatePath));
        Assert.Empty(Directory.GetDirectories(paths.ConfigDirectory, "login-*"));
    }

    [Theory]
    [InlineData(AuthUrl, true)]
    [InlineData("https://dash.cloudflare.com.evil.test/argotunnel?callback=x", false)]
    [InlineData("http://dash.cloudflare.com/argotunnel?callback=x", false)]
    [InlineData("https://dash.cloudflare.com/argotunnel", false)]
    [InlineData("cmd /c start browser", false)]
    public void BrowserOnlyOpensCloudflareAuthorizationUrls(string line, bool expected)
    {
        Assert.Equal(expected, CloudflareLoginService.TryGetAuthorizationUrl(line, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
