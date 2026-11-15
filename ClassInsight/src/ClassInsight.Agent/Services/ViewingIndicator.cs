using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace ClassInsight.Agent.Services;

/// <summary>
/// The on-screen notice for the student while a teacher is viewing their screen (FR-SEC-05,
/// NFR-PRIV): a small pill at the top of the primary display reading "Your screen is being viewed
/// by your teacher". It appears when the first viewer connects and disappears when the last one
/// leaves.
///
/// It is click-through, never takes focus, stays on top, and is excluded from screen capture
/// (<c>WDA_EXCLUDEFROMCAPTURE</c>) so it never shows up in the teacher's own view. Institutions
/// whose policy doesn't call for notice can switch it off with
/// <c>ClassInsight:ShowViewingIndicator=false</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ViewingIndicator : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    private readonly ILogger _logger;
    private readonly Thread _ui;
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher? _dispatcher;
    private Window? _window;
    private bool _visible;

    public ViewingIndicator(ILogger logger)
    {
        _logger = logger;
        _ui = new Thread(Run) { IsBackground = true, Name = "ClassInsight viewing indicator" };
        _ui.SetApartmentState(ApartmentState.STA);
        _ui.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Show or hide the notice. Safe to call from any thread; cheap when unchanged.</summary>
    public void SetViewing(bool viewing)
    {
        if (viewing == _visible || _dispatcher is null) return;
        _visible = viewing;
        _dispatcher.BeginInvoke(() =>
        {
            if (_window is null) return;
            if (viewing)
            {
                Position(_window);
                _window.Show();
            }
            else
            {
                _window.Hide();
            }
        });
    }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _window = BuildWindow();
            _ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            // No interactive desktop (e.g. a headless session): monitoring still works, just
            // without the notice — log it so an admin who relies on it can see why.
            _logger.LogWarning(ex, "Could not create the 'screen is being viewed' indicator.");
            _dispatcher = null;
            _ready.Set();
        }
    }

    private static Window BuildWindow()
    {
        var text = new TextBlock
        {
            Text = "●  Your screen is being viewed by your teacher",
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 6, 16, 7),
        };
        var pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x5B, 0x2C, 0x9E)),
            CornerRadius = new CornerRadius(14),
            Child = text,
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Focusable = false,
            Content = pill,
            Title = "ClassInsight",
        };

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            // Keep the notice out of the teacher's stream (Windows 10 2004+; ignored on older builds).
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };
        window.SizeChanged += (_, _) => Position(window);
        return window;
    }

    private static void Position(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
        window.Top = area.Top + 8;
    }

    public void Dispose()
    {
        try { _dispatcher?.InvokeShutdown(); } catch { }
        _ui.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
