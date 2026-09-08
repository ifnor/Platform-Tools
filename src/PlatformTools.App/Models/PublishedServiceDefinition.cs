using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace PlatformTools.App.Models;

public sealed record PublishedServiceDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public bool DeletionPending { get; init; }
    public string Protocol { get; init; } = "http";
    public string LocalAddress { get; init; } = "http://localhost:8080";
    public bool IsQuick { get; init; } = true;
    public string TunnelName { get; init; } = "";
    public string Hostname { get; init; } = "";
    public int SuggestedPort { get; init; } = 8080;
    public bool ProtectedAccess { get; init; }
    public string AccountId { get; init; } = "";
    public string[] AllowedEmails { get; init; } = [];

    public PublishedServiceDefinition NormalizeAndValidate(IEnumerable<PublishedServiceDefinition> others)
    {
        var normalized = this with
        {
            Name = (Name ?? "").Trim(),
            Hostname = (Hostname ?? "").Trim().TrimEnd('.').ToLowerInvariant(),
            TunnelName = (TunnelName ?? "").Trim(),
            LocalAddress = ServiceAddress.Normalize(LocalAddress, Protocol) ?? "",
            AccountId = (AccountId ?? "").Trim(),
            AllowedEmails = (AllowedEmails ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        if (Id == Guid.Empty) throw new ArgumentException("服务 ID 无效。");
        if (normalized.Name.Length is < 1 or > 100) throw new ArgumentException("服务名称需要 1–100 个字符。");
        if (Protocol is not ("http" or "https" or "ssh" or "rdp" or "tcp" or "smb") || normalized.LocalAddress.Length == 0)
            throw new ArgumentException("请输入有效的协议和本地服务地址。");
        if (SuggestedPort is < 1 or > 65535) throw new ArgumentException("连接端口需要在 1–65535 之间。");
        if (IsQuick)
        {
            if (Protocol is not ("http" or "https")) throw new ArgumentException("临时地址仅支持 HTTP / HTTPS。");
            normalized = normalized with { Hostname = "", TunnelName = "", ProtectedAccess = false, AccountId = "", AllowedEmails = [] };
        }
        else
        {
            if (Uri.CheckHostName(normalized.Hostname) != UriHostNameType.Dns || !normalized.Hostname.Contains('.'))
                throw new ArgumentException("请输入完整公网域名，例如 api.example.com。");
            if (!Regex.IsMatch(normalized.TunnelName, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,99}$"))
                throw new ArgumentException("隧道名称仅支持字母、数字、下划线和短横线。");
            if (others.Any(x => x.Id != Id && !x.IsQuick && string.Equals(x.Hostname.TrimEnd('.'), normalized.Hostname, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("这个公网域名已分配给其他服务。");
            if (others.Any(x => x.Id != Id && !x.IsQuick && string.Equals(x.TunnelName, normalized.TunnelName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("每个服务需要独立的隧道名称，该名称已被使用。");
            if (ProtectedAccess && (normalized.AllowedEmails.Length == 0 || normalized.AllowedEmails.Any(e => !MailAddress.TryCreate(e, out _))))
                throw new ArgumentException("身份保护需要至少一个有效邮箱。");
        }
        return normalized;
    }
}
