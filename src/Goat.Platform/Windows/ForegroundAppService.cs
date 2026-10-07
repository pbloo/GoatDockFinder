using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Goat.Platform.Windows;

public sealed record ForegroundAppInfo(IntPtr Hwnd, int ProcessId, string ProcessName, string DisplayName, string ExecutablePath, bool IsDesktop = false)
{
    public bool IsOwnProcess => ProcessId == Environment.ProcessId;
}

/// <summary>Observa qual aplicativo está em primeiro plano (nome amigável, executável e janela).</summary>
public sealed class ForegroundAppService : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder text, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private readonly Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private SynchronizationContext? _context;

    public ForegroundAppInfo? Current { get; private set; }
    public event Action<ForegroundAppInfo>? ForegroundChanged;

    // Chamar na thread de UI (o hook usa o loop de mensagens dela).
    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _context = SynchronizationContext.Current;
        _callback = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0) return;
            // Garante que os assinantes (ViewModels) sempre rodem na thread de UI.
            if (_context != null && SynchronizationContext.Current != _context) _context.Post(_ => Refresh(hwnd), null);
            else Refresh(hwnd);
        };
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        Refresh(GetForegroundWindow());
    }

    private void Refresh(IntPtr hwnd)
    {
        var info = Describe(hwnd);
        if (info == null) return;
        if (Current != null && Current.Hwnd == info.Hwnd && Current.DisplayName == info.DisplayName) return;
        Current = info;
        ForegroundChanged?.Invoke(info);
    }

    /// <summary>Descreve qualquer janela (nome amigável do app, executável, processo).</summary>
    public ForegroundAppInfo? Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        var className = cls.ToString();
        // Cliques na barra de tarefas ou em menus de sistema não devem trocar o "app ativo".
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "TaskListThumbnailWnd" or "NotifyIconOverflowWindow")
            return null;

        var exe = GetExecutablePath(pid);
        var processName = string.IsNullOrEmpty(exe) ? $"pid{pid}" : System.IO.Path.GetFileNameWithoutExtension(exe);

        string display;
        bool isDesktop = className is "Progman" or "WorkerW";
        if (isDesktop) display = "Área de Trabalho";
        else if (processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) display = WindowTitle(hwnd);
        else display = FriendlyName(exe, processName);

        if (string.IsNullOrWhiteSpace(display)) display = processName;
        return new ForegroundAppInfo(hwnd, (int)pid, processName, display, exe, isDesktop);
    }

    private string FriendlyName(string exe, string processName)
    {
        if (string.IsNullOrEmpty(exe)) return processName;
        if (_nameCache.TryGetValue(exe, out var cached)) return cached;

        string name;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(exe);
            name = !string.IsNullOrWhiteSpace(version.FileDescription) ? version.FileDescription!
                 : !string.IsNullOrWhiteSpace(version.ProductName) ? version.ProductName!
                 : processName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            name = processName;
        }
        _nameCache[exe] = name;
        return name;
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetExecutablePath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            int size = 1024;
            var sb = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : string.Empty;
        }
        finally { CloseHandle(handle); }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
    }
}
