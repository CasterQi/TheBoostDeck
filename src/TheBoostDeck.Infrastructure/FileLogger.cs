using System.Text;
using System.Text.Json;
using TheBoostDeck.Core;

namespace TheBoostDeck.Infrastructure;

public sealed class FileLogger : IAppLogger
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private readonly object _sync = new();
    private readonly string _directory;

    public FileLogger()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheBoostDeck",
            "logs");
    }

    public void Info(string message) => Write("info", message, null);

    public void Error(string message, Exception? exception = null) => Write("error", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"theboostdeck-{DateTime.Now:yyyyMMdd}.log");
                RotateIfNeeded(path);

                var entry = new
                {
                    timestamp = DateTimeOffset.Now,
                    level,
                    message,
                    exception = exception?.ToString()
                };
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // Logging must never terminate hardware monitoring.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
            return;

        for (var index = 2; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var target = $"{path}.{index + 1}";
            if (!File.Exists(source))
                continue;
            if (File.Exists(target))
                File.Delete(target);
            File.Move(source, target);
        }

        var firstArchive = $"{path}.1";
        if (File.Exists(firstArchive))
            File.Delete(firstArchive);
        File.Move(path, firstArchive);
    }
}
