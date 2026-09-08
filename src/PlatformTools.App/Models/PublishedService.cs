using System;
using System.ComponentModel;
using System.Threading;
using PlatformTools.App.Services;

namespace PlatformTools.App.Models;

public enum PublishedServiceState { Stopped, Starting, Running, Stopping, Failed, Deleting }

public sealed class PublishedService(PublishedServiceDefinition definition) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public PublishedServiceDefinition Definition { get; internal set; } = definition;
    public PublishedServiceState State { get; internal set; }
    public string PublicAddress { get; internal set; } = "";
    public string Error { get; internal set; } = "";
    public string Log { get; internal set; } = "";
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal CancellationTokenSource? Cancellation { get; set; }
    internal IPublishedServiceRuntime? Runtime { get; set; }
    internal bool IsDeleting { get; set; }
    public bool IsActive => IsDeleting || State is PublishedServiceState.Starting or PublishedServiceState.Running or PublishedServiceState.Stopping or PublishedServiceState.Deleting;
    public bool CanStart => !IsActive && !Definition.DeletionPending;
    public bool CanStop => !IsDeleting && State is PublishedServiceState.Starting or PublishedServiceState.Running;
    public bool CanEdit => !IsActive && !Definition.DeletionPending;
    public bool CanDelete => !IsDeleting && State is not (PublishedServiceState.Stopping or PublishedServiceState.Deleting);
    public bool CanShare => State == PublishedServiceState.Running && PublicAddress.Length > 0;
    public string Name => Definition.Name;
    public string ProtocolText => Definition.Protocol.ToUpperInvariant();
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public string LocalAddress => Definition.LocalAddress;
    public string ModeText => Definition.IsQuick ? LocalizationService.T("临时地址", "Temporary URL") : Definition.Hostname;
    public string AddressText => PublicAddress.Length > 0 ? PublicAddress : "—";
    public string StatusText => State switch
    {
        PublishedServiceState.Deleting => LocalizationService.T("删除中", "Deleting"),
        PublishedServiceState.Starting => LocalizationService.T("启动中", "Starting"),
        PublishedServiceState.Running => LocalizationService.T("运行中", "Running"),
        PublishedServiceState.Stopping => LocalizationService.T("停止中", "Stopping"),
        PublishedServiceState.Failed => LocalizationService.T("异常", "Failed"),
        _ => Definition.DeletionPending ? LocalizationService.T("待完成删除", "Deletion pending") : LocalizationService.T("已停止", "Stopped")
    };
    public string StatusColor => State switch { PublishedServiceState.Running => "#36B58B", PublishedServiceState.Failed => "#E88740", _ => "#8396B0" };
    internal void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public ShareProfile CreateShareProfile()
    {
        if (!CanShare) throw new InvalidOperationException("服务尚未就绪，暂时无法分享。");
        var protocol = Definition.Protocol is "http" or "https" ? "https" : Definition.Protocol;
        return new ShareProfile(1, Name, protocol, PublicAddress, Definition.SuggestedPort, Definition.ProtectedAccess);
    }
}
