using System.Collections.ObjectModel;
using System.Windows.Media;
using Goat.Platform.Windows;
using Goat.Ui;
using GoatFinder.Core;
using GoatFinder.Services;

namespace GoatFinder.ViewModels;

/// <summary>Uma opção de personalização do Windows (camada A) com o botão Aplicar/Desfazer.</summary>
public sealed class TweakItemViewModel : ObservableObject
{
    private bool _applied;

    public TweakItemViewModel(TweakDefinition definition, bool applied)
    {
        Definition = definition;
        _applied = applied;
    }

    public TweakDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Description => Definition.Description;

    public bool IsApplied
    {
        get => _applied;
        set
        {
            if (!SetProperty(ref _applied, value)) return;
            OnPropertyChanged(nameof(ActionText));
        }
    }

    public string ActionText => IsApplied ? "Desfazer" : "Aplicar";
}

/// <summary>Configurações do GoatFinder: cada propriedade grava na hora e a barra se reaplica ao vivo.</summary>
public sealed class FinderSettingsViewModel : ObservableObject
{
    private static readonly string[] FontPresets =
    [
        "Segoe UI Variable Text, Segoe UI", "Segoe UI", "Bahnschrift", "Cascadia Mono", "Consolas", "Calibri", "Arial", "Verdana", "Tahoma",
    ];

    private readonly FinderShell _shell;

    public FinderSettingsViewModel(FinderShell shell)
    {
        _shell = shell;
        RefreshTweaks();
    }

    public IReadOnlyList<string> Fonts => FontPresets;
    public IReadOnlyList<string> Backdrops { get; } = ["Sem material (cor sólida)", "Acrílico (desfoque)", "Mica"];
    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];
    public bool CanCustomizeWindows => _shell.Tweaks != null;

    private MenuBarSettings Bar => _shell.Settings.Bar;

    private void Set(Action<MenuBarSettings> change)
    {
        _shell.ChangeBar(change);
        // O valor pode ter sido ajustado ao limite (Normalize): relê tudo para a tela refletir o que valeu.
        OnPropertyChanged(string.Empty);
    }

    public double HeightDip { get => Bar.HeightDip; set => Set(b => b.HeightDip = value); }
    public bool AtBottom { get => Bar.Position == BarPosition.Bottom; set => Set(b => b.Position = value ? BarPosition.Bottom : BarPosition.Top); }
    public double LengthPercent { get => Bar.LengthPercent; set => Set(b => b.LengthPercent = value); }
    public double GapDip { get => Bar.GapDip; set => Set(b => b.GapDip = value); }
    public bool ReserveSpace { get => Bar.ReserveSpace; set => Set(b => b.ReserveSpace = value); }
    public bool IsFloating => Bar.IsFloating;
    public int BackdropIndex { get => (int)Bar.Backdrop; set => Set(b => b.Backdrop = (BarBackdrop)Math.Clamp(value, 0, 2)); }
    public double Opacity { get => Bar.Opacity; set => Set(b => b.Opacity = value); }
    public string BackgroundColor { get => Bar.BackgroundColor; set => Set(b => b.BackgroundColor = value); }
    public string ForegroundColor { get => Bar.ForegroundColor; set => Set(b => b.ForegroundColor = value); }
    public string BorderColor { get => Bar.BorderColor; set => Set(b => b.BorderColor = value); }
    public double BorderOpacity { get => Bar.BorderOpacity; set => Set(b => b.BorderOpacity = value); }
    public double BorderThickness { get => Bar.BorderThickness; set => Set(b => b.BorderThickness = value); }
    public bool IsRounded { get => Bar.CornerRadius > 0; set => Set(b => b.CornerRadius = value ? 8 : 0); }
    public double ItemPadding { get => Bar.ItemPadding; set => Set(b => b.ItemPadding = value); }
    public double IconSize { get => Bar.IconSize; set => Set(b => b.IconSize = value); }
    public string FontFamily { get => Bar.FontFamily; set => Set(b => b.FontFamily = value); }
    public double FontSize { get => Bar.FontSize; set => Set(b => b.FontSize = value); }
    public bool Animations { get => Bar.Animations; set => Set(b => b.Animations = value); }
    public bool AllMonitors { get => Bar.AllMonitors; set => Set(b => b.AllMonitors = value); }

    public bool ShowLogo { get => Bar.ShowLogo; set => Set(b => b.ShowLogo = value); }
    public bool ShowAppName { get => Bar.ShowAppName; set => Set(b => b.ShowAppName = value); }
    public bool ShowMenus { get => Bar.ShowMenus; set => Set(b => b.ShowMenus = value); }
    public bool ShowStageButton { get => Bar.ShowStageButton; set => Set(b => b.ShowStageButton = value); }
    public bool ShowBattery { get => Bar.ShowBattery; set => Set(b => b.ShowBattery = value); }
    public bool ShowNetwork { get => Bar.ShowNetwork; set => Set(b => b.ShowNetwork = value); }
    public bool ShowSearch { get => Bar.ShowSearch; set => Set(b => b.ShowSearch = value); }
    public bool ShowControlCenter { get => Bar.ShowControlCenter; set => Set(b => b.ShowControlCenter = value); }
    public bool ShowClock { get => Bar.ShowClock; set => Set(b => b.ShowClock = value); }

    public bool StageManagerEnabled
    {
        get => _shell.StageManagerEnabled;
        set { _shell.StageManagerEnabled = value; OnPropertyChanged(); }
    }

    // Cores inválidas digitadas voltam ao valor anterior; a prévia usa sempre um valor válido.
    public Brush BackgroundPreview => ToBrush(Bar.BackgroundColor);
    public Brush ForegroundPreview => ToBrush(Bar.ForegroundColor);
    public Brush BorderPreview => ToBrush(Bar.BorderColor);

    private static Brush ToBrush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public void ResetBar() => Set(b =>
    {
        var defaults = new MenuBarSettings();
        foreach (var property in typeof(MenuBarSettings).GetProperties().Where(p => p.CanWrite))
            property.SetValue(b, property.GetValue(defaults));
    });

    public void RefreshTweaks()
    {
        Tweaks.Clear();
        if (_shell.Tweaks is not { } service) return;
        foreach (var definition in WindowsTweakService.Catalog)
            Tweaks.Add(new TweakItemViewModel(definition, service.IsApplied(definition.Id)));
    }

    /// <summary>Aplica ou desfaz uma opção. A confirmação do usuário é pedida pela janela, antes desta chamada.</summary>
    public void Toggle(TweakItemViewModel tweak)
    {
        if (_shell.Tweaks is not { } service) return;
        if (tweak.IsApplied) service.Undo(tweak.Definition.Id);
        else service.Apply(tweak.Definition.Id);
        tweak.IsApplied = service.IsApplied(tweak.Definition.Id);
    }

    public void UndoAllTweaks()
    {
        _shell.Tweaks?.UndoAll();
        RefreshTweaks();
    }
}
