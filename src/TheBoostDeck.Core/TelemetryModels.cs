namespace TheBoostDeck.Core;

public enum MetricStatus
{
    Available,
    Unavailable
}

public sealed record MetricValue
{
    public double? Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public MetricStatus Status { get; init; } = MetricStatus.Unavailable;
    public string? Reason { get; init; }
    public string? Source { get; init; }

    public static MetricValue Available(double value, string unit, string? source = null) => new()
    {
        Value = value,
        Unit = unit,
        Status = MetricStatus.Available,
        Source = source
    };

    public static MetricValue Unavailable(string unit, string reason) => new()
    {
        Unit = unit,
        Status = MetricStatus.Unavailable,
        Reason = reason
    };
}

public sealed record CpuMetrics
{
    public string Name { get; init; } = "CPU";
    public MetricValue Usage { get; init; } = MetricValue.Unavailable("%", "尚未采集");
    public MetricValue Temperature { get; init; } = MetricValue.Unavailable("°C", "尚未采集");
    public MetricValue Power { get; init; } = MetricValue.Unavailable("W", "尚未采集");
    public MetricValue Frequency { get; init; } = MetricValue.Unavailable("GHz", "尚未采集");
}

public sealed record GpuMetrics
{
    public string Name { get; init; } = "GPU";
    public MetricValue Usage { get; init; } = MetricValue.Unavailable("%", "尚未采集");
    public MetricValue Temperature { get; init; } = MetricValue.Unavailable("°C", "尚未采集");
    public MetricValue Power { get; init; } = MetricValue.Unavailable("W", "尚未采集");
    public MetricValue Frequency { get; init; } = MetricValue.Unavailable("MHz", "尚未采集");
    public MetricValue FanSpeed { get; init; } = MetricValue.Unavailable("RPM", "尚未采集");
    public MetricValue MemoryUsed { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue MemoryTotal { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue MemoryUsage { get; init; } = MetricValue.Unavailable("%", "尚未采集");
}

public sealed record MemoryMetrics
{
    public MetricValue Used { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue Total { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue Usage { get; init; } = MetricValue.Unavailable("%", "尚未采集");
}

public sealed record DriveMetrics
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public MetricValue Used { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue Total { get; init; } = MetricValue.Unavailable("GB", "尚未采集");
    public MetricValue Usage { get; init; } = MetricValue.Unavailable("%", "尚未采集");
}

public sealed record StorageMetrics
{
    public IReadOnlyList<DriveMetrics> Drives { get; init; } = Array.Empty<DriveMetrics>();
    public MetricValue ReadRate { get; init; } = MetricValue.Unavailable("B/s", "尚未采集");
    public MetricValue WriteRate { get; init; } = MetricValue.Unavailable("B/s", "尚未采集");
}

public sealed record NetworkMetrics
{
    public string Name { get; init; } = "未选择活动网卡";
    public MetricValue Download { get; init; } = MetricValue.Unavailable("B/s", "尚未采集");
    public MetricValue Upload { get; init; } = MetricValue.Unavailable("B/s", "尚未采集");
}

public sealed record SystemMetrics
{
    public MetricValue MotherboardTemperature { get; init; } = MetricValue.Unavailable("°C", "尚未采集");
    public MetricValue FanSpeed { get; init; } = MetricValue.Unavailable("RPM", "尚未采集");
    public MetricValue Power { get; init; } = MetricValue.Unavailable("W", "尚未采集");
    public string PowerSource { get; init; } = "未知";
}

public sealed record SensorDriverInfo
{
    public string Name { get; init; } = "PawnIO";
    public string Status { get; init; } = "unknown";
    public bool Ready { get; init; }
    public string? Version { get; init; }
    public string? Reason { get; init; }
}

public sealed record TelemetrySnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public CpuMetrics Cpu { get; init; } = new();
    public GpuMetrics Gpu { get; init; } = new();
    public MemoryMetrics Memory { get; init; } = new();
    public StorageMetrics Storage { get; init; } = new();
    public NetworkMetrics Network { get; init; } = new();
    public SystemMetrics System { get; init; } = new();
    public SensorDriverInfo SensorDriver { get; init; } = new();
}
