using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using GoatFinder.Core;
using GoatFinder.Models;
using GoatFinder.ViewModels;

namespace GoatFinder.Services;

/// <summary>Fonte única dos menus do Finder: a barra superior e os menus de contexto usam as mesmas listas.</summary>
public static class FinderMenus
{
    public static IReadOnlyList<MenuNode> ForMenuBar(FinderShell shell) =>
    [
        Top("Arquivo", () => FileMenu(shell)),
        Top("Editar", () => EditMenu(shell)),
        Top("Visualizar", () => ViewMenu(shell)),
        Top("Ir", () => GoMenu(shell)),
        Top("Janela", () => WindowMenu(shell)),
    ];

    public static IReadOnlyList<MenuNode> SortMenu(FinderViewModel vm)
    {
        MenuNode Sort(string header, FileSortKey key) =>
            Cmd(header, null, vm.SortByCommand, key.ToString(), isChecked: vm.SortKey == key);

        return
        [
            Sort("Nome", FileSortKey.Name),
            Sort("Data de modificação", FileSortKey.Modified),
            Sort("Data de criação", FileSortKey.Created),
            Sort("Tamanho", FileSortKey.Size),
            Sort("Tipo", FileSortKey.Kind),
        ];
    }

    public static IReadOnlyList<MenuNode> LogoMenu(FinderShell shell) =>
    [
        Item("Sobre o GoatFinder", null, () => MessageBox.Show(
            "GoatFinder\nBarra de menus, gerenciador de arquivos e Stage Manager para o Windows.",
            "Sobre o GoatFinder", MessageBoxButton.OK, MessageBoxImage.Information)),
        MenuNode.Separator,
        Item("Configurações do Sistema…", null, () => Launch("ms-settings:")),
        Item("Configurações do GoatFinder…", null, shell.OpenSettings),
        Item("Nova janela do Finder", "Ctrl+N", () => shell.OpenWindow()),
        MenuNode.Separator,
        Item("Stage Manager", null, () => shell.StageManagerEnabled = !shell.StageManagerEnabled, isChecked: shell.StageManagerEnabled),
        Item(shell.DockConnected ? "GoatDock conectado" : "GoatDock não está em execução", null, () => { }, enabled: false),
        MenuNode.Separator,
        Item("Bloquear tela", null, () => Launch("rundll32.exe", "user32.dll,LockWorkStation")),
        Item("Encerrar o Finder", null, shell.Quit),
    ];

    public static IReadOnlyList<MenuNode> ItemContext(FinderViewModel vm) =>
    [
        Cmd("Abrir", null, vm.OpenCommand),
        Cmd("Mostrar no Explorer", null, vm.RevealCommand),
        Cmd("Abrir Terminal aqui", null, vm.TerminalCommand),
        MenuNode.Separator,
        Cmd("Obter informações", "Ctrl+I", vm.InfoCommand),
        Cmd("Renomear", "F2", vm.RenameCommand),
        Cmd("Duplicar", "Ctrl+D", vm.DuplicateCommand),
        Cmd("Comprimir", null, vm.CompressCommand),
        MenuNode.Separator,
        Cmd("Copiar", "Ctrl+C", vm.CopyCommand),
        Cmd("Recortar", "Ctrl+X", vm.CutCommand),
        Cmd("Colar", "Ctrl+V", vm.PasteCommand),
        MenuNode.Separator,
        Cmd("Mover para a Lixeira", "Del", vm.TrashCommand),
        Cmd("Excluir permanentemente", "Shift+Del", vm.DeletePermanentlyCommand),
    ];

    public static IReadOnlyList<MenuNode> BackgroundContext(FinderViewModel vm) =>
    [
        Cmd("Nova pasta", "Ctrl+Shift+N", vm.NewFolderCommand),
        Cmd("Colar", "Ctrl+V", vm.PasteCommand),
        MenuNode.Separator,
        Cmd("Abrir Terminal aqui", null, vm.TerminalCommand),
        Cmd("Obter informações da pasta", "Ctrl+I", vm.InfoCommand),
        MenuNode.Separator,
        Cmd("Selecionar tudo", "Ctrl+A", vm.SelectAllCommand),
        Cmd("Atualizar", "F5", vm.RefreshCommand),
        MenuNode.Separator,
        Cmd("Mostrar itens ocultos", null, vm.ToggleHiddenCommand, isChecked: vm.ShowHidden),
    ];

    private static IReadOnlyList<MenuNode> FileMenu(FinderShell shell)
    {
        var vm = shell.ActiveViewModel;
        return
        [
            Item("Nova janela do Finder", "Ctrl+N", () => shell.OpenWindow()),
            Cmd("Nova pasta", "Ctrl+Shift+N", vm?.NewFolderCommand),
            Cmd("Abrir", "Enter", vm?.OpenCommand),
            MenuNode.Separator,
            Cmd("Obter informações", "Ctrl+I", vm?.InfoCommand),
            Cmd("Renomear", "F2", vm?.RenameCommand),
            Cmd("Duplicar", "Ctrl+D", vm?.DuplicateCommand),
            Cmd("Comprimir", null, vm?.CompressCommand),
            MenuNode.Separator,
            Cmd("Mover para a Lixeira", "Del", vm?.TrashCommand),
            Cmd("Excluir permanentemente", "Shift+Del", vm?.DeletePermanentlyCommand),
            MenuNode.Separator,
            Cmd("Mostrar no Explorer", null, vm?.RevealCommand),
            Cmd("Abrir Terminal aqui", null, vm?.TerminalCommand),
            MenuNode.Separator,
            Item("Fechar janela", "Ctrl+W", () => shell.ActiveWindow?.Close(), enabled: shell.ActiveWindow != null),
        ];
    }

