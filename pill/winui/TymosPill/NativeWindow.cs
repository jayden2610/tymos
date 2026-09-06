using System.Runtime.InteropServices;

namespace TymosPill;

internal static class Log
{
    private static readonly object Gate = new();

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "tymos-pill.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            }
        }
        catch
        {
            // Logging must never take the pill down.
        }
    }
}

internal static class NativeWindow
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    internal static void MoveToLargestBottomCenter(IntPtr hwnd, int width, int height, int bottomMargin)
    {
        if (hwnd == IntPtr.Zero) return;

        var best = new Rect();
        var bestArea = 0;
        MonitorEnumProc proc = (hMon, hdc, lprc, data) =>
        {
            var mi = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                var mw = mi.rcWork.Right - mi.rcWork.Left;
                var mh = mi.rcWork.Bottom - mi.rcWork.Top;
                var area = mw * mh;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = mi.rcWork;
                }
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        if (bestArea <= 0) return;

        var workW = best.Right - best.Left;
        var workH = best.Bottom - best.Top;
        var x = best.Left + (workW - width) / 2;
        var y = best.Top + workH - height - bottomMargin;
        SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
    }

    private const int GwlStyle = -16;
    private const long WsBorder = 0x00800000;
    private const long WsDlgFrame = 0x00400000;
    private const long WsThickFrame = 0x00040000;
    private const long WsSysMenu = 0x00080000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsPopup = 0x80000000L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    // DWMWA_* attribute ids (dwmapi DWMWINDOWATTRIBUTE).
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaDarkMode = 20;
    private const int DwmwaCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaCornerDoNotRound = 1;
    private const int DwmncrpDisabled = 2;
    private const int DwmsbtNone = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static void ApplyHudChrome(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Strip the Win32 frame bits first: with an Overlapped presenter the OS
        // still paints a 1px border even when AppWindow reports borderless.
        // Go full WS_POPUP: frameless popups get no DWM drop-shadow halo,
        // which is what reads as a border on white backgrounds.
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var stripped = style
            & ~(WsBorder | WsDlgFrame | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox)
            | WsPopup;
        if (stripped != style)
        {
            SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)stripped);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        // Per-pixel transparency via the composition accent policy: ACCENT_
        // ENABLE_TRANSPARENTGRADIENT with a fully transparent color is the one
        // mechanism that reliably alpha-composites DComp/XAML windows
        // (TranslucentTB / TaskbarX use it). Legacy routes are inert here:
        // SetLayeredWindowAttributes, SetWindowRgn and DwmEnableBlurBehindWindow
        // are all ignored for DirectComposition content.
        var accent = new AccentPolicy
        {
            AccentState = AccentEnableTransparentGradient,
            AccentFlags = 2,
            GradientColor = 0x00000000,
        };
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        RefreshHudBorder(hwnd);
    }

    /// <summary>
    /// Re-applies the DWM border/corner attributes without touching window
    /// styles. Presenter switches and frame recalculations reset these, so call
    /// after the presenter is final and again once the window is shown.
    /// </summary>
    internal static void RefreshHudBorder(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Dark frame so any residual chrome never flashbangs white.
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaDarkMode, ref dark, sizeof(int));
        // We shape the window ourselves (capsule region + keyed black). DWM's own
        // corner rounding must stay OFF: it rounds the full window rect at the
        // system radius and draws the system shadow around it, which reads as a
        // dark halo band hugging the pill.
        var doNotRound = DwmwaCornerDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaCornerPreference, ref doNotRound, sizeof(int));
        // Belt and braces: kill DWM non-client rendering (system shadow) outright.
        var ncOff = DwmncrpDisabled;
        DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref ncOff, sizeof(int));
        // And opt the window out of the system backdrop layer (Win11 22H2+),
        // which draws the soft dark halo around region-clipped windows.
        var btNone = DwmsbtNone;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref btNone, sizeof(int));
        // No border color.
        var none = DwmwaColorNone;
        var hr = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
        if (hr != 0)
        {
            System.Diagnostics.Debug.WriteLine($"DWMWA_BORDER_COLOR failed: 0x{hr:X8}");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    internal static double GetScale(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 1.0;
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    internal static void ResizeHud(IntPtr hwnd, int width, int height)
    {
        SetWindowPos(hwnd, HwndTopmost, 0, 0, width, height,
            SwpNoMove | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private const uint LwaColorKey = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableTransparentGradient = 2;

    internal static void PinTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        // WS_EX_LAYERED is deliberately NOT set (see PinTopmost) — it disables
        // both region clipping and layered attributes on DComp windows.
        ex |= WsExTopmost | WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)ex);
        // No layered-window attributes here: SetLayeredWindowAttributes (alpha
        // and color key alike) is silently ignored for DirectComposition/XAML
        // windows, and a layered flag also makes SetWindowRgn a no-op — which
        // is how the old build showed a dark background band around the capsule.
        // WS_POPUP + region does the whole job on a plain DComp window.
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

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

    private const uint DwmBbEnable = 0x00000001;
    private const uint DwmBbBlurRegion = 0x00000002;

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("user32.dll")]
    private static extern bool FillRect(IntPtr hDC, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    /// <summary>
    /// Clears the window's GDI redirection surface to black. GDI 32bpp fills
    /// leave the alpha byte at 0, so combined with EnablePerPixelAlpha the base
    /// layer composites as transparent. Without this, the surface carries the
    /// opaque class-background brush — the dark band that used to surround the
    /// capsule. Call after every resize (resizes re-expose the surface).
    /// </summary>
    internal static void ClearWindowBackground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (!GetClientRect(hwnd, out var rect)) return;
        var hdc = GetDC(hwnd);
        if (hdc == IntPtr.Zero) return;
        try
        {
            var brush = CreateSolidBrush(0x00000000);
            FillRect(hdc, ref rect, brush);
            DeleteObject(brush);
        }
        finally
        {
            ReleaseDC(hwnd, hdc);
        }
    }

    /// <summary>
    /// Switches the window onto DWM's per-pixel-alpha path: blur-behind with an
    /// EMPTY region means "alpha-composite this window, blur nothing".
    ///
    /// KNOWN BROKEN on Windows App SDK 1.6 / Win11 24H2: this call crashes the
    /// process during the first compositor commit (combase 0x80131523,
    /// InvalidOperationException). Opt-in via --alpha only; see
    /// pill/TRANSPARENCY-NOTES.md for the full investigation and next steps.
    /// </summary>
    internal static void EnablePerPixelAlpha(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
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
}
