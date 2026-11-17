namespace TheBoostDeck.Skins;

public sealed record SkinManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string Entry { get; init; } = "index.html";
    public string? Preview { get; init; }
    public SkinWindowOptions? Window { get; init; }
}

public sealed record SkinWindowOptions
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? MinWidth { get; init; }
    public int? MinHeight { get; init; }
    public string? Background { get; init; }
}

public sealed record SkinDescriptor(
    string Id,
    string Name,
    string Version,
    string? PackagePath,
    bool IsBuiltIn,
    DateTimeOffset? PackageTimestamp = null)
{
    public SkinManifest Manifest { get; init; } = new();
}

public sealed record SkinRuntime(
    SkinDescriptor Descriptor,
    string RootDirectory,
    string EntryRelativePath)
{
    public string Id => Descriptor.Id;
    public string DisplayName => Descriptor.Name;
    public bool IsBuiltIn => Descriptor.IsBuiltIn;
}

public sealed class SkinPackageException : Exception
{
    public SkinPackageException(string message) : base(message)
    {
    }

    public SkinPackageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
