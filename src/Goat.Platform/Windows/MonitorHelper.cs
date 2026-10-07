using System.Runtime.InteropServices;
using System.Windows;

namespace Goat.Platform.Windows;

/// <summary>Um monitor, em pixels físicos. <see cref="DpiScale"/> é 1.0 em 96 DPI, 1.5 em 144 etc.</summary>
public sealed record MonitorDescriptor(IntPtr Handle, Int32Rect Bounds, Int32Rect WorkArea, bool IsPrimary, double DpiScale);

public static class MonitorHelper
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>Retângulo, em pixels de tela, do monitor que contém (ou fica mais perto de) o ponto.</summary>
    public static Int32Rect GetMonitorRectAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { x = x, y = y }, MONITOR_DEFAULTTONEAREST);
        return Describe(monitor)?.Bounds ?? new Int32Rect(0, 0, 1920, 1080);
    }

    /// <summary>Todos os monitores ativos, o principal primeiro.</summary>
    public static IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var found = new List<MonitorDescriptor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            if (Describe(monitor) is { } d) found.Add(d);
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    public static MonitorDescriptor? Primary() => GetMonitors().FirstOrDefault(m => m.IsPrimary);

    public static MonitorDescriptor? FromHandle(IntPtr monitor) => GetMonitors().FirstOrDefault(m => m.Handle == monitor);

    /// <summary>Monitor onde a janela está (ou o mais próximo).</summary>
    public static MonitorDescriptor? FromWindow(IntPtr hwnd) => Describe(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    private static MonitorDescriptor? Describe(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return null;

        double scale = 1.0;
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;

        static Int32Rect ToRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
        return new MonitorDescriptor(monitor, ToRect(info.rcMonitor), ToRect(info.rcWork), (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale);
    }
}
