using System.Reflection;

namespace TheBoostDeck.App;

/// <summary>
/// Version and compact build-date information for the tray menu.
/// Build dates use the format YMDD: year offset from 2026 (base36),
/// month letter a-l, and a two-digit day. For example:
/// 0a20 = 2026-01-20, 1c02 = 2027-03-02, 0k17 = 2026-11-17.
/// </summary>
internal static class BuildInfo
{
    private const int FirstBuildYear = 2026;
    private const string Base36Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    internal static string Version => GetVersion();

    internal static string BuildDateCode => FormatBuildDate(GetBuildTimestamp());

    internal static string MenuText(string? language)
    {
        var version = Version;
        var build = BuildDateCode;
        return UiText.Normalize(language) == UiText.English
            ? $"Version {version} · Build {build}"
            : $"版本 {version} · 构建 {build}";
    }

    internal static string FormatBuildDate(DateTime date)
    {
        var yearOffset = Math.Max(0, date.Year - FirstBuildYear);
        var yearCode = Base36Digits[yearOffset % Base36Digits.Length];
        var monthCode = (char)('a' + date.Month - 1);
        return $"{yearCode}{monthCode}{date.Day:00}";
    }

    private static string GetVersion()
    {
        var assembly = typeof(App).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Trim();
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataSeparator = informational.IndexOf('+');
            if (metadataSeparator >= 0)
                informational = informational[..metadataSeparator];
            return informational;
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null ? "0.1.0" : assemblyVersion.ToString(3);
    }

    private static DateTime GetBuildTimestamp()
    {
        var assemblyPath = typeof(App).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
            return File.GetLastWriteTime(assemblyPath);

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            return File.GetLastWriteTime(processPath);

        return DateTime.Now;
    }
}
