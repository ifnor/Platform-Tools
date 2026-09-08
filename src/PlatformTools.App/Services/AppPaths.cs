using System;
using System.Diagnostics;
using System.IO;

namespace PlatformTools.App.Services;

internal sealed class AppPaths
{
    private static readonly Lazy<AppPaths> Default = new(() => new(
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLATFORMTOOLS_DATA_HOME"))
            ? AppContext.BaseDirectory : Environment.GetEnvironmentVariable("PLATFORMTOOLS_DATA_HOME")!,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    public static AppPaths Current => Default.Value;

    public string ConfigDirectory { get; }
    public string CredentialsDirectory => Path.Combine(ConfigDirectory, ".cloudflared");
    public string CertificatePath => Path.Combine(CredentialsDirectory, "cert.pem");
    public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");
    public string TunnelsDirectory => Path.Combine(ConfigDirectory, "tunnels");

    internal AppPaths(string applicationDirectory, string legacyUserDirectory, string legacyAppDataDirectory)
    {
        ConfigDirectory = Path.GetFullPath(Path.Combine(applicationDirectory, "config"));
        Directory.CreateDirectory(CredentialsDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ConfigDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(CredentialsDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var marker = Path.Combine(ConfigDirectory, ".migration-complete");
        if (File.Exists(marker)) return;
        CopyIfMissing(Path.Combine(legacyAppDataDirectory, "PlatformTools", "settings.json"), SettingsPath);
        var legacyCredentials = Path.Combine(legacyUserDirectory, ".cloudflared");
        // An existing portable certificate may belong to a different account.
        if (!File.Exists(CertificatePath) && File.Exists(Path.Combine(legacyCredentials, "cert.pem")))
        {
            foreach (var file in Directory.EnumerateFiles(legacyCredentials, "*.json"))
                if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                    CopyIfMissing(file, Path.Combine(CredentialsDirectory, Path.GetFileName(file)));
            CopyIfMissing(Path.Combine(legacyCredentials, "cert.pem"), CertificatePath);
        }
        File.WriteAllText(marker, "1");
    }

    public void ConfigureCloudflared(ProcessStartInfo info, string? homeDirectory = null)
    {
        var home = homeDirectory ?? ConfigDirectory;
        var credentials = Path.Combine(home, ".cloudflared");
        info.UseShellExecute = false;
        info.WorkingDirectory = home;
        // cloudflared login and Access token caching use the child process's home.
        info.Environment["HOME"] = home;
        info.Environment["USERPROFILE"] = home;
        info.Environment["TUNNEL_ORIGIN_CERT"] = Path.Combine(credentials, "cert.pem");
        info.Environment["CFDPATH"] = credentials;
        // Do not inherit external credential/config/log destinations.
        foreach (var key in new[] { "TUNNEL_CRED_FILE", "TUNNEL_TOKEN", "TUNNEL_TOKEN_FILE", "TUNNEL_LOGFILE", "TUNNEL_LOGDIRECTORY", "TUNNEL_TRACE_OUTPUT", "TUNNEL_PIDFILE", "TUNNEL_CONFIG" })
            info.Environment.Remove(key);
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        File.Copy(source, destination, overwrite: false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
