using System.Security.Principal;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(AppContext.BaseDirectory, "hardware-probe.json");

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsStorageEnabled = true,
    IsNetworkEnabled = true,
    IsControllerEnabled = true
};

var samples = new List<ProbeSample>();
computer.Open();

try
{
    for (var index = 0; index < 4; index++)
    {
        computer.Accept(new UpdateVisitor());
        samples.Add(new ProbeSample(
            DateTimeOffset.Now,
            computer.Hardware.Select(HardwareSnapshot.Capture).ToArray()));

        if (index < 3)
            Thread.Sleep(1000);
    }
}
finally
{
    computer.Close();
}

var report = new ProbeReport(
    DateTimeOffset.Now,
    Environment.OSVersion.VersionString,
    new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
    samples);

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, json);

var lastSample = samples[^1];
var allHardware = lastSample.Hardware.SelectMany(Flatten).ToArray();
var allSensors = allHardware.SelectMany(h => h.Sensors).ToArray();

Console.WriteLine($"Probe report: {outputPath}");
Console.WriteLine($"Administrator: {report.IsAdministrator}");
Console.WriteLine($"Hardware nodes: {allHardware.Length}; sensors: {allSensors.Length}");

foreach (var group in allSensors
             .Where(s => s.Value.HasValue && s.Name.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
                         s.Value.HasValue && s.Name.Contains("fan", StringComparison.OrdinalIgnoreCase))
             .Take(20))
{
    Console.WriteLine($"{group.HardwareName} / {group.Name} / {group.Type}: {group.Value}");
}

static IEnumerable<HardwareSnapshot> Flatten(HardwareSnapshot hardware)
{
    yield return hardware;
    foreach (var child in hardware.SubHardware.SelectMany(Flatten))
        yield return child;
}

internal sealed record ProbeReport(
    DateTimeOffset CapturedAt,
    string OperatingSystem,
    bool IsAdministrator,
    IReadOnlyList<ProbeSample> Samples);

internal sealed record ProbeSample(
    DateTimeOffset CapturedAt,
    IReadOnlyList<HardwareSnapshot> Hardware);

internal sealed record HardwareSnapshot(
    string Name,
    string Identifier,
    string Type,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareSnapshot> SubHardware)
{
    public static HardwareSnapshot Capture(IHardware hardware) => new(
        hardware.Name,
        hardware.Identifier.ToString(),
        hardware.HardwareType.ToString(),
        hardware.Sensors.Select(sensor => new SensorSnapshot(
            sensor.Name,
            sensor.Identifier.ToString(),
            sensor.SensorType.ToString(),
            sensor.Index,
            sensor.Value.HasValue && float.IsFinite(sensor.Value.Value) ? sensor.Value.Value : null,
            hardware.Name)).ToArray(),
        hardware.SubHardware.Select(Capture).ToArray());
}

internal sealed record SensorSnapshot(
    string Name,
    string Identifier,
    string Type,
    int Index,
    float? Value,
    string HardwareName);

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            subHardware.Accept(this);
    }

    public void VisitSensor(ISensor sensor)
    {
    }

    public void VisitParameter(IParameter parameter)
    {
    }
}
