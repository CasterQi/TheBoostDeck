using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheBoostDeck.Skins;

public static class SkinPackageReader
{
    public const string ManifestFileName = "skin.json";
    public const string PackageExtension = ".boostskin";

    private const int MaxEntries = 2000;
    private const long MaxPackageBytes = 256L * 1024 * 1024;
    private const long MaxEntryBytes = 64L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;
    private const long MaxManifestBytes = 1024L * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Regex IdPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SkinManifest ReadManifest(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new SkinPackageException($"Skin package was not found: {packagePath}");

        var packageInfo = new FileInfo(packagePath);
        if (packageInfo.Length > MaxPackageBytes)
            throw new SkinPackageException("Skin package is too large (limit: 256 MB).");

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            return ReadManifest(archive, Path.GetFileNameWithoutExtension(packagePath));
        }
        catch (InvalidDataException exception)
        {
            throw new SkinPackageException("Skin package is not a valid ZIP archive.", exception);
        }
        catch (IOException exception)
        {
            throw new SkinPackageException("Skin package could not be read.", exception);
        }
    }

    public static SkinRuntime Extract(string packagePath, string cacheRoot)
    {
        var manifest = ReadManifest(packagePath);
        var hash = ComputeSha256(packagePath);
        var safeCacheRoot = Path.GetFullPath(cacheRoot);
        var cacheDirectory = Path.GetFullPath(Path.Combine(safeCacheRoot, $"{manifest.Id}-{hash[..12]}"));
        if (!IsUnderRoot(safeCacheRoot, cacheDirectory))
            throw new SkinPackageException("Skin cache path escaped the skin cache directory.");

        var entryPath = Path.Combine(cacheDirectory, manifest.Entry.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(cacheDirectory) && File.Exists(entryPath))
            return CreateRuntime(manifest, packagePath, cacheDirectory);

        if (Directory.Exists(cacheDirectory))
            DeleteGeneratedCacheDirectory(cacheDirectory, safeCacheRoot);

        Directory.CreateDirectory(safeCacheRoot);
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var relativePath = NormalizeEntryName(entry.FullName);
                if (!IsSafeRelativePath(relativePath))
                    throw new SkinPackageException($"Unsafe skin entry: {entry.FullName}");

                var destination = Path.GetFullPath(Path.Combine(cacheDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnderRoot(cacheDirectory, destination))
                    throw new SkinPackageException($"Skin entry escaped the package root: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch (InvalidDataException exception)
        {
            DeleteGeneratedCacheDirectory(cacheDirectory, safeCacheRoot);
            throw new SkinPackageException("Skin package is not a valid ZIP archive.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteGeneratedCacheDirectory(cacheDirectory, safeCacheRoot);
            throw new SkinPackageException("Skin package could not be extracted.", exception);
        }

        if (!File.Exists(entryPath))
        {
            DeleteGeneratedCacheDirectory(cacheDirectory, safeCacheRoot);
            throw new SkinPackageException($"Skin entry was not found after extraction: {manifest.Entry}");
        }

        return CreateRuntime(manifest, packagePath, cacheDirectory);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static byte[]? ReadPreviewBytes(string packagePath, string? previewPath, int maxBytes = 2 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(previewPath) || maxBytes <= 0)
            return null;

        if (!IsSafeRelativePath(previewPath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var normalizedPreview = NormalizeEntryName(previewPath);
            var entry = archive.Entries.FirstOrDefault(item =>
                !string.IsNullOrEmpty(item.Name) &&
                NormalizeEntryName(item.FullName).Equals(normalizedPreview, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > maxBytes)
                return null;

            using var stream = entry.Open();
            using var buffer = new MemoryStream((int)entry.Length);
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SkinManifest ReadManifest(ZipArchive archive, string fallbackId)
    {
        ValidateArchiveLimits(archive);

        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            NormalizeEntryName(entry.FullName).Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
            throw new SkinPackageException($"Skin package is missing {ManifestFileName}.");

        if (manifestEntry.Length > MaxManifestBytes)
            throw new SkinPackageException("skin.json is too large.");

        string json;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            json = reader.ReadToEnd();

        SkinManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SkinManifest>(json, JsonOptions)
                       ?? throw new SkinPackageException("skin.json is empty.");
        }
        catch (JsonException exception)
        {
            throw new SkinPackageException("skin.json is not valid JSON.", exception);
        }

        if (manifest.SchemaVersion != 1)
            throw new SkinPackageException($"Unsupported skin schema version: {manifest.SchemaVersion}. Expected 1.");

        var id = string.IsNullOrWhiteSpace(manifest.Id)
            ? SanitizeFallbackId(fallbackId)
            : manifest.Id.Trim();
        if (!IdPattern.IsMatch(id))
            throw new SkinPackageException("skin.json id must contain only letters, digits, dots, dashes, or underscores.");
        if (id.Equals("classic", StringComparison.OrdinalIgnoreCase))
            throw new SkinPackageException("The skin id 'classic' is reserved for the built-in skin.");

        var entry = string.IsNullOrWhiteSpace(manifest.Entry) ? "index.html" : manifest.Entry.Trim();
        if (!IsSafeRelativePath(entry))
            throw new SkinPackageException("skin.json entry must be a safe, relative path.");
        entry = NormalizeEntryName(entry);

        var matchedEntry = archive.Entries
            .Select(item => NormalizeEntryName(item.FullName))
            .FirstOrDefault(item => item.Equals(entry, StringComparison.OrdinalIgnoreCase));
        if (matchedEntry is null)
            throw new SkinPackageException($"skin.json entry was not found in the package: {entry}");
        entry = matchedEntry;

        var preview = string.IsNullOrWhiteSpace(manifest.Preview)
            ? null
            : manifest.Preview.Trim();
        if (preview is not null &&
            (!IsSafeRelativePath(preview) ||
             !archive.Entries.Any(item =>
                 NormalizeEntryName(item.FullName).Equals(NormalizeEntryName(preview), StringComparison.OrdinalIgnoreCase))))
        {
            preview = null;
        }
        else if (preview is not null)
        {
            preview = NormalizeEntryName(preview);
        }

        var name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name.Trim();
        var version = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0.0" : manifest.Version.Trim();

        return manifest with
        {
            Id = id,
            Name = name,
            Version = version,
            Entry = entry,
            Preview = preview
        };
    }

    private static SkinRuntime CreateRuntime(SkinManifest manifest, string packagePath, string rootDirectory)
    {
        var descriptor = new SkinDescriptor(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            packagePath,
            IsBuiltIn: false,
            PackageTimestamp: File.GetLastWriteTimeUtc(packagePath))
        {
            Manifest = manifest
        };

        return new SkinRuntime(descriptor, rootDirectory, manifest.Entry);
    }

    private static void ValidateArchiveLimits(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxEntries)
            throw new SkinPackageException($"Skin package contains too many files (limit: {MaxEntries}).");

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (!string.IsNullOrEmpty(entry.Name) && !IsSafeRelativePath(entry.FullName))
                throw new SkinPackageException($"Unsafe skin entry: {entry.FullName}");
            if (entry.Length > MaxEntryBytes)
                throw new SkinPackageException($"Skin entry is too large: {entry.FullName}");
            totalBytes += entry.Length;
            if (totalBytes > MaxTotalBytes)
                throw new SkinPackageException("Skin package expands beyond the 512 MB limit.");
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        var value = path.Replace('\\', '/').Trim();
        if (value.Length == 0 || value.StartsWith('/') || value.Contains(':') || value.Contains('?') || value.Contains('#'))
            return false;

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment => segment is not "." and not "..");
    }

    private static string NormalizeEntryName(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private static string SanitizeFallbackId(string value)
    {
        var sanitized = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "skin" : sanitized[..Math.Min(sanitized.Length, 80)];
    }

    private static bool IsUnderRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
               !normalizedCandidate.Equals(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteGeneratedCacheDirectory(string directory, string cacheRoot)
    {
        if (!IsUnderRoot(cacheRoot, directory))
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkinPackageException("Could not clear the skin cache directory.", exception);
        }
    }
}
