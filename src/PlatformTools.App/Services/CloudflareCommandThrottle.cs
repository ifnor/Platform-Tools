using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal sealed class CloudflareRateLimitException(string message) : InvalidOperationException(message);

internal sealed class CloudflareCommandThrottle
{
    public static CloudflareCommandThrottle Current { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await command(); }
            catch (InvalidOperationException ex) when (IsRateLimited(ex.Message))
            {
                throw new CloudflareRateLimitException(LocalizationService.T(
                    "Cloudflare 管理接口返回限流（429）。可手动重试；配置和凭据已保留，无需重新登录。",
                    "Cloudflare management API returned a rate limit (429). You can retry manually. Configuration and credentials are preserved; no new login is needed."));
            }
        }
        finally { _gate.Release(); }
    }

    internal static bool IsRateLimited(string message) =>
        message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(message, @"\b(?:status|HTTP)(?:\s+code)?\s*[:=]?\s*429\b", RegexOptions.IgnoreCase);

}
