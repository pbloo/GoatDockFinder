using System.Runtime.InteropServices;

namespace GoatDock.Genie;

/// <summary>Avisa quando uma janela de aplicativo começa a minimizar ou a restaurar.</summary>
public sealed class WindowStateWatcher : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int OBJID_WINDOW = 0;

    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private SynchronizationContext? _context;

    public event Action<IntPtr>? Minimizing;
    public event Action<IntPtr>? Restoring;

    // Precisa ser chamado na thread de UI: o hook entrega os eventos pelo loop de mensagens dela.
    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _context = SynchronizationContext.Current;
        _callback = OnEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW) return;
        if (_context != null && SynchronizationContext.Current != _context) _context.Post(_ => Dispatch(eventType, hwnd), null);
        else Dispatch(eventType, hwnd);
    }

    private void Dispatch(uint eventType, IntPtr hwnd)
    {
        if (!WindowFilter.IsAppWindow(hwnd)) return;
        if (eventType == EVENT_SYSTEM_MINIMIZESTART) Minimizing?.Invoke(hwnd);
        else if (eventType == EVENT_SYSTEM_MINIMIZEEND) Restoring?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
    }
}
