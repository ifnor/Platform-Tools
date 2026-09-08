using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatformTools.App.Services;

namespace PlatformTools.App.Views;

public partial class SettingsView : UserControl
{
    private readonly CloudflaredManager _manager = new();
    private bool _initializing = true;
    public event EventHandler<bool>? LanguageChanged;

    public SettingsView()
    {
        InitializeComponent();
        var settings = AppSettingsService.Current.Settings;
        LanguageBox.SelectedIndex = settings.Language switch { "zh-CN" => 1, "en" => 2, _ => 0 };
        AutoUpdateSwitch.IsChecked = settings.AutoUpdateCloudflared;
        _initializing = false;
        AttachedToVisualTree += async (_, _) => { if (VersionText.Text is "正在读取…" or "Reading…") await RefreshVersionAsync(); };
    }

    private void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageBox is null) return;
        var english = LanguageBox.SelectedIndex == 2 || (LanguageBox.SelectedIndex == 0 && !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        AppSettingsService.Current.Settings.Language = LanguageBox.SelectedIndex switch { 1 => "zh-CN", 2 => "en", _ => "system" };
        AppSettingsService.Current.Save();
        LanguageChanged?.Invoke(this, english);
    }

    private void AutoUpdate_Click(object? sender, RoutedEventArgs e)
    {
        AppSettingsService.Current.Settings.AutoUpdateCloudflared = AutoUpdateSwitch.IsChecked == true;
        AppSettingsService.Current.Save();
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            UpdateButton.IsEnabled = false; VersionText.Text = LocalizationService.T("正在检查最新版本…", "Checking for updates…");
            var latest = await _manager.GetLatestVersionAsync(); var installed = await _manager.GetInstalledVersionAsync();
            if (installed.Contains(latest, StringComparison.OrdinalIgnoreCase)) VersionText.Text = LocalizationService.T($"{installed}（已是最新版本）", $"{installed} (up to date)");
            else { VersionText.Text = LocalizationService.T($"发现 {latest}，正在更新…", $"Found {latest}; updating…"); await _manager.UpdateAsync(); VersionText.Text = await _manager.GetInstalledVersionAsync(); }
        }
        catch (Exception ex) { VersionText.Text = LocalizationService.T("更新失败：", "Update failed: ") + ex.Message; }
        finally { UpdateButton.IsEnabled = true; }
    }

    private async Task RefreshVersionAsync()
    {
        try
        {
            var installed = await _manager.GetInstalledVersionAsync(); VersionText.Text = installed;
            if (AutoUpdateSwitch.IsChecked == true)
            {
                var latest = await _manager.GetLatestVersionAsync();
                if (!installed.Contains(latest, StringComparison.OrdinalIgnoreCase)) VersionText.Text = LocalizationService.T($"{installed}（可更新到 {latest}）", $"{installed} (update available: {latest})");
            }
        }
        catch (Exception ex) { VersionText.Text = LocalizationService.T("版本检查失败：", "Version check failed: ") + ex.Message; }
    }
}
