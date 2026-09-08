using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.App.Views;

public partial class PublishView : UserControl
{
    private readonly CloudflaredService _cloudflared = new();
    private readonly NamedTunnelService _namedTunnel = new();
    private readonly CloudflareAccessService _access = new();
    private string? _publicUrl;
    private ShareProfile? _shareProfile;

    public PublishView()
    {
        InitializeComponent();
        _cloudflared.LogReceived += (_, line) => Dispatcher.UIThread.Post(() => LogBox.Text += line + Environment.NewLine);
        _cloudflared.PublicUrlReceived += (_, url) => Dispatcher.UIThread.Post(() => { ShowPublicUrl(url); AppSettingsService.Current.AddRecent(new RecentService("Quick Tunnel", "HTTPS", url, DateTimeOffset.UtcNow)); });
        _namedTunnel.LogReceived += (_, line) => Dispatcher.UIThread.Post(() => LogBox.Text += line + Environment.NewLine);
    }

    private async void Start_Click(object? sender, RoutedEventArgs e)
    {
        var protocol = GetProtocol();
        var localUrl = ServiceAddress.Normalize(LocalUrlBox.Text, protocol);
        if (localUrl is null) { AppendLog("请输入有效端口或 HTTP/HTTPS 地址。"); return; }
        try
        {
            StartButton.IsEnabled = false; StopButton.IsEnabled = true; ResultCard.IsVisible = false; LogBox.Text = string.Empty;
            CopyShareButton.IsVisible = false; SaveShareButton.IsVisible = false; OpenBrowserButton.IsVisible = protocol is "http" or "https";
            AppendLog($"正在发布 {localUrl} …");
            if (QuickMode.IsChecked == true)
            {
                if (protocol is not ("http" or "https")) throw new InvalidOperationException("临时地址仅支持 HTTP/HTTPS；其他协议请选择自己的域名。");
                await _cloudflared.StartQuickTunnelAsync(localUrl);
            }
            else
            {
                var name = TunnelNameBox.Text?.Trim(); var hostname = HostnameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请输入隧道名称。");
                if (!IsHostname(hostname)) throw new InvalidOperationException("请输入有效的完整公网域名，例如 demo.example.com。");
                var protectedAccess = AccessModeBox.SelectedIndex == 1;
                if (protectedAccess && string.IsNullOrWhiteSpace(ApiTokenBox.Text))
                    throw new InvalidOperationException("身份保护需要填写 API Token。");
                var result = await _namedTunnel.CreateAndStartAsync(name, hostname!, localUrl);
                if (protectedAccess)
                {
                    var emails = (AllowedEmailsBox.Text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    AppendLog("正在创建 Cloudflare Access 身份保护策略 …");
                    var accountId = string.IsNullOrWhiteSpace(AccountIdBox.Text) ? result.AccountId : AccountIdBox.Text.Trim();
                    AccountIdBox.Text = accountId;
                    await _access.ConfigureEmailProtectionAsync(accountId, ApiTokenBox.Text!, name, hostname!, emails);
                    ApiTokenBox.Text = string.Empty;
                    AppendLog("身份保护策略已启用。");
                }
                var shareProtocol = protocol is "http" or "https" ? "https" : protocol;
                _shareProfile = new ShareProfile(1, name, shareProtocol, shareProtocol == "https" ? $"https://{hostname}" : hostname!, (int)(SuggestedPortBox.Value ?? DefaultPort(protocol)), protectedAccess);
                ShowPublicUrl(_shareProfile.Hostname);
                AppSettingsService.Current.AddRecent(new RecentService(name, shareProtocol.ToUpperInvariant(), _shareProfile.Hostname, DateTimeOffset.UtcNow));
                CopyShareButton.IsVisible = true; SaveShareButton.IsVisible = true;
                AppendLog($"命名隧道 {result.TunnelId} 已启动。");
            }
        }
        catch (Exception ex) { ApiTokenBox.Text = string.Empty; await _cloudflared.StopAsync(); await _namedTunnel.StopAsync(); AppendLog("启动失败：" + ex.Message); StartButton.IsEnabled = true; StopButton.IsEnabled = false; }
    }

    private async void Stop_Click(object? sender, RoutedEventArgs e)
    {
        await _cloudflared.StopAsync(); await _namedTunnel.StopAsync(); StartButton.IsEnabled = true; StopButton.IsEnabled = false; ResultCard.IsVisible = false; AppendLog("隧道已停止。");
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (_publicUrl is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(_publicUrl);
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (_publicUrl is not null) Process.Start(new ProcessStartInfo(_publicUrl) { UseShellExecute = true });
    }

    public event EventHandler? SettingsRequested;
    private void AccountSettings_Click(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Mode_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateModeVisibility();
    }

    private void Protocol_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (LocalUrlBox is null || QuickMode is null || NamedMode is null || SuggestedPortBox is null) return;
        var (address, port) = ProtocolBox.SelectedIndex switch
        {
            1 => ("ssh://localhost:22", 22), 2 => ("rdp://localhost:3389", 3389),
            3 => ("tcp://localhost:3306", 3306), 4 => ("smb://localhost:445", 445),
            5 => ("tcp://localhost:9000", 9000), _ => ("http://localhost:8080", 8080)
        };
        LocalUrlBox.Text = address; SuggestedPortBox.Value = port;
        var http = ProtocolBox.SelectedIndex == 0; QuickMode.IsEnabled = http;
        if (!http) NamedMode.IsChecked = true;
        UpdateModeVisibility();
    }

    private void UpdateModeVisibility()
    {
        if (NamedOptions is null || QuickHint is null) return;
        var named = NamedMode?.IsChecked == true; NamedOptions.IsVisible = named; QuickHint.IsVisible = !named;
    }

    private void AccessMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (AccessOptions is not null) AccessOptions.IsVisible = AccessModeBox?.SelectedIndex == 1;
    }

    private async void CopyShare_Click(object? sender, RoutedEventArgs e)
    {
        if (_shareProfile is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(_shareProfile.ToShareCode());
    }

    private async void SaveShare_Click(object? sender, RoutedEventArgs e)
    {
        if (_shareProfile is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存分享文件", SuggestedFileName = _shareProfile.Name + ".ptlink", DefaultExtension = "ptlink", FileTypeChoices = [new FilePickerFileType("Platform Tools 分享文件") { Patterns = ["*.ptlink"] }] });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync(); await using var writer = new System.IO.StreamWriter(stream); await writer.WriteAsync(_shareProfile.ToShareCode());
    }

    private void ShowPublicUrl(string url) { _publicUrl = url; PublicUrlText.Text = url; ResultCard.IsVisible = true; }
    private void AppendLog(string text) => LogBox.Text += text + Environment.NewLine;

    private string GetProtocol() => ProtocolBox.SelectedIndex switch { 1 => "ssh", 2 => "rdp", 3 => "tcp", 4 => "smb", 5 => "tcp", _ => LocalUrlBox.Text?.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true ? "https" : "http" };
    private static int DefaultPort(string protocol) => protocol switch { "ssh" => 22, "rdp" => 3389, "smb" => 445, "https" => 443, _ => 8080 };

    private static bool IsHostname(string? value) => !string.IsNullOrWhiteSpace(value) && Uri.CheckHostName(value) == UriHostNameType.Dns && value.Contains('.');
    public async Task StopAllAsync() { await _cloudflared.StopAsync(); await _namedTunnel.StopAsync(); }
}
