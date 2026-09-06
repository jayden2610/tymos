using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TymosPill.Models;
using TymosPill.Services;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace TymosPill;

public sealed partial class MainWindow : Window
{
    private readonly StateServer _server = new();
    private readonly SolidColorBrush _focusGlass = new(Windows.UI.Color.FromArgb(230, 28, 22, 16));
    private readonly SolidColorBrush _breakGlass = new(Windows.UI.Color.FromArgb(230, 24, 28, 34));
    private readonly SolidColorBrush _focusHairline = new(Windows.UI.Color.FromArgb(41, 243, 234, 220));
    private readonly SolidColorBrush _breakHairline = new(Windows.UI.Color.FromArgb(41, 200, 228, 236));
    private readonly SolidColorBrush _focusTime = new(Windows.UI.Color.FromArgb(255, 243, 234, 220));
    private readonly SolidColorBrush _breakTime = new(Windows.UI.Color.FromArgb(255, 232, 241, 244));
    private readonly SolidColorBrush _focusTitle = new(Windows.UI.Color.FromArgb(158, 243, 234, 220));
    private readonly SolidColorBrush _breakTitle = new(Windows.UI.Color.FromArgb(158, 210, 230, 236));
    private readonly SolidColorBrush _focusTrack = new(Windows.UI.Color.FromArgb(36, 243, 234, 220));
    private readonly SolidColorBrush _breakTrack = new(Windows.UI.Color.FromArgb(36, 232, 241, 244));
    private readonly SolidColorBrush _focusRing = new(Windows.UI.Color.FromArgb(255, 201, 168, 122));
    private readonly SolidColorBrush _breakRing = new(Windows.UI.Color.FromArgb(255, 126, 175, 192));

    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _dragging;
    private PointInt32 _dragStartScreen;
    private PointInt32 _windowStartPos;
    private bool _demoMode;
    private bool _placed;

    public MainWindow()
    {
        InitializeComponent();
        // XamlCompiler crashes on Border Pointer* attributes on this toolchain; wire in code instead.
        PillChrome.PointerPressed += Pill_PointerPressed;
        PillChrome.PointerMoved += Pill_PointerMoved;
        PillChrome.PointerReleased += Pill_PointerReleased;
        PillChrome.PointerCaptureLost += Pill_PointerCaptureLost;
        Activated += OnActivated;
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

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_placed)
        {
            PlaceOnLargestDisplay();
            _placed = true;
        }
        PinTopmost();
    }

    private void ConfigureWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.Title = "Tymos";
        _appWindow.IsShownInSwitchers = false;

        try
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
        }
        catch
        {
            ApplyOverlappedChrome();
        }

        if (_appWindow.Presenter is OverlappedPresenter)
        {
            ApplyOverlappedChrome();
        }

        try
        {
            SystemBackdrop = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SystemBackdrop: {ex.Message}");
        }
    }

    private void ApplyOverlappedChrome()
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter) return;
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
    }

    private void PlaceOnLargestDisplay()
    {
        if (_appWindow is null) return;
        NativeWindow.MoveToLargestBottomCenter(_hwnd, 360, 68, 24);
        PinTopmost();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            NativeWindow.MoveToLargestBottomCenter(_hwnd, 360, 68, 24);
            PinTopmost();
        };
        timer.Start();
    }

    private void PinTopmost()
    {
        if (_appWindow is null) return;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }
        try
        {
            _appWindow.MoveInZOrderAtTop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MoveInZOrderAtTop: {ex.Message}");
        }
        NativeWindow.PinTopmost(_hwnd);
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
            _appWindow?.Hide();
            return;
        }

        PillChrome.Visibility = Visibility.Visible;
        _appWindow?.Show();
        PinTopmost();
        TimeText.Text = state.FormatTime();

        var title = state.TaskTitle?.Trim() ?? "";
        if (string.IsNullOrEmpty(title))
        {
            // Collapse to orb + time, matching Flow's resting bubble when there's nothing to name.
            TitleText.Visibility = Visibility.Collapsed;
            TitleText.Text = "";
            PillChrome.Padding = new Thickness(8, 7, 8, 7);
        }
        else
        {
            TitleText.Visibility = Visibility.Visible;
            TitleText.Text = title;
            PillChrome.Padding = new Thickness(8, 7, 16, 7);
        }

        SetRing(state.RemainingRatio());

        if (state.IsBreak)
        {
            PillChrome.Background = _breakGlass;
            PillChrome.BorderBrush = _breakHairline;
            RingTrack.Stroke = _breakTrack;
            RingFill.Stroke = _breakRing;
            CoreDot.Fill = _breakRing;
            TimeText.Foreground = _breakTime;
            TitleText.Foreground = _breakTitle;
        }
        else
        {
            PillChrome.Background = _focusGlass;
            PillChrome.BorderBrush = _focusHairline;
            RingTrack.Stroke = _focusTrack;
            RingFill.Stroke = _focusRing;
            CoreDot.Fill = _focusRing;
            TimeText.Foreground = _focusTime;
            TitleText.Foreground = _focusTitle;
        }

        PillChrome.Opacity = !state.Running && _demoMode ? 0.72 : 1;
    }

    private void SetRing(double remaining)
    {
        const double size = 28;
        const double cx = size / 2;
        const double cy = size / 2;
        const double r = 11;
        remaining = Math.Clamp(remaining, 0, 1);

        if (remaining <= 0.001)
        {
            RingFill.Data = null;
            RingFill.Visibility = Visibility.Collapsed;
            return;
        }

        RingFill.Visibility = Visibility.Visible;

        if (remaining >= 0.999)
        {
            RingFill.Data = new EllipseGeometry
            {
                Center = new Point(cx, cy),
                RadiusX = r,
                RadiusY = r,
            };
            return;
        }

        var angle = remaining * 360.0;
        var rad = (angle - 90.0) * Math.PI / 180.0;
        var end = new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));

        var figure = new PathFigure
        {
            StartPoint = new Point(cx, cy - r),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = angle > 180,
            RotationAngle = 0,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        RingFill.Data = geometry;
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
