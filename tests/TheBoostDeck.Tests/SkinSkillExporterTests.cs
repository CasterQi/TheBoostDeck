using System.IO.Compression;
using System.Text.Json;
using TheBoostDeck.Core;
using TheBoostDeck.Skins;
using Xunit;

namespace TheBoostDeck.Tests;

public sealed class SkinSkillExporterTests
{
    [Fact]
    public void Export_WritesPortableSkillWithPlatformInventory()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "skill.zip");
        var inventory = CreateInventory();

        SkinSkillExporter.Export(destination, inventory, "0.1.0", new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(8)));

        using var archive = ZipFile.OpenRead(destination);
        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("theboostdeck-skin-author/SKILL.md", names);
        Assert.Contains("theboostdeck-skin-author/references/skin-format.md", names);
        Assert.Contains("theboostdeck-skin-author/references/protocol.md", names);
        Assert.Contains("theboostdeck-skin-author/references/design-guide.md", names);
        Assert.Contains("theboostdeck-skin-author/references/derived-metrics.md", names);
        Assert.Contains("theboostdeck-skin-author/references/styles.md", names);
        Assert.Contains("theboostdeck-skin-author/references/from-reference.md", names);
        Assert.Contains("theboostdeck-skin-author/references/platform.json", names);
        Assert.Contains("theboostdeck-skin-author/references/platform-summary.md", names);
        Assert.Contains("theboostdeck-skin-author/scripts/validate_boostskin.py", names);
        Assert.Contains("theboostdeck-skin-author/scripts/render_preview.py", names);

        using var platformJson = JsonDocument.Parse(ReadEntry(archive, "theboostdeck-skin-author/references/platform.json"));
        var root = platformJson.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("TestMachine", root.GetProperty("machineName").GetString());
        Assert.Equal(2, root.GetProperty("sensorCount").GetInt32());
        Assert.Equal(2, root.GetProperty("sensors").GetArrayLength());
        Assert.Equal("CPU Package", root.GetProperty("sensors")[0].GetProperty("sensorName").GetString());
        Assert.Equal("Fan #1", root.GetProperty("sensors")[1].GetProperty("sensorName").GetString());
        Assert.Equal("unavailable", root.GetProperty("metrics")[4].GetProperty("status").GetString());
        Assert.Equal("Test CPU", root.GetProperty("snapshot").GetProperty("cpu").GetProperty("name").GetString());

        var derived = root.GetProperty("derivedMetrics").EnumerateArray().ToList();
        var memoryFree = derived.Single(metric => metric.GetProperty("id").GetString() == "memory.free");
        Assert.True(memoryFree.GetProperty("available").GetBoolean());
        Assert.Equal(19.5, memoryFree.GetProperty("value").GetDouble());
        Assert.Equal("GB", memoryFree.GetProperty("unit").GetString());

        var networkTotal = derived.Single(metric => metric.GetProperty("id").GetString() == "network.total");
        Assert.True(networkTotal.GetProperty("available").GetBoolean());
        Assert.Equal(150, networkTotal.GetProperty("value").GetDouble());

        var computePower = derived.Single(metric => metric.GetProperty("id").GetString() == "power.computeTotal");
        Assert.True(computePower.GetProperty("available").GetBoolean());
        Assert.Equal(100, computePower.GetProperty("value").GetDouble());

        var totalFree = derived.Single(metric => metric.GetProperty("id").GetString() == "storage.freeTotal");
        Assert.False(totalFree.GetProperty("available").GetBoolean());
        Assert.False(totalFree.TryGetProperty("value", out _));

        var totalCapacity = derived.Single(metric => metric.GetProperty("id").GetString() == "storage.totalCapacity");
        Assert.False(totalCapacity.GetProperty("available").GetBoolean());
        Assert.Equal("sum(storage.drives[i].total)", totalCapacity.GetProperty("formula").GetString());

        var totalUsed = derived.Single(metric => metric.GetProperty("id").GetString() == "storage.usedTotal");
        Assert.False(totalUsed.GetProperty("available").GetBoolean());
        Assert.Equal("GB", totalUsed.GetProperty("unit").GetString());

        var fromReference = ReadEntry(archive, "theboostdeck-skin-author/references/from-reference.md");
        Assert.Contains("Step 2 — Map modules to metrics", fromReference);
        Assert.Contains("total compute power", fromReference);

        var styles = ReadEntry(archive, "theboostdeck-skin-author/references/styles.md");
        Assert.Contains("## cyberpunk", styles);
        Assert.Contains("## anime-cel", styles);
        Assert.Contains("## kawaii-pastel", styles);

        var summary = ReadEntry(archive, "theboostdeck-skin-author/references/platform-summary.md");
        Assert.Contains("CPU Package", summary);
        Assert.Contains("Fan #1", summary);
        Assert.Contains("Runtime metric coverage", summary);
        Assert.Contains("Metrics marked `unavailable` are absent on this machine", summary);
        Assert.Contains("do not reserve placeholder cards", summary);
        Assert.Contains("Required controls", summary);
        Assert.Contains("host control-center entry", summary);
        Assert.Contains("General landing page", summary);
        Assert.Contains("visible drag region", summary);
        Assert.Contains("entire title/header strip", summary);
        Assert.Contains("suggestion, not a fixed layout rule", summary);
        Assert.Contains("open-settings", summary);
        Assert.Contains("open-skin-manager", summary);
        Assert.Contains("Candidate derived metrics", summary);
        Assert.Contains("memory.free", summary);
    }

    [Fact]
    public void Export_OverwritesExistingDestination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "skill.zip");
        File.WriteAllText(destination, "old");

        SkinSkillExporter.Export(destination, CreateInventory(), "0.1.0");

        using var archive = ZipFile.OpenRead(destination);
        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("theboostdeck-skin-author/SKILL.md", names);
    }

    private static TelemetryInventory CreateInventory()
    {
        var snapshot = new TelemetrySnapshot
        {
            Timestamp = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(8)),
            MachineName = "TestMachine",
            Cpu = new CpuMetrics
            {
                Name = "Test CPU",
                Usage = MetricValue.Available(12.5, "%", "CPU Total"),
                Power = MetricValue.Available(65, "W", "CPU Package"),
                FanSpeed = MetricValue.Unavailable("RPM", "No fan sensor")
            },
            Gpu = new GpuMetrics
            {
                Name = "Test GPU",
                Power = MetricValue.Available(35, "W", "GPU Package"),
                MemoryUsed = MetricValue.Available(2.5, "GB", "GPU Memory Used"),
                MemoryTotal = MetricValue.Available(8, "GB", "GPU Memory Total")
            },
            Memory = new MemoryMetrics
            {
                Used = MetricValue.Available(12.5, "GB", "Memory Used"),
                Total = MetricValue.Available(32, "GB", "Memory Total")
            },
            Storage = new StorageMetrics(),
            Network = new NetworkMetrics
            {
                Download = MetricValue.Available(100, "B/s", "Network Download"),
                Upload = MetricValue.Available(50, "B/s", "Network Upload")
            },
            System = new SystemMetrics(),
            SensorDriver = new SensorDriverInfo { Name = "PawnIO", Status = "ready", Ready = true }
        };

        return new TelemetryInventory
        {
            CapturedAt = snapshot.Timestamp,
            MachineName = "TestMachine",
            OsDescription = "Windows test",
            Architecture = "X64",
            ProcessorCount = 8,
            Collector = "TestCollector",
            SensorDriver = snapshot.SensorDriver,
            Sensors =
            [
                new SensorInventoryEntry
                {
                    HardwareName = "Test CPU",
                    HardwareType = "Cpu",
                    HardwareIdentifier = "/cpu/0",
                    HardwarePath = "Test CPU",
                    SensorName = "CPU Package",
                    SensorType = "Temperature",
                    Unit = "°C",
                    Value = 48.5,
                    Available = true,
                    Source = "Test CPU / CPU Package"
                },
                new SensorInventoryEntry
                {
                    HardwareName = "Test CPU",
                    HardwareType = "Cpu",
                    HardwareIdentifier = "/cpu/0",
                    HardwarePath = "Test CPU",
                    SensorName = "Fan #1",
                    SensorType = "Fan",
                    Unit = "RPM",
                    Value = null,
                    Available = false,
                    Source = "Test CPU / Fan #1"
                }
            ],
            Snapshot = snapshot
        };
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing zip entry: {name}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "theboostdeck-skill-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
