using System;

namespace PlatformTools.App.Models;

public static class ServiceAddress
{
    public static string? Normalize(string? value, string protocol)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, out var port) && port is > 0 and <= 65535) return $"{protocol}://localhost:{port}";
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"{protocol}://{value}";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme.Equals(protocol, StringComparison.OrdinalIgnoreCase)
               && uri.Port is > 0 and <= 65535
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            : null;
    }
}
