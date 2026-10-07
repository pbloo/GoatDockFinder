using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GoatFinder.Models;

namespace GoatFinder.Services;

public static class MenuBuilder
{
    // Abre um menu escuro abaixo do elemento (ou na posição do mouse quando target é nulo).
    public static void Show(IEnumerable<MenuNode> nodes, FrameworkElement? target)
    {
        var menu = new ContextMenu();
        foreach (var node in nodes) menu.Items.Add(Create(node));
        if (menu.Items.Count == 0) return;

        if (target != null)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
        }
        menu.IsOpen = true;
    }

    private static object Create(MenuNode node)
    {
        if (node.IsSeparator) return new Separator();

        var item = new MenuItem
        {
            Header = node.Header,
            InputGestureText = node.Gesture,
            IsEnabled = node.IsEnabled,
            IsChecked = node.IsChecked,
        };

        var children = node.ResolveChildren();
        if (children.Count > 0)
        {
            foreach (var child in children) item.Items.Add(Create(child));
        }
        else if (node.Execute != null)
        {
            var action = node.Execute;
            item.Click += (_, _) => action();
        }
        return item;
    }
}
