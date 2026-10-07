using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatFinder.Core;
using Goat.Ui;
using GoatFinder.Services;
using Goat.Platform.Windows;

namespace GoatFinder.ViewModels;

public sealed record BreadcrumbItem(string Name, string FullPath, bool ShowSeparator);

public sealed class FinderViewModel : ObservableObject, IDisposable
{
    private const int MaxSearchResults = 2000;
    private const long MaxTextPreviewBytes = 1024 * 1024;

    private readonly FileSystemService _files;
    private readonly ShellFileService _shell;
    private readonly ShellIconProvider _icons;
    private readonly FinderSettings _settings;
    private readonly Action _saveSettings;
    private readonly NavigationHistory _history = new();
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _previewTimer;
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _loadCts, _searchCts, _previewCts;
    private IReadOnlyList<FileEntry> _entries = [];
    private IReadOnlyList<FileEntry> _searchResults = [];
    private ObservableCollection<FileItemViewModel> _items = [];
    private ObservableCollection<BreadcrumbItem> _breadcrumb = [];
    private string _currentPath = string.Empty, _searchText = string.Empty, _status = string.Empty;
    private string _previewTitle = string.Empty, _previewDetails = string.Empty, _previewText = string.Empty;
    private ImageSource? _previewImage, _previewIcon;
    private bool _isSearching;

