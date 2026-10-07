using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows.Media;
using System.Windows.Threading;
using Goat.Ui;
using GoatFinder.Core;
using GoatFinder.Models;
using GoatFinder.Services;
using Goat.Platform.Windows;

namespace GoatFinder.ViewModels;

public sealed class MenuBarViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    private readonly ForegroundAppService _foreground;
    private readonly IconExtractionService _icons;
    private readonly IBateriaService _battery;
    private readonly FinderShell _shell;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;

    private ForegroundAppInfo? _current;
    private string _appName = "Finder", _clock = string.Empty, _batteryText = string.Empty, _batteryGlyph = "\uE83F", _networkTip = string.Empty;
    private ImageSource? _appIcon;
    private bool _hasBattery, _networkOnline = true;

    public MenuBarViewModel(ForegroundAppService foreground, IconExtractionService icons, IBateriaService battery, FinderShell shell)
    {
        _foreground = foreground;
        _icons = icons;
        _battery = battery;
        _shell = shell;

        _foreground.ForegroundChanged += OnForegroundChanged;
        _shell.StageManagerChanged += _ => OnPropertyChanged(nameof(StageManagerEnabled));
        _shell.BarSettingsChanged += RefreshAppearance;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();

        if (_foreground.Current != null) OnForegroundChanged(_foreground.Current);
    }

    public ObservableCollection<MenuNode> Menus { get; } = [];

    public string AppName { get => _appName; private set => SetProperty(ref _appName, value); }
    public ImageSource? AppIcon { get => _appIcon; private set => SetProperty(ref _appIcon, value); }
    public string Clock { get => _clock; private set => SetProperty(ref _clock, value); }
    public string BatteryText { get => _batteryText; private set => SetProperty(ref _batteryText, value); }
    public string BatteryGlyph { get => _batteryGlyph; private set => SetProperty(ref _batteryGlyph, value); }
    public bool ShowBattery => _hasBattery && Bar.ShowBattery;
    public bool NetworkOnline { get => _networkOnline; private set => SetProperty(ref _networkOnline, value); }
    public string NetworkTip { get => _networkTip; private set => SetProperty(ref _networkTip, value); }

    public MenuBarSettings Bar => _shell.Settings.Bar;
    public bool ShowLogo => Bar.ShowLogo;
    public bool ShowAppName => Bar.ShowAppName;
    public bool ShowMenus => Bar.ShowMenus;
    public bool ShowStageButton => Bar.ShowStageButton;
    public bool ShowNetwork => Bar.ShowNetwork;
    public bool ShowSearch => Bar.ShowSearch;
    public bool ShowControlCenter => Bar.ShowControlCenter;
    public bool ShowClock => Bar.ShowClock;
    public double IconSize => Bar.IconSize;
    public double GlyphSize => Math.Round(Bar.IconSize * 0.9);
    public bool DockConnected => _shell.DockConnected;

    /// <summary>As configurações mudaram: pede a todos os bindings que releiam os valores.</summary>
    public void RefreshAppearance() => OnPropertyChanged(string.Empty);

    public bool StageManagerEnabled
    {
        get => _shell.StageManagerEnabled;
        set { _shell.StageManagerEnabled = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<MenuNode> LogoMenu() => FinderMenus.LogoMenu(_shell);

    // Menu do nome do app (o equivalente ao menu em negrito do macOS).
    public IReadOnlyList<MenuNode> AppMenu()
    {
        var info = _current;
        if (info == null || info.IsOwnProcess || info.IsDesktop)
        {
            return
            [
                new MenuNode { Header = "Nova janela do Finder", Gesture = "Ctrl+N", Execute = () => _shell.OpenWindow() },
                new MenuNode { Header = "Encerrar o Finder", Execute = _shell.Quit },
            ];
        }

        var hwnd = info.Hwnd;
        return
        [
            new MenuNode { Header = $"Minimizar {AppName}", Execute = () => WindowCommands.Minimize(hwnd) },
            new MenuNode { Header = $"Zoom em {AppName}", Execute = () => WindowCommands.ToggleMaximize(hwnd) },
            MenuNode.Separator,
            new MenuNode { Header = $"Fechar janela de {AppName}", Execute = () => WindowCommands.Close(hwnd) },
        ];
    }

    private void OnForegroundChanged(ForegroundAppInfo info)
    {
        _current = info;
        bool finderMode = info.IsOwnProcess || info.IsDesktop;

        AppName = finderMode ? "Finder" : info.DisplayName;
        AppIcon = finderMode ? null : _icons.ObterIconeJanela(info.Hwnd);

        Menus.Clear();
        foreach (var menu in BuildMenus(info, finderMode)) Menus.Add(menu);
    }

    private IEnumerable<MenuNode> BuildMenus(ForegroundAppInfo info, bool finderMode)
    {
        if (finderMode) return FinderMenus.ForMenuBar(_shell);

        var hwnd = info.Hwnd;
        var bar = Win32MenuReader.ReadMenuBar(hwnd);
        if (bar is { Count: > 0 })
        {
            // Só o primeiro nível é lido agora; cada submenu é lido quando o usuário o abre.
            return bar.Where(i => !i.IsSeparator).Select(top => new MenuNode
            {
                Header = top.Text,
                LoadChildren = () => Map(hwnd, Win32MenuReader.ReadSubMenu(hwnd, top.Position)),
            }).ToList();
        }

        // Apps modernos (WinUI, Electron, UWP) não expõem a barra de menus clássica: oferece ações de janela.
        return
        [
            new MenuNode
            {
                Header = "Janela",
                Children =
                [
                    new MenuNode { Header = "Minimizar", Execute = () => WindowCommands.Minimize(hwnd) },
                    new MenuNode { Header = "Zoom", Execute = () => WindowCommands.ToggleMaximize(hwnd) },
                    MenuNode.Separator,
                    new MenuNode { Header = "Fechar", Execute = () => WindowCommands.Close(hwnd) },
                ],
            },
        ];
    }

    private static IReadOnlyList<MenuNode> Map(IntPtr hwnd, IReadOnlyList<Win32MenuItem> items) =>
        items.Select(item => item.IsSeparator
            ? MenuNode.Separator
            : new MenuNode
            {
                Header = item.Text,
                Gesture = item.Shortcut,
                IsEnabled = item.IsEnabled,
                IsChecked = item.IsChecked,
                Children = Map(hwnd, item.Children),
                Execute = item.CommandId > 0 ? () => Win32MenuReader.Invoke(hwnd, item.CommandId) : null,
            }).ToList();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var day = char.ToUpper(now.ToString("ddd", Portuguese)[0], Portuguese) + now.ToString("ddd", Portuguese)[1..];
        Clock = $"{day} {now.ToString("d 'de' MMM", Portuguese)} {now:HH:mm}";
    }

    private void UpdateStatus()
    {
        var status = _battery.ObterStatus();
        _hasBattery = status.PossuiBateria == true && status.Porcentagem is not null;
        OnPropertyChanged(nameof(ShowBattery));
        if (_hasBattery)
        {
            int percent = status.Porcentagem!.Value;
            int level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
            BatteryText = $"{percent}%";
            // Glifos da fonte de ícones: 0xE850.. = bateria descarregando, 0xE85A.. = carregando.
            BatteryGlyph = status.Carregando
                ? (level == 10 ? "\uE83E" : char.ConvertFromUtf32(0xE85A + level))
                : (level == 10 ? "\uE83F" : char.ConvertFromUtf32(0xE850 + level));
        }

        bool online = NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet);
        NetworkOnline = online;
        NetworkTip = online ? "Conectado" : "Sem conexão";
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        _statusTimer.Stop();
        _foreground.ForegroundChanged -= OnForegroundChanged;
        _shell.BarSettingsChanged -= RefreshAppearance;
    }
}
