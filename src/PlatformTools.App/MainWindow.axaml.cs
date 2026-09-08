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
        _ = new Controls.AdaptiveWindowLayout(this, AdaptiveContent);
        PageHost.Content = _home;
        _home.PublishRequested += (_, _) => Navigate(_publish, PublishButton);
        _home.ConnectRequested += (_, _) => Navigate(_connect, ConnectButton);
        _publish.SettingsRequested += (_, _) => Navigate(_settings, SettingsButton);
        _settings.LanguageChanged += (_, english) => { LocalizationService.SetLanguage(english, this, _home, _publish, _connect, _settings); RenderAccountState(); };
        var saved = AppSettingsService.Current.Settings;
        var useEnglish = saved.Language == "en" || (saved.Language == "system" && !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase));
        LocalizationService.SetLanguage(useEnglish, this, _home, _publish, _connect, _settings);
        if (Application.Current is { } app && saved.Theme != "system") app.RequestedThemeVariant = saved.Theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        CloudflareAccountService.Current.Changed += AccountChanged;
        RenderAccountState();
        Closed += async (_, _) =>
        {
            CloudflareAccountService.Current.Changed -= AccountChanged;
            CloudflareAccountService.Current.Cancel();
            await _publish.StopAllAsync(); await _connect.StopAllAsync();
        };
        Opened += async (_, _) => await CloudflareAccountService.Current.RefreshAsync();
    }

    private void AccountChanged(object? sender, System.EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(RenderAccountState);
    private void RenderAccountState()
    {
        var account = CloudflareAccountService.Current;
        CloudflareStatusText.Text = "Cloudflare · " + account.StatusText;
        AccountStatusDot.Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(account.StatusColor));
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
