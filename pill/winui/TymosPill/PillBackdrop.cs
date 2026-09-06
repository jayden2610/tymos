using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TymosPill;

/// <summary>
/// Per-pixel-alpha backdrop for the pill, replicating WinUIEx's
/// <c>TransparentTintBackdrop</c> exactly. The key pieces WinUIEx does that the
/// old direct-interop route did not:
///  1. hooks WM_ERASEBKGND and fills the client black (GDI 32bpp leaves the
///     alpha byte at 0) every time, returning 1 = handled;
///  2. re-applies the DWM blur-behind config on WM_DWMCOMPOSITIONCHANGED (798);
///  3. runs DwmExtendFrameIntoClientArea + DwmEnableBlurBehindWindow inside
///     OnTargetConnected, before the brush is assigned, then clears the surface
///     via GetDC at connect time.
/// </summary>
public sealed class PillTransparentBackdrop : SystemBackdrop
{
    private const int GwlpWndProc = -4;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmDwmCompositionChanged = 0x031E;

    private const uint DwmBbEnable = 0x00000001;
    private const uint DwmBbBlurRegion = 0x00000002;

    private readonly IntPtr _hwnd;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private IntPtr _backgroundBrush;
    private WndProc? _hook;
    private IntPtr _oldWndProc;

    public PillTransparentBackdrop(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        Log.Write("PillTransparentBackdrop.OnTargetConnected");
        InstallHook();
        ConfigureDwm(_hwnd);

        _brush = new Windows.UI.Composition.Compositor()
            .CreateColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
        connectedTarget.SystemBackdrop = _brush;
        base.OnTargetConnected(connectedTarget, xamlRoot);

        var hdc = GetDC(_hwnd);
        if (hdc != IntPtr.Zero)
        {
            try { ClearBackground(_hwnd, hdc); }
            finally { ReleaseDC(_hwnd, hdc); }
        }
        Log.Write("PillTransparentBackdrop connected (blur-behind + brush + clear)");
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        RemoveHook();
        var backdrop = disconnectedTarget.SystemBackdrop;
        disconnectedTarget.SystemBackdrop = null;
        backdrop?.Dispose();
        _brush?.Dispose();
        _brush = null;
        if (_backgroundBrush != IntPtr.Zero)
        {
            DeleteObject(_backgroundBrush);
            _backgroundBrush = IntPtr.Zero;
        }
        base.OnTargetDisconnected(disconnectedTarget);
    }

    private void InstallHook()
    {
        if (_oldWndProc != IntPtr.Zero) return;
        _hook = HookProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_hook));
    }

    private void RemoveHook()
    {
        if (_oldWndProc == IntPtr.Zero) return;
        SetWindowLongPtr(_hwnd, GwlpWndProc, _oldWndProc);
        _oldWndProc = IntPtr.Zero;
        _hook = null;
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmEraseBkgnd)
        {
            // wParam is the HDC to fill; black fill leaves alpha at 0 so the
            // unpainted capsule corners composite through to the desktop.
            if (ClearBackground(_hwnd, wParam)) return new IntPtr(1);
            return IntPtr.Zero;
        }
        if (msg == WmDwmCompositionChanged)
        {
            ConfigureDwm(_hwnd);
            return IntPtr.Zero;
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private bool ClearBackground(IntPtr hwnd, IntPtr hdc)
    {
        if (!GetClientRect(hwnd, out var rect)) return false;
        if (_backgroundBrush == IntPtr.Zero)
            _backgroundBrush = CreateSolidBrush(0x00000000);
        FillRect(hdc, ref rect, _backgroundBrush);
        return true;
    }

    private static void ConfigureDwm(IntPtr hwnd)
    {
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var emptyRegion = CreateRectRgn(-2, -2, -1, -1);
        var blur = new DwmBlurBehind
        {
            Flags = DwmBbEnable | DwmBbBlurRegion,
            Enable = true,
            BlurRegion = emptyRegion,
            TransitionOnMaximized = false,
        };
        DwmEnableBlurBehindWindow(hwnd, ref blur);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        public bool Enable;
        public IntPtr BlurRegion;
        public bool TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool FillRect(IntPtr hDC, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);
}
