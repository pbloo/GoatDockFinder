using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoatFinder.Core;
using GoatFinder.Services;
using GoatFinder.ViewModels;
using Goat.Platform.Windows;

namespace GoatFinder.Views;

public partial class FinderWindow : Window
{
    private Point _dragStart;
    private bool _syncingSidebar;

    public FinderWindow(FinderViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.PromptText = (title, message, initial) => PromptWindow.Show(this, title, message, initial);
        viewModel.Confirm = (title, message) => MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        viewModel.ShowMessage = message => MessageBox.Show(this, message, "Finder", MessageBoxButton.OK, MessageBoxImage.Warning);
        viewModel.FocusSearchRequested += () => { SearchBox.Focus(); SearchBox.SelectAll(); };
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FinderViewModel.CurrentPath)) SyncSidebar(); };

        SourceInitialized += (_, _) => WindowBackdrop.UseDarkTitleBar(this);
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    public FinderViewModel ViewModel { get; }

    private ListBox ActiveList => ViewModel.IsIconView ? IconsList : FilesList;

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;

        if (e.Key == Key.N && !shift) { App.Shell.OpenWindow(ViewModel.CurrentPath); e.Handled = true; }
        else if (e.Key == Key.W) { Close(); e.Handled = true; }
        else if (e.Key == Key.M) { WindowState = WindowState.Minimized; e.Handled = true; }
        else if (e.Key == Key.G && shift)
        {
            var path = ViewModel.PromptText?.Invoke("Ir para a pasta", "Caminho da pasta:", ViewModel.CurrentPath);
            if (!string.IsNullOrWhiteSpace(path)) ViewModel.NavigateCommand.Execute(path.Trim());
            e.Handled = true;
        }
    }

    // ---- Barra lateral ----

    private void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSidebar || sender is not ListBox { SelectedItem: FinderLocation location }) return;
        ViewModel.NavigateCommand.Execute(location.Path);
    }

    private void SyncSidebar()
    {
        _syncingSidebar = true;
        try
        {
            FavoritesList.SelectedItem = ViewModel.Favorites.FirstOrDefault(l => SamePath(l.Path, ViewModel.CurrentPath));
            DrivesList.SelectedItem = ViewModel.Drives.FirstOrDefault(l => SamePath(l.Path, ViewModel.CurrentPath));
        }
        finally { _syncingSidebar = false; }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void OnSortClick(object sender, RoutedEventArgs e) =>
        MenuBuilder.Show(FinderMenus.SortMenu(ViewModel), (FrameworkElement)sender);

    // ---- Lista e ícones ----

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Enter: ViewModel.OpenCommand.Execute(null); break;
            case Key.Delete: (shift ? ViewModel.DeletePermanentlyCommand : ViewModel.TrashCommand).Execute(null); break;
            case Key.F2: ViewModel.RenameCommand.Execute(null); break;
            case Key.Back: ViewModel.BackCommand.Execute(null); break;
            case Key.Space when Keyboard.Modifiers == ModifierKeys.None: ViewModel.PreviewPaneVisible = !ViewModel.PreviewPaneVisible; break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderPointer((ListBox)sender, e.OriginalSource) is { } item) ViewModel.OpenItem(item);
    }

    private void OnListRightClick(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        var item = ItemUnderPointer(list, e.OriginalSource);
        if (item == null) { list.UnselectAll(); return; }

        // Clique direito num item não selecionado seleciona só ele; num já selecionado preserva a seleção múltipla.
        if (!item.IsSelected)
        {
            list.UnselectAll();
            item.IsSelected = true;
        }
    }

    private void OnListContextMenu(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        var nodes = ViewModel.HasSelection ? FinderMenus.ItemContext(ViewModel) : FinderMenus.BackgroundContext(ViewModel);
        MenuBuilder.Show(nodes, null);
    }

    private static FileItemViewModel? ItemUnderPointer(ListBox list, object originalSource)
    {
        if (originalSource is not DependencyObject source) return null;
        return ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: FileItemViewModel item } ? item : null;
    }

    // ---- Arrastar e soltar ----

    private void OnListMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var offset = e.GetPosition(null) - _dragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var list = (ListBox)sender;
        if (ItemUnderPointer(list, e.OriginalSource) == null) return;

        var paths = ViewModel.SelectedPaths;
        if (paths.Count == 0) return;

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        DragDrop.DoDragDrop(list, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e) => e.Effects = ResolveEffect(e, ViewModel.CurrentPath);

    private void OnListDrop(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        var target = ItemUnderPointer(list, e.OriginalSource);
        var destination = target is { IsDirectory: true } ? target.FullPath : ViewModel.CurrentPath;
        HandleDrop(e, destination);
    }

    private void OnSidebarDragOver(object sender, DragEventArgs e)
    {
        var location = SidebarLocationUnderPointer(sender, e.OriginalSource);
        e.Effects = location == null ? DragDropEffects.None : ResolveEffect(e, location.Path);
        e.Handled = true;
    }

    private void OnSidebarDrop(object sender, DragEventArgs e)
    {
        if (SidebarLocationUnderPointer(sender, e.OriginalSource) is { } location) HandleDrop(e, location.Path);
    }

    private static FinderLocation? SidebarLocationUnderPointer(object sender, object originalSource) =>
        originalSource is DependencyObject source &&
        ItemsControl.ContainerFromElement((ListBox)sender, source) is ListBoxItem { DataContext: FinderLocation location }
            ? location : null;

    private void HandleDrop(DragEventArgs e, string destination)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var move = ResolveEffect(e, destination) == DragDropEffects.Move;
        _ = ViewModel.DropAsync(paths, destination, move);
        e.Handled = true;
    }

    // Padrão do Explorer: mover no mesmo disco, copiar entre discos; Ctrl força copiar, Shift força mover.
    private static DragDropEffects ResolveEffect(DragEventArgs e, string destination)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DragDropEffects.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)) return DragDropEffects.Move;

        var first = (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault();
        var sameRoot = first != null && string.Equals(System.IO.Path.GetPathRoot(first), System.IO.Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
        return sameRoot ? DragDropEffects.Move : DragDropEffects.Copy;
    }
}
