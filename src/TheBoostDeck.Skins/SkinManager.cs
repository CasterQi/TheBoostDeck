using System.Globalization;

namespace TheBoostDeck.Skins;

public sealed class SkinManager
{
    public const string BuiltInId = "classic";
    public const string BuiltInName = "TheBoostDeck Classic";

    private readonly string _skinsDirectory;
    private readonly string _cacheDirectory;

    public SkinManager(string skinsDirectory, string builtInRootDirectory, string builtInEntry)
    {
        _skinsDirectory = Path.GetFullPath(skinsDirectory);
        _cacheDirectory = Path.Combine(_skinsDirectory, ".cache");
        Directory.CreateDirectory(_skinsDirectory);

        var root = Path.GetFullPath(builtInRootDirectory);
        var entry = string.IsNullOrWhiteSpace(builtInEntry) ? "dashboard.html" : builtInEntry;
        if (!File.Exists(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar))))
            throw new SkinPackageException("The built-in skin entry is missing.");

        BuiltIn = new SkinRuntime(
            new SkinDescriptor(BuiltInId, BuiltInName, "built-in", null, true)
            {
                Manifest = new SkinManifest
                {
                    Id = BuiltInId,
                    Name = BuiltInName,
                    Version = "built-in",
                    Entry = entry
                }
            },
            root,
            entry);
    }

    public SkinRuntime BuiltIn { get; }
    public string SkinsDirectory => _skinsDirectory;

    public IReadOnlyList<SkinDescriptor> GetInstalledSkins()
    {
        if (!Directory.Exists(_skinsDirectory))
            return Array.Empty<SkinDescriptor>();

        var descriptors = new List<SkinDescriptor>();
        foreach (var path in Directory.EnumerateFiles(_skinsDirectory, $"*{SkinPackageReader.PackageExtension}", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var manifest = SkinPackageReader.ReadManifest(path);
                descriptors.Add(new SkinDescriptor(
                    manifest.Id,
                    manifest.Name,
                    manifest.Version,
                    Path.GetFullPath(path),
                    false,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
                {
                    Manifest = manifest
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
            {
                // An invalid package in the user's skin folder must not prevent the app
                // from discovering or loading other skins.
            }
        }

        return descriptors
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.PackageTimestamp).First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public SkinRuntime Resolve(string? id)
    {
        var requested = string.IsNullOrWhiteSpace(id) ? BuiltInId : id.Trim();
        if (requested.Equals(BuiltInId, StringComparison.OrdinalIgnoreCase))
            return BuiltIn;

        var descriptor = GetInstalledSkins()
            .FirstOrDefault(item => item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (descriptor?.PackagePath is null)
            throw new SkinPackageException($"Skin '{requested}' is not installed.");

        return SkinPackageReader.Extract(descriptor.PackagePath, _cacheDirectory);
    }

    public string? GetPackagePath(string? id)
    {
        var requested = string.IsNullOrWhiteSpace(id) ? BuiltInId : id.Trim();
        if (requested.Equals(BuiltInId, StringComparison.OrdinalIgnoreCase))
            return null;

        return GetInstalledSkins()
            .FirstOrDefault(item => item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?.PackagePath;
    }

    public bool TryResolve(string? id, out SkinRuntime? runtime, out string? error)
    {
        try
        {
            runtime = Resolve(id);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
        {
            runtime = null;
            error = exception.Message;
            return false;
        }
    }

    public SkinRuntime Import(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
            throw new SkinPackageException("The selected skin package was not found.");
        if (!string.Equals(Path.GetExtension(source), SkinPackageReader.PackageExtension, StringComparison.OrdinalIgnoreCase))
            throw new SkinPackageException("Only .boostskin packages can be imported.");

        var manifest = SkinPackageReader.ReadManifest(source);
        var hash = SkinPackageReader.ComputeSha256(source);
        var fileName = string.Format(CultureInfo.InvariantCulture, "{0}-{1}{2}", manifest.Id, hash[..8], SkinPackageReader.PackageExtension);
        var destination = Path.Combine(_skinsDirectory, fileName);
        if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(destination))
                File.Copy(source, destination, overwrite: false);

            // File.Copy preserves the source timestamp, and the installed list ranks
            // packages by last write time. Touch the import so the freshly added
            // build is the one the id resolves to.
            try
            {
                File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Ranking is cosmetic; the package itself was copied successfully.
            }
        }

        return SkinPackageReader.Extract(destination, _cacheDirectory);
    }

    /// <summary>
    /// Removes every installed package that carries the requested id, plus the
    /// extracted caches produced from those packages. The caller is responsible
    /// for switching away from the skin before deleting it.
    /// </summary>
    public void Delete(string? id)
    {
        var requested = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requested))
            throw new SkinPackageException("A skin id is required to delete a skin.");
        if (requested.Equals(BuiltInId, StringComparison.OrdinalIgnoreCase))
            throw new SkinPackageException("The built-in skin cannot be deleted.");

        var installed = GetInstalledSkins()
            .FirstOrDefault(item => item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (installed?.PackagePath is null)
            throw new SkinPackageException($"Skin '{requested}' is not installed.");

        var deletedAny = false;
        foreach (var path in Directory.EnumerateFiles(_skinsDirectory, $"*{SkinPackageReader.PackageExtension}", SearchOption.TopDirectoryOnly))
        {
            var matches = false;
            try
            {
                matches = SkinPackageReader.ReadManifest(path).Id.Equals(requested, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
            {
                // A broken sibling package must not block deleting the requested skin.
            }

            if (!matches)
                continue;

            File.Delete(path);
            deletedAny = true;
        }

        if (!deletedAny)
            throw new SkinPackageException($"Skin '{requested}' could not be deleted.");

        DeleteCachedCopies(requested);
    }

    private void DeleteCachedCopies(string id)
    {
        if (!Directory.Exists(_cacheDirectory))
            return;

        // Cache folders are named <id>-<12 hex chars of the package hash>. The
        // suffix check keeps ids that are prefixes of other ids apart.
        var prefix = id + "-";
        foreach (var directory in Directory.EnumerateDirectories(_cacheDirectory))
        {
            var name = Path.GetFileName(directory);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = name[prefix.Length..];
            if (suffix.Length != 12 || !suffix.All(Uri.IsHexDigit))
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The cache is rebuilt on the next load, so a locked folder is not fatal.
            }
        }
    }
}
