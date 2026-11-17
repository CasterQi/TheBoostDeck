using System.Runtime.InteropServices;

namespace TheBoostDeck.Core;

public sealed record SensorInventoryEntry
{
    public string HardwareName { get; init; } = string.Empty;
    public string HardwareType { get; init; } = string.Empty;
    public string HardwareIdentifier { get; init; } = string.Empty;
    public string HardwarePath { get; init; } = string.Empty;
    public string SensorName { get; init; } = string.Empty;
    public string SensorType { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public double? Value { get; init; }
    public bool Available { get; init; }
    public string Source { get; init; } = string.Empty;
}

/// <summary>
/// A point-in-time inventory of every sensor exposed by the active collector.
/// The snapshot contains the canonical metrics delivered to skins; Sensors is
/// the broader discovery set used when authoring a platform-specific skin.
/// </summary>
public sealed record TelemetryInventory
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;
    public string Architecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();
    public int ProcessorCount { get; init; } = Environment.ProcessorCount;
    public string Collector { get; init; } = "unknown";
    public SensorDriverInfo SensorDriver { get; init; } = new();
    public IReadOnlyList<SensorInventoryEntry> Sensors { get; init; } = Array.Empty<SensorInventoryEntry>();
    public TelemetrySnapshot Snapshot { get; init; } = new();

    public static TelemetryInventory FromSnapshot(TelemetrySnapshot snapshot, string collector = "snapshot-only") => new()
    {
        CapturedAt = snapshot.Timestamp,
        MachineName = snapshot.MachineName,
        Collector = collector,
        SensorDriver = snapshot.SensorDriver,
        Sensors = Array.Empty<SensorInventoryEntry>(),
        Snapshot = snapshot
    };
}

/// <summary>
/// Optional collector capability implemented by collectors that can enumerate
/// raw hardware sensors in addition to the canonical snapshot.
/// </summary>
public interface ITelemetryInventoryProvider
{
    TelemetryInventory CaptureInventory();
}
