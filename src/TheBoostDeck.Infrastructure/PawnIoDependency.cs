using Microsoft.Win32;
using TheBoostDeck.Core;

namespace TheBoostDeck.Infrastructure;

public static class PawnIoDependency
{
    public const string RequiredVersionText = "2.2.0.0";
    private static readonly Version RequiredVersion = new(2, 2, 0, 0);
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static SensorDriverInfo Detect()
    {
        try
        {
            var versions = new List<Version>();
            var installed = false;

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(UninstallKeyPath);
                if (key is null)
                    continue;

                installed = true;
                var versionText = key.GetValue("DisplayVersion") as string;
                if (Version.TryParse(versionText?.Trim(), out var version))
                    versions.Add(version);
            }

            if (!installed)
            {
                return new SensorDriverInfo
                {
                    Status = "missing",
                    Reason = "未检测到 PawnIO 2.2.0 或更高版本；CPU 温度、频率和功耗可能不可用。请安装 PawnIO 后重启 TheBoostDeck。"
                };
            }

            var highestVersion = versions.OrderByDescending(version => version).FirstOrDefault();
            if (highestVersion is not null && highestVersion < RequiredVersion)
            {
                return new SensorDriverInfo
                {
                    Status = "outdated",
                    Version = highestVersion.ToString(),
                    Reason = $"PawnIO {highestVersion} 低于要求的 {RequiredVersionText}；CPU 温度、频率和功耗可能不可用，请更新驱动后重启 TheBoostDeck。"
                };
            }

            return new SensorDriverInfo
            {
                Status = "ready",
                Ready = true,
                Version = highestVersion?.ToString(),
                Reason = highestVersion is null
                    ? "已检测到 PawnIO，但未能读取版本号。"
                    : $"PawnIO {highestVersion} 已安装。"
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new SensorDriverInfo
            {
                Status = "unknown",
                Reason = "无法读取 PawnIO 安装状态；如果 CPU 温度、频率和功耗不可用，请检查驱动安装。"
            };
        }
    }
}
