using System;
using System.Collections.Generic;

namespace PlatformTools.App.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public bool AutoUpdateCloudflared { get; set; } = true;
    public List<RecentService> RecentServices { get; set; } = [];
}

public sealed record RecentService(string Name, string Protocol, string Address, DateTimeOffset LastUsedUtc)
{
    public string LastUsedDisplay => LastUsedUtc.ToLocalTime().ToString("g");
}
