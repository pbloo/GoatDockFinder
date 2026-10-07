using System.Runtime.InteropServices;
using System.Text;

namespace GoatFinder.Platform;

public sealed record Win32MenuItem(
    string Text,
    string? Shortcut,
    int CommandId,
    int Position,
    bool IsSeparator,
    bool IsEnabled,
    bool IsChecked,
    IReadOnlyList<Win32MenuItem> Children);

/// <summary>
/// Lê a barra de menus clássica (HMENU) de janelas Win32 de outros programas e dispara seus comandos.
/// Apps modernos (WinUI, Electron, UWP) não usam HMENU e retornam null.
/// </summary>
public static class Win32MenuReader
{
    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr hMenu, int pos);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr hMenu, int pos);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr hMenu, uint id, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr hMenu, uint id, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hWnd);

    private const uint MF_BYPOSITION = 0x400, MF_GRAYED = 0x1, MF_DISABLED = 0x2, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800, MF_POPUP = 0x10;
    private const uint WM_COMMAND = 0x0111, WM_INITMENUPOPUP = 0x0117, SMTO_ABORTIFHUNG = 0x2;
    private const int MaxDepth = 4;

    public static bool HasMenu(IntPtr hwnd) => hwnd != IntPtr.Zero && GetMenu(hwnd) != IntPtr.Zero;

    public static IReadOnlyList<Win32MenuItem>? ReadMenuBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsHungAppWindow(hwnd)) return null;
        var bar = GetMenu(hwnd);
        if (bar == IntPtr.Zero) return null;
        return ReadItems(hwnd, bar, 0, loadChildren: false);
    }

    // Lê o conteúdo de um item do topo (submenu). Avisa o app antes (WM_INITMENUPOPUP) para que
    // ele atualize estados como "desabilitado" e "marcado", como faria ao abrir o menu de verdade.
    public static IReadOnlyList<Win32MenuItem> ReadSubMenu(IntPtr hwnd, int topLevelIndex)
    {
        var bar = GetMenu(hwnd);
        if (bar == IntPtr.Zero) return [];
        var sub = GetSubMenu(bar, topLevelIndex);
        if (sub == IntPtr.Zero) return [];
        NotifyInit(hwnd, sub, topLevelIndex);
        return ReadItems(hwnd, sub, 1, loadChildren: true);
    }

    public static bool Invoke(IntPtr hwnd, int commandId) =>
        commandId > 0 && PostMessage(hwnd, WM_COMMAND, (IntPtr)(commandId & 0xFFFF), IntPtr.Zero);

    private static List<Win32MenuItem> ReadItems(IntPtr hwnd, IntPtr menu, int depth, bool loadChildren)
    {
        var items = new List<Win32MenuItem>();
        int count = GetMenuItemCount(menu);
        if (count <= 0) return items;

        for (int i = 0; i < count; i++)
        {
            uint state = GetMenuState(menu, (uint)i, MF_BYPOSITION);
            if (state == 0xFFFFFFFF) continue;

            if ((state & MF_SEPARATOR) != 0)
            {
                items.Add(new Win32MenuItem(string.Empty, null, 0, i, true, false, false, []));
                continue;
            }

            var sb = new StringBuilder(256);
            GetMenuString(menu, (uint)i, sb, sb.Capacity, MF_BYPOSITION);
            SplitShortcut(sb.ToString(), out var text, out var shortcut);
            if (string.IsNullOrWhiteSpace(text)) continue; // itens desenhados pelo próprio app (owner-draw)

            bool isPopup = (state & MF_POPUP) != 0;
            var children = (IReadOnlyList<Win32MenuItem>)[];
            if (isPopup && loadChildren && depth < MaxDepth)
            {
                var sub = GetSubMenu(menu, i);
                if (sub != IntPtr.Zero)
                {
                    NotifyInit(hwnd, sub, i);
                    children = ReadItems(hwnd, sub, depth + 1, true);
                }
            }

            int id = isPopup ? 0 : unchecked((int)GetMenuItemID(menu, i));
            items.Add(new Win32MenuItem(text, shortcut, id, i, false,
                (state & (MF_GRAYED | MF_DISABLED)) == 0, (state & MF_CHECKED) != 0, children));
        }
        return items;
    }

    private static void NotifyInit(IntPtr hwnd, IntPtr submenu, int position) =>
        SendMessageTimeout(hwnd, WM_INITMENUPOPUP, submenu, (IntPtr)(position & 0xFFFF), SMTO_ABORTIFHUNG, 150, out _);

    // "&Abrir\tCtrl+O" -> texto "Abrir" (sem o &) e atalho "Ctrl+O".
    private static void SplitShortcut(string raw, out string text, out string? shortcut)
    {
        var parts = raw.Split('\t', 2);
        text = parts[0].Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");
        shortcut = parts.Length > 1 ? parts[1] : null;
    }
}
