using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBoostDeck.Core;

namespace TheBoostDeck.Skins;

/// <summary>
/// Builds a portable, platform-specific skin-authoring skill ZIP. The
/// template is embedded in this assembly; the sensor inventory and latest
/// snapshot are written into the package at export time.
/// </summary>
public static class SkinSkillExporter
{
    public const string SkillFolderName = "theboostdeck-skin-author";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly (string Path, string ResourceName)[] TemplateFiles =
    [
        ("SKILL.md", "TheBoostDeck.Skins.SkinSkill.SKILL.md"),
        ("references/skin-format.md", "TheBoostDeck.Skins.SkinSkill.references.skin-format.md"),
        ("references/protocol.md", "TheBoostDeck.Skins.SkinSkill.references.protocol.md"),
        ("references/design-guide.md", "TheBoostDeck.Skins.SkinSkill.references.design-guide.md"),
        ("scripts/validate_boostskin.py", "TheBoostDeck.Skins.SkinSkill.scripts.validate_boostskin.py")
    ];

    public static void Export(
        string destinationPath,
        TelemetryInventory inventory,
        string? appVersion = null,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(inventory);

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The destination directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var template in TemplateFiles)
                {
                    using var resource = assembly.GetManifestResourceStream(template.ResourceName)
                        ?? throw new InvalidOperationException($"The embedded skin skill resource is missing: {template.ResourceName}");
                    using var entry = archive.CreateEntry(
                        $"{SkillFolderName}/{template.Path}",
                        CompressionLevel.Optimal).Open();
                    resource.CopyTo(entry);
                }

