using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.App.Controls;

internal static class ServiceDialogs
{
    internal sealed record EditResult(PublishedService Service, bool Start);

    public static Window Create(Window owner, string title, Control content, double width = 600, double height = 400)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        var layout = screen is null ? new WindowLayout(width, height, width, height, 300, 200) : WindowLayoutPolicy.ForScreen(screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling);
        var dialog = new Window
        {
            Title = title, Content = content, Width = Math.Min(width, layout.MaxWidth), Height = Math.Min(height, layout.MaxHeight),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = owner.FontFamily, Icon = owner.Icon,
            Background = owner.Background, MinWidth = Math.Min(360, layout.MaxWidth), MinHeight = Math.Min(240, layout.MaxHeight)
        };
        dialog.Opened += (_, _) => LocalizationService.Apply(dialog);
        return dialog;
    }

    public static async Task<EditResult?> EditAsync(Window owner, PublishedServiceManager manager, PublishedService? service = null)
    {
        var editor = new ServiceEditor(); editor.Load(service?.Definition);
        var dialog = Create(owner, LocalizationService.T("服务配置", "Service configuration"), editor, 640, 620);
        editor.CancelRequested += (_, _) => dialog.Close();
        editor.SaveRequested += (_, start) =>
        {
            try { dialog.Close(new EditResult(manager.Save(editor.Read()), start)); }
            catch (Exception ex) { editor.ShowError(ex.Message); }
        };
        return await dialog.ShowDialog<EditResult?>(owner);
    }

    public static Task<bool> ConfirmAsync(Window owner, string title, string message, string accept)
    {
        var content = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = LocalizationService.T("取消", "Cancel") };
        var confirm = new Button { Content = accept };
        cancel.Classes.Add("quiet"); confirm.Classes.Add("primary");
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); Grid.SetRow(buttons, 1); content.Children.Add(buttons);
        var dialog = Create(owner, title, content, 540, 320);
        cancel.Click += (_, _) => dialog.Close(false); confirm.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(owner);
    }

    public static async Task<string?> RequestTokenAsync(Window owner, bool forDeletion = false)
    {
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        content.Children.Add(new TextBlock { Text = forDeletion
            ? LocalizationService.T("登录凭据没有清理权限。请输入具有 Cloudflare Tunnel 编辑及此域名 DNS 编辑、Zone 读取权限的 API Token，仅用于本次删除，不会保存。", "Enter an API token with Cloudflare Tunnel Edit and DNS Edit and Zone Read permissions for this zone. It is used only for this deletion and will not be saved.")
            : LocalizationService.T("请输入 Access API Token，仅用于本次启动，不会保存。", "Enter an Access API token for this start. It will not be saved."), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { PasswordChar = '●', PlaceholderText = "API Token" };
        content.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = LocalizationService.T("取消", "Cancel") };
        var start = new Button { Content = forDeletion ? LocalizationService.T("继续删除", "Continue deletion") : LocalizationService.T("启动", "Start") };
        cancel.Classes.Add("quiet"); start.Classes.Add("primary");
        buttons.Children.Add(cancel); buttons.Children.Add(start); content.Children.Add(buttons);
        var dialog = Create(owner, forDeletion ? "Cloudflare · " + LocalizationService.T("删除服务", "Delete service") : "Cloudflare Access", content, 520, forDeletion ? 310 : 270);
        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) { var token = input.Text.Trim(); input.Text = ""; dialog.Close(token); } };
        try { return await dialog.ShowDialog<string?>(owner); }
        finally { input.Text = ""; }
    }

    public static async Task ShowLogAsync(Window owner, PublishedService service)
    {
        var text = new TextBox { Text = service.Log, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20), FontSize = 12 };
        var dialog = Create(owner, service.Name + " · " + LocalizationService.T("运行日志", "Runtime log"), text, 760, 520);
        void Changed(object? sender, PropertyChangedEventArgs e) { text.Text = service.Log; }
        service.PropertyChanged += Changed;
        try { await dialog.ShowDialog(owner); }
        finally { service.PropertyChanged -= Changed; }
    }
}
