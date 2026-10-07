using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Goat.Platform.Windows;

/// <summary>Aplica o fundo translúcido (acrílico) do Windows 11 e a janela sem moldura.</summary>
public static class WindowBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int left, right, top, bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_DONOTROUND = 1;
    private const int Windows11Build22H2 = 22621;

    /// <summary>Barra de título escura em janelas comuns (Windows 10 2004+ e Windows 11).</summary>
    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// <returns>true quando o backdrop do sistema está disponível (Windows 11 22H2+).</returns>
    public static bool Apply(Window window, bool roundCorners, BackdropKind kind = BackdropKind.Acrylic)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (!roundCorners)
        {
            int corner = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        return SetBackdrop(window, kind);
    }

    /// <summary>Troca o material do fundo em tempo de execução. <see cref="BackdropKind.None"/> devolve a janela a um fundo comum.</summary>
    /// <returns>true quando o sistema aplicou o material (Windows 11 22H2+).</returns>
    public static bool SetBackdrop(Window window, BackdropKind kind)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var margins = kind == BackdropKind.None ? new MARGINS() : new MARGINS { left = -1, right = -1, top = -1, bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        if (Environment.OSVersion.Version.Build < Windows11Build22H2) return false;

        int backdrop = (int)kind;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0 && kind != BackdropKind.None;
    }
}

/// <summary>Valores de DWMWA_SYSTEMBACKDROP_TYPE.</summary>
public enum BackdropKind
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
}
