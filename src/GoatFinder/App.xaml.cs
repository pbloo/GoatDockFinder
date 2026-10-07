using System.IO;
using System.Windows;
using GoatFinder.Core;
using GoatFinder.Services;
using GoatFinder.ViewModels;
using GoatFinder.Views;
using Goat.Platform.Windows;
using Goat.Shared.Ipc;
using Goat.Shared.Journal;
using Goat.Shared.Product;

namespace GoatFinder;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _isPrimaryInstance;

    private ShellIconProvider? _fileIcons;
    private ForegroundAppService? _foreground;
    private Win32WindowTrackingService? _tracking;
    private WindowSnapshotService? _snapshots;
    private IconExtractionService? _windowIcons;
    private MenuBarViewModel? _menuBarViewModel;
    private readonly Dictionary<IntPtr, MenuBarWindow> _bars = [];
    private StageManagerViewModel? _stageViewModel;
    private StageManagerWindow? _stageWindow;
    private FinderSettingsWindow? _settingsWindow;
    private IpcPeer? _ipc;

    // Janelas do Finder precisam alcançar o coordenador (Ctrl+N abre outra janela).
    public static FinderShell Shell { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Uma instância por usuário: abrir de novo só encerra a segunda.
        _instance = new Mutex(false, ProductInfo.MutexName(ComponentId.Finder));
        try { _isPrimaryInstance = _instance.WaitOne(0); }
        catch (AbandonedMutexException) { _isPrimaryInstance = true; }
        if (!_isPrimaryInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var store = new FinderSettingsStore();
        var settings = store.Load();
        _fileIcons = new ShellIconProvider();
        var tweaks = new WindowsTweakService(new ChangeJournal(), ProductInfo.FinderName);
        Shell = new FinderShell(settings, store, new FileSystemService(), new ShellFileService(), _fileIcons, tweaks)
        {
            ShowSettings = ShowSettingsWindow,
        };

        _windowIcons = new IconExtractionService();
        _snapshots = new WindowSnapshotService();
        _foreground = new ForegroundAppService();
        _foreground.Start();

        _menuBarViewModel = new MenuBarViewModel(_foreground, _windowIcons, new BateriaService(), Shell);
        ReconcileBars();
        Shell.BarSettingsChanged += ReconcileBars;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Shell.StageManagerChanged += SetStageManager;
        SetStageManager(settings.StageManagerEnabled);

        StartIpc();
    }

    // Uma barra por monitor quando "todos os monitores" está ligado; senão só no principal.
    private void ReconcileBars()
    {
        if (_menuBarViewModel == null) return;
        var monitors = MonitorHelper.GetMonitors();
        var wanted = Shell.Settings.Bar.AllMonitors ? monitors : monitors.Where(m => m.IsPrimary).Take(1).ToList();

        foreach (var (handle, window) in _bars.Where(b => wanted.All(m => m.Handle != b.Key)).ToList())
        {
            window.Close();
            _bars.Remove(handle);
        }

        foreach (var monitor in wanted.Where(m => !_bars.ContainsKey(m.Handle)))
        {
            var window = new MenuBarWindow(_menuBarViewModel, Shell, monitor.Handle);
            _bars[monitor.Handle] = window;
            window.Show();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ReconcileBars);

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new FinderSettingsWindow(new FinderSettingsViewModel(Shell));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void SetStageManager(bool enabled)
    {
        if (!enabled)
        {
            _stageViewModel?.Stop();
            _stageWindow?.Close();
            _stageWindow = null;
            _stageViewModel = null;
            _tracking?.Parar();
            return;
        }

        if (_stageViewModel != null || _windowIcons == null || _snapshots == null) return;

        _tracking ??= new Win32WindowTrackingService();
        _tracking.TelaCheiaAlterada -= OnFullscreenChanged;
        _tracking.TelaCheiaAlterada += OnFullscreenChanged;
        _tracking.Iniciar();

        _stageViewModel = new StageManagerViewModel(_tracking, _windowIcons, _snapshots);
        _stageWindow = new StageManagerWindow(_stageViewModel);
        _stageViewModel.Start();
    }

    // O evento chega de uma thread de timer; a interface só pode ser tocada na thread de UI.
    private void OnFullscreenChanged(bool fullscreen) =>
        Dispatcher.BeginInvoke(() => { if (_stageViewModel != null) _stageViewModel.Suppressed = fullscreen; });

    // Integração com o GoatDock: funciona sem ele; quando ele existe, ouvimos os pedidos dele.
    private void StartIpc()
    {
        _ipc = new IpcPeer(ComponentId.Finder);
        _ipc.PeerPresenceChanged += present => Dispatcher.BeginInvoke(() => Shell.SetDock(present));
        _ipc.MessageReceived += message => Dispatcher.BeginInvoke(() => OnIpcMessage(message));
        _ipc.Start();
    }

    private void OnIpcMessage(IpcPayload message)
    {
        switch (message)
        {
            case DockBounds bounds:
                Shell.SetDock(true, bounds.Areas);
                break;
            case OpenFolder open when Path.IsPathFullyQualified(open.Path) && Directory.Exists(open.Path):
                Shell.OpenWindow(open.Path);
                break;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_isPrimaryInstance)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _ipc?.Dispose();
            _stageViewModel?.Dispose();
            _tracking?.Dispose();
            _menuBarViewModel?.Dispose();
            _foreground?.Dispose();
            _fileIcons?.Dispose();
            _instance?.ReleaseMutex();
        }
        _instance?.Dispose();
        base.OnExit(e);
    }
}
