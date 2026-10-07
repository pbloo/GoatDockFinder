namespace GoatFinder.Models;

/// <summary>Item de menu independente de origem (menu do Finder, menu Win32 de outro app, ações genéricas).</summary>
public sealed class MenuNode
{
    public string Header { get; init; } = string.Empty;
    public string? Gesture { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
    public Action? Execute { get; init; }

    // Calculado só quando o menu é aberto (menus de outros apps precisam ser lidos na hora).
    public Func<IReadOnlyList<MenuNode>>? LoadChildren { get; init; }
    public IReadOnlyList<MenuNode> Children { get; init; } = [];

    public static MenuNode Separator { get; } = new() { IsSeparator = true };

    public IReadOnlyList<MenuNode> ResolveChildren() => LoadChildren?.Invoke() ?? Children;
}
