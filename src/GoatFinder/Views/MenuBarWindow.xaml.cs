using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoatFinder.Core;
using GoatFinder.Models;
using GoatFinder.Services;
using GoatFinder.ViewModels;
using Goat.Platform.Windows;

namespace GoatFinder.Views;

public partial class MenuBarWindow : Window
{
    private const byte KeyA = 0x41, KeyN = 0x4E, KeyS = 0x53;

    private readonly MenuBarViewModel _viewModel;
    private readonly FinderShell _shell;
    private readonly IntPtr _monitor;
    private EdgeAppBar? _appBar;
    private bool _hasBackdrop;

    /// <param name="monitor">Monitor desta barra; <see cref="IntPtr.Zero"/> é o principal.</param>
    public MenuBarWindow(MenuBarViewModel viewModel, FinderShell shell, IntPtr monitor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _shell = shell;
        _monitor = monitor;
        DataContext = viewModel;

        SourceInitialized += (_, _) =>
        {
            WindowStyles.Apply(this, noActivate: true, toolWindow: true);
            _appBar = new EdgeAppBar(this, BuildPlacement(), "GoatFinder.AppBar." + monitor);
            _appBar.Attach();
            ApplyAppearance();
        };
        shell.BarSettingsChanged += ApplyAppearance;
        viewModel.Menus.CollectionChanged += OnMenusChanged;
        Closed += (_, _) =>
        {
            shell.BarSettingsChanged -= ApplyAppearance;
            viewModel.Menus.CollectionChanged -= OnMenusChanged;
            _appBar?.Dispose();
        };
    }

    private MenuBarSettings Settings => _shell.Settings.Bar;

    private BarPlacement BuildPlacement() => new()
    {
        Edge = Settings.Position == BarPosition.Top ? AppBarEdge.Top : AppBarEdge.Bottom,
        ThicknessDip = Settings.HeightDip,
        ReserveSpace = Settings.ReserveSpace,
        LengthPercent = Settings.LengthPercent,
        GapDip = Settings.GapDip,
        Monitor = _monitor,
    };

    /// <summary>Lê as configurações e as aplica: cores, fonte, espaçamento, material do fundo e posição.</summary>
    public void ApplyAppearance()
    {
        var s = Settings;
        Resources["Bar.Fg"] = Frozen(ToColor(s.ForegroundColor, 1));
        Resources["Bar.FontFamily"] = new FontFamily(s.FontFamily);
        Resources["Bar.FontSize"] = s.FontSize;
        Resources["Bar.Padding"] = new Thickness(s.ItemPadding, 0, s.ItemPadding, 0);
        Resources["Bar.ButtonHeight"] = Math.Max(14, s.HeightDip - 6);

        var kind = s.Backdrop switch { BarBackdrop.Mica => BackdropKind.Mica, BarBackdrop.Acrylic => BackdropKind.Acrylic, _ => BackdropKind.None };
        _hasBackdrop = WindowBackdrop.Apply(this, roundCorners: s.CornerRadius > 0, kind);

        // Sem material do sistema (ou desligado), um fundo quase opaco mantém o texto legível.
        var opacity = kind != BackdropKind.None && _hasBackdrop ? s.Opacity : Math.Max(s.Opacity, 0.94);
        Background = Frozen(ToColor(s.BackgroundColor, opacity));
        BorderBrush = Frozen(ToColor(s.BorderColor, s.BorderOpacity));
        BorderThickness = new Thickness(s.BorderThickness);

        Height = s.HeightDip;
        _appBar?.Apply(BuildPlacement());
    }

    private static Color ToColor(string hex, double opacity)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        return c;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Ao trocar de app a barra "respira": os menus aparecem com um fade curto (desligável nas configurações).
    private void OnMenusChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Settings.Animations || e.Action != NotifyCollectionChangedAction.Add) return;
        LeftPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MenuNode node } element)
            MenuBuilder.Show(node.ResolveChildren(), element);
    }

    private void OnLogoClick(object sender, RoutedEventArgs e) => MenuBuilder.Show(_viewModel.LogoMenu(), (FrameworkElement)sender);

    private void OnAppNameClick(object sender, RoutedEventArgs e) => MenuBuilder.Show(_viewModel.AppMenu(), (FrameworkElement)sender);

    private void OnStageClick(object sender, RoutedEventArgs e) => _viewModel.StageManagerEnabled = !_viewModel.StageManagerEnabled;

    private void OnSearchClick(object sender, RoutedEventArgs e) => WindowCommands.SendWinChord(KeyS);

    private void OnQuickSettingsClick(object sender, RoutedEventArgs e) => WindowCommands.SendWinChord(KeyA);

    private void OnClockClick(object sender, RoutedEventArgs e) => WindowCommands.SendWinChord(KeyN);
}
