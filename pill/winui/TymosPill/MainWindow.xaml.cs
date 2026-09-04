using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TymosPill.Models;
using TymosPill.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace TymosPill;

public sealed partial class MainWindow : Window
{
    private readonly StateServer _server = new();
    private readonly SolidColorBrush _focusBg = new(Windows.UI.Color.FromArgb(255, 240, 233, 220));
    private readonly SolidColorBrush _breakBg = new(Windows.UI.Color.FromArgb(255, 232, 238, 241));
    private readonly SolidColorBrush _focusDot = new(Windows.UI.Color.FromArgb(255, 160, 120, 80));
    private readonly SolidColorBrush _breakDot = new(Windows.UI.Color.FromArgb(255, 90, 140, 160));
    private readonly SolidColorBrush _focusTime = new(Windows.UI.Color.FromArgb(255, 46, 31, 13));
    private readonly SolidColorBrush _breakTime = new(Windows.UI.Color.FromArgb(255, 58, 90, 104));
    private readonly SolidColorBrush _focusPhase = new(Windows.UI.Color.FromArgb(255, 160, 139, 117));
    private readonly SolidColorBrush _breakPhase = new(Windows.UI.Color.FromArgb(255, 90, 140, 160));

    private AppWindow? _appWindow;
    private bool _dragging;
    private PointInt32 _dragStartScreen;
    private PointInt32 _windowStartPos;
    private bool _demoMode;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        Closed += (_, _) => _server.Dispose();

        _demoMode = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));

        ApplyState(LiveSessionState.SampleRunning);

        if (!_demoMode)
        {
            try
            {
                _server.StateChanged += OnStateFromBridge;
                _server.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StateServer failed to start: {ex.Message}");
            }
        }
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.Title = "Tymos";
        _appWindow.IsShownInSwitchers = false;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        const int width = 440;
        const int height = 88;
        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + work.Height - height - 28;
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));

        try
        {
            // Transparent hit-outside chrome: content draws the pill.
            SystemBackdrop = null;
        }
        catch
        {
            // Older runtimes may lack backdrop APIs; solid is fine.
        }
    }

    private void OnStateFromBridge(LiveSessionState state)
    {
        DispatcherQueue.TryEnqueue(() => ApplyState(state));
    }

    private void ApplyState(LiveSessionState state)
    {
        if (!state.Running && !_demoMode)
        {
            PillChrome.Visibility = Visibility.Collapsed;
            return;
        }

        PillChrome.Visibility = Visibility.Visible;
        TimeText.Text = state.FormatTime();
        TitleText.Text = string.IsNullOrWhiteSpace(state.TaskTitle)
            ? "Focus session"
            : state.TaskTitle;
        PhaseText.Text = state.PhaseLabel().ToUpperInvariant();

        if (state.IsBreak)
        {
            PillChrome.Background = _breakBg;
            StatusDot.Fill = _breakDot;
            TimeText.Foreground = _breakTime;
            PhaseText.Foreground = _breakPhase;
        }
        else
        {
            PillChrome.Background = _focusBg;
            StatusDot.Fill = _focusDot;
            TimeText.Foreground = _focusTime;
            PhaseText.Foreground = _focusPhase;
        }
    }

    private void Pill_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_appWindow is null) return;
        _dragging = true;
        PillChrome.CapturePointer(e.Pointer);
        var pt = e.GetCurrentPoint(null).Position;
        // Screen coords via AppWindow position + local point approximation.
        _windowStartPos = _appWindow.Position;
        _dragStartScreen = new PointInt32(
            _windowStartPos.X + (int)pt.X,
            _windowStartPos.Y + (int)pt.Y);
    }

    private void Pill_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _appWindow is null) return;
        var pt = e.GetCurrentPoint(null).Position;
        var screenX = _appWindow.Position.X + (int)pt.X;
        var screenY = _appWindow.Position.Y + (int)pt.Y;
        var dx = screenX - _dragStartScreen.X;
        var dy = screenY - _dragStartScreen.Y;
        _appWindow.Move(new PointInt32(_windowStartPos.X + dx, _windowStartPos.Y + dy));
    }

    private void Pill_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        PillChrome.ReleasePointerCapture(e.Pointer);
    }

    private void Pill_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
    }
}
