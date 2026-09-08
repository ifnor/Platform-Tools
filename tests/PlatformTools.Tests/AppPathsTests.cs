using System.Diagnostics;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlatformTools.Tests", Guid.NewGuid().ToString("N"));
    private string Application => Path.Combine(_root, "portable app 中文");
    private string LegacyUser => Path.Combine(_root, "user");
    private string LegacyAppData => Path.Combine(_root, "appdata");
    private const string CredentialName = "11111111-2222-3333-4444-555555555555.json";

    [Fact]
    public void Migration_CopiesSettingsAndCredentialsWithoutRemovingOriginals()
    {
        WriteLegacyFiles();
        var paths = CreatePaths();

        Assert.Equal(Path.Combine(Application, "config"), paths.ConfigDirectory);
        Assert.Equal("old settings", File.ReadAllText(paths.SettingsPath));
        Assert.Equal("old certificate", File.ReadAllText(paths.CertificatePath));
        Assert.Equal("old credential", File.ReadAllText(Path.Combine(paths.CredentialsDirectory, CredentialName)));
        Assert.True(File.Exists(Path.Combine(LegacyUser, ".cloudflared", "cert.pem")));
        Assert.False(File.Exists(Path.Combine(paths.CredentialsDirectory, "config.yml")));
        Assert.False(File.Exists(Path.Combine(paths.CredentialsDirectory, "unrelated.json")));
    }

    [Fact]
    public void Migration_PreservesPortableAccountAndSettings()
    {
        WriteLegacyFiles();
        var portableCloudflare = Path.Combine(Application, "config", ".cloudflared");
        Directory.CreateDirectory(portableCloudflare);
        File.WriteAllText(Path.Combine(portableCloudflare, "cert.pem"), "portable certificate");
        File.WriteAllText(Path.Combine(Application, "config", "settings.json"), "portable settings");
        var paths = CreatePaths();

        Assert.Equal("portable certificate", File.ReadAllText(paths.CertificatePath));
        Assert.Equal("portable settings", File.ReadAllText(paths.SettingsPath));
        Assert.False(File.Exists(Path.Combine(paths.CredentialsDirectory, CredentialName)));
    }

    [Fact]
    public void Migration_DoesNotRestoreCredentialsDeletedAfterFirstRun()
    {
        WriteLegacyFiles();
        var paths = CreatePaths();
        File.Delete(paths.CertificatePath);
        CreatePaths();
        Assert.False(File.Exists(paths.CertificatePath));
    }

    [Fact]
    public void Cloudflared_UsesPortableHomeWithoutChangingParentEnvironment()
    {
        var paths = CreatePaths();
        var parentHome = Environment.GetEnvironmentVariable("HOME");
        var parentProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var info = new ProcessStartInfo();
        info.Environment["TUNNEL_CRED_FILE"] = "outside.json";
        info.Environment["TUNNEL_TOKEN"] = "external-token";
        paths.ConfigureCloudflared(info);

        Assert.False(info.UseShellExecute);
        Assert.Equal(paths.ConfigDirectory, info.WorkingDirectory);
        Assert.Equal(paths.ConfigDirectory, info.Environment["HOME"]);
        Assert.Equal(paths.ConfigDirectory, info.Environment["USERPROFILE"]);
        Assert.Equal(paths.CertificatePath, info.Environment["TUNNEL_ORIGIN_CERT"]);
        Assert.False(info.Environment.ContainsKey("TUNNEL_CRED_FILE"));
        Assert.False(info.Environment.ContainsKey("TUNNEL_TOKEN"));
        Assert.Equal(parentHome, Environment.GetEnvironmentVariable("HOME"));
        Assert.Equal(parentProfile, Environment.GetEnvironmentVariable("USERPROFILE"));
    }

    private AppPaths CreatePaths() => new(Application, LegacyUser, LegacyAppData);

    private void WriteLegacyFiles()
    {
        var cloudflare = Path.Combine(LegacyUser, ".cloudflared");
        Directory.CreateDirectory(cloudflare);
        File.WriteAllText(Path.Combine(cloudflare, "cert.pem"), "old certificate");
        File.WriteAllText(Path.Combine(cloudflare, CredentialName), "old credential");
        File.WriteAllText(Path.Combine(cloudflare, "config.yml"), "old absolute paths");
        File.WriteAllText(Path.Combine(cloudflare, "unrelated.json"), "unrelated");
        var settings = Path.Combine(LegacyAppData, "PlatformTools");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "settings.json"), "old settings");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
