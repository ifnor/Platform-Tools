using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatformTools.App.Services;

namespace PlatformTools.App.Controls;

public partial class AppUpdateCard : UserControl
{
    private readonly AppUpdateService _service = new();
    private AppRelease? _release;
    private CancellationTokenSource? _operation;
    private bool _checked;
    public AppUpdateCard()
    {
        InitializeComponent();
        InstalledText.Text = "Platform Tools " + AppUpdateService.CurrentVersion + " · " + AppUpdateService.RuntimeId;
        if (!OperatingSystem.IsWindows()) InstallButton.Content = "下载安装包";
        var result = Path.Combine(AppPaths.Current.ConfigDirectory, "update-result.txt");
        if (File.Exists(result))
        {
            try { UpdateStatus.Text = File.ReadAllText(result); _checked = true; File.Delete(result); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        AttachedToVisualTree += async (_, _) => { if (!_checked) { _checked = true; await CheckAsync(); } };
        DetachedFromVisualTree += (_, _) => { _operation?.Cancel(); TokenBox.Text = ""; };
    }
    private async void Check_Click(object? sender, RoutedEventArgs e) => await CheckAsync();
    private async Task CheckAsync()
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); _operation = operation;
        Busy(true); _release = null; InstallButton.IsVisible = false; ReleaseNotes.IsVisible = false;
        UpdateStatus.Text = LocalizationService.T("正在检查软件版本…", "Checking application version…");
        try
        {
            _release = await _service.CheckAsync(TokenBox.Text, operation.Token);
            UpdateStatus.Text = _release is null ? LocalizationService.T("当前已是最新正式版。", "You have the latest stable version.") : LocalizationService.T($"发现 {_release.Tag} · {_release.Size / 1024d / 1024d:F1} MB", $"Available: {_release.Tag} · {_release.Size / 1024d / 1024d:F1} MB");
            InstallButton.IsVisible = _release is not null;
            NotesText.Text = _release?.Notes; ReleaseNotes.IsVisible = !string.IsNullOrWhiteSpace(_release?.Notes);
        }
        catch (OperationCanceledException) { UpdateStatus.Text = "检查已取消或超时。"; }
        catch (Exception ex) { UpdateStatus.Text = ex.Message; }
        finally { _operation = null; Busy(false); }
    }
    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (_operation is not null || _release is null || TopLevel.GetTopLevel(this) is not MainWindow owner) return;
        if (!await ServiceDialogs.ConfirmAsync(owner, "软件更新", OperatingSystem.IsWindows()
            ? $"下载并安装 {_release.Tag}？校验通过后将停止当前发布和连接服务，关闭软件并重启。config 中的配置与凭据会保留。"
            : $"下载 {_release.Tag} 的安装包？校验通过后会打开下载目录，请退出软件后安装。", "继续")) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(15)); _operation = operation;
        Busy(true); DownloadProgress.IsVisible = true; DownloadProgress.Value = 0;
        UpdateStatus.Text = "正在下载并校验更新包…";
        try
        {
            var download = await _service.DownloadAsync(_release, TokenBox.Text, new Progress<double>(value => DownloadProgress.Value = value), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            TokenBox.Text = "";
            if (OperatingSystem.IsWindows())
            {
                var plan = AppUpdateInstaller.Prepare(download);
                UpdateStatus.Text = "正在停止服务并重启更新…";
                await owner.InstallApplicationUpdateAsync(plan);
            }
            else
            {
                UpdateStatus.Text = "校验通过。安装包已保存到：" + download.FilePath;
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(download.FilePath)!) { UseShellExecute = true });
            }
        }
        catch (OperationCanceledException) { UpdateStatus.Text = "下载已取消或超时，当前版本未更改。"; }
        catch (Exception ex) { UpdateStatus.Text = "更新失败：" + ex.Message; }
        finally { TokenBox.Text = ""; _operation = null; Busy(false); DownloadProgress.IsVisible = false; }
    }
    private void Busy(bool busy) { CheckButton.IsEnabled = !busy; InstallButton.IsEnabled = !busy; CancelButton.IsVisible = busy; TokenBox.IsEnabled = !busy; }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => _operation?.Cancel();
    private void OpenRelease_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppUpdateService.ReleasesUrl) { UseShellExecute = true }); }
        catch (Exception ex) { UpdateStatus.Text = ex.Message; }
    }
}