    private static IReadOnlyList<MenuNode> EditMenu(FinderShell shell)
    {
        var vm = shell.ActiveViewModel;
        return
        [
            Cmd("Copiar", "Ctrl+C", vm?.CopyCommand),
            Cmd("Recortar", "Ctrl+X", vm?.CutCommand),
            Cmd("Colar", "Ctrl+V", vm?.PasteCommand),
            MenuNode.Separator,
            Cmd("Selecionar tudo", "Ctrl+A", vm?.SelectAllCommand),
            Cmd("Buscar", "Ctrl+F", vm?.FocusSearchCommand),
        ];
    }

    private static IReadOnlyList<MenuNode> ViewMenu(FinderShell shell)
    {
        var vm = shell.ActiveViewModel;
        MenuNode Sort(string header, FileSortKey key) =>
            Cmd(header, null, vm?.SortByCommand, key.ToString(), isChecked: vm?.SortKey == key);

        return
        [
            Cmd("como Lista", "Ctrl+1", vm?.ShowListCommand, isChecked: vm?.IsListView == true),
            Cmd("como Ícones", "Ctrl+2", vm?.ShowIconsCommand, isChecked: vm?.IsIconView == true),
            MenuNode.Separator,
            Cmd("Mostrar itens ocultos", "Ctrl+Shift+.", vm?.ToggleHiddenCommand, isChecked: vm?.ShowHidden == true),
            Cmd("Painel de visualização", "Espaço", vm?.TogglePreviewCommand, isChecked: vm?.PreviewPaneVisible == true),
            MenuNode.Separator,
            new MenuNode
            {
                Header = "Ordenar por",
                IsEnabled = vm != null,
                Children =
                [
                    Sort("Nome", FileSortKey.Name),
                    Sort("Data de modificação", FileSortKey.Modified),
                    Sort("Data de criação", FileSortKey.Created),
                    Sort("Tamanho", FileSortKey.Size),
                    Sort("Tipo", FileSortKey.Kind),
                ],
            },
            MenuNode.Separator,
            Cmd("Atualizar", "F5", vm?.RefreshCommand),
        ];
    }

    private static IReadOnlyList<MenuNode> GoMenu(FinderShell shell)
    {
        var vm = shell.ActiveViewModel;
        var places = FinderLocations.Favorites()
            .Select(l => Cmd(l.Name, null, vm?.NavigateCommand, l.Path))
            .ToList();

        var list = new List<MenuNode>
        {
            Cmd("Voltar", "Alt+←", vm?.BackCommand),
            Cmd("Avançar", "Alt+→", vm?.ForwardCommand),
            Cmd("Pasta superior", "Ctrl+↑", vm?.UpCommand),
            MenuNode.Separator,
        };
        list.AddRange(places);
        list.Add(MenuNode.Separator);
        list.Add(Item("Ir para a pasta…", "Ctrl+Shift+G", () =>
        {
            var path = vm?.PromptText?.Invoke("Ir para a pasta", "Caminho da pasta:", vm.CurrentPath);
            if (!string.IsNullOrWhiteSpace(path)) vm!.NavigateCommand.Execute(path.Trim());
        }, enabled: vm != null));
        return list;
    }

    private static IReadOnlyList<MenuNode> WindowMenu(FinderShell shell)
    {
        var window = shell.ActiveWindow;
        return
        [
            Item("Minimizar", "Ctrl+M", () => { if (window != null) window.WindowState = WindowState.Minimized; }, enabled: window != null),
            Item("Zoom", null, () =>
            {
                if (window != null) window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }, enabled: window != null),
            Item("Fechar janela", "Ctrl+W", () => window?.Close(), enabled: window != null),
            MenuNode.Separator,
            Item("Stage Manager", null, () => shell.StageManagerEnabled = !shell.StageManagerEnabled, isChecked: shell.StageManagerEnabled),
        ];
    }

    private static MenuNode Top(string header, Func<IReadOnlyList<MenuNode>> children) =>
        new() { Header = header, LoadChildren = children };

    private static MenuNode Item(string header, string? gesture, Action execute, bool enabled = true, bool isChecked = false) =>
        new() { Header = header, Gesture = gesture, Execute = execute, IsEnabled = enabled, IsChecked = isChecked };

    private static MenuNode Cmd(string header, string? gesture, ICommand? command, object? parameter = null, bool isChecked = false) =>
        new()
        {
            Header = header,
            Gesture = gesture,
            IsChecked = isChecked,
            IsEnabled = command?.CanExecute(parameter) == true,
            Execute = command == null ? null : () => command.Execute(parameter),
        };

    private static void Launch(string file, string? arguments = null)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments ?? string.Empty) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
