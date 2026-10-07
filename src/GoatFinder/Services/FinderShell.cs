using System.Windows;
using GoatFinder.Core;
using GoatFinder.ViewModels;
using GoatFinder.Views;
using Goat.Platform.Windows;
using Goat.Shared.Ipc;

namespace GoatFinder.Services;

/// <summary>Coordena as janelas do Finder e os recursos do shell (Stage Manager).</summary>
public sealed class FinderShell
{
    private readonly FinderSettingsStore _store;
    private readonly FileSystemService _files;
    private readonly ShellFileService _shell;
    private readonly ShellIconProvider _icons;
    private readonly List<FinderWindow> _windows = [];

    public FinderShell(FinderSettings settings, FinderSettingsStore store, FileSystemService files, ShellFileService shell, ShellIconProvider icons, WindowsTweakService? tweaks = null)
    {
        Settings = settings;
        _store = store;
        _files = files;
        _shell = shell;
        _icons = icons;
        Tweaks = tweaks;
    }

    public FinderSettings Settings { get; }

    /// <summary>Personalização do Windows (camada A) com desfazer. Nulo em testes.</summary>
    public WindowsTweakService? Tweaks { get; }

    public event Action<bool>? StageManagerChanged;

    /// <summary>As configurações da barra mudaram: cada janela da barra se reaplica.</summary>
    public event Action? BarSettingsChanged;

    /// <summary>O GoatDock apareceu ou saiu.</summary>
    public event Action? DockPresenceChanged;

    /// <summary>Áreas ocupadas pelo GoatDock na tela (pixels). Vazia sem o Dock.</summary>
    public IReadOnlyList<DockArea> DockAreas { get; private set; } = [];

    public bool DockConnected { get; private set; }

    public Action? ShowSettings { get; set; }

    public void OpenSettings() => ShowSettings?.Invoke();

    public void SetDock(bool connected, IReadOnlyList<DockArea>? areas = null)
    {
        DockConnected = connected;
        DockAreas = connected ? areas ?? DockAreas : [];
        DockPresenceChanged?.Invoke();
    }

    /// <summary>Aplica uma mudança nas configurações da barra, valida, grava e avisa as janelas.</summary>
    public void ChangeBar(Action<MenuBarSettings> change)
    {
        change(Settings.Bar);
        Settings.Bar.Normalize();
        SaveSettings();
        BarSettingsChanged?.Invoke();
    }

    public FinderWindow? ActiveWindow => _windows.FirstOrDefault(w => w.IsActive) ?? _windows.LastOrDefault();
    public FinderViewModel? ActiveViewModel => ActiveWindow?.ViewModel;

    public bool StageManagerEnabled
    {
        get => Settings.StageManagerEnabled;
        set
        {
            if (Settings.StageManagerEnabled == value) return;
            Settings.StageManagerEnabled = value;
            SaveSettings();
            StageManagerChanged?.Invoke(value);
        }
    }

    public FinderWindow OpenWindow(string? path = null)
    {
        var viewModel = new FinderViewModel(_files, _shell, _icons, Settings, SaveSettings);
        var window = new FinderWindow(viewModel);
        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            viewModel.Dispose();
        };

        window.Show();
        window.Activate();
        _ = viewModel.InitializeAsync(path);
        return window;
    }

    public void SaveSettings() => _store.Save(Settings);

    public void Quit()
    {
        SaveSettings();
        Application.Current.Shutdown();
    }
}
