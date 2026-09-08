using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

public sealed class CloudflareAccessService(HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };

    public async Task ConfigureEmailProtectionAsync(string accountId, string apiToken, string appName, string hostname, IEnumerable<string> emails)
    {
        var allowedEmails = emails.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allowedEmails.Length == 0) throw new ArgumentException("身份保护至少需要一个允许访问的邮箱。");
        using var applicationRequest = CreateRequest(HttpMethod.Post, $"accounts/{accountId}/access/apps", apiToken,
            new { name = appName, type = "self_hosted", domain = hostname, session_duration = "24h", destinations = new[] { new { type = "public", uri = hostname } } });
        using var applicationResponse = await _http.SendAsync(applicationRequest);
        var appId = await ReadResultIdAsync(applicationResponse, "创建 Access 应用失败");

        var include = allowedEmails.Select(email => new Dictionary<string, object> { ["email"] = new { email } }).ToArray();
        using var policyRequest = CreateRequest(HttpMethod.Post, $"accounts/{accountId}/access/apps/{appId}/policies", apiToken,
            new { name = "Platform Tools allowed users", decision = "allow", precedence = 1, include });
        using var policyResponse = await _http.SendAsync(policyRequest);
        _ = await ReadResultIdAsync(policyResponse, "创建 Access 访问策略失败");
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
