using System.Text.Json;
using TheBoostDeck.Core;
using TheBoostDeck.Skins;

namespace TheBoostDeck.App;

/// <summary>
/// Installs the skin packages shipped inside the application into the user's
/// skin folder. The first run copies every bundled package; a marker file then
/// records which ids were already offered so a skin the user deleted stays
/// deleted instead of returning on the next launch. "Restore bundled skins"
/// intentionally bypasses that marker.
/// </summary>
internal static class BundledSkinInstaller
{
    private const string MarkerFileName = ".bundled-skins.json";
    private const string BundledDirectoryName = "BundledSkins";

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, BundledDirectoryName);

    public static int EnsureInstalled(SkinManager manager, IAppLogger? logger) =>
        Install(manager, force: false, logger);

    public static int RestoreAll(SkinManager manager, IAppLogger? logger) =>
        Install(manager, force: true, logger);

    private static int Install(SkinManager manager, bool force, IAppLogger? logger)
    {
        var directory = BundledDirectory;
        if (!Directory.Exists(directory))
            return 0;

        var markerPath = Path.Combine(manager.SkinsDirectory, MarkerFileName);
        var marker = LoadMarker(markerPath);
        var installed = manager.GetInstalledSkins()
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Version, StringComparer.OrdinalIgnoreCase);
        var imported = 0;

        foreach (var package in Directory.EnumerateFiles(directory, $"*{SkinPackageReader.PackageExtension}"))
        {
            SkinManifest manifest;
            try
            {
                manifest = SkinPackageReader.ReadManifest(package);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
            {
                logger?.Error($"Bundled skin '{Path.GetFileName(package)}' is not a valid package.", exception);
                continue;
            }

            if (!force && installed.TryGetValue(manifest.Id, out var installedVersion))
            {
                // A user may already carry an older build of the same id from a
                // previous release; ship the newer bundled build instead of
                // leaving the old package in charge.
                if (IsAtLeast(installedVersion, manifest.Version))
                {
                    marker.Add(manifest.Id);
                    continue;
                }
            }
            else if (!force && marker.Contains(manifest.Id))
            {
                // The user deleted this default skin; do not resurrect it.
                continue;
            }

            try
            {
                manager.Import(package);
                marker.Add(manifest.Id);
                imported++;
                logger?.Info($"Bundled skin '{manifest.Id}' installed from the application package.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
            {
                logger?.Error($"Bundled skin '{manifest.Id}' could not be installed.", exception);
            }
        }

        SaveMarker(markerPath, marker, logger);
        return imported;
    }

    private static bool IsAtLeast(string? installed, string? bundled)
    {
        if (Version.TryParse(installed, out var current) && Version.TryParse(bundled, out var shipped))
            return current >= shipped;

        return string.Equals(installed, bundled, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> LoadMarker(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
            return ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken marker only risks reinstalling a deleted default skin;
            // it must never block the application from starting.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveMarker(string path, HashSet<string> ids, IAppLogger? logger)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.Error("The bundled skin marker could not be written.", exception);
        }
    }
}
