using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatformTools.App.Models;
using PlatformTools.App.Services;

namespace PlatformTools.App.Controls;

public sealed class ServiceActionEventArgs(PublishedService service, string action) : EventArgs
{
    public PublishedService Service { get; } = service;
    public string Action { get; } = action;
}

public partial class PublishedServiceRow : UserControl
{
    public event EventHandler<ServiceActionEventArgs>? ActionRequested;
    public PublishedServiceRow()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => LocalizationService.Apply(this);
    }
    private void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PublishedService service && sender is Button { Tag: string action }) ActionRequested?.Invoke(this, new(service, action));
    }
}
