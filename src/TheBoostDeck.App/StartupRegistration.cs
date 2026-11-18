using Microsoft.Win32;

namespace TheBoostDeck.App;

/// <summary>
/// Registers TheBoostDeck in the current user's Windows Run key so it starts
/// with the signed-in user.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TheBoostDeck";

    internal static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                var value = key?.GetValue(ValueName);
                return value is string text && !string.IsNullOrWhiteSpace(text);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                error = "Unable to open the current-user startup registry key.";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                error = null;
                return true;
            }

            var appHost = Path.Combine(AppContext.BaseDirectory, "TheBoostDeck.exe");
            var executable = File.Exists(appHost) ? appHost : Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                error = "Unable to locate the TheBoostDeck executable.";
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
