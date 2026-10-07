using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Goat.Platform.Windows;

public static class WindowStyles
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    /// <param name="noActivate">Clicar na janela não tira o foco do aplicativo que estava ativo.</param>
    /// <param name="toolWindow">Não aparece na barra de tarefas nem no Alt+Tab.</param>
    /// <param name="clickThrough">Cliques atravessam a janela (usado em sobreposições de animação).</param>
    public static void Apply(Window window, bool noActivate, bool toolWindow, bool clickThrough = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = Get(hwnd);
        if (noActivate) style |= WS_EX_NOACTIVATE;
        if (toolWindow) style |= WS_EX_TOOLWINDOW;
        if (clickThrough) style |= WS_EX_TRANSPARENT;
        Set(hwnd, style);
    }

    private static long Get(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hwnd, GWL_EXSTYLE);

    private static void Set(IntPtr hwnd, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(value));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, (int)value);
    }
}
