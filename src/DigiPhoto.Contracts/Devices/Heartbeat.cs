using System.Text.Json.Serialization;

namespace DigiPhoto.Contracts.Devices;

[JsonConverter(typeof(JsonStringEnumConverter<HardwareState>))]
public enum HardwareState
{
    Unknown,
    Ready,
    Busy,
    Disconnected,
    AttentionRequired,
    Failed,
}

public sealed record DeviceHeartbeat(
    int SchemaVersion,
    Guid TenantId,
    Guid DeviceId,
    string AppVersion,
    DateTimeOffset LeaseExpiresAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    long DiskFreeBytes,
    HardwareState CameraState,
    HardwareState PrinterState,
    bool EventBundleReady,
    int PendingOutboxItems,
    DateTimeOffset RecordedAtUtc);
