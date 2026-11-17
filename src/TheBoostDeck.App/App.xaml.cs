using System.Windows;
using System.Runtime.InteropServices;
using TheBoostDeck.Core;
using TheBoostDeck.Infrastructure;
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
    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayIconImage;
    private string? _logoPath;
    private WinForms.ToolStripMenuItem? _toggleWindowMenuItem;
    private WinForms.ToolStripMenuItem? _screensMenuItem;
    private WinForms.ToolStripMenuItem? _topmostMenuItem;
    private WinForms.ToolStripMenuItem? _exitMenuItem;

    internal bool IsExitRequested => _exitRequested;
    internal string Language => _settings.Language;
    internal IAppLogger Logger => _logger ?? throw new InvalidOperationException("The application logger is not initialized.");
    internal TelemetryService Telemetry => _telemetry ?? throw new InvalidOperationException("The telemetry service is not initialized.");
    internal WindowSettingsStore SettingsStore => _settingsStore ?? throw new InvalidOperationException("The settings store is not initialized.");

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
        _logoPath = WebAssetExtractor.ExtractLogo();

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

        _topmostMenuItem = new WinForms.ToolStripMenuItem(UiText.Get(Language, "AlwaysOnTop"))
        {
            Checked = _settings.Topmost,
            CheckOnClick = true
        };
        _topmostMenuItem.CheckedChanged += (_, _) => SetTopmost(_topmostMenuItem.Checked);
        menu.Items.Add(_topmostMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
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
        if (_topmostMenuItem is not null)
            _topmostMenuItem.Text = UiText.Get(Language, "AlwaysOnTop");
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
                Cpu = new CpuMetrics { Usage = MetricValue.Unavailable("%", _reason) },
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
                    FanSpeed = MetricValue.Unavailable("RPM", _reason),
                    Power = MetricValue.Unavailable("W", _reason)
                }
            };
            return Task.FromResult(snapshot);
        }
    }
}
