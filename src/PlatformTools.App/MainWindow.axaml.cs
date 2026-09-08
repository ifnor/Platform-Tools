using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using PlatformTools.App.Views;
using PlatformTools.App.Services;

namespace PlatformTools.App;

public partial class MainWindow : Window
{
    private readonly HomeView _home = new();
    private readonly PublishView _publish = new();
    private readonly ConnectView _connect = new();
    private readonly SettingsView _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        PageHost.Content = _home;
        _home.PublishRequested += (_, _) => Navigate(_publish, PublishButton);
        _home.ConnectRequested += (_, _) => Navigate(_connect, ConnectButton);
        _settings.LanguageChanged += (_, english) => LocalizationService.SetLanguage(english, this, _home, _publish, _connect, _settings);
        var saved = AppSettingsService.Current.Settings;
        var useEnglish = saved.Language == "en" || (saved.Language == "system" && !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase));
        LocalizationService.SetLanguage(useEnglish, this, _home, _publish, _connect, _settings);
        if (Application.Current is { } app && saved.Theme != "system") app.RequestedThemeVariant = saved.Theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Closed += async (_, _) => { await _publish.StopAllAsync(); await _connect.StopAllAsync(); };
        Opened += async (_, _) =>
        {
            try
            {
                var version = await new CloudflaredManager().GetInstalledVersionAsync();
                CloudflareStatusText.Text = version == "未安装" ? LocalizationService.T("cloudflared 未安装", "cloudflared missing") : LocalizationService.T("Cloudflare 就绪", "Cloudflare ready");
            }
            catch { CloudflareStatusText.Text = LocalizationService.T("cloudflared 不可用", "cloudflared unavailable"); }
        };
    }

    private void Home_Click(object? sender, RoutedEventArgs e) => Navigate(_home, HomeButton);
    private void Publish_Click(object? sender, RoutedEventArgs e) => Navigate(_publish, PublishButton);
    private void Connect_Click(object? sender, RoutedEventArgs e) => Navigate(_connect, ConnectButton);
    private void Settings_Click(object? sender, RoutedEventArgs e) => Navigate(_settings, SettingsButton);

    private void Navigate(Control page, Button active)
    {
        PageHost.Content = page;
        LocalizationService.Apply(page);
        foreach (var button in new[] { HomeButton, PublishButton, ConnectButton, SettingsButton }) button.Classes.Remove("active");
        active.Classes.Add("active");
    }

    public async System.Threading.Tasks.Task OpenShareFileAsync(string path)
    {
        Navigate(_connect, ConnectButton);
        await _connect.LoadProfileFileAsync(path);
    }

    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
            AppSettingsService.Current.Settings.Theme = app.RequestedThemeVariant == ThemeVariant.Dark ? "dark" : "light";
            AppSettingsService.Current.Save();
        }
    }
}
