using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal sealed class CloudflareRateLimitException(string message) : InvalidOperationException(message);

internal sealed class CloudflareCommandThrottle(Func<DateTimeOffset>? clock = null)
{
    public static CloudflareCommandThrottle Current { get; } = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _retryAt;

    public async Task<T> RunAsync<T>(Func<Task<T>> command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock() < _retryAt) throw CooldownError();
            try { return await command(); }
            catch (InvalidOperationException ex) when (IsRateLimited(ex.Message))
            {
                // cloudflared does not expose Retry-After; use a conservative five-minute cooldown.
                _retryAt = _clock().AddMinutes(5);
                throw CooldownError();
            }
        }
        finally { _gate.Release(); }
    }

    internal static bool IsRateLimited(string message) =>
        message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(message, @"\b(?:status|HTTP)(?:\s+code)?\s*[:=]?\s*429\b", RegexOptions.IgnoreCase);

    private CloudflareRateLimitException CooldownError()
    {
        var seconds = Math.Max(1, (int)Math.Ceiling((_retryAt - _clock()).TotalSeconds));
        return new CloudflareRateLimitException(LocalizationService.T(
            $"Cloudflare 管理接口暂时限流（429）。请约 {seconds} 秒后重试；暂时不要反复点击启动或刷新登录。当前配置和凭据已保留，无需重新登录。",
            $"Cloudflare management API is rate limited (429). Retry in about {seconds} seconds; avoid repeatedly starting services or refreshing sign-in. Configuration and credentials are preserved; no new login is needed."));
    }
}
