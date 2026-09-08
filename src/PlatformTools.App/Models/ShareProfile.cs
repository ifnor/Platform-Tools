using System;
using System.Text;
using System.Text.Json;

namespace PlatformTools.App.Models;

public sealed record ShareProfile(int Version, string Name, string Protocol, string Hostname, int SuggestedLocalPort, bool RequiresAccess)
{
    public string ToShareCode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static ShareProfile Parse(string value)
    {
        value = value.Trim();
        if (value.StartsWith('{')) return Deserialize(value);
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
    }

    private static ShareProfile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<ShareProfile>(json) ?? throw new FormatException("分享资料为空。");
        if (profile.Version != 1) throw new FormatException("不支持的分享资料版本。");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100) throw new FormatException("服务名称无效。");
        if (profile.Protocol is not ("http" or "https" or "tcp" or "ssh" or "rdp" or "smb")) throw new FormatException("服务协议无效。");
        if (profile.SuggestedLocalPort is < 1 or > 65535) throw new FormatException("本地端口无效。");
        if (profile.Protocol is "http" or "https")
        {
            if (!Uri.TryCreate(profile.Hostname, UriKind.Absolute, out var uri) || uri.Scheme != profile.Protocol) throw new FormatException("服务网址无效。");
        }
        else if (Uri.CheckHostName(profile.Hostname) == UriHostNameType.Unknown) throw new FormatException("服务域名无效。");
        return profile;
    }
}
