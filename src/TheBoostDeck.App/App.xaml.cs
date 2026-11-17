using System.Windows;
using System.Runtime.InteropServices;
using TheBoostDeck.Core;
using TheBoostDeck.Infrastructure;
using TheBoostDeck.Skins;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace TheBoostDeck.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private bool _exitRequested;
    private IAppLogger? _logger;
    private ITelemetryCollector? _collector;
    private TelemetryService? _telemetry;
    private MainWindow? _window;
    private WindowSettingsStore? _settingsStore;
    private WindowSettings _settings = new();
    private SkinManager? _skinManager;
    private SkinRuntime? _activeSkin;
    private string? _webDirectory;
    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayIconImage;
    private string? _logoPath;
    private WinForms.ToolStripMenuItem? _toggleWindowMenuItem;
    private WinForms.ToolStripMenuItem? _screensMenuItem;
    private WinForms.ToolStripMenuItem? _skinMenuItem;
    private WinForms.ToolStripMenuItem? _topmostMenuItem;
    private WinForms.ToolStripMenuItem? _languageMenuItem;
    private WinForms.ToolStripMenuItem? _chineseMenuItem;
    private WinForms.ToolStripMenuItem? _englishMenuItem;
    private WinForms.ToolStripMenuItem? _versionMenuItem;
    private WinForms.ToolStripMenuItem? _exitMenuItem;

    internal bool IsExitRequested => _exitRequested;
    internal string Language => _settings.Language;
    internal IAppLogger Logger => _logger ?? throw new InvalidOperationException("The application logger is not initialized.");
    internal TelemetryService Telemetry => _telemetry ?? throw new InvalidOperationException("The telemetry service is not initialized.");
    internal WindowSettingsStore SettingsStore => _settingsStore ?? throw new InvalidOperationException("The settings store is not initialized.");
    internal SkinManager Skins => _skinManager ?? throw new InvalidOperationException("The skin manager is not initialized.");
    internal SkinRuntime ActiveSkin => _activeSkin ?? throw new InvalidOperationException("The active skin is not initialized.");
    internal string WebDirectory => _webDirectory ?? throw new InvalidOperationException("The web asset directory is not initialized.");
    internal string ThemesDirectory => Path.Combine(Path.GetDirectoryName(WebDirectory)!, "themes");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\TheBoostDeck.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        _logger = new FileLogger();
        _settingsStore = new WindowSettingsStore(_logger);
        _settings = _settingsStore.Load();

        _webDirectory = WebAssetExtractor.ExtractDashboard();
        _logoPath = Path.Combine(_webDirectory, "logo.png");
        _skinManager = new SkinManager(
            Path.Combine(Path.GetDirectoryName(_webDirectory)!, "Skins"),
            _webDirectory,
            "dashboard.html");

        if (!_skinManager.TryResolve(_settings.SkinId, out var activeSkin, out var skinError) || activeSkin is null)
        {
            _logger.Error($"Skin '{_settings.SkinId}' could not be loaded; falling back to the built-in skin. {skinError}");
            _activeSkin = _skinManager.BuiltIn;
            _settings = _settings with { SkinId = SkinManager.BuiltInId };
            _settingsStore.Save(_settings);
        }
        else
        {
            _activeSkin = activeSkin;
        }

        try
        {
            _collector = new LhmTelemetryCollector();
            _logger.Info("LibreHardwareMonitor collector initialized.");
        }
        catch (Exception exception)
        {
            _logger.Error("Hardware collector initialization failed; the dashboard will use degraded data.", exception);
            _collector = new FaultedTelemetryCollector(UiText.Get(_settings.Language, "HardwareCollectorFailed"));
        }

        _telemetry = new TelemetryService(_collector, TimeSpan.FromSeconds(1));
        _window = new MainWindow(this, _settings);
        CreateTrayIcon();
        _window.Show();
        _telemetry.Start();
        _logger.Info("TheBoostDeck started.");
    }

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _toggleWindowMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "ShowHide"));
        _toggleWindowMenuItem.Click += (_, _) => ToggleWindow();
        menu.Items.Add(_toggleWindowMenuItem);

        _screensMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "MoveToDisplay"));
        _screensMenuItem.DropDownOpening += (_, _) => PopulateScreens(_screensMenuItem);
        menu.Items.Add(_screensMenuItem);

        _skinMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "Skins"));
        _skinMenuItem.DropDownOpening += (_, _) => PopulateSkins();
        menu.Items.Add(_skinMenuItem);

        _topmostMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "AlwaysOnTop"))
        {
            Checked = _settings.Topmost,
            CheckOnClick = true
        };
        _topmostMenuItem.CheckedChanged += (_, _) => SetTopmost(_topmostMenuItem.Checked);
        menu.Items.Add(_topmostMenuItem);

        _languageMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "Language"));
        _chineseMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "Chinese"))
        {
            Checked = UiText.Normalize(Language) == UiText.Chinese
        };
        _englishMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "English"))
        {
            Checked = UiText.Normalize(Language) == UiText.English
        };
        _chineseMenuItem.Click += (_, _) => SetLanguage(UiText.Chinese);
        _englishMenuItem.Click += (_, _) => SetLanguage(UiText.English);
        _languageMenuItem.DropDownItems.Add(_chineseMenuItem);
        _languageMenuItem.DropDownItems.Add(_englishMenuItem);
        menu.Items.Add(_languageMenuItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        _versionMenuItem = new WinForms.ToolStripMenuItem(BuildInfo.MenuText(Language))
        {
            Enabled = false
        };
        menu.Items.Add(_versionMenuItem);
        _exitMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "Exit"));
        _exitMenuItem.Click += (_, _) => RequestExit();
        menu.Items.Add(_exitMenuItem);

        _trayIconImage = LoadTrayIcon(_logoPath);
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = _trayIconImage ?? Drawing.SystemIcons.Application,
            Text = UiText.Get(Language, "TrayTooltip"),
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private static Drawing.Icon? LoadTrayIcon(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
            return null;

        try
        {
            using var source = new Drawing.Bitmap(logoPath);
            using var resized = new Drawing.Bitmap(source, new Drawing.Size(32, 32));
            var handle = resized.GetHicon();
            try
            {
                using var icon = Drawing.Icon.FromHandle(handle);
                return (Drawing.Icon)icon.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void PopulateScreens(WinForms.ToolStripMenuItem screensItem)
    {
        screensItem.DropDownItems.Clear();
        foreach (var screen in WinForms.Screen.AllScreens)
        {
            var primaryLabel = screen.Primary ? $" · {UiText.Get(Language, "PrimaryDisplay")}" : string.Empty;
            var label = $"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}{primaryLabel}";
            var item = new WinForms.ToolStripMenuItem(label);
            item.Click += (_, _) => _window?.MoveToScreen(screen);
            screensItem.DropDownItems.Add(item);
        }
    }

    private void PopulateSkins()
    {
        if (_skinMenuItem is null || _skinManager is null)
            return;

        _skinMenuItem.DropDownItems.Clear();
        AddSkinMenuItem(SkinManager.BuiltInId, UiText.Get(Language, "ClassicSkin"));

        try
        {
            foreach (var skin in _skinManager.GetInstalledSkins())
                AddSkinMenuItem(skin.Id, skin.Name);
        }
        catch (Exception exception)
        {
            _logger?.Error("Could not enumerate installed skins.", exception);
        }

        _skinMenuItem.DropDownItems.Add(new WinForms.ToolStripSeparator());
        var importItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "ImportSkin"));
        importItem.Click += (_, _) => ImportSkinFromDialog();
        _skinMenuItem.DropDownItems.Add(importItem);
    }

    private void AddSkinMenuItem(string skinId, string label)
    {
        if (_skinMenuItem is null)
            return;

        var item = new WinForms.ToolStripMenuItem(label)
        {
            Checked = string.Equals(ActiveSkin.Id, skinId, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) =>
        {
            if (!TrySetSkin(skinId))
                ShowSkinError();
        };
        _skinMenuItem.DropDownItems.Add(item);
    }

    internal bool TrySetSkin(string skinId)
    {
        if (_skinManager is null || _window is null)
            return false;

        if (!_skinManager.TryResolve(skinId, out var runtime, out var error) || runtime is null)
        {
            _logger?.Error($"Skin '{skinId}' could not be loaded: {error}");
            return false;
        }

        var previousSkin = _activeSkin;
        var previousSettings = _settings;
        try
        {
            _activeSkin = runtime;
            _settings = _settings with { SkinId = runtime.Id };
            _window.LoadSkin(runtime);
            _settingsStore?.Save(_window.CaptureSettings());
            _logger?.Info($"Active skin set to '{runtime.Id}'.");
            return true;
        }
        catch (Exception exception)
        {
            _activeSkin = previousSkin;
            _settings = previousSettings;
            _settingsStore?.Save(_window.CaptureSettings());
            _logger?.Error($"Skin '{skinId}' could not be activated.", exception);
            return false;
        }
    }

    internal object BuildSkinCatalog()
    {
        if (_skinManager is null)
            throw new InvalidOperationException("The skin manager is not initialized.");

        var skins = new List<object>
        {
            new
            {
                id = SkinManager.BuiltInId,
                name = UiText.Get(Language, "ClassicSkin"),
                version = "built-in",
                author = "TheBoostDeck",
                description = UiText.Get(Language, "ClassicSkinDescription"),
                isBuiltIn = true,
                canExport = false,
                previewUrl = FileToDataUrl(Path.Combine(WebDirectory, "logo.png"), "image/png")
            }
        };

        try
        {
            foreach (var skin in _skinManager.GetInstalledSkins())
            {
                var previewBytes = SkinPackageReader.ReadPreviewBytes(
                    skin.PackagePath!,
                    skin.Manifest.Preview);
                skins.Add(new
                {
                    id = skin.Id,
                    name = skin.Name,
                    version = skin.Version,
                    author = skin.Manifest.Author,
                    description = skin.Manifest.Description,
                    isBuiltIn = false,
                    canExport = true,
                    previewUrl = ImageDataUrl(previewBytes, skin.Manifest.Preview)
                });
            }
        }
        catch (Exception exception)
        {
            _logger?.Error("Could not build the skin catalog.", exception);
        }

        return new
        {
            type = "skin-catalog",
            activeId = ActiveSkin.Id,
            skins
        };
    }

    internal bool TryImportSkinForUi(out bool cancelled, out string? error)
    {
        cancelled = false;
        error = null;
        if (_skinManager is null)
        {
            error = UiText.Get(Language, "SkinImportFailed");
            return false;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get(Language, "ImportSkin"),
            Filter = UiText.Get(Language, "SkinFileFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            cancelled = true;
            return false;
        }

        try
        {
            _skinManager.Import(dialog.FileName);
            return true;
        }
        catch (Exception exception)
        {
            _logger?.Error("Skin import from the skin manager failed.", exception);
            error = UiText.Get(Language, "SkinImportFailed");
            return false;
        }
    }

    internal bool TryExportSkinForUi(string? skinId, out bool cancelled, out string? error)
    {
        cancelled = false;
        error = null;
        if (_skinManager is null)
        {
            error = UiText.Get(Language, "SkinExportFailed");
            return false;
        }

        var packagePath = _skinManager.GetPackagePath(skinId);
        if (packagePath is null || !File.Exists(packagePath))
        {
            error = UiText.Get(Language, "SkinBuiltInNoExport");
            return false;
        }

        var safeId = string.IsNullOrWhiteSpace(skinId) ? "skin" : skinId.Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.Get(Language, "SkinExportTitle"),
            Filter = UiText.Get(Language, "SkinExportFilter"),
            FileName = $"{safeId}.boostskin",
            DefaultExt = SkinPackageReader.PackageExtension,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            cancelled = true;
            return false;
        }

        try
        {
            File.Copy(packagePath, dialog.FileName, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            _logger?.Error("Skin export failed.", exception);
            error = UiText.Get(Language, "SkinExportFailed");
            return false;
        }
    }

    internal bool TryExportSkinSkillForUi(TelemetrySnapshot? latestSnapshot, out bool cancelled, out string? error)
    {
        cancelled = false;
        error = null;

        var machineName = latestSnapshot?.MachineName;
        if (string.IsNullOrWhiteSpace(machineName))
            machineName = Environment.MachineName;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.Get(Language, "SkinSkillExportTitle"),
            Filter = UiText.Get(Language, "SkinSkillExportFilter"),
            FileName = $"TheBoostDeck-Skin-Skill-{SafeFileStem(machineName)}.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            cancelled = true;
            return false;
        }

        TelemetryInventory? inventory = null;
        if (_collector is ITelemetryInventoryProvider provider)
        {
            try
            {
                inventory = provider.CaptureInventory();
            }
            catch (Exception exception)
            {
                _logger?.Error("Hardware inventory capture for skin skill export failed.", exception);
            }
        }

        if (inventory is null && latestSnapshot is not null)
            inventory = TelemetryInventory.FromSnapshot(latestSnapshot, "snapshot-fallback");

        if (inventory is null)
        {
            error = UiText.Get(Language, "SkinSkillNoTelemetry");
            return false;
        }

        try
        {
            var appVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.1.0";
            SkinSkillExporter.Export(dialog.FileName, inventory, appVersion);
            return true;
        }
        catch (Exception exception)
        {
            _logger?.Error("Skin skill export failed.", exception);
            error = UiText.Get(Language, "SkinSkillExportFailed");
            return false;
        }
    }

    private static string SafeFileStem(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character == ' ' || character == '-' || character == '_' || char.IsLetterOrDigit(character))
                builder.Append(character);
        }
        return builder.Length == 0 ? "machine" : builder.ToString();
    }

    private static string? FileToDataUrl(string path, string mimeType)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 2 * 1024 * 1024)
                return null;
            return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ImageDataUrl(byte[]? bytes, string? path)
    {
        if (bytes is null || bytes.Length == 0 || string.IsNullOrWhiteSpace(path))
            return null;

        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null
        };
        return mimeType is null ? null : $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private void ImportSkinFromDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get(Language, "ImportSkin"),
            Filter = UiText.Get(Language, "SkinFileFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true || _skinManager is null)
            return;

        try
        {
            var runtime = _skinManager.Import(dialog.FileName);
            if (!TrySetSkin(runtime.Id))
                ShowSkinError();
        }
        catch (Exception exception)
        {
            _logger?.Error("Skin import failed.", exception);
            ShowSkinError();
        }
    }

    private void ShowSkinError()
    {
        var message = UiText.Get(Language, "SkinLoadFailed");
        if (_window is not null)
            System.Windows.MessageBox.Show(_window, message, UiText.Get(Language, "SkinErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            System.Windows.MessageBox.Show(message, UiText.Get(Language, "SkinErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal void SetLanguage(string? language)
    {
        var normalized = UiText.Normalize(language);
        if (_settings.Language == normalized)
        {
            _window?.SendHostSettings();
            return;
        }

        _settings = _settings with { Language = normalized };
        UpdateLocalizedChrome();
        _settingsStore?.Save(_window?.CaptureSettings() ?? _settings);
        _window?.SendHostSettings();
        _logger?.Info($"UI language set to {normalized}.");
    }

    private void UpdateLocalizedChrome()
    {
        if (_toggleWindowMenuItem is not null)
            _toggleWindowMenuItem.Text = UiText.Get(Language, "ShowHide");
        if (_screensMenuItem is not null)
            _screensMenuItem.Text = UiText.Get(Language, "MoveToDisplay");
        if (_skinMenuItem is not null)
            _skinMenuItem.Text = UiText.Get(Language, "Skins");
        if (_topmostMenuItem is not null)
            _topmostMenuItem.Text = UiText.Get(Language, "AlwaysOnTop");
        if (_languageMenuItem is not null)
            _languageMenuItem.Text = UiText.Get(Language, "Language");
        if (_chineseMenuItem is not null)
        {
            _chineseMenuItem.Text = UiText.Get(Language, "Chinese");
            _chineseMenuItem.Checked = UiText.Normalize(Language) == UiText.Chinese;
        }
        if (_englishMenuItem is not null)
        {
            _englishMenuItem.Text = UiText.Get(Language, "English");
            _englishMenuItem.Checked = UiText.Normalize(Language) == UiText.English;
        }
        if (_versionMenuItem is not null)
            _versionMenuItem.Text = BuildInfo.MenuText(Language);
        if (_exitMenuItem is not null)
            _exitMenuItem.Text = UiText.Get(Language, "Exit");
        if (_trayIcon is not null)
            _trayIcon.Text = UiText.Get(Language, "TrayTooltip");
    }

    internal void ToggleWindow()
    {
        if (_window is null)
            return;
        if (_window.IsVisible)
            _window.Hide();
        else
            ShowWindow();
    }

    internal void ShowWindow()
    {
        if (_window is null)
            return;
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    internal void ToggleTopmost() => SetTopmost(!_settings.Topmost);

    private void SetTopmost(bool enabled)
    {
        _settings = _settings with { Topmost = enabled };
        if (_window is not null)
            _window.Topmost = enabled;
        if (_topmostMenuItem is not null && _topmostMenuItem.Checked != enabled)
            _topmostMenuItem.Checked = enabled;
        _settingsStore?.Save(_window?.CaptureSettings() ?? _settings);
        _window?.SendHostSettings();
    }

    internal void RequestExit()
    {
        if (_exitRequested)
            return;
        _exitRequested = true;
        if (_trayIcon is not null)
            _trayIcon.Visible = false;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // LibreHardwareMonitor may be inside a slow hardware refresh. Do not let
            // an unresponsive collector prevent the process from closing.
            var cleanup = Task.Run(() =>
            {
                if (_telemetry is not null)
                    _telemetry.DisposeAsync().AsTask().GetAwaiter().GetResult();
                if (_collector is IDisposable disposableCollector)
                    disposableCollector.Dispose();
            });

            if (!cleanup.Wait(TimeSpan.FromSeconds(2)))
                _logger?.Error("Telemetry cleanup timed out during shutdown; continuing exit.");
            else
                _logger?.Info("TheBoostDeck exited.");
        }
        catch (Exception exception)
        {
            _logger?.Error("An error occurred during application shutdown.", exception);
        }
        finally
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _trayIconImage?.Dispose();
            _trayIconImage = null;
            if (_ownsMutex)
            {
                _singleInstance?.ReleaseMutex();
                _ownsMutex = false;
            }
            _singleInstance?.Dispose();
        }

        base.OnExit(e);
    }

    private sealed class FaultedTelemetryCollector : ITelemetryCollector
    {
        private readonly string _reason;

        public FaultedTelemetryCollector(string reason) => _reason = reason;

        public Task<TelemetrySnapshot> CollectAsync(CancellationToken cancellationToken)
        {
            var snapshot = new TelemetrySnapshot
            {
                Cpu = new CpuMetrics
                {
                    Usage = MetricValue.Unavailable("%", _reason),
                    FanSpeed = MetricValue.Unavailable("RPM", _reason)
                },
                Gpu = new GpuMetrics { Usage = MetricValue.Unavailable("%", _reason) },
                Memory = new MemoryMetrics { Usage = MetricValue.Unavailable("%", _reason) },
                Storage = new StorageMetrics
                {
                    ReadRate = MetricValue.Unavailable("B/s", _reason),
                    WriteRate = MetricValue.Unavailable("B/s", _reason)
                },
                Network = new NetworkMetrics
                {
                    Download = MetricValue.Unavailable("B/s", _reason),
                    Upload = MetricValue.Unavailable("B/s", _reason)
                },
                System = new SystemMetrics
                {
                    MotherboardTemperature = MetricValue.Unavailable("°C", _reason),
                    Power = MetricValue.Unavailable("W", _reason)
                }
            };
            return Task.FromResult(snapshot);
        }
    }
}
