using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatformTools.App.Models;

namespace PlatformTools.App.Controls;

public partial class ServiceEditor : UserControl
{
    private PublishedServiceDefinition _definition = new();
    private bool _loading;
    public event EventHandler<bool>? SaveRequested;
    public event EventHandler? CancelRequested;
    public ServiceEditor() { InitializeComponent(); }
    public void Load(PublishedServiceDefinition? definition)
    {
        _loading = true;
        _definition = definition ?? new PublishedServiceDefinition();
        NameBox.Text = _definition.Name;
        ProtocolBox.SelectedIndex = _definition.Protocol switch { "ssh" => 1, "rdp" => 2, "tcp" => 3, "smb" => 4, _ => 0 };
        LocalUrlBox.Text = _definition.LocalAddress;
        QuickMode.IsChecked = _definition.IsQuick; NamedMode.IsChecked = !_definition.IsQuick;
        TunnelNameBox.Text = string.IsNullOrWhiteSpace(_definition.TunnelName) ? "pt-" + _definition.Id.ToString("N") : _definition.TunnelName;
        HostnameBox.Text = _definition.Hostname;
        AccessModeBox.SelectedIndex = _definition.ProtectedAccess ? 1 : 0;
        AccountIdBox.Text = _definition.AccountId;
        AllowedEmailsBox.Text = string.Join(", ", _definition.AllowedEmails);
        SuggestedPortBox.Value = _definition.SuggestedPort;
        _loading = false;
        UpdateVisibility();
    }
    public PublishedServiceDefinition Read() => _definition with
    {
        Name = NameBox.Text ?? "", LocalAddress = LocalUrlBox.Text ?? "",
        Protocol = ProtocolBox.SelectedIndex switch { 1 => "ssh", 2 => "rdp", 3 => "tcp", 4 => "smb", _ => LocalUrlBox.Text?.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true ? "https" : "http" },
        IsQuick = QuickMode.IsChecked == true, TunnelName = TunnelNameBox.Text ?? "", Hostname = HostnameBox.Text ?? "",
        ProtectedAccess = AccessModeBox.SelectedIndex == 1, AccountId = AccountIdBox.Text ?? "",
        AllowedEmails = (AllowedEmailsBox.Text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        SuggestedPort = (int)(SuggestedPortBox.Value ?? 8080)
    };
    public void ShowError(string message) => ErrorText.Text = message;
    private void Save_Click(object? sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, false);
    private void SaveStart_Click(object? sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
    private void Mode_Changed(object? sender, RoutedEventArgs e) => UpdateVisibility();
    private void AccessMode_Changed(object? sender, SelectionChangedEventArgs e) => UpdateVisibility();
    private void Protocol_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || LocalUrlBox is null || SuggestedPortBox is null) return;
        var (address, port) = ProtocolBox.SelectedIndex switch { 1 => ("ssh://localhost:22", 22), 2 => ("rdp://localhost:3389", 3389), 3 => ("tcp://localhost:3306", 3306), 4 => ("smb://localhost:445", 445), _ => ("http://localhost:8080", 8080) };
        LocalUrlBox.Text = address; SuggestedPortBox.Value = port;
        if (ProtocolBox.SelectedIndex > 0) NamedMode.IsChecked = true;
        UpdateVisibility();
    }
    private void UpdateVisibility()
    {
        if (_loading || NamedOptions is null || AccessOptions is null || QuickMode is null) return;
        QuickMode.IsEnabled = ProtocolBox.SelectedIndex == 0;
        NamedOptions.IsVisible = NamedMode.IsChecked == true;
        QuickHint.IsVisible = !NamedOptions.IsVisible;
        AccessOptions.IsVisible = AccessModeBox.SelectedIndex == 1;
    }
}
