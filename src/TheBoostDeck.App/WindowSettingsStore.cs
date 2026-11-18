using System.Text;
using System.Text.Json;
using TheBoostDeck.Core;

namespace TheBoostDeck.App;

public sealed record SkinViewState
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public int? ZoomPercent { get; init; }
}

public sealed record WindowSettings
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 1440;
    public double Height { get; init; } = 900;
    public bool Topmost { get; init; } = true;
    public string Language { get; init; } = UiText.Chinese;
    public int RefreshIntervalMs { get; init; } = 1000;
    public string SkinId { get; init; } = "classic";

    /// <summary>WebView zoom level in percent (50–200), equivalent to Ctrl + wheel.</summary>
    public int ZoomPercent { get; init; } = 100;

    /// <summary>
    /// Window placement, size, and zoom remembered per skin id, so switching skins
    /// restores the layout that skin was last shown with.
    /// </summary>
    public Dictionary<string, SkinViewState> SkinStates { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The built-in skin stays available as the internal fallback, but it can be
    /// removed from the skin list by the user. This flag records that choice.
    /// </summary>
    public bool HideBuiltInSkin { get; init; }

    public SkinViewState? FindSkinState(string skinId) =>
        SkinStates.TryGetValue(NormalizeSkinKey(skinId), out var state) ? state : null;

    public WindowSettings WithSkinState(string skinId, SkinViewState state)
    {
        var states = new Dictionary<string, SkinViewState>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SkinStates)
            states[pair.Key] = pair.Value;
        states[NormalizeSkinKey(skinId)] = state;
        return this with { SkinStates = states };
    }

    internal static string NormalizeSkinKey(string skinId) => skinId.Trim().ToLowerInvariant();
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

            var skinStates = new Dictionary<string, SkinViewState>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in settings.SkinStates ?? new Dictionary<string, SkinViewState>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                    continue;
                skinStates[WindowSettings.NormalizeSkinKey(pair.Key)] = pair.Value;
            }

            return settings with
            {
                Width = Math.Clamp(settings.Width, 420, 7680),
                Height = Math.Clamp(settings.Height, 560, 4320),
                Language = UiText.Normalize(settings.Language),
                RefreshIntervalMs = Math.Clamp(settings.RefreshIntervalMs, 100, 5000),
                ZoomPercent = Math.Clamp(settings.ZoomPercent <= 0 ? 100 : settings.ZoomPercent, 50, 200),
                SkinId = string.IsNullOrWhiteSpace(settings.SkinId) ? "classic" : settings.SkinId.Trim(),
                SkinStates = skinStates
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
