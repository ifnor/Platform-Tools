using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

internal sealed class CleanupAuthorizationException() : InvalidOperationException("当前凭据没有云端清理权限。需要此账户的 Cloudflare Tunnel 编辑权限，以及域名的 DNS 编辑、Zone 读取权限；请提供相应 API Token 后重试删除。");

internal sealed class CloudflareServiceDeletion(AppPaths paths, HttpClient? httpClient = null, Action<string>? deleteFile = null)
{
    private static readonly HttpClient DefaultHttp = new() { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"), Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http = httpClient ?? DefaultHttp;
    private readonly Action<string> _deleteFile = deleteFile ?? DeleteLocalFile;
    private sealed record Checkpoint(string AccountId, string ZoneId, string TunnelId, string TunnelName, string Hostname, bool RemoteCleanupCompleted = false);

    public async Task DeleteAsync(PublishedServiceDefinition definition, string? apiToken = null)
    {
        if (definition.IsQuick) return;
        var checkpointDirectory = Path.Combine(paths.ConfigDirectory, "deletions");
        var checkpointFile = Path.Combine(checkpointDirectory, definition.Id.ToString("N") + ".json");
        Checkpoint? checkpoint = null;
        if (File.Exists(checkpointFile))
        {
            checkpoint = JsonSerializer.Deserialize<Checkpoint>(await File.ReadAllTextAsync(checkpointFile)) ?? throw new InvalidDataException("删除进度文件无效。");
            if (checkpoint.TunnelName != definition.TunnelName || checkpoint.Hostname != definition.Hostname || !Guid.TryParse(checkpoint.TunnelId, out _))
                throw new InvalidOperationException("删除进度与此服务不一致，无法继续清理。");
            if (checkpoint.RemoteCleanupCompleted)
            {
                CleanupLocalFiles(checkpoint, checkpointFile);
                return;
            }
        }
        var (account, zone, loginToken) = ReadCredentials();
        if (!string.IsNullOrWhiteSpace(definition.AccountId) && definition.AccountId != account)
            throw new InvalidOperationException("当前登录账户与服务账户不一致，请登录原账户后删除。");
        var token = string.IsNullOrWhiteSpace(apiToken) ? loginToken : apiToken;
        var zoneDetails = await SendAsync(HttpMethod.Get, $"zones/{Segment(zone)}", token);
        var zoneName = zoneDetails.GetProperty("name").GetString() ?? "";
        if (zoneName.Length == 0 || !(string.Equals(definition.Hostname, zoneName, StringComparison.OrdinalIgnoreCase) || definition.Hostname.EndsWith("." + zoneName, StringComparison.OrdinalIgnoreCase))
            || zoneDetails.GetProperty("account").GetProperty("id").GetString() != account)
            throw new InvalidOperationException("当前登录授权的域名区域与此服务不一致，请在设置中重新授权此服务所属的域名。");
        if (checkpoint is not null)
        {
            if (checkpoint.AccountId != account || checkpoint.ZoneId != zone || checkpoint.TunnelName != definition.TunnelName || checkpoint.Hostname != definition.Hostname)
                throw new InvalidOperationException("删除进度与当前账户或服务不一致，请恢复原账户后重试。");
        }
        else
        {
            var tunnels = await ListAsync($"accounts/{Segment(account)}/cfd_tunnel?is_deleted=false&name={Uri.EscapeDataString(definition.TunnelName)}", token);
            var matches = tunnels.Where(t => string.Equals(t.GetProperty("name").GetString(), definition.TunnelName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("找到多个同名隧道，无法确定要删除的资源。");
            if (matches.Length == 1)
            {
                checkpoint = new(account, zone, matches[0].GetProperty("id").GetString()!, definition.TunnelName, definition.Hostname);
                if (!Guid.TryParse(checkpoint.TunnelId, out _)) throw new InvalidDataException("云端隧道 ID 无效。");
                Directory.CreateDirectory(checkpointDirectory);
                // Persist identity before the first remote mutation, so interrupted deletion can resume.
                await SaveCheckpointAsync(checkpointFile, checkpoint);
            }
        }
        var records = await ListAsync($"zones/{Segment(zone)}/dns_records?name={Uri.EscapeDataString(definition.Hostname)}", token);
        if (checkpoint is null)
        {
            // No known tunnel identity: never infer ownership from the hostname alone.
            if (records.Any(r => Same(r, "name", definition.Hostname) && Same(r, "type", "CNAME") && (r.GetProperty("content").GetString() ?? "").TrimEnd('.').EndsWith(".cfargotunnel.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("隧道不存在，但此域名仍指向一个 Cloudflare 隧道。无法确认归属，请在 Cloudflare 核对并清理该记录后重试删除。");
            return;
        }
        if (!Guid.TryParse(checkpoint.TunnelId, out var tunnelId)) throw new InvalidDataException("删除进度中的隧道 ID 无效。");
        var target = tunnelId + ".cfargotunnel.com";
        var credentialsFile = Path.Combine(paths.CredentialsDirectory, tunnelId + ".json");
        if (File.Exists(credentialsFile))
        {
            using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(credentialsFile));
            if (credentials.RootElement.GetProperty("AccountTag").GetString() != account)
                throw new InvalidOperationException("隧道凭据不属于当前账户，已停止删除。");
        }
        var tunnelPath = $"accounts/{Segment(account)}/cfd_tunnel/{tunnelId}";
        var tunnel = await SendAsync(HttpMethod.Get, tunnelPath, token, allowMissing: true);
        var exists = tunnel.ValueKind == JsonValueKind.Object && (!tunnel.TryGetProperty("deleted_at", out var deleted) || deleted.ValueKind == JsonValueKind.Null);
        if (exists && tunnel.TryGetProperty("connections", out var connections) && connections.GetArrayLength() > 0)
            throw new InvalidOperationException("隧道仍有活动连接，请稍后重试；若其他设备也在使用此隧道，请先停止它们。");
        foreach (var record in records.Where(r => Same(r, "name", definition.Hostname) && Same(r, "type", "CNAME") && Same(r, "content", target)))
        {
            var recordPath = $"zones/{Segment(zone)}/dns_records/{Segment(record.GetProperty("id").GetString()!)}";
            var current = await SendAsync(HttpMethod.Get, recordPath, token, allowMissing: true);
            if (current.ValueKind == JsonValueKind.Undefined) continue;
            if (!Same(current, "name", definition.Hostname) || !Same(current, "type", "CNAME") || !Same(current, "content", target))
                throw new InvalidOperationException("DNS 记录在删除前发生变化，已暂停清理，请核对后重试。");
            await SendAsync(HttpMethod.Delete, recordPath, token, allowMissing: true);
        }
        if (exists) await SendAsync(HttpMethod.Delete, tunnelPath, token, allowMissing: true);
        checkpoint = checkpoint with { RemoteCleanupCompleted = true };
        await SaveCheckpointAsync(checkpointFile, checkpoint);
        CleanupLocalFiles(checkpoint, checkpointFile);
    }

    private static async Task SaveCheckpointAsync(string path, Checkpoint checkpoint)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(checkpoint));
        File.Move(temporary, path, true);
    }

    private void CleanupLocalFiles(Checkpoint checkpoint, string checkpointFile)
    {
        var tunnelId = Guid.Parse(checkpoint.TunnelId).ToString();
        try
        {
            // Only the selected tunnel's files; never modify the shared login certificate or ACLs.
            _deleteFile(Path.Combine(paths.CredentialsDirectory, tunnelId + ".json"));
            _deleteFile(Path.Combine(paths.TunnelsDirectory, tunnelId, "config.yml"));
            _deleteFile(checkpointFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(LocalizationService.T(
                "云端清理已完成，但本地文件仍被占用或没有删除权限。请关闭占用此隧道文件的程序，确认 config 目录可写后重试删除；无需重新登录或再次清理云端。",
                "Cloud cleanup is complete, but local files are locked or cannot be deleted. Close programs using this tunnel's files, ensure config is writable, and retry deletion. No sign-in or further cloud cleanup is needed."), ex);
        }
    }

    internal static void DeleteLocalFile(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("待删除路径不是普通文件。");
        var readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        if (readOnly) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        try { File.Delete(path); }
        catch
        {
            if (readOnly)
                try { File.SetAttributes(path, attributes); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private (string Account, string Zone, string Token) ReadCredentials()
    {
        try
        {
            var pem = File.ReadAllText(paths.CertificatePath);
            if (!PemEncoding.TryFind(pem, out var fields) || pem[fields.Label] != "ARGO TUNNEL TOKEN") throw new InvalidDataException();
            using var json = JsonDocument.Parse(Convert.FromBase64String(pem[fields.Base64Data]));
            var root = json.RootElement;
            var account = root.GetProperty("accountID").GetString(); var zone = root.GetProperty("zoneID").GetString(); var token = root.GetProperty("apiToken").GetString();
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(zone) || string.IsNullOrWhiteSpace(token)) throw new InvalidDataException();
            return (account, zone, token);
        }
        catch { throw new InvalidOperationException("请先在设置中登录原 Cloudflare 账户，再删除此固定域名服务。"); }
    }
    private static bool Same(JsonElement item, string property, string value) => item.TryGetProperty(property, out var field) && string.Equals(field.GetString()?.TrimEnd('.'), value.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
    private static string Segment(string value) => Uri.EscapeDataString(value);
    private async Task<List<JsonElement>> ListAsync(string path, string token)
    {
        var items = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var envelope = await SendAsync(HttpMethod.Get, path + $"&page={page}&per_page=100", token, envelope: true);
            var results = envelope.GetProperty("result");
            items.AddRange(results.EnumerateArray().Select(r => r.Clone()));
            if (envelope.TryGetProperty("result_info", out var info) && info.TryGetProperty("total_pages", out var total))
            { if (page >= total.GetInt32()) return items; }
            else if (results.GetArrayLength() < 100) return items;
        }
    }
    private async Task<JsonElement> SendAsync(HttpMethod method, string path, string token, bool allowMissing = false, bool envelope = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new CleanupAuthorizationException();
        if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Cloudflare 清理失败（HTTP {(int)response.StatusCode}），服务配置已保留，请重试删除。");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean()) throw new InvalidOperationException("Cloudflare 未确认清理成功，服务配置已保留，请重试删除。");
        return envelope ? json.RootElement.Clone() : json.RootElement.GetProperty("result").Clone();
    }
}
