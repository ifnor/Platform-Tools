using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace PlatformTools.App.Services;

public sealed class CloudflareAccessService(HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };

    public async Task ConfigureEmailProtectionAsync(string accountId, string apiToken, string appName, string hostname, IEnumerable<string> emails, CancellationToken cancellationToken = default)
    {
        var allowedEmails = emails.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allowedEmails.Length == 0) throw new ArgumentException("身份保护至少需要一个允许访问的邮箱。");
        var applications = await ListAsync($"accounts/{accountId}/access/apps?domain={Uri.EscapeDataString(hostname)}", apiToken, cancellationToken);
        var existing = applications.FirstOrDefault(app => app.TryGetProperty("domain", out var domain) && string.Equals(domain.GetString(), hostname, StringComparison.OrdinalIgnoreCase));
        string appId;
        if (existing.ValueKind == JsonValueKind.Object) appId = existing.GetProperty("id").GetString()!;
        else
        {
            using var applicationRequest = CreateRequest(HttpMethod.Post, $"accounts/{accountId}/access/apps", apiToken,
                new { name = appName, type = "self_hosted", domain = hostname, session_duration = "24h", destinations = new[] { new { type = "public", uri = hostname } } });
            using var applicationResponse = await _http.SendAsync(applicationRequest, cancellationToken);
            appId = await ReadResultIdAsync(applicationResponse, "创建 Access 应用失败");
        }

        var include = allowedEmails.Select(email => new Dictionary<string, object> { ["email"] = new { email } }).ToArray();
        var policyPath = $"accounts/{accountId}/access/apps/{appId}/policies";
        var policies = await ListAsync(policyPath, apiToken, cancellationToken);
        if (policies.Any(policy => !policy.TryGetProperty("name", out var name) || name.GetString() != "Platform Tools allowed users"))
            throw new InvalidOperationException("此域名已有其他 Access 策略，请先在 Cloudflare 中确认访问规则；现有策略未被修改。");
        var policyId = policies.FirstOrDefault().ValueKind == JsonValueKind.Object ? policies[0].GetProperty("id").GetString() : null;
        using var policyRequest = CreateRequest(policyId is null ? HttpMethod.Post : HttpMethod.Put, policyId is null ? policyPath : policyPath + "/" + policyId, apiToken,
            new { name = "Platform Tools allowed users", decision = "allow", precedence = 1, include });
        using var policyResponse = await _http.SendAsync(policyRequest, cancellationToken);
        _ = await ReadResultIdAsync(policyResponse, "创建 Access 访问策略失败");
    }

    private async Task<List<JsonElement>> ListAsync(string path, string token, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + (path.Contains('?') ? "&" : "?") + $"page={page}&per_page=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.TryGetProperty("success", out var success) && !success.GetBoolean()) throw new InvalidOperationException("无法读取 Cloudflare Access 配置。");
            items.AddRange(json.RootElement.GetProperty("result").EnumerateArray().Select(item => item.Clone()));
            if (!json.RootElement.TryGetProperty("result_info", out var info) || !info.TryGetProperty("total_pages", out var total) || page >= total.GetInt32()) return items;
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> ReadResultIdAsync(HttpResponseMessage response, string prefix)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{prefix}：HTTP {(int)response.StatusCode} {content}");
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("success", out var success) && !success.GetBoolean()) throw new InvalidOperationException($"{prefix}：{content}");
        return document.RootElement.GetProperty("result").GetProperty("id").GetString() ?? throw new InvalidOperationException(prefix + "：响应缺少 ID。");
    }
}
