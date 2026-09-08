using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;
using PlatformTools.App.Services;

namespace PlatformTools.App.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? PublishRequested;
    public event EventHandler? ConnectRequested;
    public HomeView() { InitializeComponent(); RefreshRecents(); AppSettingsService.Current.Changed += (_, _) => RefreshRecents(); }
    private void Publish_Click(object? sender, RoutedEventArgs e) => PublishRequested?.Invoke(this, EventArgs.Empty);
    private void Connect_Click(object? sender, RoutedEventArgs e) => ConnectRequested?.Invoke(this, EventArgs.Empty);
    public void RefreshRecents()
    {
        var items = AppSettingsService.Current.Settings.RecentServices.ToArray(); RecentItems.ItemsSource = items;
        RecentItems.IsVisible = items.Length > 0; EmptyState.IsVisible = items.Length == 0;
    }
}
