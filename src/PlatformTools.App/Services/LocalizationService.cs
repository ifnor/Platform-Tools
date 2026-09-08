using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PlatformTools.App.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, string> ZhToEn = new(StringComparer.Ordinal)
    {
        ["服务总数"]="Total services", ["运行中"]="Running", ["异常"]="Failed",
        ["新增服务"]="Add service", ["多个服务可同时运行，配置会自动保留。"]="Run multiple services at once. Configurations are saved automatically.",
        ["账号设置"]="Account settings", ["还没有发布服务"]="No services yet", ["添加本地网页、接口或远程连接，按需独立启动。"]="Add a local website, API, or remote connection and start each independently.",
        ["本地地址"]="Local address", ["启动"]="Start", ["编辑"]="Edit", ["日志"]="Logs", ["删除"]="Delete",
        ["服务配置"]="Service configuration", ["服务名称"]="Service name", ["例如 前端预览"]="For example: Frontend preview", ["数据库 / TCP"]="Database / TCP",
        ["临时地址每次启动会重新生成；停止后失效。"]="A new temporary URL is generated on each start and expires when stopped.",
        ["请先在设置中登录 Cloudflare。每个服务使用独立的隧道和公网域名。"]="Sign in to Cloudflare in Settings first. Each service has its own tunnel and hostname.",
        ["Cloudflare Account ID（可留空）"]="Cloudflare Account ID (optional)", ["启动时输入 API Token，仅在当前操作内存中使用。"]="Enter an API token when starting. It is kept only in memory for that operation.",
        ["取消"]="Cancel", ["保存"]="Save", ["保存并启动"]="Save and start",
        ["账号登录与状态请在设置中管理"]="Manage your account and sign-in status in Settings", ["前往设置"]="Open settings",
        ["登录 Cloudflare"]="Sign in to Cloudflare", ["重新授权"]="Reauthorize", ["刷新状态"]="Refresh status", ["取消授权"]="Cancel authorization", ["授权日志"]="Authorization log",
        ["首页"]="Home", ["发布服务"]="Publish", ["连接服务"]="Connect", ["设置"]="Settings",
        ["简单 · 安全 · 连接世界"]="Simple · Secure · Connected", ["Cloudflare 就绪"]="Cloudflare ready",
        ["让本地服务安全上线"]="Bring local services online safely", ["无需命令行，几步即可发布或连接服务"]="Publish or connect in a few steps — no command line required",
        ["发布本地服务"]="Publish a local service", ["将这台电脑上的服务分享出去"]="Share a service running on this computer",
        ["连接分享服务"]="Connect to a shared service", ["使用分享码或 .ptlink 文件连接"]="Connect with a share code or .ptlink file",
        ["最近使用"]="Recent", ["暂无最近使用的服务"]="No recently used services",
        ["告诉我们服务在哪里，其余工作交给 Platform Tools。"]="Tell us where the service is; Platform Tools handles the rest.",
        ["服务类型"]="Service type", ["HTTP / HTTPS（网页或 API）"]="HTTP / HTTPS (web or API)", ["远程桌面 RDP"]="Remote Desktop (RDP)", ["数据库"]="Database", ["SMB 文件共享"]="SMB file sharing", ["自定义 TCP"]="Custom TCP",
        ["本地服务地址"]="Local service address", ["只填写端口时，会自动使用 http://localhost。"]="Enter only a port to use http://localhost automatically.",
        ["发布方式"]="Publishing mode", ["临时地址（无需登录）"]="Temporary URL (no sign-in)", ["使用自己的域名"]="Use my own domain",
        ["临时地址适合开发和演示：地址随机生成，停止后失效，最多支持 200 个并发请求。"]="Temporary URLs are for development and demos. They expire when stopped and support up to 200 concurrent requests.",
        ["Cloudflare 账户"]="Cloudflare account", ["首次使用需要在浏览器中授权"]="Authorize in your browser the first time", ["登录 / 重新授权"]="Sign in / Reauthorize",
        ["隧道名称"]="Tunnel name", ["完整公网域名"]="Full public hostname", ["访问权限"]="Access", ["公开访问"]="Public", ["身份保护"]="Protected",
        ["建议连接端口"]="Suggested local port", ["Cloudflare Access 身份保护"]="Cloudflare Access protection", ["API Token 需要 Access: Apps and Policies Write 权限。"]="The API token needs Access: Apps and Policies Write permission.",
        ["开始发布"]="Publish", ["停止"]="Stop", ["发布成功"]="Published", ["复制地址"]="Copy address", ["浏览器打开"]="Open in browser", ["复制分享码"]="Copy share code", ["保存 .ptlink"]="Save .ptlink", ["运行日志"]="Runtime log",
        ["粘贴分享码或导入 .ptlink 文件，无需手动输入 cloudflared 命令。"]="Paste a share code or import a .ptlink file — no cloudflared commands needed.", ["分享码"]="Share code", ["解析分享码"]="Parse share code", ["导入 .ptlink 文件"]="Import .ptlink file", ["本地连接端口"]="Local connection port", ["本机应用将连接这个端口"]="Your local app connects to this port", ["开始连接"]="Connect", ["断开"]="Disconnect",
        ["语言、主题、cloudflared 更新和安全存储选项。"]="Language, theme, cloudflared updates, and secure storage.", ["界面语言"]="Language", ["默认跟随系统语言"]="Follows the system by default", ["跟随系统"]="System default", ["简体中文"]="Simplified Chinese", ["自动检查 cloudflared 更新"]="Automatically check for cloudflared updates", ["只从 Cloudflare 官方渠道获取"]="Only download from official Cloudflare sources",
        ["例如 8080 或 http://localhost:8080"]="For example: 8080 or http://localhost:8080", ["例如 platform-tools"]="For example: platform-tools", ["例如 demo.example.com"]="For example: demo.example.com", ["API Token（仅在内存中使用，不会保存）"]="API Token (memory only; never saved)", ["允许的邮箱，多个邮箱用逗号分隔"]="Allowed emails, separated by commas", ["在这里粘贴分享码"]="Paste a share code here", ["复制连接命令"]="Copy connection command",
        ["cloudflared 版本"]="cloudflared version", ["正在读取…"]="Reading…", ["检查更新"]="Check for updates", ["凭据安全"]="Credential security", ["API Token 仅在当前操作的内存中使用，操作后立即清空；不会写入配置或分享文件。账户凭据、应用设置和连接记录保存在程序目录下的 config 文件夹。"]="API tokens are held in memory only and cleared after each operation. They are never written to settings or share files. Account credentials, app settings, and recent connections are stored in the config folder beside the application."
    };
    // Several controls intentionally share the same English caption (for example the
    // navigation item and action button both use "Publish"). Keep the first Chinese
    // caption for reverse lookup instead of throwing during type initialization.
    private static readonly Dictionary<string, string> EnToZh = ZhToEn
        .GroupBy(x => x.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
    public static bool IsEnglish { get; private set; }
    public static string T(string chinese, string english) => IsEnglish ? english : chinese;

    public static void SetLanguage(bool english, params Control[] roots)
    {
        IsEnglish = english;
        foreach (var root in roots) Apply(root);
    }

    public static void Apply(Control root)
    {
        var map = IsEnglish ? ZhToEn : EnToZh;
        foreach (var control in new[] { root }.Concat(root.GetVisualDescendants().OfType<Control>()))
        {
            if (control is PlatformTools.App.Controls.ActionCard card)
            {
                if (map.TryGetValue(card.Heading, out var heading)) card.Heading = heading;
                if (map.TryGetValue(card.Description, out var description)) card.Description = description;
            }
            if (control is TextBlock text && text.Text is { } t && map.TryGetValue(t, out var translated)) text.Text = translated;
            if (control is ContentControl content && content.Content is string c && map.TryGetValue(c, out var translatedContent)) content.Content = translatedContent;
            if (control is TextBox box && box.PlaceholderText is { } p && map.TryGetValue(p, out var translatedPlaceholder)) box.PlaceholderText = translatedPlaceholder;
        }
    }
}
