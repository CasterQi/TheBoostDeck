using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using TheBoostDeck.Core;

namespace TheBoostDeck.Infrastructure;

public sealed class LhmTelemetryCollector : ITelemetryCollector, IDisposable
{
    private static readonly string[] VirtualAdapterKeywords =
    [
        "virtual", "vethernet", "bluetooth", "loopback", "teredo", "vpn", "tap",
        "idd", "todesk", "oray", "gameviewer", "parsec", "hyper-v", "default switch"
    ];

    private static readonly string[] CpuFanKeywords = ["cpu", "processor", "aio", "pump"];

    private readonly object _sync = new();
    private readonly Computer _computer;
    private readonly SensorDriverInfo _sensorDriver;
    private bool _disposed;

    public LhmTelemetryCollector()
    {
        _sensorDriver = PawnIoDependency.Detect();
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true
        };
        _computer.Open();
    }

    public Task<TelemetrySnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _computer.Accept(new UpdateVisitor());
            var hardware = _computer.Hardware.SelectMany(Flatten).ToArray();

            var snapshot = new TelemetrySnapshot
            {
                Timestamp = DateTimeOffset.Now,
                MachineName = Environment.MachineName,
                Cpu = CaptureCpu(hardware),
                Gpu = CaptureGpu(hardware),
                Memory = CaptureMemory(hardware),
                Storage = CaptureStorage(hardware),
                Network = CaptureNetwork(hardware),
                System = CaptureSystem(hardware),
                SensorDriver = _sensorDriver
            };

            return Task.FromResult(snapshot);
        }
    }

    private static CpuMetrics CaptureCpu(IReadOnlyList<IHardware> hardware)
    {
        var cpu = hardware.FirstOrDefault(item => item.HardwareType == HardwareType.Cpu);
        if (cpu is null)
        {
            return new CpuMetrics
            {
                Name = "未检测到 CPU",
                Usage = MetricValue.Unavailable("%", "LibreHardwareMonitor 未枚举到 CPU")
            };
        }

        return new CpuMetrics
        {
            Name = cpu.Name,
            Usage = ReadSensor(cpu, SensorType.Load, "CPU Total", "%", "未检测到 CPU 总占用传感器"),
            Temperature = ReadSensor(cpu, SensorType.Temperature, "CPU Package", "°C", "未检测到 CPU 封装温度传感器"),
            Power = ReadSensor(cpu, SensorType.Power, "CPU Package", "W", "未检测到 CPU 封装功耗传感器", zeroIsInvalid: true),
            Frequency = ReadAverageCpuFrequency(cpu),
            FanSpeed = ReadCpuFan(hardware)
        };
    }

    private static GpuMetrics CaptureGpu(IReadOnlyList<IHardware> hardware)
    {
        var gpu = hardware
            .Where(item => item.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia)
            .Where(item => !ContainsVirtualKeyword(item.Name))
            .OrderByDescending(item => FindSensor(item, SensorType.SmallData, "GPU Memory Total")?.Value > 0)
            .ThenByDescending(item => FindSensor(item, SensorType.Temperature, "GPU Core")?.Value.HasValue == true)
            .FirstOrDefault();

        if (gpu is null)
        {
            return new GpuMetrics
            {
                Name = "未检测到物理 GPU",
                Usage = MetricValue.Unavailable("%", "未枚举到可用的物理 GPU")
            };
        }

        var used = ReadSensor(gpu, SensorType.SmallData, "GPU Memory Used", "GB", "未检测到专用显存占用传感器", 1d / 1024d);
        var total = ReadSensor(gpu, SensorType.SmallData, "GPU Memory Total", "GB", "未检测到专用显存总量传感器", 1d / 1024d, zeroIsInvalid: true);

        return new GpuMetrics
        {
            Name = gpu.Name,
            Usage = ReadSensor(gpu, SensorType.Load, "GPU Core", "%", "未检测到 GPU 核心占用传感器"),
            Temperature = ReadSensor(gpu, SensorType.Temperature, "GPU Core", "°C", "未检测到 GPU 核心温度传感器"),
            Power = ReadSensor(gpu, SensorType.Power, "GPU Package", "W", "未检测到 GPU 封装功耗传感器"),
            Frequency = ReadSensor(gpu, SensorType.Clock, "GPU Core", "MHz", "未检测到 GPU 核心频率传感器"),
            FanSpeed = ReadSensor(gpu, SensorType.Fan, "GPU Fan", "RPM", "未检测到 GPU 风扇转速传感器"),
            MemoryUsed = used,
            MemoryTotal = total,
            MemoryUsage = CalculatePercentage(used, total, "专用显存占用")
        };
    }

    private static MemoryMetrics CaptureMemory(IReadOnlyList<IHardware> hardware)
    {
        var memory = hardware.FirstOrDefault(item => item.HardwareType == HardwareType.Memory && item.Identifier.ToString().Equals("/ram", StringComparison.OrdinalIgnoreCase))
                     ?? hardware.FirstOrDefault(item => item.HardwareType == HardwareType.Memory && item.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
        if (memory is null)
        {
            return new MemoryMetrics
            {
                Usage = MetricValue.Unavailable("%", "未检测到物理内存传感器")
            };
        }

        var used = ReadSensor(memory, SensorType.Data, "Memory Used", "GB", "未检测到物理内存占用传感器");
        var available = ReadSensor(memory, SensorType.Data, "Memory Available", "GB", "未检测到物理内存可用量传感器");
        var total = used.Status == MetricStatus.Available && available.Status == MetricStatus.Available
            ? MetricValue.Available(used.Value!.Value + available.Value!.Value, "GB", "LibreHardwareMonitor / physical memory")
            : MetricValue.Unavailable("GB", "无法根据已用与可用内存计算总量");

        return new MemoryMetrics
        {
            Used = used,
            Total = total,
            Usage = CalculatePercentage(used, total, "物理内存占用")
        };
    }

    private static StorageMetrics CaptureStorage(IReadOnlyList<IHardware> hardware)
    {
        var drives = new List<DriveMetrics>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.IsReady && item.DriveType == DriveType.Fixed))
            {
                try
                {
                    var totalBytes = drive.TotalSize;
                    if (totalBytes <= 0)
                        continue;

                    var usedBytes = totalBytes - drive.TotalFreeSpace;
                    var total = MetricValue.Available(totalBytes / 1024d / 1024d / 1024d, "GB", $"DriveInfo / {drive.Name}");
                    var used = MetricValue.Available(usedBytes / 1024d / 1024d / 1024d, "GB", $"DriveInfo / {drive.Name}");
                    drives.Add(new DriveMetrics
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel,
                        Used = used,
                        Total = total,
                        Usage = CalculatePercentage(used, total, $"磁盘 {drive.Name} 占用")
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A single inaccessible volume must not invalidate other volumes.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        var storage = hardware.Where(item => item.HardwareType == HardwareType.Storage).ToArray();
        return new StorageMetrics
        {
            Drives = drives,
            ReadRate = SumSensors(storage, SensorType.Throughput, "Read Rate", "B/s", "未检测到磁盘读取速率传感器"),
            WriteRate = SumSensors(storage, SensorType.Throughput, "Write Rate", "B/s", "未检测到磁盘写入速率传感器")
        };
    }

    private static NetworkMetrics CaptureNetwork(IReadOnlyList<IHardware> hardware)
    {
        var networkHardware = hardware.Where(item => item.HardwareType == HardwareType.Network).ToArray();
        var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (networkInterface.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                    continue;
                if (ContainsVirtualKeyword(networkInterface.Name) || ContainsVirtualKeyword(networkInterface.Description))
                    continue;

                activeNames.Add(networkInterface.Name);
                activeNames.Add(networkInterface.Description);
            }
        }
        catch (NetworkInformationException)
        {
        }

        var selected = networkHardware.Where(item => activeNames.Contains(item.Name)).ToArray();
        if (selected.Length == 0)
        {
            selected = networkHardware
                .Where(item => !ContainsVirtualKeyword(item.Name))
                .Where(item => item.Sensors.Any(sensor => sensor.SensorType == SensorType.Throughput && sensor.Value > 0))
                .ToArray();
        }

        if (selected.Length == 0)
        {
            return new NetworkMetrics
            {
                Download = MetricValue.Unavailable("B/s", "未检测到活动物理网卡"),
                Upload = MetricValue.Unavailable("B/s", "未检测到活动物理网卡")
            };
        }

        return new NetworkMetrics
        {
            Name = string.Join("、", selected.Select(item => item.Name).Distinct()),
            Download = SumSensors(selected, SensorType.Throughput, "Download Speed", "B/s", "活动网卡未返回下载速率"),
            Upload = SumSensors(selected, SensorType.Throughput, "Upload Speed", "B/s", "活动网卡未返回上传速率")
        };
    }

    private static SystemMetrics CaptureSystem(IReadOnlyList<IHardware> hardware)
    {
        var superIo = hardware.Where(item => item.HardwareType == HardwareType.SuperIO).ToArray();
        var motherboardTemperature = superIo
            .SelectMany(item => item.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature && IsValid(sensor.Value))
            .OrderByDescending(sensor => sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        var power = superIo
            .SelectMany(item => item.Sensors)
            .FirstOrDefault(sensor => sensor.SensorType == SensorType.Power && IsValid(sensor.Value) && sensor.Value > 0);

        return new SystemMetrics
        {
            MotherboardTemperature = motherboardTemperature is null
                ? MetricValue.Unavailable("°C", "未检测到可用的主板温度传感器；可检查 BIOS、驱动或传感器库支持")
                : MetricFromSensor(motherboardTemperature, "°C"),
            Power = power is null
                ? MetricValue.Unavailable("W", "整机功耗通常需要受支持的电源或专用传感器，当前未检测到")
                : MetricFromSensor(power, "W"),
            PowerSource = ReadPowerSource()
        };
    }

    private static MetricValue ReadCpuFan(IReadOnlyList<IHardware> hardware)
    {
        var fanSensors = hardware
            .Where(item => item.HardwareType is not HardwareType.GpuAmd and not HardwareType.GpuIntel and not HardwareType.GpuNvidia)
            .SelectMany(item => item.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Fan && IsValid(sensor.Value))
            .ToArray();

        var namedCpuFan = fanSensors.FirstOrDefault(sensor =>
            CpuFanKeywords.Any(keyword => sensor.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        if (namedCpuFan is not null)
            return MetricFromSensor(namedCpuFan, "RPM");

        var fallbackFan = hardware
            .Where(item => item.HardwareType == HardwareType.SuperIO)
            .SelectMany(item => item.Sensors)
            .FirstOrDefault(sensor => sensor.SensorType == SensorType.Fan && IsValid(sensor.Value));

        return fallbackFan is null
            ? MetricValue.Unavailable("RPM", "未检测到 CPU 风扇转速传感器；部分 3-pin 风扇或无测速线风扇不会返回转速")
            : MetricFromSensor(fallbackFan, "RPM");
    }

    private static MetricValue ReadAverageCpuFrequency(IHardware cpu)
    {
        var clocks = cpu.Sensors
            .Where(sensor => sensor.SensorType == SensorType.Clock)
            .Where(sensor => sensor.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase))
            .Where(sensor => IsValid(sensor.Value) && sensor.Value > 0)
            .Select(sensor => sensor.Value!.Value)
            .ToArray();

        if (clocks.Length == 0)
            return MetricValue.Unavailable("GHz", "CPU 核心频率传感器未返回有效数据，可能受权限或平台支持限制");

        return MetricValue.Available(clocks.Average() / 1000d, "GHz", $"{cpu.Name} / core clock average");
    }

    private static MetricValue ReadSensor(
        IHardware hardware,
        SensorType type,
        string sensorName,
        string unit,
        string missingReason,
        double scale = 1d,
        bool zeroIsInvalid = false)
    {
        var sensor = FindSensor(hardware, type, sensorName);
        if (sensor is null)
            return MetricValue.Unavailable(unit, missingReason);
        if (!IsValid(sensor.Value))
            return MetricValue.Unavailable(unit, "传感器已识别但未返回有效数据，可能受权限、驱动或平台支持限制");
        if (zeroIsInvalid && sensor.Value <= 0)
            return MetricValue.Unavailable(unit, "传感器返回的零值在当前平台上不可信，已按不可用处理");

        return MetricValue.Available(sensor.Value!.Value * scale, unit, $"{hardware.Name} / {sensor.Name}");
    }

    private static MetricValue MetricFromSensor(ISensor sensor, string unit, double scale = 1d) =>
        MetricValue.Available(sensor.Value!.Value * scale, unit, $"{sensor.Hardware.Name} / {sensor.Name}");

    private static ISensor? FindSensor(IHardware hardware, SensorType type, string name) =>
        hardware.Sensors.FirstOrDefault(sensor => sensor.SensorType == type && sensor.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static MetricValue SumSensors(
        IEnumerable<IHardware> hardware,
        SensorType type,
        string sensorName,
        string unit,
        string missingReason)
    {
        var sensors = hardware
            .Select(item => FindSensor(item, type, sensorName))
            .Where(sensor => sensor is not null && IsValid(sensor.Value))
            .Cast<ISensor>()
            .ToArray();

        if (sensors.Length == 0)
            return MetricValue.Unavailable(unit, missingReason);

        var source = string.Join("；", sensors.Select(sensor => sensor.Hardware.Name).Distinct());
        return MetricValue.Available(sensors.Sum(sensor => (double)sensor.Value!.Value), unit, source);
    }

    private static MetricValue CalculatePercentage(MetricValue used, MetricValue total, string description)
    {
        if (used.Status != MetricStatus.Available || total.Status != MetricStatus.Available || !total.Value.HasValue || total.Value <= 0)
            return MetricValue.Unavailable("%", $"{description}缺少有效的已用量或总量");

        return MetricValue.Available(Math.Clamp(used.Value!.Value / total.Value.Value * 100d, 0d, 100d), "%", used.Source);
    }

    private static bool IsValid(float? value) => value.HasValue && float.IsFinite(value.Value);

    private static bool ContainsVirtualKeyword(string value) =>
        VirtualAdapterKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<IHardware> Flatten(IHardware hardware)
    {
        yield return hardware;
        foreach (var child in hardware.SubHardware.SelectMany(Flatten))
            yield return child;
    }

    private static string ReadPowerSource()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return "未知";
            return status.ACLineStatus switch
            {
                0 => "电池",
                1 => "交流电源",
                _ => "未知"
            };
        }
        catch (DllNotFoundException)
        {
            return "未知";
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _computer.Close();
            _disposed = true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private sealed class UpdateVisitor : IVisitor
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
}
