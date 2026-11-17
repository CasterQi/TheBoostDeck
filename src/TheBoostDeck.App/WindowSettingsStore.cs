using System.Text;
using System.Text.Json;
using TheBoostDeck.Core;

namespace TheBoostDeck.App;

public sealed record WindowSettings
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 1440;
    public double Height { get; init; } = 900;
    public bool Topmost { get; init; } = true;
    public string Language { get; init; } = UiText.Chinese;
}

public sealed class WindowSettingsStore
{
    private readonly IAppLogger _logger;
    private readonly string _path;

    public WindowSettingsStore(IAppLogger logger)
    {
        _logger = logger;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheBoostDeck",
            "settings.json");
    }

    public WindowSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new WindowSettings();

            var settings = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(_path));
            if (settings is null)
                return new WindowSettings();

            return settings with
            {
                Width = Math.Clamp(settings.Width, 420, 7680),
                Height = Math.Clamp(settings.Height, 560, 4320),
                Language = UiText.Normalize(settings.Language)
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Error("Failed to load window settings; defaults will be used.", exception);
            return new WindowSettings();
        }
    }

    public void Save(WindowSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        var temporaryPath = Path.Combine(directory, $"settings.{Environment.ProcessId}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }
            _logger.Error("Failed to save window settings.", exception);
        }
    }
}
