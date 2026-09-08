using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

public sealed class CloudflaredManager(HttpClient? client = null)
{
    private readonly HttpClient _http = client ?? CreateClient();
    public string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "tools", OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");

    public async Task<string> GetInstalledVersionAsync()
    {
        if (!File.Exists(ExecutablePath)) return "未安装";
        var info = new ProcessStartInfo { FileName = ExecutablePath, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        info.ArgumentList.Add("--version");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法读取 cloudflared 版本。");
        var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
        return output.Trim();
    }

    public async Task<string> GetLatestVersionAsync()
    {
        using var response = await _http.GetAsync("https://api.github.com/repos/cloudflare/cloudflared/releases/latest");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("无法读取最新版本。");
    }

    public async Task UpdateAsync()
    {
        var asset = GetAssetName();
        var directory = Path.GetDirectoryName(ExecutablePath)!; Directory.CreateDirectory(directory);
        var download = Path.Combine(directory, asset + ".download");
        await using (var input = await _http.GetStreamAsync($"https://github.com/cloudflare/cloudflared/releases/latest/download/{asset}"))
        await using (var output = File.Create(download)) await input.CopyToAsync(output);
        var candidate = download;
        if (asset.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            var extract = Path.Combine(directory, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(extract);
            await using var archive = File.OpenRead(download); await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, extract, overwriteFiles: true);
            candidate = Path.Combine(extract, "cloudflared");
            if (!File.Exists(candidate)) candidate = Directory.GetFiles(extract, "cloudflared", SearchOption.AllDirectories)[0];
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(candidate, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(candidate, ExecutablePath, overwrite: true);
        if (File.Exists(download)) File.Delete(download);
    }

    public static string GetAssetName() => (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), System.Runtime.InteropServices.RuntimeInformation.OSArchitecture) switch
    {
        (true, _, _) => "cloudflared-windows-amd64.exe",
        (_, true, System.Runtime.InteropServices.Architecture.Arm64) => "cloudflared-darwin-arm64.tgz",
        (_, true, _) => "cloudflared-darwin-amd64.tgz",
        (_, _, System.Runtime.InteropServices.Architecture.Arm64) => "cloudflared-linux-arm64",
        _ => "cloudflared-linux-amd64"
    };

    private static HttpClient CreateClient() { var http = new HttpClient(); http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlatformTools", "0.1")); return http; }
}
