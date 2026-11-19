using System.Runtime.InteropServices;

namespace TheBoostDeck.Infrastructure;

/// <summary>
/// Reads CPU utilisation with the same accounting model as Task Manager.
/// Task Manager reports "Processor Information\% Processor Utility", which
/// weights busy time by the current turbo ratio; LibreHardwareMonitor reports
/// the plain "Processor\% Processor Time" counter and therefore reads low on
/// boosting CPUs. PDH is called directly so the English counter path keeps
/// working on localised Windows installations.
/// </summary>
internal sealed class CpuUsageReader
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhCStatusValidData = 0;
    private const string UtilityCounterPath = @"\Processor Information(_Total)\% Processor Utility";

    private static readonly object Sync = new();
    private static nint _query;
    private static nint _counter;
    private static bool _initialized;
    private static bool _available;

    public static bool TryRead(out double percent)
    {
        percent = 0;
        lock (Sync)
        {
            if (!_initialized)
                Initialize();
            if (!_available)
                return false;
            if (PdhCollectQueryData(_query) != 0)
                return false;
            if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble, out _, out var sample) != 0)
                return false;
            // The very first sample after opening the query has no rate yet.
            if (sample.CStatus != PdhCStatusValidData)
                return false;
            if (double.IsNaN(sample.DoubleValue) || sample.DoubleValue < 0)
                return false;

            percent = Math.Min(100d, sample.DoubleValue);
            return true;
        }
    }

    private static void Initialize()
    {
        _initialized = true;
        if (PdhOpenQueryW(null, 0, out _query) != 0)
            return;
        if (PdhAddEnglishCounterW(_query, UtilityCounterPath, 0, out _counter) != 0)
            return;

        _available = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint counterType, out PdhFmtCounterValue value);
}
