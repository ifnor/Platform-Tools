using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class CloudflareServiceDeletionTests : IDisposable
{
    private const string Tunnel = "11111111-2222-3333-4444-555555555555";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly FakeCloud _cloud = new();
    private readonly PublishedServiceDefinition _definition = new() { Name = "web", IsQuick = false, TunnelName = "web-tunnel", Hostname = "web.example.com" };
    public CloudflareServiceDeletionTests()
    {
        _paths = new AppPaths(_root, Path.Combine(_root, "legacy-user"), Path.Combine(_root, "legacy-app"));
        File.WriteAllText(_paths.CertificatePath, PemEncoding.WriteString("ARGO TUNNEL TOKEN", Encoding.UTF8.GetBytes("{\"accountID\":\"account\",\"zoneID\":\"zone\",\"apiToken\":\"secret-login\"}")));
        File.WriteAllText(Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json"), "{\"AccountTag\":\"account\"}");
        Directory.CreateDirectory(Path.Combine(_paths.TunnelsDirectory, Tunnel));
        File.WriteAllText(Path.Combine(_paths.TunnelsDirectory, Tunnel, "config.yml"), "demo");
    }
    private CloudflareServiceDeletion Cleaner() => new(_paths, new HttpClient(_cloud) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") });

    [Fact]
    public async Task DeletesExactDnsThenTunnelThenLocalFiles_AndKeepsLogin()
    {
        await Cleaner().DeleteAsync(_definition);
        Assert.Equal(new[] { "/client/v4/zones/zone/dns_records/record", "/client/v4/accounts/account/cfd_tunnel/" + Tunnel }, _cloud.Deleted);
        Assert.True(File.Exists(_paths.CertificatePath));
        Assert.False(File.Exists(Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json")));
        Assert.False(File.Exists(Path.Combine(_paths.TunnelsDirectory, Tunnel, "config.yml")));
    }
    [Fact]
    public async Task TunnelDeleteFailureRetainsCheckpointAndCredentials_AndRetryResumes()
    {
        _cloud.FailTunnelDelete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cleaner().DeleteAsync(_definition));
        Assert.True(File.Exists(Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json")));
        Assert.Single(Directory.GetFiles(Path.Combine(_paths.ConfigDirectory, "deletions"), "*.json"));
        _cloud.FailTunnelDelete = false;
        await Cleaner().DeleteAsync(_definition);
        Assert.False(_cloud.TunnelExists);
        Assert.Empty(Directory.GetFiles(Path.Combine(_paths.ConfigDirectory, "deletions")));
        Assert.Equal(1, _cloud.Deleted.Count(path => path.Contains("dns_records")));
    }
    [Fact]
    public async Task ForeignDnsIsNeverDeleted()
    {
        _cloud.ForeignRecord = true;
        await Cleaner().DeleteAsync(_definition);
        Assert.DoesNotContain(_cloud.Deleted, path => path.Contains("dns_records"));
    }
    [Fact]
    public async Task DnsChangedAfterListingStopsCleanup()
    {
        _cloud.ChangeRecordOnRead = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cleaner().DeleteAsync(_definition));
        Assert.Empty(_cloud.Deleted);
    }
    [Fact]
    public async Task PermissionFailureDoesNotDeleteAnythingOrLeakToken()
    {
        _cloud.Deny = true;
        var error = await Assert.ThrowsAsync<CleanupAuthorizationException>(() => Cleaner().DeleteAsync(_definition));
        Assert.DoesNotContain("secret-login", error.ToString());
        Assert.Empty(_cloud.Deleted);
    }
    [Fact]
    public async Task QuickServiceNeedsNoCredentialsOrNetwork()
    {
        File.Delete(_paths.CertificatePath);
        await Cleaner().DeleteAsync(_definition with { IsQuick = true });
        Assert.Equal(0, _cloud.Requests);
    }
    [Fact]
    public async Task WrongZoneOrActiveConnectionsStopsBeforeMutation()
    {
        _cloud.ZoneName = "other.example";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cleaner().DeleteAsync(_definition));
        _cloud.ZoneName = "example.com"; _cloud.Active = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cleaner().DeleteAsync(_definition));
        Assert.Empty(_cloud.Deleted);
    }
    [Fact]
    public async Task ReadOnlyTunnelFilesAreDeletedWithoutChangingSharedCertificate()
    {
        var credentials = Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json");
        var config = Path.Combine(_paths.TunnelsDirectory, Tunnel, "config.yml");
        File.SetAttributes(credentials, File.GetAttributes(credentials) | FileAttributes.ReadOnly);
        File.SetAttributes(config, File.GetAttributes(config) | FileAttributes.ReadOnly);
        var certificateAttributes = File.GetAttributes(_paths.CertificatePath);
        await Cleaner().DeleteAsync(_definition);
        Assert.False(File.Exists(credentials)); Assert.False(File.Exists(config));
        Assert.Equal(certificateAttributes, File.GetAttributes(_paths.CertificatePath));
    }
    [Fact]
    public async Task LocalFailureSavesRemoteCompletion_AndRetryNeedsNoNetworkOrLogin()
    {
        var cleaner = new CloudflareServiceDeletion(_paths, new HttpClient(_cloud) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") },
            _ => throw new UnauthorizedAccessException("locked"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cleaner.DeleteAsync(_definition));
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
        var checkpoint = Directory.GetFiles(Path.Combine(_paths.ConfigDirectory, "deletions"), "*.json").Single();
        using (var json = JsonDocument.Parse(File.ReadAllText(checkpoint))) Assert.True(json.RootElement.GetProperty("RemoteCleanupCompleted").GetBoolean());
        var requests = _cloud.Requests;
        File.Delete(_paths.CertificatePath); _cloud.Deny = true;
        await Cleaner().DeleteAsync(_definition);
        Assert.Equal(requests, _cloud.Requests);
        Assert.False(File.Exists(checkpoint));
        Assert.False(File.Exists(Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json")));
    }
    [Fact]
    public async Task LegacyCheckpointWithAlreadyDeletedTunnelCanFinishReadOnlyFileCleanup()
    {
        var folder = Path.Combine(_paths.ConfigDirectory, "deletions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, _definition.Id.ToString("N") + ".json"), JsonSerializer.Serialize(new
        { AccountId = "account", ZoneId = "zone", TunnelId = Tunnel, TunnelName = _definition.TunnelName, Hostname = _definition.Hostname }));
        _cloud.TunnelExists = false; _cloud.DnsExists = false;
        var credentials = Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json");
        File.SetAttributes(credentials, File.GetAttributes(credentials) | FileAttributes.ReadOnly);
        await Cleaner().DeleteAsync(_definition);
        Assert.Empty(_cloud.Deleted);
        Assert.False(File.Exists(credentials));
        Assert.Empty(Directory.GetFiles(folder));
    }
    [Fact]
    public async Task CompletedCheckpointCannotDeleteFilesForMismatchedService()
    {
        var folder = Path.Combine(_paths.ConfigDirectory, "deletions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, _definition.Id.ToString("N") + ".json"), JsonSerializer.Serialize(new
        { AccountId = "account", ZoneId = "zone", TunnelId = Tunnel, TunnelName = "different", Hostname = _definition.Hostname, RemoteCleanupCompleted = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cleaner().DeleteAsync(_definition));
        Assert.True(File.Exists(Path.Combine(_paths.CredentialsDirectory, Tunnel + ".json")));
        Assert.Equal(0, _cloud.Requests);
    }
    private sealed class FakeCloud : HttpMessageHandler
    {
        public List<string> Deleted { get; } = [];
        public int Requests;
        public bool FailTunnelDelete, ForeignRecord, ChangeRecordOnRead, Deny, Active;
        public bool TunnelExists = true, DnsExists = true;
        public string ZoneName = "example.com";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.NotNull(request.Headers.Authorization);
            var path = request.RequestUri!.AbsolutePath;
            if (Deny) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            object Record(bool changed = false) => new { id = "record", name = "web.example.com", type = "CNAME", content = ForeignRecord || changed ? "other.example.net" : Tunnel + ".cfargotunnel.com" };
            object TunnelResult() => new { id = Tunnel, name = "web-tunnel", deleted_at = TunnelExists ? (string?)null : "2026-09-08", connections = Active ? new[] { new { id = "connection" } } : [] };
            object result;
            if (request.Method == HttpMethod.Delete)
            {
                if (path.Contains("cfd_tunnel") && FailTunnelDelete) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                Deleted.Add(path);
                if (path.Contains("dns_records")) DnsExists = false; else TunnelExists = false;
                result = new { id = "deleted" };
            }
            else if (path.EndsWith("/zones/zone")) result = new { name = ZoneName, account = new { id = "account" } };
            else if (path.EndsWith("/cfd_tunnel")) result = TunnelExists ? new[] { TunnelResult() } : [];
            else if (path.Contains("cfd_tunnel/")) result = TunnelResult();
            else if (path.EndsWith("/dns_records")) result = DnsExists ? new[] { Record() } : [];
            else if (path.EndsWith("/dns_records/record")) result = Record(ChangeRecordOnRead);
            else throw new Exception("Unexpected request " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { success = true, result })) });
        }
    }
    public void Dispose() { foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(_root, true); }
}