    public FinderViewModel(FileSystemService files, ShellFileService shell, ShellIconProvider icons, FinderSettings settings, Action saveSettings)
    {
        _files = files;
        _shell = shell;
        _icons = icons;
        _settings = settings;
        _saveSettings = saveSettings;

        Favorites = FinderLocations.Favorites();
        Drives = FinderLocations.Drives();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Run(RunSearchAsync); };
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); Run(UpdatePreviewAsync); };

        BackCommand = new RelayCommand(GoBack, () => _history.CanGoBack);
        ForwardCommand = new RelayCommand(GoForward, () => _history.CanGoForward);
        UpCommand = new RelayCommand(GoUp, () => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null);
        RefreshCommand = new RelayCommand(() => Run(RefreshAsync));
        NavigateCommand = new RelayCommand(p => { if (p is string path) Run(() => NavigateAsync(path)); });
        OpenCommand = new RelayCommand(OpenSelected, () => HasSelection);
        NewFolderCommand = new RelayCommand(() => Run(NewFolderAsync));
        RenameCommand = new RelayCommand(() => Run(RenameAsync), () => SelectionCount == 1);
        DuplicateCommand = new RelayCommand(() => Run(DuplicateAsync), () => HasSelection);
        CompressCommand = new RelayCommand(() => Run(CompressAsync), () => HasSelection);
        TrashCommand = new RelayCommand(() => Run(TrashAsync), () => HasSelection);
        DeletePermanentlyCommand = new RelayCommand(() => Run(DeletePermanentlyAsync), () => HasSelection);
        CopyCommand = new RelayCommand(() => CopyToClipboard(cut: false), () => HasSelection);
        CutCommand = new RelayCommand(() => CopyToClipboard(cut: true), () => HasSelection);
        PasteCommand = new RelayCommand(() => Run(PasteAsync));
        SelectAllCommand = new RelayCommand(SelectAll);
        InfoCommand = new RelayCommand(ShowInfo);
        RevealCommand = new RelayCommand(Reveal, () => HasSelection);
        TerminalCommand = new RelayCommand(OpenTerminal);
        ShowListCommand = new RelayCommand(() => IsIconView = false);
        ShowIconsCommand = new RelayCommand(() => IsIconView = true);
        ToggleHiddenCommand = new RelayCommand(() => ShowHidden = !ShowHidden);
        TogglePreviewCommand = new RelayCommand(() => PreviewPaneVisible = !PreviewPaneVisible);
        SortByCommand = new RelayCommand(p => { if (p is string key && Enum.TryParse<FileSortKey>(key, out var parsed)) SortBy(parsed); });
        FocusSearchCommand = new RelayCommand(() => FocusSearchRequested?.Invoke());
    }

    // Ganchos preenchidos pela janela: a ViewModel não conhece caixas de diálogo.
    public Func<string, string, string, string?>? PromptText { get; set; }
    public Func<string, string, bool>? Confirm { get; set; }
    public Action<string>? ShowMessage { get; set; }
    public event Action? FocusSearchRequested;

    public IReadOnlyList<FinderLocation> Favorites { get; }
    public IReadOnlyList<FinderLocation> Drives { get; }

    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand CompressCommand { get; }
    public ICommand TrashCommand { get; }
    public ICommand DeletePermanentlyCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand InfoCommand { get; }
    public ICommand RevealCommand { get; }
    public ICommand TerminalCommand { get; }
    public ICommand ShowListCommand { get; }
    public ICommand ShowIconsCommand { get; }
    public ICommand ToggleHiddenCommand { get; }
    public ICommand TogglePreviewCommand { get; }
    public ICommand SortByCommand { get; }
    public ICommand FocusSearchCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set { if (SetProperty(ref _currentPath, value)) OnPropertyChanged(nameof(Title)); }
    }

    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(_currentPath)) return "Finder";
            var name = Path.GetFileName(_currentPath.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? _currentPath : name;
        }
    }

    public ObservableCollection<FileItemViewModel> Items
    {
        get => _items;
        private set
        {
            if (!SetProperty(ref _items, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            OnSelectionChanged();
        }
    }

    public ObservableCollection<BreadcrumbItem> Breadcrumb
    {
        get => _breadcrumb;
        private set => SetProperty(ref _breadcrumb, value);
    }

    public bool IsEmpty => _items.Count == 0;
    public string EmptyMessage => IsSearching ? "Nenhum resultado" : "Pasta vazia";

    public bool IsSearching
    {
        get => _isSearching;
        private set { if (SetProperty(ref _isSearching, value)) OnPropertyChanged(nameof(EmptyMessage)); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }

    public bool HasSelection => _items.Any(i => i.IsSelected);
    public int SelectionCount => _items.Count(i => i.IsSelected);

    public bool IsIconView
    {
        get => _settings.IconView;
        set
        {
            if (_settings.IconView != value)
            {
                _settings.IconView = value;
                _saveSettings();
            }
            // Notifica sempre: um ToggleButton ligado em duas vias precisa voltar ao estado real.
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsListView));
        }
    }

    public bool IsListView
    {
        get => !IsIconView;
        set => IsIconView = !value;
    }

    public bool ShowHidden
    {
        get => _settings.ShowHidden;
        set
        {
            if (_settings.ShowHidden == value) return;
            _settings.ShowHidden = value;
            OnPropertyChanged();
            _saveSettings();
            Run(RefreshAsync);
        }
    }

    public bool PreviewPaneVisible
    {
        get => _settings.PreviewPaneVisible;
        set
        {
            if (_settings.PreviewPaneVisible == value) return;
            _settings.PreviewPaneVisible = value;
            OnPropertyChanged();
            _saveSettings();
        }
    }

    public FileSortKey SortKey => _settings.SortKey;
    public bool SortAscending => _settings.SortAscending;
    public string HeaderName => Header("Nome", FileSortKey.Name);
    public string HeaderModified => Header("Data de modificação", FileSortKey.Modified);
    public string HeaderSize => Header("Tamanho", FileSortKey.Size);
    public string HeaderKind => Header("Tipo", FileSortKey.Kind);

    public string PreviewTitle { get => _previewTitle; private set => SetProperty(ref _previewTitle, value); }
    public string PreviewDetails { get => _previewDetails; private set => SetProperty(ref _previewDetails, value); }
    public string PreviewText { get => _previewText; private set => SetProperty(ref _previewText, value); }
    public ImageSource? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public ImageSource? PreviewIcon { get => _previewIcon; private set => SetProperty(ref _previewIcon, value); }

    public IReadOnlyList<string> SelectedPaths => _items.Where(i => i.IsSelected).Select(i => i.FullPath).ToList();

    public async Task InitializeAsync(string? path)
    {
        var start = path;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) start = _settings.LastFolder;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateAsync(start);
    }

    public async Task NavigateAsync(string path, bool addToHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Directory.Exists(path))
        {
            Report($"A pasta “{path}” não existe mais.");
            return;
        }

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        IReadOnlyList<FileEntry> list;
        try { list = await Task.Run(() => _files.List(path, ShowHidden), cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (UnauthorizedAccessException) { Report("Você não tem permissão para abrir esta pasta."); return; }
        catch (IOException ex) { Report(ex.Message); return; }
        if (cts.IsCancellationRequested) return;

        _entries = list;
        var full = Path.GetFullPath(path);
        CurrentPath = full;
        if (addToHistory) _history.Visit(full);

        _searchTimer.Stop();
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        IsSearching = false;

        Breadcrumb = new ObservableCollection<BreadcrumbItem>(
            PathSegments.Build(full).Select((s, i) => new BreadcrumbItem(s.Name, s.FullPath, i > 0)));
        Rebuild();

        _settings.LastFolder = full;
        _saveSettings();
        CommandManager.InvalidateRequerySuggested();
    }

    public Task RefreshAsync() => string.IsNullOrEmpty(CurrentPath) ? Task.CompletedTask : NavigateAsync(CurrentPath, addToHistory: false);

    // Chamado pela janela em arrastar e soltar.
    public async Task DropAsync(IReadOnlyList<string> paths, string destinationDirectory, bool move)
    {
        await Task.Run(() => move ? _files.Move(paths, destinationDirectory) : _files.Copy(paths, destinationDirectory));
        await RefreshAsync();
    }

    public void OpenItem(FileItemViewModel item)
    {
        if (item.IsDirectory) { Run(() => NavigateAsync(item.FullPath)); return; }
        try { _shell.Open(item.FullPath); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Report($"Não foi possível abrir “{item.Name}”: {ex.Message}");
        }
    }

    public void SelectByPath(string path)
    {
        foreach (var item in _items)
            item.IsSelected = string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase);
    }

    private void Rebuild()
    {
        var source = IsSearching ? _searchResults : _entries;
        var sorted = FileSorter.Sort(source, SortKey, SortAscending);
        Items = new ObservableCollection<FileItemViewModel>(sorted.Select(Create));
        UpdateStatus();
    }

    private FileItemViewModel Create(FileEntry entry)
    {
        var item = new FileItemViewModel(entry, _icons.GetIconAsync) { IsCut = _cutPaths.Contains(entry.FullPath) };
        item.PropertyChanged += OnItemChanged;
        return item;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItemViewModel.IsSelected)) OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCount));
        UpdateStatus();
        _previewTimer.Stop();
        _previewTimer.Start();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(IsEmpty));
        int total = _items.Count, selected = SelectionCount;
        var text = IsSearching
            ? $"{total} resultado(s) para “{_searchText.Trim()}”"
            : selected > 0 ? $"{selected} de {total} selecionado(s)" : $"{total} item(ns)";

        try
        {
            var root = Path.GetPathRoot(CurrentPath);
            if (!string.IsNullOrEmpty(root) && !IsSearching)
                text += $" · {ByteSizeFormatter.Format(new DriveInfo(root).AvailableFreeSpace)} disponíveis";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        StatusText = text;
    }

    private async Task RunSearchAsync()
    {
        _searchCts?.Cancel();
        var query = _searchText.Trim();
        if (query.Length == 0)
        {
            IsSearching = false;
            Rebuild();
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        IsSearching = true;
        StatusText = "Buscando…";
        try
        {
            var results = await _files.SearchAsync(CurrentPath, query, ShowHidden, MaxSearchResults, cts.Token);
            if (cts.IsCancellationRequested) return;
            _searchResults = results;
            Rebuild();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report(ex.Message); }
    }

    private void SortBy(FileSortKey key)
    {
        if (_settings.SortKey == key) _settings.SortAscending = !_settings.SortAscending;
        else { _settings.SortKey = key; _settings.SortAscending = true; }
        _saveSettings();
        OnPropertyChanged(nameof(SortKey));
        OnPropertyChanged(nameof(SortAscending));
        OnPropertyChanged(nameof(HeaderName));
        OnPropertyChanged(nameof(HeaderModified));
        OnPropertyChanged(nameof(HeaderSize));
        OnPropertyChanged(nameof(HeaderKind));
        Rebuild();
    }

    private string Header(string text, FileSortKey key) =>
        SortKey != key ? text : text + (SortAscending ? "  ▲" : "  ▼");

    private void GoBack()
    {
        var path = _history.Back();
        if (path != null) Run(() => NavigateAsync(path, addToHistory: false));
    }

    private void GoForward()
    {
        var path = _history.Forward();
        if (path != null) Run(() => NavigateAsync(path, addToHistory: false));
    }

    private void GoUp()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var parent = Directory.GetParent(CurrentPath)?.FullName;
        if (parent != null) Run(() => NavigateAsync(parent));
    }

    private void OpenSelected()
    {
        foreach (var item in _items.Where(i => i.IsSelected).ToList()) OpenItem(item);
    }

    private void SelectAll()
    {
        foreach (var item in _items) item.IsSelected = true;
    }

    private async Task NewFolderAsync()
    {
        var created = await Task.Run(() => _files.CreateFolder(CurrentPath));
        await RefreshAsync();
        SelectByPath(created);
        await RenameAsync();
    }

    private async Task RenameAsync()
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        if (selected.Count != 1 || PromptText is null) return;

        var item = selected[0];
        var name = PromptText("Renomear", $"Novo nome para “{item.Name}”:", item.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Name) return;

        var target = await Task.Run(() => _files.Rename(item.FullPath, name));
        await RefreshAsync();
        SelectByPath(target);
    }

    private async Task DuplicateAsync()
    {
        var paths = SelectedPaths;
        await Task.Run(() => _files.Copy(paths, CurrentPath));
        await RefreshAsync();
    }

    private async Task CompressAsync()
    {
        var paths = SelectedPaths;
        var zip = await Task.Run(() => _files.Compress(paths, CurrentPath));
        await RefreshAsync();
        SelectByPath(zip);
    }

    private async Task TrashAsync()
    {
        var paths = SelectedPaths;
        await Task.Run(() => { foreach (var path in paths) _shell.MoveToRecycleBin(path); });
        await RefreshAsync();
    }

    private async Task DeletePermanentlyAsync()
    {
        var paths = SelectedPaths;
        if (Confirm?.Invoke("Excluir permanentemente", $"Excluir {paths.Count} item(ns) sem passar pela Lixeira? Esta ação não pode ser desfeita.") != true) return;
        await Task.Run(() => { foreach (var path in paths) _files.DeletePermanently(path); });
        await RefreshAsync();
    }

    private void CopyToClipboard(bool cut)
    {
        var paths = SelectedPaths;
        if (paths.Count == 0) return;
        FileClipboard.Set(paths, cut);

        _cutPaths.Clear();
        if (cut) foreach (var path in paths) _cutPaths.Add(path);
        foreach (var item in _items) item.IsCut = _cutPaths.Contains(item.FullPath);
    }

    private async Task PasteAsync()
    {
        if (FileClipboard.Get() is not { } clip) return;
        var destination = CurrentPath;
        await Task.Run(() => clip.Cut ? _files.Move(clip.Paths, destination) : _files.Copy(clip.Paths, destination));
        if (clip.Cut) _cutPaths.Clear();
        await RefreshAsync();
    }

    private void ShowInfo()
    {
        var selected = SelectedPaths;
        _shell.ShowProperties(selected.Count == 1 ? selected[0] : CurrentPath);
    }

    private void Reveal()
    {
        var selected = SelectedPaths;
        if (selected.Count > 0) _shell.RevealInExplorer(selected[0]);
    }

    private void OpenTerminal()
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        var folder = selected.Count == 1 && selected[0].IsDirectory ? selected[0].FullPath : CurrentPath;
        _shell.OpenTerminalHere(folder);
    }

    private async Task UpdatePreviewAsync()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        var ct = cts.Token;

        PreviewImage = null;
        PreviewText = string.Empty;
        var selected = _items.Where(i => i.IsSelected).ToList();

        if (selected.Count == 0)
        {
            PreviewIcon = null;
            PreviewTitle = Title;
            PreviewDetails = $"{_items.Count} item(ns)";
            return;
        }
        if (selected.Count > 1)
        {
            PreviewIcon = null;
            PreviewTitle = $"{selected.Count} itens selecionados";
            PreviewDetails = ByteSizeFormatter.Format(selected.Where(i => !i.IsDirectory).Sum(i => i.Entry.Size));
            return;
        }

        var item = selected[0];
        PreviewIcon = item.Icon;
        PreviewTitle = item.Name;
        PreviewDetails = $"{item.Kind}\n{item.SizeText}\nModificado: {item.ModifiedText}";
        if (item.IsDirectory) return;

        switch (FileTypeCatalog.GetPreviewKind(item.Entry.Extension))
        {
            case PreviewKind.Image:
                var image = await Task.Run<ImageSource?>(() => LoadPreviewImage(item.FullPath), ct);
                if (!ct.IsCancellationRequested) PreviewImage = image;
                break;
            case PreviewKind.Text:
                var text = await ReadTextAsync(item.FullPath, item.Entry.Size, ct);
                if (!ct.IsCancellationRequested) PreviewText = text;
                break;
        }
    }

    private static ImageSource? LoadPreviewImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 600;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string> ReadTextAsync(string path, long size, CancellationToken ct)
    {
        if (size > MaxTextPreviewBytes) return "Arquivo grande demais para a pré-visualização.";
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[4000];
            int read = await reader.ReadAsync(buffer.AsMemory(), ct);
            return new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private void Report(string message)
    {
        if (ShowMessage != null) ShowMessage(message);
        else StatusText = message;
    }

    // Executa uma tarefa "fire and forget" mostrando falhas esperadas de arquivo ao usuário.
    private async void Run(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Report(ex.Message);
        }
    }

    public void Dispose()
    {
        _searchTimer.Stop();
        _previewTimer.Stop();
        _loadCts?.Cancel();
        _searchCts?.Cancel();
        _previewCts?.Cancel();
    }
}
