using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

public sealed class AppSettingsService
{
    public static AppSettingsService Current { get; } = new();
    private readonly string _path;
    public AppSettings Settings { get; private set; }
    public event EventHandler? Changed;

    private AppSettingsService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatformTools");
        Directory.CreateDirectory(directory); _path = Path.Combine(directory, "settings.json"); Settings = Load();
    }

    public void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _path, overwrite: true); Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddRecent(RecentService service)
    {
        Settings.RecentServices.RemoveAll(x => string.Equals(x.Protocol, service.Protocol, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Address, service.Address, StringComparison.OrdinalIgnoreCase));
        Settings.RecentServices.Insert(0, service);
        if (Settings.RecentServices.Count > 8) Settings.RecentServices.RemoveRange(8, Settings.RecentServices.Count - 8);
        Save();
    }

    private AppSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings() : new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
    }
}
