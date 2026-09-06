using System.Runtime.InteropServices;

namespace TymosPill;

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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    internal static void PinTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        ex |= WsExTopmost | WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)ex);
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }
}
