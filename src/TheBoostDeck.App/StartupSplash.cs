using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Color = System.Windows.Media.Color;

namespace TheBoostDeck.App;

/// <summary>
/// Native WPF startup indicator shown before hardware collection and WebView2
/// initialization. It stays centered on the Windows primary working area and
/// does not depend on the HTML skin being ready.
/// </summary>
internal sealed class StartupSplash : IDisposable
{
    private readonly Window _window;
    private readonly TextBlock _status;
    private bool _closed;

    internal StartupSplash()
    {
        _status = new TextBlock
        {
            Text = "TheBoostDeck 正在启动…",
            Foreground = new SolidColorBrush(Color.FromRgb(176, 199, 224)),
            FontSize = 12.5,
            LineHeight = 18,
            Margin = new Thickness(0, 7, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var text = new StackPanel
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Children =
            {
                new TextBlock
                {
                    Text = "TheBoostDeck",
                    Foreground = new SolidColorBrush(Color.FromRgb(231, 243, 255)),
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    LineHeight = 26,
                    TextAlignment = TextAlignment.Center
                },
                _status,
                new TextBlock
                {
                    Text = "正在准备传感器与界面",
                    Foreground = new SolidColorBrush(Color.FromRgb(116, 145, 177)),
                    FontSize = 10.5,
                    LineHeight = 15,
                    Margin = new Thickness(0, 5, 0, 0),
                    TextAlignment = TextAlignment.Center
                }
            }
        };

        var grid = new Grid { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logoSource = LoadLogoSource();
        if (logoSource is not null)
        {
            var logo = new System.Windows.Controls.Image
            {
                Source = logoSource,
                Width = 76,
                Height = 76,
                Stretch = Stretch.Uniform,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            Grid.SetColumn(logo, 0);
            grid.Children.Add(logo);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 7, 21, 41)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 42, 101, 154)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 18, 22, 18),
            Child = grid
        };

        _window = new Window
        {
            Title = "TheBoostDeck",
            Width = 420,
            Height = 132,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            // The layered window itself must stay clear: a filled Window background
            // paints the full rectangle and leaves black corners around the card.
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = card
        };

        _window.SourceInitialized += (_, _) => CenterOnPrimaryWorkingArea();
        _window.ShowActivated = false;
        _window.Show();
        CenterOnPrimaryWorkingArea();
        Pump();
    }

    private static ImageSource? LoadLogoSource()
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("TheBoostDeck.Web.logo.png");
            if (stream is null)
                return null;

            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal void Update(string message)
    {
        if (_closed) return;
        _status.Text = message;
        Pump();
    }

    internal void Update(string? language, string key)
    {
        Update(UiText.Get(language, key));
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _window.Close();
        Pump();
    }

    public void Dispose() => Close();

    private void CenterOnPrimaryWorkingArea()
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens.FirstOrDefault();
        var dpi = VisualTreeHelper.GetDpi(_window);
        if (screen is null)
        {
            var fallbackX = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX / 2d - _window.Width * dpi.DpiScaleX / 2d);
            var fallbackY = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY / 2d - _window.Height * dpi.DpiScaleY / 2d);
            var fallbackHandle = new WindowInteropHelper(_window).Handle;
            if (fallbackHandle != IntPtr.Zero)
                SetWindowPos(fallbackHandle, IntPtr.Zero, fallbackX, fallbackY, 0, 0, SwpNoZOrder | SwpNoActivate | SwpNoSize);
            else
            {
                _window.Left = (SystemParameters.PrimaryScreenWidth - _window.Width) / 2;
                _window.Top = (SystemParameters.PrimaryScreenHeight - _window.Height) / 2;
            }
            return;
        }

        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
        {
            _window.Left = (SystemParameters.PrimaryScreenWidth - _window.Width) / 2;
            _window.Top = (SystemParameters.PrimaryScreenHeight - _window.Height) / 2;
            return;
        }

        var width = (int)Math.Round(_window.Width * dpi.DpiScaleX);
        var height = (int)Math.Round(_window.Height * dpi.DpiScaleY);
        var x = area.Left + Math.Max(0, (area.Width - width) / 2);
        var y = area.Top + Math.Max(0, (area.Height - height) / 2);
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
