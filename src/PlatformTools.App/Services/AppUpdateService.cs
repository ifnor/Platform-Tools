using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal sealed record AppRelease(string Tag, string Notes, string AssetName, long AssetId, long ChecksumId, long Size);
internal sealed record AppUpdateDownload(AppRelease Release, string FilePath);

internal sealed class AppUpdateService(HttpClient? client = null, string? updateDirectory = null)
{
    internal const string Repository = "ifnor/Platform-Tools";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private const long MaxDownload = 512L * 1024 * 1024;
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly HttpClient _http = client ?? DefaultClient;
    public static string CurrentVersion => typeof(AppUpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    public static string RuntimeId => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    internal static bool IsNewer(string tag, string current)
    {
        var candidate = tag.TrimStart('v');
        if (!Regex.IsMatch(candidate, @"^\d+\.\d+\.\d+$")) return false;
        var local = current.Split('+')[0];
        if (!Version.TryParse(candidate, out var latest) || !Version.TryParse(local.Split('-')[0], out var installed)) return false;
        return latest > installed || latest == installed && local.Contains('-');
    }
    internal static string PackageName(string tag, string rid) => $"PlatformTools-{tag.TrimStart('v')}-{rid}" + (rid.StartsWith("win-", StringComparison.Ordinal) ? "-portable.zip" : rid.StartsWith("osx-", StringComparison.Ordinal) ? ".dmg" : ".AppImage");

    public async Task<AppRelease?> CheckAsync(string? token, CancellationToken cancellationToken, string? currentVersion = null, string? runtimeId = null)
    {
        using var request = Request("releases/latest", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        CheckResponse(response);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean() || !IsNewer(tag, currentVersion ?? CurrentVersion)) return null;
        var assetName = PackageName(tag, runtimeId ?? RuntimeId);
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var asset = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == assetName);
        var checksums = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
        if (asset.ValueKind != JsonValueKind.Object || checksums.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("发现新版本，但此平台的安装包或 SHA256SUMS.txt 尚未发布，请稍后重试。");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxDownload) throw new InvalidDataException("更新包大小无效。");
        return new(tag, root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "", assetName, asset.GetProperty("id").GetInt64(), checksums.GetProperty("id").GetInt64(), size);
    }

    public async Task<AppUpdateDownload> DownloadAsync(AppRelease release, string? token, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(updateDirectory ?? Path.Combine(AppPaths.Current.ConfigDirectory, "updates"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checksumPath = Path.Combine(directory, "SHA256SUMS.txt");
        var destination = Path.Combine(directory, release.AssetName);
        try
        {
            if (Path.GetFileName(release.AssetName) != release.AssetName) throw new InvalidDataException("更新文件名无效。");
            await DownloadAssetAsync(release.ChecksumId, checksumPath, token, 1024 * 1024, null, cancellationToken);
            var expected = ReadChecksum(await File.ReadAllTextAsync(checksumPath, cancellationToken), release.AssetName);
            await DownloadAssetAsync(release.AssetId, destination, token, release.Size, progress, cancellationToken);
            if (new FileInfo(destination).Length != release.Size) throw new InvalidDataException("更新包不完整，请重新下载。");
            await VerifyChecksumAsync(destination, expected, cancellationToken);
            return new(release, destination);
        }
        catch { File.Delete(destination); File.Delete(checksumPath); throw; }
    }
    internal static string ReadChecksum(string manifest, string assetName)
    {
        var matches = manifest.Split('\n').Select(l => Regex.Match(l.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$"))
            .Where(m => m.Success && m.Groups[2].Value == assetName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("无法确认更新包的 SHA256 校验值。");
        return matches[0].Groups[1].Value;
    }
    internal static async Task VerifyChecksumAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包 SHA256 校验失败，已停止安装。");
    }
    private async Task DownloadAssetAsync(long id, string path, string? token, long limit, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = Request("releases/assets/" + id, token, download: true);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        CheckResponse(response);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("下载文件超过预期大小。");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report(total * 100d / limit);
        }
    }
    private static HttpRequestMessage Request(string path, string? token, bool download = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/" + path);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PlatformTools", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(download ? "application/octet-stream" : "application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return request;
    }
    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("没有找到正式发布版本；私有仓库请填写有 Contents 读取权限的 GitHub Token。");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new InvalidOperationException("GitHub 请求受限或没有读取权限，请稍后重试或检查 Token 权限。");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub 更新请求失败（HTTP {(int)response.StatusCode}）。");
    }
}
