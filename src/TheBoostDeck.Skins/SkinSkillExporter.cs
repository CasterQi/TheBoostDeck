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
        ("references/derived-metrics.md", "TheBoostDeck.Skins.SkinSkill.references.derived-metrics.md"),
        ("references/styles.md", "TheBoostDeck.Skins.SkinSkill.references.styles.md"),
        ("references/from-reference.md", "TheBoostDeck.Skins.SkinSkill.references.from-reference.md"),
        ("scripts/validate_boostskin.py", "TheBoostDeck.Skins.SkinSkill.scripts.validate_boostskin.py"),
        ("scripts/render_preview.py", "TheBoostDeck.Skins.SkinSkill.scripts.render_preview.py")
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
        DerivedMetrics: BuildDerivedMetrics(inventory.Snapshot),
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
        builder.AppendLine("These are the canonical fields the host currently delivers to a running skin. Build the layout only from metrics marked `available`. Metrics marked `unavailable` are absent on this machine: omit them and do not reserve placeholder cards. If a previously available metric drops out at runtime, use a compact no-data state.");
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

        builder.AppendLine("## Required controls");
        builder.AppendLine();
        builder.AppendLine("Every skin must expose a visible host control-center entry, a visible hide/exit entry, and a visible drag region. Recommended: use the entire title/header strip as the drag surface, excluding buttons, links, inputs, and other interactive controls from the drag hit area; this is a suggestion, not a fixed layout rule. One control hub may combine the first two. The control-center entry sends `open-settings`; the host opens the General landing page, with Skins and About tabs, and skin switching in the Skins tab. A direct `open-skin-manager` shortcut is optional. Do not embed or copy any settings/manager UI in the skin. Position is free, but the controls must remain visible and reachable in both 16:9 and 9:16. Do not ask the user whether to include these controls.");
        builder.AppendLine();

        builder.AppendLine("## Candidate derived metrics");
        builder.AppendLine();
        builder.AppendLine("These candidates are calculated from canonical `snapshot` fields at export time. The host does not send these values; a skin may recompute them from each new snapshot using the formulas in `references/derived-metrics.md`. A candidate is available only when every required input is available. If a candidate is unavailable on this machine, omit it from the layout rather than reserving an empty widget.");
        builder.AppendLine();
        builder.AppendLine("| Group | Id | Label | Formula | Inputs | Available | Value | Unit |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var derived in BuildDerivedMetrics(inventory.Snapshot))
        {
            builder.AppendLine(
                $"| {Escape(derived.Group)} | {Escape(derived.Id)} | {Escape(derived.Label)} | " +
                $"{Escape(derived.Formula)} | {Escape(string.Join(", ", derived.Inputs))} | " +
                $"{(derived.Available ? "yes" : "no")} | {Escape(FormatValue(derived.Value))} | {Escape(derived.Unit)} |");
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
        builder.AppendLine("3. Omit metrics and hardware groups marked `unavailable` in this inventory; do not reserve empty cards, rows, or N/A slots. For a metric that was available at export but drops out at runtime, show a compact no-data state; never substitute zero.");
        builder.AppendLine("4. Design 16:9 landscape and 9:16 portrait as first-class layouts, using only the metrics available on this machine. Both orientations should aim to show the complete HUD in one screen within the monitor work area, with taskbar height excluded; use vertical scrolling only as a fallback for unusually small windows.");
        builder.AppendLine("5. FPS is intentionally out of scope.");
        builder.AppendLine("6. Derived values are allowed only from available `snapshot` inputs; label them as derived and recompute them every snapshot. Omit a derived metric whose inputs are unavailable on this machine.");
        builder.AppendLine("7. Validate the finished `.boostskin` with `scripts/validate_boostskin.py`.");
        builder.AppendLine("8. Include the mandatory controls: a host control-center entry (`open-settings`), hide/exit, and a drag region. Recommended: use the entire title/header strip as the drag surface, excluding interactive controls from the drag hit area; this is a suggestion, not a fixed layout rule. A single control hub may combine the first two; a direct `open-skin-manager` shortcut is optional. Never embed or copy a settings/manager UI into the package.");
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

    private static IReadOnlyList<DerivedMetricEntry> BuildDerivedMetrics(TelemetrySnapshot snapshot)
    {
        var derived = new List<DerivedMetricEntry>();

        var memoryFree = Difference(snapshot.Memory.Total, snapshot.Memory.Used);
        AddDerived(derived, "Memory", "memory.free", "Memory free", "memory.total - memory.used", "GB",
            ["memory.total", "memory.used"], memoryFree,
            "Derived free capacity; the host does not send this field.");
        var memoryFreePercent = Percentage(Difference(snapshot.Memory.Total, snapshot.Memory.Used), snapshot.Memory.Total);
        AddDerived(derived, "Memory", "memory.freePercent", "Memory free percent", "(memory.total - memory.used) / memory.total * 100", "%",
            ["memory.total", "memory.used"], memoryFreePercent,
            "Use host memory.usage for the used percentage when it is available.");

        var gpuFree = Difference(snapshot.Gpu.MemoryTotal, snapshot.Gpu.MemoryUsed);
        AddDerived(derived, "GPU", "gpu.vramFree", "VRAM free", "gpu.memoryTotal - gpu.memoryUsed", "GB",
            ["gpu.memoryTotal", "gpu.memoryUsed"], gpuFree,
            "Derived free VRAM; the host does not send this field.");
        var gpuFreePercent = Percentage(gpuFree, snapshot.Gpu.MemoryTotal);
        AddDerived(derived, "GPU", "gpu.vramFreePercent", "VRAM free percent", "(gpu.memoryTotal - gpu.memoryUsed) / gpu.memoryTotal * 100", "%",
            ["gpu.memoryTotal", "gpu.memoryUsed"], gpuFreePercent,
            "Use host gpu.memoryUsage when it is available.");

        var driveFreeValues = new List<double>();
        var driveTotalValues = new List<double>();
        var driveUsedValues = new List<double>();
        var allDrivesAvailable = true;
        var allDriveTotalsAvailable = true;
        var allDriveUsedAvailable = true;
        for (var index = 0; index < snapshot.Storage.Drives.Count; index++)
        {
            var drive = snapshot.Storage.Drives[index];
            var driveLabel = string.IsNullOrWhiteSpace(drive.Label) ? drive.Name : drive.Label;
            var driveFree = Difference(drive.Total, drive.Used);
            var driveFreePercent = Percentage(driveFree, drive.Total);
            if (driveFree.HasValue)
                driveFreeValues.Add(driveFree.Value);
            if (!IsAvailable(drive.Total) || !IsAvailable(drive.Used))
                allDrivesAvailable = false;
            if (drive.Total.Value is double totalValue)
                driveTotalValues.Add(totalValue);
            else
                allDriveTotalsAvailable = false;
            if (drive.Used.Value is double usedValue)
                driveUsedValues.Add(usedValue);
            else
                allDriveUsedAvailable = false;

            AddDerived(derived, "Storage", $"storage.drive{index}.free", $"Free space ({driveLabel})",
                "storage.drives[i].total - storage.drives[i].used", "GB",
                [$"storage.drives[{index}].total", $"storage.drives[{index}].used"], driveFree,
                "Derived per-drive free space; recompute from each snapshot.");
            AddDerived(derived, "Storage", $"storage.drive{index}.freePercent", $"Free space percent ({driveLabel})",
                "(storage.drives[i].total - storage.drives[i].used) / storage.drives[i].total * 100", "%",
                [$"storage.drives[{index}].total", $"storage.drives[{index}].used"], driveFreePercent,
                "Use the host drive usage when it is available.");
        }

        var driveCount = snapshot.Storage.Drives.Count;
        double? totalFree = allDrivesAvailable && driveCount > 0
                            && driveFreeValues.Count == driveCount
            ? driveFreeValues.Sum()
            : null;
        AddDerived(derived, "Storage", "storage.freeTotal", "Total free space",
            "sum(storage.drives[i].total - storage.drives[i].used)", "GB",
            [.. snapshot.Storage.Drives.Select((_, index) => $"storage.drives[{index}].total"), .. snapshot.Storage.Drives.Select((_, index) => $"storage.drives[{index}].used")],
            totalFree,
            "Only available when every drive reports both total and used capacity.");

        double? totalCapacity = allDriveTotalsAvailable && driveCount > 0
                            && driveTotalValues.Count == driveCount
            ? driveTotalValues.Sum()
            : null;
        AddDerived(derived, "Storage", "storage.totalCapacity", "Total storage capacity",
            "sum(storage.drives[i].total)", "GB",
            [.. snapshot.Storage.Drives.Select((_, index) => $"storage.drives[{index}].total")],
            totalCapacity,
            "Combined capacity of every drive; only available when every drive reports a total.");

        double? totalUsed = allDriveUsedAvailable && driveCount > 0
                        && driveUsedValues.Count == driveCount
            ? driveUsedValues.Sum()
            : null;
        AddDerived(derived, "Storage", "storage.usedTotal", "Total used space",
            "sum(storage.drives[i].used)", "GB",
            [.. snapshot.Storage.Drives.Select((_, index) => $"storage.drives[{index}].used")],
            totalUsed,
            "Combined used space across drives; only available when every drive reports a used value.");

        var networkTotal = Sum(snapshot.Network.Download, snapshot.Network.Upload);
        AddDerived(derived, "Network", "network.total", "Combined throughput",
            "network.download + network.upload", "B/s",
            ["network.download", "network.upload"], networkTotal,
            "Combined adapter throughput; not an internet speed test.");

        var computePower = Sum(snapshot.Cpu.Power, snapshot.Gpu.Power);
        AddDerived(derived, "Power", "power.computeTotal", "CPU + GPU power",
            "cpu.power + gpu.power", "W",
            ["cpu.power", "gpu.power"], computePower,
            "Partial system power estimate; never label it as total PSU draw.");

        return derived;
    }

    private static void AddDerived(
        List<DerivedMetricEntry> target,
        string group,
        string id,
        string label,
        string formula,
        string unit,
        IReadOnlyList<string> inputs,
        double? value,
        string? note) =>
        target.Add(new DerivedMetricEntry(group, id, label, formula, inputs, unit, value, value.HasValue, note));

    private static bool IsAvailable(MetricValue value) =>
        value.Status == MetricStatus.Available && value.Value.HasValue && double.IsFinite(value.Value.Value);

    private static double? Difference(MetricValue minuend, MetricValue subtrahend) =>
        IsAvailable(minuend) && IsAvailable(subtrahend)
            ? minuend.Value!.Value - subtrahend.Value!.Value
            : null;

    private static double? Percentage(double? part, MetricValue total)
    {
        if (part is null || !IsAvailable(total) || total.Value!.Value <= 0)
            return null;
        return part.Value / total.Value.Value * 100;
    }

    private static double? Sum(MetricValue first, MetricValue second) =>
        IsAvailable(first) && IsAvailable(second)
            ? first.Value!.Value + second.Value!.Value
            : null;

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
        IReadOnlyList<DerivedMetricEntry> DerivedMetrics,
        TelemetrySnapshot Snapshot);

    private sealed record MetricCoverageEntry(
        string Group,
        string Metric,
        string Status,
        double? Value,
        string Unit,
        string? Source,
        string? Reason);

    private sealed record DerivedMetricEntry(
        string Group,
        string Id,
        string Label,
        string Formula,
        IReadOnlyList<string> Inputs,
        string Unit,
        double? Value,
        bool Available,
        string? Note);
}
