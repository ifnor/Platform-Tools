using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.App.Views;

public partial class ConnectView : UserControl
{
    private readonly ClientConnectionService _connection = new();
    private ShareProfile? _profile;
    public ConnectView() { InitializeComponent(); _connection.LogReceived += (_, line) => Dispatcher.UIThread.Post(() => StatusText.Text = line); }
    private void Parse_Click(object? sender, RoutedEventArgs e) => ParseProfile(ShareCodeBox.Text);

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 Platform Tools 分享文件", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Platform Tools 分享文件") { Patterns = ["*.ptlink"] }] });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(); ShareCodeBox.Text = content; ParseProfile(content);
    }

    public async Task LoadProfileFileAsync(string path)
    {
        try { var content = await File.ReadAllTextAsync(path); ShareCodeBox.Text = content; ParseProfile(content); }
        catch (Exception ex) { StatusText.Text = "无法打开分享文件：" + ex.Message; }
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        try
        {
            var port = (int)(LocalPortBox.Value ?? _profile.SuggestedLocalPort); await _connection.ConnectAsync(_profile, port);
            var opensBrowser = _profile.Protocol is "http" or "https";
            ConnectButton.IsEnabled = opensBrowser; DisconnectButton.IsEnabled = !opensBrowser;
            StatusText.Text = opensBrowser ? "已在浏览器中打开服务。" : "连接已启动。本地应用现在可以连接到所示端口。"; UpdateHint(port);
            AppSettingsService.Current.AddRecent(new RecentService(_profile.Name, _profile.Protocol.ToUpperInvariant(), _profile.Hostname, DateTimeOffset.UtcNow));
            if (_profile.Protocol == "rdp" && OperatingSystem.IsWindows()) { var info = new System.Diagnostics.ProcessStartInfo("mstsc.exe") { UseShellExecute = true }; info.ArgumentList.Add($"/v:localhost:{port}"); System.Diagnostics.Process.Start(info); }
        }
        catch (Exception ex) { StatusText.Text = "连接失败：" + ex.Message; }
    }

    private async void Disconnect_Click(object? sender, RoutedEventArgs e) { await _connection.StopAsync(); ConnectButton.IsEnabled = true; DisconnectButton.IsEnabled = false; StatusText.Text = "连接已断开。"; }
    private async void CopyCommand_Click(object? sender, RoutedEventArgs e) { if (_profile is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(GetConnectionHint((int)(LocalPortBox.Value ?? _profile.SuggestedLocalPort))); }

    private void ParseProfile(string? value)
    {
        try { _profile = ShareProfile.Parse(value ?? ""); ProfileNameText.Text = _profile.Name; ProfileDetailsText.Text = $"{_profile.Protocol.ToUpperInvariant()} · {_profile.Hostname}"; LocalPortBox.Value = _profile.SuggestedLocalPort; UpdateHint(_profile.SuggestedLocalPort); ProfileCard.IsVisible = true; StatusText.Text = _profile.RequiresAccess ? "连接时将打开浏览器进行 Cloudflare 身份验证。" : "分享资料有效，可以连接。"; }
        catch (Exception ex) { _profile = null; ProfileCard.IsVisible = false; StatusText.Text = "无法识别分享码：" + ex.Message; }
    }

    private void UpdateHint(int port) => ConnectionHintText.Text = GetConnectionHint(port);
    private string GetConnectionHint(int port) => _profile?.Protocol switch { "ssh" => $"ssh -p {port} 用户名@localhost", "rdp" => $"localhost:{port}", "smb" => $"SMB 本地代理：localhost:{port}", "tcp" => $"localhost:{port}", _ => _profile?.Hostname ?? string.Empty };
    public Task StopAllAsync() => _connection.StopAsync();
}
