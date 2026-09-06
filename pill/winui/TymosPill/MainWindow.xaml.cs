using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TymosPill.Models;
using TymosPill.Services;
using Windows.Foundation;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace TymosPill;

/// <summary>
/// Fully transparent window backdrop: the XAML surface's own alpha is what
/// shows (capsule chrome opaque, everything else punched through to the
/// desktop). The DWM blur-behind window call in NativeWindow is what actually
/// enables the per-pixel alpha path; this brush just replaces the default
/// opaque window background.
/// </summary>
/// <summary>
/// Hosts the Windows.System DispatcherQueue bootstrap the UWP composition
/// compositor requires (see ConfigureWindow — the brush seam needs it).
/// </summary>
public static class TransparentBackdrop
{
    private static IntPtr _dispatcherQueueController;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;     // 2 = DQTYPE_THREAD_CURRENT
        public int ApartmentType;  // 2 = DQTAT_COM_STA
    }

    /// <summary>The UWP compositor refuses to start without a Windows.System
    /// DispatcherQueue, and the WinUI queue doesn't count. The CreateOnCurrentThread
    /// projection API is missing from the SDK, so call the CoreMessaging entry
    /// point directly (same trick WinUIEx uses).</summary>
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options, out IntPtr controller);

    internal static void EnsureWindowsDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        if (_dispatcherQueueController != IntPtr.Zero) return;
        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2,
            ApartmentType = 2,
        };
        var hr = CreateDispatcherQueueController(options, out _dispatcherQueueController);
        Log.Write($"CreateDispatcherQueueController hr=0x{hr:X8}");
    }
}

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
    private readonly SolidColorBrush _urgentRing = new(Windows.UI.Color.FromArgb(255, 217, 123, 95));

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
            // Borderless overlapped presenter: CompactOverlay always draws a caption
            // close button, which flashbangs over the pill. Overlapped + SetBorderAndTitleBar(false, false)
            // gives us a chromeless, always-on-top HUD instead.
            ApplyOverlappedChrome();
        }
        catch
        {
            try { _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Presenter: {ex.Message}"); }
        }

        if (_appWindow.Presenter is OverlappedPresenter)
        {
            ApplyOverlappedChrome();
        }

        // Apply DWM chrome LAST: presenter switches reset DWM frame attributes,
        // so setting border-color/corners before this point gets silently lost.
        var launchArgs = Environment.GetCommandLineArgs();
        // Per-pixel alpha is on by default via PillTransparentBackdrop (the
        // WinUIEx blur-behind recipe). --no-alpha falls back to the legacy
        // opaque direct-brush path; --no-clear skips the GDI alpha-0 base fill.
        var noAlpha = launchArgs.Any(a => string.Equals(a, "--no-alpha", StringComparison.OrdinalIgnoreCase));
        var noClear = launchArgs.Any(a => string.Equals(a, "--no-clear", StringComparison.OrdinalIgnoreCase));
        Log.Write($"flags: noAlpha={noAlpha} noClear={noClear}");

        try
        {
            NativeWindow.ApplyHudChrome(_hwnd);
            Log.Write("dwm chrome applied");
        }
        catch (Exception ex)
        {
            Log.Write($"dwm chrome failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            if (!noAlpha)
            {
                TransparentBackdrop.EnsureWindowsDispatcherQueue();
                SystemBackdrop = new PillTransparentBackdrop(_hwnd);
                Log.Write("pill transparent backdrop (blur-behind) assigned");
            }
            else
            {
                // Legacy escape hatch: opaque compositing, no per-pixel alpha.
                TransparentBackdrop.EnsureWindowsDispatcherQueue();
                var support = this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();
                support.SystemBackdrop = new Windows.UI.Composition.Compositor()
                    .CreateColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
                Log.Write("legacy opaque backdrop brush assigned directly");
            }
            if (!noClear)
            {
                NativeWindow.ClearWindowBackground(_hwnd);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"backdrop assign failed: {ex.GetType().Name}: {ex.Message}");
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
        FitWindowToPill();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FitWindowToPill();
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
        // Styling / showing the window resets DWM frame attributes, so the
        // no-border setting is refreshed on every pin.
        NativeWindow.RefreshHudBorder(_hwnd);
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
            // v2: phase label so the countdown is never unexplained.
            title = state.IsBreak ? "Break" : "Focus session";
        }
        TitleText.Visibility = Visibility.Visible;
        TitleText.Text = title;

        SetRing(state.RemainingRatio());

        var urgent = !state.IsBreak && state.RemainingRatio() <= 0.10;
        var ring = state.IsBreak ? _breakRing : (urgent ? _urgentRing : _focusRing);
        var glass = state.IsBreak ? _breakGlass : _focusGlass;
        var hairline = state.IsBreak ? _breakHairline : _focusHairline;
        var track = state.IsBreak ? _breakTrack : _focusTrack;
        var timeColor = state.IsBreak ? _breakTime : _focusTime;
        var titleColor = state.IsBreak ? _breakTitle : _focusTitle;

        PillChrome.Background = glass;
        PillChrome.BorderBrush = hairline;
        RingTrack.Stroke = track;
        RingFill.Stroke = ring;
        CoreDot.Fill = ring;
        TimeText.Foreground = timeColor;
        TitleText.Foreground = titleColor;

        PillChrome.Opacity = !state.Running && _demoMode ? 0.72 : 1;

        // The window is the pill: refit to content once text settles.
        DispatcherQueue.TryEnqueue(FitWindowToPill);
    }

    private void FitWindowToPill()
    {
        if (_appWindow is null) return;
        PillChrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Full capsule, matching the mock's border-radius: 999px.
        PillChrome.CornerRadius = new CornerRadius(PillChrome.DesiredSize.Height / 2);
        // DesiredSize is in DIPs; SetWindowPos wants physical pixels.
        var scale = NativeWindow.GetScale(_hwnd);
        var w = (int)Math.Ceiling(PillChrome.DesiredSize.Width * scale) + 2;
        var h = (int)Math.Ceiling(PillChrome.DesiredSize.Height * scale) + 2;
        NativeWindow.ResizeHud(_hwnd, w, h);
        NativeWindow.ClearWindowBackground(_hwnd);
        NativeWindow.MoveToLargestBottomCenter(_hwnd, w, h, 24);
        PinTopmost();
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