                var timestamp = generatedAt ?? DateTimeOffset.Now;
                var version = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
                var platform = BuildPlatformDocument(inventory, version, timestamp);
                WriteText(archive, $"{SkillFolderName}/references/platform.json",
                    JsonSerializer.Serialize(platform, JsonOptions));
                WriteText(archive, $"{SkillFolderName}/references/platform-summary.md",
                    BuildPlatformSummary(inventory, version, timestamp));
            }

            File.Move(tempPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    private static PlatformDocument BuildPlatformDocument(
        TelemetryInventory inventory,
        string appVersion,
        DateTimeOffset generatedAt) => new(
        SchemaVersion: 1,
        GeneratedAt: generatedAt,
        CapturedAt: inventory.CapturedAt,
        Generator: $"TheBoostDeck {appVersion}",
        AppVersion: appVersion,
        MachineName: inventory.MachineName,
        Os: inventory.OsDescription,
        Architecture: inventory.Architecture,
        ProcessorCount: inventory.ProcessorCount,
        Collector: inventory.Collector,
        SensorDriver: inventory.SensorDriver,
        SensorCount: inventory.Sensors.Count,
        Sensors: inventory.Sensors,
        Metrics: BuildMetricCoverage(inventory.Snapshot),
        Snapshot: inventory.Snapshot);

    private static string BuildPlatformSummary(
        TelemetryInventory inventory,
        string appVersion,
        DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# TheBoostDeck platform sensor inventory");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {generatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- Inventory captured: {inventory.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- Generator: TheBoostDeck {appVersion}");
        builder.AppendLine($"- Machine: {inventory.MachineName}");
        builder.AppendLine($"- OS: {inventory.OsDescription}");
        builder.AppendLine($"- Architecture: {inventory.Architecture}");
        builder.AppendLine($"- Logical processors: {inventory.ProcessorCount}");
        builder.AppendLine($"- Collector: {inventory.Collector}");
        builder.AppendLine($"- Sensor driver: {inventory.SensorDriver.Name} (status={inventory.SensorDriver.Status}, ready={inventory.SensorDriver.Ready})");
        if (!string.IsNullOrWhiteSpace(inventory.SensorDriver.Version))
            builder.AppendLine($"- Sensor driver version: {inventory.SensorDriver.Version}");
        if (!string.IsNullOrWhiteSpace(inventory.SensorDriver.Reason))
            builder.AppendLine($"- Sensor driver note: {inventory.SensorDriver.Reason}");
        builder.AppendLine($"- Sensors reported: {inventory.Sensors.Count}");
        builder.AppendLine();

        builder.AppendLine("## Runtime metric coverage");
        builder.AppendLine();
        builder.AppendLine("These are the canonical fields the host currently delivers to a running skin. Anything marked `unavailable` must render as a deliberate placeholder.");
        builder.AppendLine();
        builder.AppendLine("| Group | Metric | Status | Value | Unit | Source |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var metric in BuildMetricCoverage(inventory.Snapshot))
        {
            builder.AppendLine(
                $"| {Escape(metric.Group)} | {Escape(metric.Metric)} | {Escape(metric.Status)} | " +
                $"{Escape(FormatValue(metric.Value))} | {Escape(metric.Unit)} | {Escape(metric.Source ?? string.Empty)} |");
        }
        builder.AppendLine();

        builder.AppendLine("## All hardware sensors");
        builder.AppendLine();
        builder.AppendLine("This table is the complete raw inventory returned by the collector. It is discovery data: only fields represented in the runtime metric coverage above are delivered to skins today.");
        builder.AppendLine();
        if (inventory.Sensors.Count == 0)
        {
            builder.AppendLine("_No raw sensor inventory was available for this export._");
        }
        else
        {
            builder.AppendLine("| Hardware | Type | Sensor | Sensor type | Value | Unit | Status |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var sensor in inventory.Sensors)
            {
                builder.AppendLine(
                    $"| {Escape(sensor.HardwarePath)} | {Escape(sensor.HardwareType)} | " +
                    $"{Escape(sensor.SensorName)} | {Escape(sensor.SensorType)} | " +
                    $"{Escape(FormatValue(sensor.Value))} | {Escape(sensor.Unit ?? string.Empty)} | " +
                    $"{(sensor.Available ? "available" : "unavailable")} |");
            }
        }
        builder.AppendLine();

        builder.AppendLine("## Authoring notes");
        builder.AppendLine();
        builder.AppendLine("1. Use only `snapshot` fields for live values; do not build a live widget from an unsupported raw sensor.");
        builder.AppendLine("2. Preserve hardware and sensor names exactly as reported; do not translate them.");
        builder.AppendLine("3. Show `—` or a localized no-data state for unavailable metrics; never substitute zero.");
        builder.AppendLine("4. Design 16:9 landscape and 9:16 portrait as first-class layouts.");
        builder.AppendLine("5. FPS is intentionally out of scope.");
        builder.AppendLine("6. Validate the finished `.boostskin` with `scripts/validate_boostskin.py`.");
        return builder.ToString();
    }

    private static IReadOnlyList<MetricCoverageEntry> BuildMetricCoverage(TelemetrySnapshot snapshot)
    {
        var metrics = new List<MetricCoverageEntry>();
        Add(metrics, "CPU", "usage", snapshot.Cpu.Usage);
        Add(metrics, "CPU", "temperature", snapshot.Cpu.Temperature);
        Add(metrics, "CPU", "power", snapshot.Cpu.Power);
        Add(metrics, "CPU", "frequency", snapshot.Cpu.Frequency);
        Add(metrics, "CPU", "fanSpeed", snapshot.Cpu.FanSpeed);
        Add(metrics, "GPU", "usage", snapshot.Gpu.Usage);
        Add(metrics, "GPU", "temperature", snapshot.Gpu.Temperature);
        Add(metrics, "GPU", "power", snapshot.Gpu.Power);
        Add(metrics, "GPU", "frequency", snapshot.Gpu.Frequency);
        Add(metrics, "GPU", "fanSpeed", snapshot.Gpu.FanSpeed);
        Add(metrics, "GPU", "memoryUsed", snapshot.Gpu.MemoryUsed);
        Add(metrics, "GPU", "memoryTotal", snapshot.Gpu.MemoryTotal);
        Add(metrics, "GPU", "memoryUsage", snapshot.Gpu.MemoryUsage);
        Add(metrics, "Memory", "used", snapshot.Memory.Used);
        Add(metrics, "Memory", "total", snapshot.Memory.Total);
        Add(metrics, "Memory", "usage", snapshot.Memory.Usage);
        Add(metrics, "Storage", "readRate", snapshot.Storage.ReadRate);
        Add(metrics, "Storage", "writeRate", snapshot.Storage.WriteRate);
        foreach (var drive in snapshot.Storage.Drives)
        {
            var group = string.IsNullOrWhiteSpace(drive.Name) ? "Storage" : $"Storage / {drive.Name}";
            Add(metrics, group, "used", drive.Used);
            Add(metrics, group, "total", drive.Total);
            Add(metrics, group, "usage", drive.Usage);
        }
        Add(metrics, "Network", "download", snapshot.Network.Download);
        Add(metrics, "Network", "upload", snapshot.Network.Upload);
        Add(metrics, "System", "motherboardTemperature", snapshot.System.MotherboardTemperature);
        Add(metrics, "System", "power", snapshot.System.Power);
        return metrics;
    }

    private static void Add(List<MetricCoverageEntry> metrics, string group, string name, MetricValue value) =>
        metrics.Add(new MetricCoverageEntry(
            Group: group,
            Metric: name,
            Status: value.Status.ToString().ToLowerInvariant(),
            Value: value.Value,
            Unit: value.Unit,
            Source: value.Source,
            Reason: value.Reason));

    private static string FormatValue(double? value) =>
        value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "—";

    private static string Escape(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: false);
        writer.Write(content);
    }

    private sealed record PlatformDocument(
        int SchemaVersion,
        DateTimeOffset GeneratedAt,
        DateTimeOffset CapturedAt,
        string Generator,
        string AppVersion,
        string MachineName,
        string Os,
        string Architecture,
        int ProcessorCount,
        string Collector,
        SensorDriverInfo SensorDriver,
        int SensorCount,
        IReadOnlyList<SensorInventoryEntry> Sensors,
        IReadOnlyList<MetricCoverageEntry> Metrics,
        TelemetrySnapshot Snapshot);

    private sealed record MetricCoverageEntry(
        string Group,
        string Metric,
        string Status,
        double? Value,
        string Unit,
        string? Source,
        string? Reason);
}
