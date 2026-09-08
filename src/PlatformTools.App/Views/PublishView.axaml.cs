using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using PlatformTools.App.Controls;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.App.Views;

public partial class PublishView : UserControl
{
    private readonly PublishedServiceManager _manager = PublishedServiceManager.Current;
    public event EventHandler? SettingsRequested;
    public PublishView()
    {
        InitializeComponent();
        MessageText.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty) MessageText.IsVisible = !string.IsNullOrWhiteSpace(MessageText.Text);
        };
        ServiceItems.ItemsSource = _manager.Items;
        _manager.Changed += (_, _) => RefreshSummary();
        AttachedToVisualTree += (_, _) => { _manager.RefreshLabels(); RefreshSummary(); };
        RefreshSummary();
    }
    private void RefreshSummary()
    {
        SummaryText.Text = _manager.Summary;
        TotalCountText.Text = _manager.Items.Count.ToString();
        RunningCountText.Text = _manager.RunningCount.ToString();
        FailedCountText.Text = _manager.FailedCount.ToString();
        EmptyState.IsVisible = _manager.Items.Count == 0 && _manager.LoadError is null;
        AddButton.IsEnabled = _manager.LoadError is null;
        if (_manager.LoadError is not null) MessageText.Text = _manager.LoadError;
    }
    private void AccountSettings_Click(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var result = await ServiceDialogs.EditAsync(owner, _manager);
        if (result?.Start == true) await StartAsync(result.Service);
    }
    private async Task StartAsync(PublishedService item)
    {
        string? token = null;
        try
        {
            if (item.Definition.ProtectedAccess)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                token = await ServiceDialogs.RequestTokenAsync(owner);
                if (token is null) return;
            }
            await _manager.StartAsync(item, token);
            if (item.State == PublishedServiceState.Running)
                AppSettingsService.Current.AddRecent(new RecentService(item.Name, item.Definition.Protocol.ToUpperInvariant(), item.PublicAddress, DateTimeOffset.UtcNow));
        }
        catch (Exception ex) { MessageText.Text = ex.Message; }
        finally { token = null; }
    }
    private async void ServiceAction_Requested(object? sender, ServiceActionEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var item = e.Service;
        MessageText.Text = "";
        try
        {
            switch (e.Action)
            {
                case "Start": await StartAsync(item); break;
                case "Stop": await _manager.StopAsync(item); break;
                case "Edit":
                    var result = await ServiceDialogs.EditAsync(owner, _manager, item);
                    if (result?.Start == true) await StartAsync(result.Service);
                    break;
                case "Copy":
                    if (owner.Clipboard is { } clipboard) { await clipboard.SetTextAsync(item.PublicAddress); MessageText.Text = LocalizationService.T("地址已复制。", "Address copied."); }
                    break;
                case "Share":
                    if (owner.Clipboard is { } shareClipboard) { await shareClipboard.SetTextAsync(item.CreateShareProfile().ToShareCode()); MessageText.Text = LocalizationService.T("分享码已复制。", "Share code copied."); }
                    break;
                case "Export": await ExportAsync(owner, item); break;
                case "Log": await ServiceDialogs.ShowLogAsync(owner, item); break;
                case "Delete":
                    var message = item.Definition.IsQuick
                        ? LocalizationService.T($"删除“{item.Name}”？将停止服务并移除配置，临时地址会失效。", $"Delete '{item.Name}'? The service will stop, its temporary URL will expire, and its configuration will be removed.")
                        : LocalizationService.T($"删除“{item.Name}”？将停止服务，删除隧道 {item.Definition.TunnelName}、{item.Definition.Hostname} 指向此隧道的 DNS 记录，以及本地隧道凭据和配置。云端清理失败会保留条目供重试。", $"Delete '{item.Name}'? This stops the service and removes tunnel '{item.Definition.TunnelName}', the DNS record for '{item.Definition.Hostname}' pointing to this tunnel, and local tunnel credentials and configuration. Failed cleanup keeps the entry for retry.");
                    if (await ServiceDialogs.ConfirmAsync(owner, LocalizationService.T("删除服务", "Delete service"), message, LocalizationService.T("删除", "Delete")))
                    {
                        try { await _manager.DeleteAsync(item); }
                        catch (CleanupAuthorizationException)
                        {
                            var deletionToken = await ServiceDialogs.RequestTokenAsync(owner, forDeletion: true);
                            try { if (deletionToken is not null) await _manager.DeleteAsync(item, deletionToken); }
                            finally { deletionToken = null; }
                        }
                    }
                    break;
            }
        }
        catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private static async Task ExportAsync(Window owner, PublishedService item)
    {
        var profile = item.CreateShareProfile();
        var safeName = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = LocalizationService.T("保存分享文件", "Save share file"), SuggestedFileName = safeName + ".ptlink", DefaultExtension = "ptlink", FileTypeChoices = [new FilePickerFileType("Platform Tools") { Patterns = ["*.ptlink"] }] });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(profile.ToShareCode());
    }
    public Task StopAllAsync() => _manager.ShutdownAsync();
}
