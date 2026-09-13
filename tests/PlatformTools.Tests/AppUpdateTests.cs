using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    public AppUpdateTests() { Directory.CreateDirectory(_root); }
    [Theory]
    [InlineData("v0.3.8", "0.3.7", true)]
    [InlineData("v0.3.7", "0.3.7+abc", false)]
    [InlineData("v0.3.6", "0.3.7", false)]
    [InlineData("v0.3.7", "0.3.7-beta.1", true)]
    [InlineData("v0.3.8-beta.1", "0.3.7", false)]
    public void VersionsNeverDowngradeOrInstallPrereleases(string tag, string current, bool expected)
        => Assert.Equal(expected, AppUpdateService.IsNewer(tag, current));

    [Fact]
    public async Task ChecksOwnRepositoryAndDownloadsMatchingVerifiedAsset()
    {
        var handler = new ReleaseHandler();
        var service = new AppUpdateService(new HttpClient(handler), _root);
        var release = await service.CheckAsync("test-token", default, "0.3.7", "win-x64");
        Assert.NotNull(release);
        Assert.Equal("PlatformTools-0.3.8-win-x64-portable.zip", release.AssetName);
        var result = await service.DownloadAsync(release, "test-token", null, default);
        Assert.Equal(handler.Package, File.ReadAllBytes(result.FilePath));
        Assert.All(handler.Requests, uri => Assert.StartsWith("https://api.github.com/repos/ifnor/Platform-Tools/releases/", uri));
    }
    [Fact]
    public async Task MismatchedDownloadIsRejectedAndRemoved()
    {
        var handler = new ReleaseHandler { BadChecksum = true };
        var service = new AppUpdateService(new HttpClient(handler), _root);
        var release = await service.CheckAsync("test-token", default, "0.3.7", "win-x64");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release!, "test-token", null, default));
        Assert.Empty(Directory.GetFiles(_root, "*.zip", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task IncompleteReleaseCannotBeInstalled()
    {
        var handler = new ReleaseHandler { MissingChecksums = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AppUpdateService(new HttpClient(handler)).CheckAsync("test-token", default, "0.3.7", "win-x64"));
    }
    [Theory]
    [InlineData("config/settings.json")]
    [InlineData("../PlatformTools.exe")]
    [InlineData("tools/../../cert.pem")]
    [InlineData("C:/PlatformTools.exe")]
    public void ExtractionRejectsConfigAndUnsafePaths(string name)
    {
        var archive = Path.Combine(_root, "update.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("unsafe"); }
        Assert.Throws<InvalidDataException>(() => AppUpdateInstaller.Extract(archive, Path.Combine(_root, "payload")));
    }
    [Fact]
    public void InstallationPreservesConfigAndRollsBackPartialFailure()
    {
        var target = Path.Combine(_root, "target"); var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(Path.Combine(target, "config")); Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(target, "config", "services.json"), "keep credentials and services");
        File.WriteAllText(Path.Combine(target, "PlatformTools.exe"), "old app"); File.WriteAllText(Path.Combine(target, "library.dll"), "old library");
        File.WriteAllText(Path.Combine(payload, "PlatformTools.exe"), "new app"); File.WriteAllText(Path.Combine(payload, "library.dll"), "new library");
        var files = Directory.GetFiles(payload).Select(path => new UpdateFile(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();
        var plan = new UpdatePlan(target, payload, Path.Combine(_root, "backup"), "", 0, 0, files);
        var calls = 0;
        Assert.Throws<IOException>(() => AppUpdateInstaller.Apply(plan, (source, destination) => { if (++calls == 2) throw new IOException("locked"); File.Copy(source, destination); }));
        Assert.Equal("old app", File.ReadAllText(Path.Combine(target, "PlatformTools.exe")));
        Assert.Equal("old library", File.ReadAllText(Path.Combine(target, "library.dll")));
        Assert.Equal("keep credentials and services", File.ReadAllText(Path.Combine(target, "config", "services.json")));
        AppUpdateInstaller.Apply(plan);
        Assert.Equal("new app", File.ReadAllText(Path.Combine(target, "PlatformTools.exe")));
        Assert.Equal("keep credentials and services", File.ReadAllText(Path.Combine(target, "config", "services.json")));
    }
    [Fact]
    public void ModifiedStagingFilesCannotBeInstalled()
    {
        var payload = Path.Combine(_root, "payload"); Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "PlatformTools.exe"), "modified");
        var plan = new UpdatePlan(_root, payload, Path.Combine(_root, "backup"), "", 0, 0, [new("PlatformTools.exe", new string('0', 64))]);
        Assert.Throws<InvalidDataException>(() => AppUpdateInstaller.Apply(plan));
        Assert.False(File.Exists(Path.Combine(_root, "PlatformTools.exe")));
    }
    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public byte[] Package { get; } = Encoding.UTF8.GetBytes("fake update package");
        public bool BadChecksum, MissingChecksums;
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            const string asset = "PlatformTools-0.3.8-win-x64-portable.zip";
            HttpContent content;
            if (request.RequestUri.AbsolutePath.EndsWith("/latest"))
            {
                var assets = new List<object> { new { name = asset, id = 1, size = Package.Length } };
                if (!MissingChecksums) assets.Add(new { name = "SHA256SUMS.txt", id = 2, size = 100 });
                content = new StringContent(JsonSerializer.Serialize(new { tag_name = "v0.3.8", draft = false, prerelease = false, body = "Release notes", assets }));
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/2")) content = new StringContent((BadChecksum ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(Package))) + "  " + asset);
            else content = new ByteArrayContent(Package);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    public void Dispose() { Directory.Delete(_root, true); }
}
