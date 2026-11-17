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
    private readonly System.Windows.Media.Brush? _defaultBackground;

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;
    private const string SkinHost = "skin.theboostdeck";

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
        if (File.Exists(logoPath))
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
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            NavigateToSkin(_app.ActiveSkin);
        }
        catch (Exception exception)
        {
            _logger.Error("WebView2 initialization failed.", exception);
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
            }
            return;
        }

        _fallbackAttempted = false;
        _webReady = true;
        SendHostSettings();
        SendLatestSnapshot();
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
            language = _app.Language
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
        if (_app.TryImportSkinForUi(out var cancelled, out var error))
        {
            SendSkinCatalog();
            PostJson(new
            {
                type = "skin-action",
                action = "import",
                ok = true,
                message = UiText.Get(_app.Language, "SkinImported")
            });
        }
        else if (!cancelled)
        {
            PostJson(new
            {
                type = "skin-action",
                action = "import",
                ok = false,
                message = error ?? UiText.Get(_app.Language, "SkinImportFailed")
            });
        }
    }

    private void ExportSkinFromUi(string? skinId)
    {
        if (_app.TryExportSkinForUi(skinId, out var cancelled, out var error))
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
                    BeginWindowDrag();
                    break;
                case "set-language":
                    _app.SetLanguage(message.RootElement.TryGetProperty("language", out var language) ? language.GetString() : null);
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
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowProc);
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

        return IntPtr.Zero;
    }

    private void SetWindowChromeResizeBorder(double thickness)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
            chrome.ResizeBorderThickness = new Thickness(thickness);
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
        if (WindowState != WindowState.Normal)
            return _lastSettings with { Topmost = Topmost, Language = _app.Language, SkinId = _app.ActiveSkin.Id };

        return new WindowSettings
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
            Topmost = Topmost,
            Language = _app.Language,
            SkinId = _app.ActiveSkin.Id
        };
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
