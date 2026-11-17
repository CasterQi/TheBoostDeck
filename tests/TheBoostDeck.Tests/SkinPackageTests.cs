using System.IO.Compression;
using System.Text;
using TheBoostDeck.Skins;
using Xunit;

namespace TheBoostDeck.Tests;

public sealed class SkinPackageTests
{
    [Fact]
    public void ReadManifest_ParsesAndNormalizesManifest()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            {
              "schemaVersion": 1,
              "id": "demo.skin",
              "name": "Demo Skin",
              "version": "1.2.3",
              "author": "Tester",
              "entry": "ui/index.html",
              "preview": "preview.png",
              "window": { "width": 1280, "height": 720, "background": "#112233" }
            }
            """,
            new Dictionary<string, string>
            {
                ["ui/index.html"] = "<html><body>demo</body></html>",
                ["preview.png"] = "not-an-image"
            });

        var manifest = SkinPackageReader.ReadManifest(package);

        Assert.Equal("demo.skin", manifest.Id);
        Assert.Equal("Demo Skin", manifest.Name);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("ui/index.html", manifest.Entry);
        Assert.Equal("preview.png", manifest.Preview);
        Assert.Equal(1280, manifest.Window?.Width);
        Assert.Equal("#112233", manifest.Window?.Background);
    }

    [Fact]
    public void ReadManifest_MissingId_UsesSafePackageNameFallback()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "name": "Fallback skin" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html></html>"
            },
            packageName: "My Skin.boostskin");

        var manifest = SkinPackageReader.ReadManifest(package);

        Assert.Equal("my-skin", manifest.Id);
        Assert.Equal("index.html", manifest.Entry);
        Assert.Equal("Fallback skin", manifest.Name);
    }

    [Fact]
    public void ReadManifest_RejectsMissingEntry()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "id": "demo" }
            """,
            new Dictionary<string, string>());

        var exception = Assert.Throws<SkinPackageException>(() => SkinPackageReader.ReadManifest(package));
        Assert.Contains("entry", exception.Message);
    }

    [Fact]
    public void ReadManifest_RejectsUnsupportedSchemaVersion()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 2, "id": "demo" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html></html>"
            });

        Assert.Throws<SkinPackageException>(() => SkinPackageReader.ReadManifest(package));
    }

    [Fact]
    public void Extract_RejectsPathTraversalEntry()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "id": "unsafe" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html></html>",
                ["../outside.html"] = "must not be written"
            });

        Assert.Throws<SkinPackageException>(() =>
            SkinPackageReader.Extract(package, Path.Combine(directory.Path, "cache")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.html")));
    }

    [Fact]
    public void Extract_RejectsMissingEntryInsideArchive()
    {
        using var directory = new TemporaryDirectory();
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "id": "missing-entry", "entry": "ui/index.html" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html></html>"
            });

        Assert.Throws<SkinPackageException>(() =>
            SkinPackageReader.Extract(package, Path.Combine(directory.Path, "cache")));
    }

    [Fact]
    public void SkinManager_Import_CopiesPackageAndResolvesCachedRuntime()
    {
        using var directory = new TemporaryDirectory();
        var builtInRoot = Path.Combine(directory.Path, "Web");
        Directory.CreateDirectory(builtInRoot);
        File.WriteAllText(Path.Combine(builtInRoot, "dashboard.html"), "<html>classic</html>");

        var manager = new SkinManager(Path.Combine(directory.Path, "Skins"), builtInRoot, "dashboard.html");
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "id": "community", "name": "Community Skin" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html>community</html>"
            });

        var runtime = manager.Import(package);

        Assert.Equal("community", runtime.Id);
        Assert.False(runtime.IsBuiltIn);
        Assert.Equal("Community Skin", runtime.DisplayName);
        Assert.True(Directory.Exists(runtime.RootDirectory));
        Assert.True(File.Exists(Path.Combine(runtime.RootDirectory, "index.html")));

        var installed = Assert.Single(manager.GetInstalledSkins());
        Assert.Equal("community", installed.Id);
        Assert.True(File.Exists(Path.Combine(manager.SkinsDirectory, Path.GetFileName(installed.PackagePath!))));
    }

    [Fact]
    public void SkinManager_TryResolve_MissingSkinReturnsFalseAndBuiltInStillWorks()
    {
        using var directory = new TemporaryDirectory();
        var builtInRoot = Path.Combine(directory.Path, "Web");
        Directory.CreateDirectory(builtInRoot);
        File.WriteAllText(Path.Combine(builtInRoot, "dashboard.html"), "<html>classic</html>");

        var manager = new SkinManager(Path.Combine(directory.Path, "Skins"), builtInRoot, "dashboard.html");

        Assert.False(manager.TryResolve("does-not-exist", out _, out var error));
        Assert.Contains("not installed", error);

        Assert.True(manager.TryResolve(SkinManager.BuiltInId, out var builtIn, out _));
        Assert.NotNull(builtIn);
        Assert.True(builtIn!.IsBuiltIn);
    }

    [Fact]
    public void ReadPreviewBytes_ReturnsEmbeddedPreviewAndPackagePathIsDiscoverable()
    {
        using var directory = new TemporaryDirectory();
        var builtInRoot = Path.Combine(directory.Path, "Web");
        Directory.CreateDirectory(builtInRoot);
        File.WriteAllText(Path.Combine(builtInRoot, "dashboard.html"), "<html>classic</html>");

        var manager = new SkinManager(Path.Combine(directory.Path, "Skins"), builtInRoot, "dashboard.html");
        var package = CreatePackage(
            directory.Path,
            """
            { "schemaVersion": 1, "id": "preview-skin", "name": "Preview Skin", "preview": "preview.png" }
            """,
            new Dictionary<string, string>
            {
                ["index.html"] = "<html>preview</html>",
                ["preview.png"] = "preview-bytes"
            });

        var runtime = manager.Import(package);
        var bytes = SkinPackageReader.ReadPreviewBytes(
            runtime.Descriptor.PackagePath!,
            runtime.Descriptor.Manifest.Preview);

        Assert.NotNull(bytes);
        Assert.Equal("preview-bytes", Encoding.UTF8.GetString(bytes!));
        Assert.Equal(runtime.Descriptor.PackagePath, manager.GetPackagePath("preview-skin"));
        Assert.Null(manager.GetPackagePath(SkinManager.BuiltInId));
    }

    private static string CreatePackage(
        string directory,
        string manifestJson,
        Dictionary<string, string> files,
        string packageName = "skin.boostskin")
    {
        var path = Path.Combine(directory, packageName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddTextEntry(archive, "skin.json", manifestJson);
        foreach (var pair in files)
            AddTextEntry(archive, pair.Key, pair.Value);

        return path;
    }

    private static void AddTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"boostdeck-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
