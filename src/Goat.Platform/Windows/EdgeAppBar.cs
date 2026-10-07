using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Goat.Platform.Windows;

public enum AppBarEdge { Left = 0, Top = 1, Right = 2, Bottom = 3 }

/// <summary>Onde e como a barra fica no monitor. Valores em DIPs, exceto onde indicado.</summary>
public sealed record BarPlacement
{
    public AppBarEdge Edge { get; init; } = AppBarEdge.Top;
    public double ThicknessDip { get; init; } = 28;

    /// <summary>Reserva a faixa: janelas maximizadas não passam por baixo. Só vale com largura total e sem folga.</summary>
    public bool ReserveSpace { get; init; } = true;

    /// <summary>Comprimento da barra, de 20 a 100% do lado do monitor. Abaixo de 100 a barra flutua centralizada.</summary>
    public double LengthPercent { get; init; } = 100;

    /// <summary>Folga entre a barra e a borda do monitor. Acima de 0 a barra flutua.</summary>
    public double GapDip { get; init; }

    /// <summary>Monitor da barra; <see cref="IntPtr.Zero"/> usa o principal.</summary>
    public IntPtr Monitor { get; init; }

    public bool Floating => LengthPercent < 99.5 || GapDip > 0.5;
    public bool UsesAppBar => ReserveSpace && !Floating;
}

/// <summary>
/// Posiciona uma barra numa borda do monitor e, quando pedido, reserva a faixa (AppBar) para que o Windows
/// a exclua da área de trabalho. Pode mudar de posição, tamanho e monitor em tempo de execução.
/// </summary>
public sealed class EdgeAppBar : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    private const int ABN_FULLSCREENAPP = 2, ABN_POSCHANGED = 1;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private const int WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private readonly Window _window;
    private readonly string _callbackName;
    private BarPlacement _placement;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private uint _callbackMessage;
    private bool _attached;
    private bool _registered;

    public EdgeAppBar(Window window, AppBarEdge edge, double thicknessDip)
        : this(window, new BarPlacement { Edge = edge, ThicknessDip = thicknessDip }) { }

    public EdgeAppBar(Window window, BarPlacement placement, string callbackName = "GoatFinder.AppBar")
    {
        _window = window;
        _placement = placement;
        _callbackName = callbackName;
    }

    public BarPlacement Placement => _placement;

    // Chamar depois de SourceInitialized, quando a janela já tem HWND.
    public void Attach()
    {
        if (_attached) return;
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _callbackMessage = RegisterWindowMessage(_callbackName);
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _attached = true;
        Reposition();
    }

    /// <summary>Aplica uma nova posição e tamanho (por exemplo, quando o usuário muda as configurações).</summary>
    public void Apply(BarPlacement placement)
    {
        _placement = placement;
        Reposition();
    }

    public void Reposition()
    {
        if (!_attached) return;

        var monitor = ResolveMonitor();
        if (monitor is null) return;

        var thickness = (int)Math.Round(_placement.ThicknessDip * monitor.DpiScale);
        var rc = ComputeRect(monitor, thickness);

        if (_placement.UsesAppBar)
        {
            if (!_registered) RegisterAppBar();
            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uEdge = (uint)_placement.Edge,
                rc = new RECT { left = monitor.Bounds.X, top = monitor.Bounds.Y, right = monitor.Bounds.X + monitor.Bounds.Width, bottom = monitor.Bounds.Y + monitor.Bounds.Height },
            };

            // QUERYPOS deixa o sistema ajustar a faixa se outra AppBar já ocupa a mesma borda.
            SHAppBarMessage(ABM_QUERYPOS, ref data);
            switch (_placement.Edge)
            {
                case AppBarEdge.Top: data.rc.bottom = data.rc.top + thickness; break;
                case AppBarEdge.Bottom: data.rc.top = data.rc.bottom - thickness; break;
                case AppBarEdge.Left: data.rc.right = data.rc.left + thickness; break;
                case AppBarEdge.Right: data.rc.left = data.rc.right - thickness; break;
            }
            SHAppBarMessage(ABM_SETPOS, ref data);
            rc = new RECT { left = data.rc.left, top = data.rc.top, right = data.rc.right, bottom = data.rc.bottom };
        }
        else if (_registered)
        {
            UnregisterAppBar();
        }

        SetWindowPos(_hwnd, HWND_TOPMOST, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private MonitorDescriptor? ResolveMonitor()
    {
        if (_placement.Monitor != IntPtr.Zero && MonitorHelper.FromHandle(_placement.Monitor) is { } chosen) return chosen;
        return MonitorHelper.Primary();
    }

    private RECT ComputeRect(MonitorDescriptor m, int thickness)
    {
        var b = m.Bounds;
        var gap = (int)Math.Round(_placement.GapDip * m.DpiScale);
        var horizontal = _placement.Edge is AppBarEdge.Top or AppBarEdge.Bottom;
        var span = horizontal ? b.Width : b.Height;
        var length = (int)Math.Round(span * Math.Clamp(_placement.LengthPercent, 20, 100) / 100.0);
        var start = (span - length) / 2;

        return _placement.Edge switch
        {
            AppBarEdge.Top => new RECT { left = b.X + start, top = b.Y + gap, right = b.X + start + length, bottom = b.Y + gap + thickness },
            AppBarEdge.Bottom => new RECT { left = b.X + start, top = b.Y + b.Height - gap - thickness, right = b.X + start + length, bottom = b.Y + b.Height - gap },
            AppBarEdge.Left => new RECT { left = b.X + gap, top = b.Y + start, right = b.X + gap + thickness, bottom = b.Y + start + length },
            _ => new RECT { left = b.X + b.Width - gap - thickness, top = b.Y + start, right = b.X + b.Width - gap, bottom = b.Y + start + length },
        };
    }

    private void RegisterAppBar()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _callbackMessage };
        SHAppBarMessage(ABM_NEW, ref data);
        _registered = true;
    }

    private void UnregisterAppBar()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    _window.Dispatcher.BeginInvoke(Reposition);
                    break;
                case ABN_FULLSCREENAPP:
                    // Um app em tela cheia assume o topo; a barra desce para o fundo e volta depois.
                    SetWindowPos(_hwnd, lParam != IntPtr.Zero ? HWND_BOTTOM : HWND_TOPMOST, 0, 0, 0, 0,
                        0x0001 | 0x0002 | SWP_NOACTIVATE);
                    break;
            }
        }
        else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED)
        {
            _window.Dispatcher.BeginInvoke(Reposition);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_attached) return;
        _source?.RemoveHook(WndProc);
        if (_registered) UnregisterAppBar();
        _attached = false;
    }
}
