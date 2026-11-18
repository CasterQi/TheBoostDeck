using System.Globalization;
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
    private static readonly int[] RefreshIntervalOptions = [100, 250, 500, 1000, 2000, 5000];
    private static readonly int[] TrayZoomOptions = [50, 75, 100, 125, 150, 175, 200];

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
    private CancellationTokenSource? _skinImportCts;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingSkinImports = new();
    private volatile bool _skinImportsReady;
    private string? _webDirectory;
    private StartupSplash? _startupSplash;
    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayIconImage;
    private string? _logoPath;
    private WinForms.ToolStripMenuItem? _toggleWindowMenuItem;
    private WinForms.ToolStripMenuItem? _screensMenuItem;
    private WinForms.ToolStripMenuItem? _skinMenuItem;
    private WinForms.ToolStripMenuItem? _topmostMenuItem;
    private WinForms.ToolStripMenuItem? _refreshMenuItem;
    private readonly Dictionary<int, WinForms.ToolStripMenuItem> _refreshMenuItems = new();
    private WinForms.ToolStripMenuItem? _zoomMenuItem;
    private readonly Dictionary<int, WinForms.ToolStripMenuItem> _zoomMenuItems = new();
    private WinForms.ToolStripMenuItem? _startupMenuItem;
    private WinForms.ToolStripMenuItem? _languageMenuItem;
    private WinForms.ToolStripMenuItem? _chineseMenuItem;
    private WinForms.ToolStripMenuItem? _englishMenuItem;
    private WinForms.ToolStripMenuItem? _versionMenuItem;
    private WinForms.ToolStripMenuItem? _exitMenuItem;

    internal bool IsExitRequested => _exitRequested;
    internal string Language => _settings.Language;
    internal int RefreshIntervalMs => _settings.RefreshIntervalMs;
    internal int ZoomPercent => _settings.ZoomPercent;
    internal IAppLogger Logger => _logger ?? throw new InvalidOperationException("The application logger is not initialized.");
    internal TelemetryService Telemetry => _telemetry ?? throw new InvalidOperationException("The telemetry service is not initialized.");
    internal WindowSettingsStore SettingsStore => _settingsStore ?? throw new InvalidOperationException("The settings store is not initialized.");
    internal SkinManager Skins => _skinManager ?? throw new InvalidOperationException("The skin manager is not initialized.");
    internal SkinRuntime ActiveSkin => _activeSkin ?? throw new InvalidOperationException("The active skin is not initialized.");
    internal string WebDirectory => _webDirectory ?? throw new InvalidOperationException("The web asset directory is not initialized.");
    internal string ThemesDirectory => Path.Combine(Path.GetDirectoryName(WebDirectory)!, "themes");
    internal bool BuiltInSkinHidden => _settings.HideBuiltInSkin;

    internal void CompleteStartup()
    {
        _startupSplash?.Close();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\TheBoostDeck.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            // A .boostskin double-click starts a second process; hand the package to
            // the instance that already owns the tray icon instead of dropping it.
            var forwarded = GetSkinArgumentPath(e.Args);
            if (forwarded is not null)
                SkinImportChannel.TrySend(forwarded);

            Shutdown();
            return;
        }

        _skinImportCts = new CancellationTokenSource();
        SkinImportChannel.Listen(HandleSkinImportRequest, _skinImportCts.Token);

        StartupSplash? splash = null;
        try
        {
            _startupSplash = new StartupSplash();
            splash = _startupSplash;
        }
        catch (Exception exception)
        {
            // The startup indicator must never prevent the application from starting.
            _logger?.Error("The native startup indicator could not be shown.", exception);
        }

        try
        {
            _logger = new FileLogger();
            _settingsStore = new WindowSettingsStore(_logger);
            _settings = _settingsStore.Load();
            _settings = _settings with { RefreshIntervalMs = NormalizeRefreshInterval(_settings.RefreshIntervalMs) };
            splash?.Update(_settings.Language, "Starting");

            _webDirectory = WebAssetExtractor.ExtractDashboard();
            _logoPath = Path.Combine(_webDirectory, "logo.png");
            _skinManager = new SkinManager(
                Path.Combine(Path.GetDirectoryName(_webDirectory)!, "Skins"),
                _webDirectory,
                "dashboard.html");

            // The default skins ship inside the app folder. Install them once,
            // before resolving the active skin, so a fresh machine opens into a
            // real skin instead of the classic dashboard.
            BundledSkinInstaller.EnsureInstalled(_skinManager, _logger);

            var startupSkinPath = GetSkinArgumentPath(e.Args);
            var startupSkin = TryImportSkinAtStartup(startupSkinPath);
            if (startupSkin is not null)
            {
                // Launched from a .boostskin double-click: apply the imported package
                // before the first navigation so the window opens straight into it.
                _activeSkin = startupSkin;
                _settings = _settings with { SkinId = startupSkin.Id };
                _settingsStore.Save(_settings);
            }
            else if (!_skinManager.TryResolve(_settings.SkinId, out var activeSkin, out var skinError) || activeSkin is null)
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

            // The window opens with the layout that belongs to the active skin.
            var startView = _settings.FindSkinState(_activeSkin.Id);
            if (startView is not null)
            {
                _settings = _settings with
                {
                    Left = startView.Left ?? _settings.Left,
                    Top = startView.Top ?? _settings.Top,
                    Width = Math.Clamp(startView.Width ?? _settings.Width, 420, 7680),
                    Height = Math.Clamp(startView.Height ?? _settings.Height, 560, 4320),
                    ZoomPercent = Math.Clamp(startView.ZoomPercent ?? _settings.ZoomPercent, 50, 200)
                };
            }

            splash?.Update(_settings.Language, "CheckingDriver");
            PawnIoAutoInstaller.TryInstallIfMissing(
                _logger,
                () => splash?.Update(_settings.Language, "InstallingDriver"));

            try
            {
                splash?.Update(_settings.Language, "StartingCollector");
                _collector = new LhmTelemetryCollector();
                _logger.Info("LibreHardwareMonitor collector initialized.");
            }
            catch (Exception exception)
            {
                _logger.Error("Hardware collector initialization failed; the dashboard will use degraded data.", exception);
                _collector = new FaultedTelemetryCollector(UiText.Get(_settings.Language, "HardwareCollectorFailed"));
            }

            splash?.Update(_settings.Language, "StartingInterface");
            _telemetry = new TelemetryService(_collector, TimeSpan.FromMilliseconds(_settings.RefreshIntervalMs));
            _window = new MainWindow(this, _settings);
            CreateTrayIcon();
            _window.Show();
            _window.Activate();

            try
            {
                SkinFileAssociation.EnsureRegistered(_logger!);
            }
            catch (Exception exception)
            {
                _logger?.Error($"The {SkinFileAssociation.Extension} file association could not be registered.", exception);
            }

            _skinImportsReady = true;
            while (_pendingSkinImports.TryDequeue(out var pendingPath))
                _window.Dispatcher.BeginInvoke(new Action(() => ImportSkinFromExternal(pendingPath)));

            _telemetry.Start();
            _logger?.Info("TheBoostDeck started.");
        }
        catch (Exception exception)
        {
            splash?.Dispose();
            _logger?.Error("Application startup failed.", exception);
            System.Windows.MessageBox.Show(
                UiText.Get(_settings.Language, "ApplicationStartupFailed"),
                "TheBoostDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static string? GetSkinArgumentPath(string[] args)
    {
        if (args.Length == 0)
            return null;

        for (var index = 0; index < args.Length; index++)
        {
            var value = args[index];
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (value.Equals(SkinFileAssociation.CommandLineArgument, StringComparison.OrdinalIgnoreCase))
                return index + 1 < args.Length ? args[index + 1] : null;

            if (value.EndsWith(SkinFileAssociation.Extension, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private SkinRuntime? TryImportSkinAtStartup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _skinManager is null)
            return null;

        try
        {
            var runtime = _skinManager.Import(path);
            _logger?.Info($"Skin '{runtime.Id}' was imported at startup from '{path}'.");
            return runtime;
        }
        catch (Exception exception)
        {
            _logger?.Error($"Skin '{path}' could not be imported at startup.", exception);
            return null;
        }
    }

    private void HandleSkinImportRequest(string path)
    {
        if (!_skinImportsReady)
        {
            _pendingSkinImports.Enqueue(path);
            return;
        }

        _window?.Dispatcher.BeginInvoke(new Action(() => ImportSkinFromExternal(path)));
    }

    private void ImportSkinFromExternal(string path)
    {
        if (_skinManager is null || _window is null)
            return;

        try
        {
            var runtime = _skinManager.Import(path);
            _logger?.Info($"Skin '{runtime.Id}' was imported from '{path}'.");

            // A skin opened from Explorer should be visible right away.
            _window.Show();
            _window.Activate();

            if (!TryActivateSkin(runtime))
                ShowSkinError();
        }
        catch (Exception exception)
        {
            _logger?.Error($"Skin '{path}' could not be imported.", exception);
            ShowSkinError();
        }
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

        _refreshMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "RefreshRate"));
        foreach (var milliseconds in RefreshIntervalOptions)
        {
            var intervalItem = new WinForms.ToolStripMenuItem(FormatRefreshInterval(Language, milliseconds))
            {
                Checked = _settings.RefreshIntervalMs == milliseconds
            };
            intervalItem.Click += (_, _) => SetRefreshInterval(milliseconds);
            _refreshMenuItem.DropDownItems.Add(intervalItem);
            _refreshMenuItems[milliseconds] = intervalItem;
        }
        menu.Items.Add(_refreshMenuItem);

        _zoomMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "Zoom"));
        foreach (var percent in TrayZoomOptions)
        {
            var zoomItem = new WinForms.ToolStripMenuItem(FormatZoomPercent(percent))
            {
                Checked = _settings.ZoomPercent == percent
            };
            zoomItem.Click += (_, _) => SetZoom(percent);
            _zoomMenuItem.DropDownItems.Add(zoomItem);
            _zoomMenuItems[percent] = zoomItem;
        }
        menu.Items.Add(_zoomMenuItem);

        _startupMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "StartWithWindows"))
        {
            Checked = StartupRegistration.IsEnabled
        };
        _startupMenuItem.Click += (_, _) => ToggleStartupFromTray();
        menu.Items.Add(_startupMenuItem);

        menu.Opening += (_, _) =>
        {
            UpdateStartupMenu();
            UpdateZoomMenu();
        };

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
        var embedded = LoadEmbeddedTrayIcon();
        if (embedded is not null)
            return embedded;

        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
            return null;

        try
        {
            using var source = new Drawing.Bitmap(logoPath);
            using var resized = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Drawing.Graphics.FromImage(resized))
            {
                graphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Drawing.Rectangle(0, 0, 32, 32));
            }

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

    private static Drawing.Icon? LoadEmbeddedTrayIcon()
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("TheBoostDeck.Assets.app.ico");
            if (stream is null)
                return null;

            using var icon = new Drawing.Icon(stream, new Drawing.Size(32, 32));
            return (Drawing.Icon)icon.Clone();
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
        if (!_settings.HideBuiltInSkin)
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

        return TryActivateSkin(runtime);
    }

    private bool TryActivateSkin(SkinRuntime runtime)
    {
        if (_window is null)
            return false;

        var previousSkin = _activeSkin;
        var previousSettings = _settings;
        try
        {
            // Capture also stores the outgoing skin's own placement and zoom.
            var captured = _window.CaptureSettings();
            var state = captured.FindSkinState(runtime.Id);

            if (state is not null)
            {
                _settings = _settings with
                {
                    SkinId = runtime.Id,
                    Width = state.Width ?? _settings.Width,
                    Height = state.Height ?? _settings.Height,
                    Left = state.Left ?? _settings.Left,
                    Top = state.Top ?? _settings.Top,
                    ZoomPercent = Math.Clamp(state.ZoomPercent ?? _settings.ZoomPercent, 50, 200)
                };
                _window.ApplySkinPlacement(state);
            }
            else
            {
                // First visit for this skin: keep the current window geometry and zoom,
                // which then becomes this skin's remembered layout.
                _settings = _settings with { SkinId = runtime.Id };
            }

            _activeSkin = runtime;
            _window.LoadSkin(runtime);
            _window.ApplyZoom(_settings.ZoomPercent);
            _settingsStore?.Save(_window.CaptureSettings());
            _logger?.Info($"Active skin set to '{runtime.Id}'.");
            return true;
        }
        catch (Exception exception)
        {
            _activeSkin = previousSkin;
            _settings = previousSettings;
            _settingsStore?.Save(_window.CaptureSettings());
            _logger?.Error($"Skin '{runtime.Id}' could not be activated.", exception);
            return false;
        }
    }

    internal object BuildSkinCatalog()
    {
        if (_skinManager is null)
            throw new InvalidOperationException("The skin manager is not initialized.");

        var skins = new List<object>();
        if (!_settings.HideBuiltInSkin)
        {
            skins.Add(new
            {
                id = SkinManager.BuiltInId,
                name = UiText.Get(Language, "ClassicSkin"),
                version = "built-in",
                author = "TheBoostDeck",
                description = UiText.Get(Language, "ClassicSkinDescription"),
                isBuiltIn = true,
                canExport = false,
                previewUrl = FileToDataUrl(Path.Combine(WebDirectory, "logo.png"), "image/png")
            });
        }

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
            builtInHidden = _settings.HideBuiltInSkin,
            skins
        };
    }

    internal SkinRuntime? TryImportSkinForUi(out bool cancelled, out string? error)
    {
        cancelled = false;
        error = null;
        if (_skinManager is null)
        {
            error = UiText.Get(Language, "SkinImportFailed");
            return null;
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
            return null;
        }

        try
        {
            var runtime = _skinManager.Import(dialog.FileName);

            // Importing another build of the visible skin must reload it right away;
            // otherwise the running page keeps the old package until the user
            // switches skins back and forth.
            if (runtime.Id.Equals(ActiveSkin.Id, StringComparison.OrdinalIgnoreCase) &&
                !TryActivateSkin(runtime))
            {
                error = UiText.Get(Language, "SkinLoadFailed");
                return null;
            }

            return runtime;
        }
        catch (Exception exception)
        {
            _logger?.Error("Skin import from the skin manager failed.", exception);
            error = UiText.Get(Language, "SkinImportFailed");
            return null;
        }
    }

    internal bool TryDeleteSkinForUi(string? skinId, out string? error)
    {
        error = null;
        if (_skinManager is null)
        {
            error = UiText.Get(Language, "SkinDeleteFailed");
            return false;
        }

        if (string.IsNullOrWhiteSpace(skinId))
        {
            error = UiText.Get(Language, "SkinDeleteFailed");
            return false;
        }

        var requested = skinId.Trim();
        if (requested.Equals(SkinManager.BuiltInId, StringComparison.OrdinalIgnoreCase))
            return TryHideBuiltInSkin(out error);

        if (ActiveSkin.Id.Equals(requested, StringComparison.OrdinalIgnoreCase) &&
            !TrySetSkin(SkinManager.BuiltInId))
        {
            _logger?.Error($"Skin '{requested}' could not be deleted because the built-in skin could not be restored.");
            error = UiText.Get(Language, "SkinDeleteFailed");
            return false;
        }

        try
        {
            _skinManager.Delete(requested);
            _logger?.Info($"Skin '{requested}' was deleted.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
        {
            _logger?.Error($"Skin '{requested}' could not be deleted.", exception);
            error = UiText.Get(Language, "SkinDeleteFailed");
            return false;
        }
    }

    /// <summary>
    /// The built-in skin is embedded in the application, so removing it means
    /// hiding it from the skin list while it stays available as the fallback.
    /// </summary>
    private bool TryHideBuiltInSkin(out string? error)
    {
        error = null;
        if (_settings.HideBuiltInSkin)
            return true;

        var installed = _skinManager!.GetInstalledSkins();
        if (installed.Count == 0)
        {
            error = UiText.Get(Language, "SkinDeleteLast");
            return false;
        }

        if (ActiveSkin.Id.Equals(SkinManager.BuiltInId, StringComparison.OrdinalIgnoreCase))
        {
            var replacement = installed.FirstOrDefault(item => TrySetSkin(item.Id));
            if (replacement is null)
            {
                error = UiText.Get(Language, "SkinDeleteFailed");
                return false;
            }
            _logger?.Info($"Switched to skin '{replacement.Id}' before hiding the built-in skin.");
        }

        _settings = _settings with { HideBuiltInSkin = true };
        if (_window is not null)
            _settingsStore?.Save(_window.CaptureSettings());
        else
            _settingsStore?.Save(_settings);
        _logger?.Info("The built-in skin was removed from the skin list.");
        return true;
    }

    internal bool TryRestoreBuiltInSkin(out string? error)
    {
        error = null;
        if (!_settings.HideBuiltInSkin)
            return true;

        _settings = _settings with { HideBuiltInSkin = false };
        if (_window is not null)
            _settingsStore?.Save(_window.CaptureSettings());
        else
            _settingsStore?.Save(_settings);
        _logger?.Info("The built-in skin was restored to the skin list.");
        return true;
    }

    internal bool TryRestoreBundledSkins(out string? error)
    {
        error = null;
        if (_skinManager is null)
        {
            error = UiText.Get(Language, "SkinBundledRestoreFailed");
            return false;
        }

        try
        {
            BundledSkinInstaller.RestoreAll(_skinManager, _logger);
            _logger?.Info("Bundled skins were restored from the application package.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SkinPackageException)
        {
            _logger?.Error("Bundled skins could not be restored.", exception);
            error = UiText.Get(Language, "SkinBundledRestoreFailed");
            return false;
        }
    }

    internal bool TryExportSkinForUi(string? skinId, out bool cancelled, out string? error)    {
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

    private void ToggleStartupFromTray()
    {
        if (TrySetStartup(!StartupRegistration.IsEnabled, out var error))
            return;

        _logger?.Error($"Could not update startup registration: {error}");
        var message = UiText.Get(Language, "StartupFailed");
        var title = UiText.Get(Language, "StartupFailedTitle");
        if (_window is not null)
            System.Windows.MessageBox.Show(_window, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal bool TrySetStartup(bool enabled, out string? error)
    {
        if (!StartupRegistration.TrySetEnabled(enabled, out error))
        {
            UpdateStartupMenu();
            return false;
        }

        UpdateStartupMenu();
        _window?.SendHostSettings();
        _logger?.Info($"Start with Windows set to {enabled}.");
        return true;
    }

    private void UpdateStartupMenu()
    {
        if (_startupMenuItem is not null)
            _startupMenuItem.Checked = StartupRegistration.IsEnabled;
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

    internal void SetRefreshInterval(int milliseconds)
    {
        var normalized = NormalizeRefreshInterval(milliseconds);
        if (_settings.RefreshIntervalMs == normalized)
        {
            UpdateLocalizedChrome();
            return;
        }

        _settings = _settings with { RefreshIntervalMs = normalized };
        _telemetry?.SetInterval(TimeSpan.FromMilliseconds(normalized));
        UpdateLocalizedChrome();
        _settingsStore?.Save(_window?.CaptureSettings() ?? _settings);
        _window?.SendHostSettings();
        _logger?.Info($"Telemetry refresh interval set to {normalized} ms.");
    }

    internal void SetZoom(int percent)
    {
        var normalized = NormalizeZoom(percent);
        if (_settings.ZoomPercent == normalized)
        {
            _window?.ApplyZoom(normalized);
            UpdateZoomMenu();
            return;
        }

        _settings = _settings with { ZoomPercent = normalized };
        _window?.ApplyZoom(normalized);
        UpdateZoomMenu();
        _settingsStore?.Save(_window?.CaptureSettings() ?? _settings);
        _window?.SendHostSettings();
        _logger?.Info($"WebView zoom set to {normalized}%.");
    }

    internal void SetZoomFromUi(int percent) => SetZoom(percent);

    private static int NormalizeZoom(int percent) => Math.Clamp(percent, 50, 200);

    private static string FormatZoomPercent(int percent) => $"{percent}%";

    private void UpdateZoomMenu()
    {
        if (_zoomMenuItem is not null)
            _zoomMenuItem.Text = UiText.Get(Language, "Zoom");
        foreach (var pair in _zoomMenuItems)
            pair.Value.Checked = _settings.ZoomPercent == pair.Key;
    }

    private static int NormalizeRefreshInterval(int milliseconds)
    {
        if (milliseconds <= 0)
            return 1000;

        var closest = RefreshIntervalOptions[0];
        var closestDistance = int.MaxValue;
        foreach (var option in RefreshIntervalOptions)
        {
            var distance = Math.Abs(option - milliseconds);
            if (distance < closestDistance)
            {
                closest = option;
                closestDistance = distance;
            }
        }
        return closest;
    }

    private static string FormatRefreshInterval(string? language, int milliseconds)
    {
        var seconds = (milliseconds / 1000d).ToString("0.##", CultureInfo.InvariantCulture);
        return UiText.Normalize(language) == UiText.English
            ? $"{seconds} s"
            : $"{seconds} 秒";
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
        if (_refreshMenuItem is not null)
            _refreshMenuItem.Text = UiText.Get(Language, "RefreshRate");
        if (_zoomMenuItem is not null)
            _zoomMenuItem.Text = UiText.Get(Language, "Zoom");
        foreach (var pair in _zoomMenuItems)
            pair.Value.Checked = _settings.ZoomPercent == pair.Key;
        if (_startupMenuItem is not null)
        {
            _startupMenuItem.Text = UiText.Get(Language, "StartWithWindows");
            _startupMenuItem.Checked = StartupRegistration.IsEnabled;
        }
        foreach (var pair in _refreshMenuItems)
        {
            pair.Value.Text = FormatRefreshInterval(Language, pair.Key);
            pair.Value.Checked = _settings.RefreshIntervalMs == pair.Key;
        }
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

    internal void SetTopmostFromUi(bool enabled) => SetTopmost(enabled);

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
        _startupSplash?.Dispose();
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
            _skinImportCts?.Cancel();
            _skinImportCts?.Dispose();
            _skinImportCts = null;
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
