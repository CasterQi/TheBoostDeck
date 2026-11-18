using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using TheBoostDeck.Core;
using TheBoostDeck.Skins;
using WinForms = System.Windows.Forms;

namespace TheBoostDeck.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly IAppLogger _logger;
    private WindowSettings _lastSettings;
    private readonly DispatcherTimer _saveTimer;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private TelemetrySnapshot? _latestSnapshot;
    private bool _webReady;
    private bool _placementRestored;
    private bool _closed;
    private bool _skinHostConfigured;
    private bool _fallbackAttempted;
    private bool _reopenSkinManager;
    private readonly System.Windows.Media.Brush? _defaultBackground;
    private HwndSource? _windowSource;

    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const uint MonitorDefaultToNearest = 2;
    private const string SkinHost = "skin.theboostdeck";

    /// <summary>
    /// WebView2 renders in its own child HWND, so WM_NCHITTEST for the window
    /// edge never reaches the WPF window. This transparent overlay lives in the
    /// page instead: it shows the resize cursors and asks the host to start the
    /// native resize loop, keeping the visual area free of any painted border.
    /// </summary>
    private const string ResizeBandScript = """
(function () {
  var CONTAINER_ID = "tbd-resize-bands";
  var previous = document.getElementById(CONTAINER_ID);
  if (previous && previous.parentNode) previous.parentNode.removeChild(previous);

  var container = document.createElement("div");
  container.id = CONTAINER_ID;
  container.style.cssText = "position:fixed;inset:0;z-index:2147483000;pointer-events:none;";

  var zones = [
    ["top", "top:0;left:0;right:0;height:6px;", "ns-resize"],
    ["bottom", "bottom:0;left:0;right:0;height:6px;", "ns-resize"],
    ["left", "top:0;bottom:0;left:0;width:6px;", "ew-resize"],
    ["right", "top:0;bottom:0;right:0;width:6px;", "ew-resize"],
    ["top-left", "top:0;left:0;width:12px;height:12px;", "nwse-resize"],
    ["top-right", "top:0;right:0;width:12px;height:12px;", "nesw-resize"],
    ["bottom-left", "bottom:0;left:0;width:12px;height:12px;", "nesw-resize"],
    ["bottom-right", "bottom:0;right:0;width:12px;height:12px;", "nwse-resize"]
  ];

  zones.forEach(function (zone) {
    var band = document.createElement("div");
    band.style.cssText = "position:fixed;" + zone[1] +
      "cursor:" + zone[2] + ";pointer-events:auto;background:transparent;";
    band.addEventListener("pointerdown", function (event) {
      if (event.button !== 0) return;
      event.preventDefault();
      event.stopPropagation();
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ type: "begin-resize", edge: zone[0] });
      }
    });
    container.appendChild(band);
  });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", function (event) {
      var data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      if (data && data.type === "settings") {
        container.style.display = data.maximized ? "none" : "block";
      }
    });
  }

  (document.body || document.documentElement).appendChild(container);
})();
""";

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public MainWindow(App app, WindowSettings settings)
    {
        _app = app;
        _logger = app.Logger;
        _lastSettings = settings;
        InitializeComponent();
        _defaultBackground = Background;

        var logoPath = Path.Combine(_app.WebDirectory, "logo.png");
        Icon = LoadWindowIcon();
        if (Icon is null && File.Exists(logoPath))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(logoPath);
            image.EndInit();
            image.Freeze();
            Icon = image;
        }

        Width = settings.Width;
        Height = settings.Height;
        Topmost = settings.Topmost;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePlacement();
        };

        Loaded += OnLoaded;
        Closing += OnClosing;
        LocationChanged += (_, _) => RestartSaveTimer();
        SizeChanged += (_, _) => RestartSaveTimer();
        IsVisibleChanged += OnIsVisibleChanged;
        StateChanged += OnStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _app.Telemetry.SnapshotAvailable += OnSnapshotAvailable;
        _app.Telemetry.CollectionFailed += OnCollectionFailed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePlacement();
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TheBoostDeck",
                "WebView2");
            CoreWebView2EnvironmentOptions? environmentOptions = null;
            var debugPort = Environment.GetEnvironmentVariable("THEBOOSTDECK_DEBUG_PORT");
            if (!string.IsNullOrWhiteSpace(debugPort) && ushort.TryParse(debugPort, out _))
            {
                environmentOptions = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={debugPort}");
            }
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder, options: environmentOptions);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "themes.theboostdeck",
                _app.ThemesDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostSkinManager.VirtualHost,
                _app.WebDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            NavigateToSkin(_app.ActiveSkin);
        }
        catch (Exception exception)
        {
            _logger.Error("WebView2 initialization failed.", exception);
            _app.CompleteStartup();
            ShowInitializationError(exception);
        }
    }

    internal void LoadSkin(SkinRuntime skin)
    {
        if (WebView.CoreWebView2 is null)
        {
            _logger.Info($"Skin '{skin.Id}' will load when WebView2 is ready.");
            return;
        }

        NavigateToSkin(skin);
    }

    private void NavigateToSkin(SkinRuntime skin)
    {
        if (WebView.CoreWebView2 is null)
            return;

        if (_skinHostConfigured)
        {
            try
            {
                WebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(SkinHost);
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                _logger.Error("Could not clear the previous skin host mapping.", exception);
            }
        }

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            SkinHost,
            skin.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);
        _skinHostConfigured = true;
        if (!skin.IsBuiltIn)
            _fallbackAttempted = false;

        ApplySkinBackground(skin);
        _webReady = false;
        var entry = skin.EntryRelativePath.Replace('\\', '/');
        WebView.CoreWebView2.Navigate($"https://{SkinHost}/{entry}");
        _logger.Info($"Loading skin '{skin.Id}' from {skin.RootDirectory}.");
    }

    private void ApplySkinBackground(SkinRuntime skin)
    {
        var value = skin.Descriptor.Manifest.Window?.Background;
        if (string.IsNullOrWhiteSpace(value))
        {
            Background = _defaultBackground;
            return;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is System.Windows.Media.Color color)
            {
                Background = new SolidColorBrush(color);
                return;
            }
        }
        catch (FormatException)
        {
            _logger.Error($"Skin '{skin.Id}' has an invalid window background: {value}");
        }

        Background = _defaultBackground;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith($"https://{SkinHost}/", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith($"https://{HostSkinManager.VirtualHost}/", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        _logger.Error($"Blocked navigation outside the skin host: {e.Uri}");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _logger.Error($"Blocked a skin request to open a new window: {e.Uri}");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _logger.Error($"Skin navigation failed: {e.WebErrorStatus}.");
            if (!_fallbackAttempted && !_app.ActiveSkin.IsBuiltIn)
            {
                _fallbackAttempted = true;
                if (_app.TrySetSkin(SkinManager.BuiltInId))
                    _logger.Info("The selected skin could not be displayed; the built-in skin was restored.");
                else
                    _logger.Error("The built-in skin could not be restored after a skin navigation failure.");
                _app.CompleteStartup();
            }
            return;
        }

        _fallbackAttempted = false;
        _webReady = true;
        ApplyZoom(_app.ZoomPercent);
        InstallResizeBands();
        SendHostSettings();
        SendLatestSnapshot();
        _app.CompleteStartup();

        if (_reopenSkinManager)
        {
            _reopenSkinManager = false;
            ShowHostSkinManager();
        }
    }

    internal void ApplyZoom(int percent)
    {
        var factor = Math.Clamp(percent, 50, 200) / 100.0;
        try
        {
            WebView.ZoomFactor = factor;
        }
        catch (InvalidOperationException exception)
        {
            _logger.Error("The WebView zoom factor could not be applied yet.", exception);
        }
        catch (ArgumentException exception)
        {
            _logger.Error($"The WebView zoom factor {factor} was rejected.", exception);
        }
    }

    private static ImageSource? LoadWindowIcon()
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("TheBoostDeck.Assets.app.ico");
            if (stream is null)
                return null;

            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;

            // Prefer the frame closest to a 32 px taskbar icon; Windows scales from there.
            var frame = decoder.Frames.OrderBy(item => Math.Abs(item.PixelWidth - 32)).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void OnSnapshotAvailable(object? sender, TelemetrySnapshot snapshot)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed)
                return;
            _latestSnapshot = snapshot;
            if (IsVisible)
                SendLatestSnapshot();
        }));
    }

    private void OnCollectionFailed(object? sender, Exception exception)
    {
        _logger.Error("A telemetry collection cycle failed.", exception);
        _ = Dispatcher.BeginInvoke(new Action(() =>
            PostJson(new { type = "collection-error", messageKey = "collectionFailed" })));
    }

    private void SendLatestSnapshot()
    {
        if (_latestSnapshot is not null)
            PostJson(new { type = "snapshot", data = _latestSnapshot });
    }

    internal void SendHostSettings()
    {
        PostJson(new
        {
            type = "settings",
            topmost = Topmost,
            visible = IsVisible,
            maximized = WindowState == WindowState.Maximized,
            language = _app.Language,
            refreshIntervalMs = _app.RefreshIntervalMs,
            zoomPercent = _app.ZoomPercent,
            startup = StartupRegistration.IsEnabled,
            version = BuildInfo.Version,
            buildDate = BuildInfo.BuildDateCode
        });
    }

    private void SendSkinCatalog()
    {
        try
        {
            PostJson(_app.BuildSkinCatalog());
        }
        catch (Exception exception)
        {
            _logger.Error("Could not send the skin catalog.", exception);
        }
    }

    private void ShowHostSettings() => ShowHostPanel("general");

    private void ShowHostSkinManager() => ShowHostPanel("skins");

    private async void ShowHostPanel(string tab)
    {
        if (!_webReady || WebView.CoreWebView2 is null)
            return;

        var pageUrl = $"https://{HostSkinManager.VirtualHost}/{HostSkinManager.PageName}" +
                      $"?tab={Uri.EscapeDataString(tab)}&lang={Uri.EscapeDataString(_app.Language)}";
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(HostSkinManager.BuildOpenScript(pageUrl, tab));
        }
        catch (Exception exception)
        {
            _logger.Error("Could not show the host settings panel.", exception);
        }
    }

    private void SetStartupFromUi(bool enabled)
    {
        if (_app.TrySetStartup(enabled, out var error))
            return;

        PostJson(new
        {
            type = "settings-error",
            message = error ?? UiText.Get(_app.Language, "StartupFailed")
        });
        SendHostSettings();
    }

    private void OpenExternalFromUi(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open an external link.", exception);
        }
    }

    private void SelectSkinFromUi(string? skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
            return;

        if (skinId.Equals(_app.ActiveSkin.Id, StringComparison.OrdinalIgnoreCase))
        {
            SendSkinCatalog();
            return;
        }

        if (!_app.TrySetSkin(skinId))
        {
            PostJson(new
            {
                type = "skin-action",
                action = "select",
                ok = false,
                message = UiText.Get(_app.Language, "SkinLoadFailed")
            });
        }
    }

    private void ImportSkinFromUi()
    {
        var runtime = _app.TryImportSkinForUi(out var cancelled, out var error);
        if (runtime is null)
        {
            if (!cancelled)
            {
                PostJson(new
                {
                    type = "skin-action",
                    action = "import",
                    ok = false,
                    message = error ?? UiText.Get(_app.Language, "SkinImportFailed")
                });
            }
            return;
        }

        if (_app.ActiveSkin.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase))
        {
            // The package replaced the skin that is currently on screen. The new
            // page is already navigating, so reopen the manager on the Skins tab
            // once it finishes loading and shows the refreshed preview.
            _reopenSkinManager = true;
            return;
        }

        SendSkinCatalog();
        PostJson(new
        {
            type = "skin-action",
            action = "import",
            ok = true,
            message = UiText.Get(_app.Language, "SkinImported")
        });
    }

    private void ExportSkinFromUi(string? skinId)
    {        if (_app.TryExportSkinForUi(skinId, out var cancelled, out var error))
        {
            PostJson(new
            {
                type = "skin-action",
                action = "export",
                ok = true,
                message = UiText.Get(_app.Language, "SkinExported")
            });
        }
        else if (!cancelled)
        {
            PostJson(new
            {
                type = "skin-action",
                action = "export",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinExportFailed")
            });
        }
    }

    private void ExportSkinSkillFromUi()
    {
        if (_app.TryExportSkinSkillForUi(_latestSnapshot, out var cancelled, out var error))
        {
            PostJson(new
            {
                type = "skin-action",
                action = "export-skill",
                ok = true,
                message = UiText.Get(_app.Language, "SkinSkillExported")
            });
        }
        else if (!cancelled)
        {
            PostJson(new
            {
                type = "skin-action",
                action = "export-skill",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinSkillExportFailed")
            });
        }
    }

    private void DeleteSkinFromUi(string? skinId)
    {
        var requested = skinId?.Trim() ?? string.Empty;
        var deletingBuiltIn = requested.Equals(SkinManager.BuiltInId, StringComparison.OrdinalIgnoreCase);
        var wasActive = requested.Length > 0 && _app.ActiveSkin.Id.Equals(requested, StringComparison.OrdinalIgnoreCase);

        if (!_app.TryDeleteSkinForUi(skinId, out var error))
        {
            PostJson(new
            {
                type = "skin-action",
                action = "delete",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinDeleteFailed")
            });
            return;
        }

        // Removing the active skin forces a navigation to the fallback skin, which
        // tears down the control center iframe. Reopen it once the new page is up.
        _reopenSkinManager = wasActive;
        if (!wasActive)
        {
            SendSkinCatalog();
        }

        PostJson(new
        {
            type = "skin-action",
            action = "delete",
            ok = true,
            message = UiText.Get(_app.Language, deletingBuiltIn ? "SkinBuiltInRemoved" : "SkinDeleted")
        });
    }

    private void RestoreBuiltInSkinFromUi()
    {
        if (_app.TryRestoreBuiltInSkin(out var error))
        {
            SendSkinCatalog();
            PostJson(new
            {
                type = "skin-action",
                action = "restore",
                ok = true,
                message = UiText.Get(_app.Language, "SkinBuiltInRestored")
            });
        }
        else
        {
            PostJson(new
            {
                type = "skin-action",
                action = "restore",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinDeleteFailed")
            });
        }
    }

    private void RestoreBundledSkinsFromUi()
    {
        if (_app.TryRestoreBundledSkins(out var error))
        {
            SendSkinCatalog();
            PostJson(new
            {
                type = "skin-action",
                action = "restore",
                ok = true,
                message = UiText.Get(_app.Language, "SkinBundledRestored")
            });
        }
        else
        {
            PostJson(new
            {
                type = "skin-action",
                action = "restore",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinBundledRestoreFailed")
            });
        }
    }

    private void PostJson<T>(T message)
    {
        if (!_webReady || WebView.CoreWebView2 is null)
            return;
        try
        {
            WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, _jsonOptions));
        }
        catch (InvalidOperationException exception)
        {
            _logger.Error("Failed to post a message to the dashboard.", exception);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            if (!message.RootElement.TryGetProperty("type", out var type))
                return;

            switch (type.GetString())
            {
                case "toggle-topmost":
                    _app.ToggleTopmost();
                    break;
                case "hide-window":
                    Hide();
                    break;
                case "toggle-maximize":
                    ToggleWindowMaximized();
                    break;
                case "close-window":
                    _app.RequestExit();
                    break;
                case "begin-drag":
                    // Leave the WebMessageReceived handler before starting the modal drag
                    // loop, otherwise the WebView2 renderer cannot finish its callback and
                    // the matching mouse-up is swallowed, breaking later clicks.
                    Dispatcher.BeginInvoke(new Action(BeginWindowDrag), DispatcherPriority.Input);
                    break;
                case "begin-resize":
                    BeginWindowResize(message.RootElement.TryGetProperty("edge", out var edgeValue) ? edgeValue.GetString() : null);
                    break;
                case "set-language":
                    _app.SetLanguage(message.RootElement.TryGetProperty("language", out var language) ? language.GetString() : null);
                    break;
                case "open-settings":
                    ShowHostSettings();
                    break;
                case "open-skin-manager":
                    ShowHostSkinManager();
                    break;
                case "request-settings":
                    SendHostSettings();
                    break;
                case "set-refresh-interval":
                    if (message.RootElement.TryGetProperty("value", out var refreshValue) && refreshValue.TryGetInt32(out var interval))
                        _app.SetRefreshInterval(interval);
                    break;
                case "set-zoom":
                    if (message.RootElement.TryGetProperty("value", out var zoomValue) && zoomValue.TryGetInt32(out var zoomPercent))
                        _app.SetZoomFromUi(zoomPercent);
                    break;
                case "set-topmost":
                    _app.SetTopmostFromUi(message.RootElement.TryGetProperty("value", out var topmostValue) && topmostValue.ValueKind == JsonValueKind.True);
                    break;
                case "set-startup":
                    SetStartupFromUi(message.RootElement.TryGetProperty("enabled", out var startupValue) && startupValue.ValueKind == JsonValueKind.True);
                    break;
                case "open-external":
                    OpenExternalFromUi(message.RootElement.TryGetProperty("url", out var externalUrl) ? externalUrl.GetString() : null);
                    break;
                case "request-skins":
                    SendSkinCatalog();
                    break;
                case "set-skin":
                    SelectSkinFromUi(message.RootElement.TryGetProperty("id", out var skinId) ? skinId.GetString() : null);
                    break;
                case "import-skin":
                    ImportSkinFromUi();
                    break;
                case "export-skin":
                    ExportSkinFromUi(message.RootElement.TryGetProperty("id", out var exportId) ? exportId.GetString() : null);
                    break;
                case "delete-skin":
                    DeleteSkinFromUi(message.RootElement.TryGetProperty("id", out var deleteId) ? deleteId.GetString() : null);
                    break;
                case "restore-built-in-skin":
                    RestoreBuiltInSkinFromUi();
                    break;
                case "restore-bundled-skins":
                    RestoreBundledSkinsFromUi();
                    break;
                case "export-skill":
                    ExportSkinSkillFromUi();
                    break;
            }
        }
        catch (JsonException exception)
        {
            _logger.Error("Ignored an invalid dashboard message.", exception);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _app.Telemetry.SetPaused(!IsVisible);
        SendHostSettings();
        if (IsVisible)
            SendLatestSnapshot();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            return;
        }

        SetWindowChromeResizeBorder(WindowState == WindowState.Maximized ? 0 : 6);
        SendHostSettings();
    }

    private void ToggleWindowMaximized()
    {
        if (WindowState == WindowState.Maximized)
        {
            SetWindowChromeResizeBorder(6);
            WindowState = WindowState.Normal;
            return;
        }

        SetWindowChromeResizeBorder(0);
        WindowState = WindowState.Maximized;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.WorkArea;
                info.MaxPosition.X = work.Left - monitorInfo.Monitor.Left;
                info.MaxPosition.Y = work.Top - monitorInfo.Monitor.Top;
                info.MaxSize.X = work.Right - work.Left;
                info.MaxSize.Y = work.Bottom - work.Top;
                Marshal.StructureToPtr(info, lParam, true);
            }
        }
        else if (message == WmNcHitTest && WindowState != WindowState.Maximized && _windowSource is not null)
        {
            var point = new NativePoint
            {
                X = unchecked((short)(long)lParam),
                Y = unchecked((short)((long)lParam >> 16))
            };
            if (ScreenToClient(_windowSource.Handle, ref point) && GetClientRect(_windowSource.Handle, out var rect))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var border = Math.Max(1, (int)Math.Round(6 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY)));
                var left = point.X < border;
                var right = rect.Right - point.X <= border;
                var top = point.Y < border;
                var bottom = rect.Bottom - point.Y <= border;

                var hit = top && left ? HtTopLeft
                    : top && right ? HtTopRight
                    : bottom && left ? HtBottomLeft
                    : bottom && right ? HtBottomRight
                    : left ? HtLeft
                    : right ? HtRight
                    : top ? HtTop
                    : bottom ? HtBottom
                    : HtClient;
                if (hit != HtClient)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
            }
        }

        return IntPtr.Zero;
    }

    private void SetWindowChromeResizeBorder(double thickness)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
            chrome.ResizeBorderThickness = new Thickness(thickness);

        // Keep the HTML flush with the visual window edge. The resize band is
        // invisible and handled by WM_NCHITTEST, not by a painted WPF margin.
        WebView.Margin = new Thickness(0);
    }

    private void RestorePlacement()
    {
        if (_placementRestored)
            return;
        _placementRestored = true;

        if (_lastSettings.Left.HasValue && _lastSettings.Top.HasValue)
        {
            Left = _lastSettings.Left.Value;
            Top = _lastSettings.Top.Value;
            EnsureVisible();
            return;
        }

        var targetScreen = WinForms.Screen.PrimaryScreen
                           ?? WinForms.Screen.AllScreens.FirstOrDefault();
        if (targetScreen is not null)
            MoveToScreen(targetScreen);
    }

    internal void MoveToScreen(WinForms.Screen screen)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPixels = ActualWidth * dpi.DpiScaleX;
        var heightPixels = ActualHeight * dpi.DpiScaleY;
        var x = screen.WorkingArea.Left + (int)Math.Max(0, (screen.WorkingArea.Width - widthPixels) / 2d);
        var y = screen.WorkingArea.Top + (int)Math.Max(0, (screen.WorkingArea.Height - heightPixels) / 2d);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private async void InstallResizeBands()
    {
        try
        {
            if (WebView.CoreWebView2 is null)
                return;
            await WebView.CoreWebView2.ExecuteScriptAsync(ResizeBandScript);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not install the window resize bands.", exception);
        }
    }

    private void BeginWindowResize(string? edge)
    {
        var hitTest = edge switch
        {
            "left" => HtLeft,
            "right" => HtRight,
            "top" => HtTop,
            "bottom" => HtBottom,
            "top-left" => HtTopLeft,
            "top-right" => HtTopRight,
            "bottom-left" => HtBottomLeft,
            "bottom-right" => HtBottomRight,
            _ => HtClient
        };
        if (hitTest == HtClient)
            return;

        if (WindowState == WindowState.Maximized)
            return;

        // Leave the WebMessageReceived handler before starting the modal resize
        // loop, otherwise the WebView2 renderer cannot finish its callback.
        Dispatcher.BeginInvoke(new Action(() => BeginNativeResize(hitTest)), DispatcherPriority.Input);
    }

    private void BeginNativeResize(int hitTest)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || WindowState == WindowState.Maximized)
            return;

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, (IntPtr)hitTest, IntPtr.Zero);
    }

    private void BeginWindowDrag()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, (IntPtr)HitTestCaption, IntPtr.Zero);
    }

    private void EnsureVisible()
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var visible = Left < virtualRight - 80 && Left + Width > virtualLeft + 80 &&
                      Top < virtualBottom - 80 && Top + Height > virtualTop + 80;
        if (!visible && WinForms.Screen.PrimaryScreen is not null)
            MoveToScreen(WinForms.Screen.PrimaryScreen);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _ = Dispatcher.BeginInvoke(new Action(EnsureVisible));

    private void RestartSaveTimer()
    {
        if (!_placementRestored || _closed)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal WindowSettings CaptureSettings()
    {
        var current = _lastSettings with
        {
            Topmost = Topmost,
            Language = _app.Language,
            RefreshIntervalMs = _app.RefreshIntervalMs,
            ZoomPercent = _app.ZoomPercent,
            SkinId = _app.ActiveSkin.Id,
            HideBuiltInSkin = _app.BuiltInSkinHidden
        };

        if (WindowState == WindowState.Normal)
            current = current with { Left = Left, Top = Top, Width = Width, Height = Height };

        // Remember this skin's own placement, size, and zoom, so switching back to it
        // restores the layout it was last shown with.
        current = current.WithSkinState(_app.ActiveSkin.Id, new SkinViewState
        {
            Left = current.Left,
            Top = current.Top,
            Width = current.Width,
            Height = current.Height,
            ZoomPercent = current.ZoomPercent
        });

        _lastSettings = current;
        return current;
    }

    internal void ApplySkinPlacement(SkinViewState state)
    {
        if (state.Width is > 0)
            Width = Math.Clamp(state.Width.Value, 420, 7680);
        if (state.Height is > 0)
            Height = Math.Clamp(state.Height.Value, 560, 4320);
        if (state.Left is not null)
            Left = state.Left.Value;
        if (state.Top is not null)
            Top = state.Top.Value;

        EnsureVisible();
    }

    private void SavePlacement()
    {
        _lastSettings = CaptureSettings();
        _app.SettingsStore.Save(_lastSettings);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePlacement();
        if (!_app.IsExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _closed = true;
        _saveTimer.Stop();
        _app.Telemetry.SnapshotAvailable -= OnSnapshotAvailable;
        _app.Telemetry.CollectionFailed -= OnCollectionFailed;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        // Do not dispose WebView synchronously here: WebView2 teardown can block the
        // window close path. The host process exit releases the WebView2 runtime.
    }

    private void ShowInitializationError(Exception exception)
    {
        WebView.Visibility = Visibility.Collapsed;
        if (Content is Grid grid)
        {
            grid.Children.Add(new ScrollViewer
            {
                Padding = new Thickness(32),
                Content = new TextBlock
                {
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 235, 255)),
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    Text = UiText.Get(_app.Language, "WebViewInitError") + "\n\n" + exception.Message
                }
            });
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 0x0002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
