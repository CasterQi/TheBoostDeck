using System.Runtime.InteropServices;
using Microsoft.Win32;
using TheBoostDeck.Core;

namespace TheBoostDeck.App;

/// <summary>
/// Associates .boostskin packages with the current executable so a double-click in
/// Explorer imports and applies the skin. Everything is written under HKCU and
/// needs no administrator rights.
/// </summary>
internal static class SkinFileAssociation
{
    internal const string Extension = ".boostskin";
    internal const string ProgId = "TheBoostDeck.BoostSkin";
    internal const string CommandLineArgument = "--import-skin";

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static void EnsureRegistered(IAppLogger logger)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            logger.Info("The executable path is unavailable; the .boostskin association was skipped.");
            return;
        }

        var command = $"\"{exePath}\" {CommandLineArgument} \"%1\"";
        var changed = false;

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        if (classes is null)
        {
            logger.Info("The user class registry could not be opened; the .boostskin association was skipped.");
            return;
        }

        using (var extension = classes.CreateSubKey(Extension))
        {
            changed |= SetDefaultValue(extension, ProgId);
            changed |= SetValue(extension, "Content Type", "application/x-theboostdeck-skin");
        }

        using (var progId = classes.CreateSubKey(ProgId))
        {
            changed |= SetDefaultValue(progId, "TheBoostDeck Skin");

            using (var icon = progId.CreateSubKey("DefaultIcon"))
                changed |= SetDefaultValue(icon, $"\"{exePath}\",0");

            using (var commandKey = progId.CreateSubKey(@"shell\open\command"))
                changed |= SetDefaultValue(commandKey, command);
        }

        if (!changed)
            return;

        NotifyShell();
        logger.Info($"Registered the {Extension} file association to '{exePath}'.");
    }

    private static bool SetDefaultValue(RegistryKey key, string value) => SetValue(key, string.Empty, value);

    private static bool SetValue(RegistryKey key, string name, string value)
    {
        var current = key.GetValue(name);
        if (current is string existing && existing.Equals(value, StringComparison.OrdinalIgnoreCase))
            return false;

        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Explorer picks the association up on its next refresh anyway.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
