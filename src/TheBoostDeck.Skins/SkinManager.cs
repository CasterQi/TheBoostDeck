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
        if (!File.Exists(destination) && !source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: false);

        return Resolve(manifest.Id);
    }
}
