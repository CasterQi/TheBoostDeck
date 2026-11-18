using System.Reflection;

namespace TheBoostDeck.App;

public static class WebAssetExtractor
{
    public static string ExtractDashboard()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheBoostDeck",
            "Web");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "dashboard.html");
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("TheBoostDeck.Web.dashboard.html")
                           ?? throw new InvalidOperationException("The embedded dashboard resource is missing.");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);

        ExtractHostSkinManager(directory, assembly);
        ExtractLogo(directory, assembly);

        var themesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheBoostDeck",
            "themes");
        Directory.CreateDirectory(themesDirectory);
        var customTheme = Path.Combine(themesDirectory, "custom.css");
        if (!File.Exists(customTheme))
        {
            File.WriteAllText(customTheme,
                "/* TheBoostDeck custom theme. Override variables from :root in dashboard.html. */\n" +
                "/* Example: :root { --cyan: #66d9ff; --green: #46f0b0; }\n" +
                "   Keep the example commented or replace it with your own values. */\n");
        }

        return directory;
    }

    public static string ExtractLogo()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TheBoostDeck",
            "Web");
        Directory.CreateDirectory(directory);
        return ExtractLogo(directory, Assembly.GetExecutingAssembly());
    }

    private static string ExtractLogo(string directory, Assembly assembly)
    {
        var path = Path.Combine(directory, "logo.png");
        using var stream = assembly.GetManifestResourceStream("TheBoostDeck.Web.logo.png")
                           ?? throw new InvalidOperationException("The embedded logo resource is missing.");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);
        return path;
    }

    private static void ExtractHostSkinManager(string directory, Assembly assembly)
    {
        var path = Path.Combine(directory, "host-skin-manager.html");
        using var stream = assembly.GetManifestResourceStream("TheBoostDeck.Web.host-skin-manager.html")
                           ?? throw new InvalidOperationException("The embedded host skin manager resource is missing.");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);
    }
}
