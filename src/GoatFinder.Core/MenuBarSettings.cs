namespace GoatFinder.Core;

public enum BarPosition { Top, Bottom }

/// <summary>Material do fundo da barra. O Windows só oferece estes (API documentada DWM); não existe "intensidade" do desfoque.</summary>
public enum BarBackdrop { None, Acrylic, Mica }

/// <summary>Aparência e comportamento da barra de menu. Todos os valores são normalizados por <see cref="Normalize"/>.</summary>
public sealed class MenuBarSettings
{
    public double HeightDip { get; set; } = 28;
    public BarPosition Position { get; set; } = BarPosition.Top;

    /// <summary>Comprimento da barra, em % do lado do monitor (20 a 100). Menos de 100 deixa a barra flutuante.</summary>
    public double LengthPercent { get; set; } = 100;

    /// <summary>Folga até a borda do monitor. Mais de 0 deixa a barra flutuante.</summary>
    public double GapDip { get; set; }
    public bool ReserveSpace { get; set; } = true;

    public BarBackdrop Backdrop { get; set; } = BarBackdrop.Acrylic;

    /// <summary>Opacidade da cor de fundo (0 a 1). Com desfoque ligado, é a "intensidade" da tinta sobre o desfoque.</summary>
    public double Opacity { get; set; } = 0.6;
    public string BackgroundColor { get; set; } = "#121214";
    public string ForegroundColor { get; set; } = "#FFFFFF";
    public string BorderColor { get; set; } = "#FFFFFF";
    public double BorderOpacity { get; set; } = 0.12;
    public double BorderThickness { get; set; }
    public double CornerRadius { get; set; }

    /// <summary>Espaço horizontal dentro de cada botão da barra.</summary>
    public double ItemPadding { get; set; } = 9;
    public double IconSize { get; set; } = 16;
    public string FontFamily { get; set; } = "Segoe UI Variable Text, Segoe UI";
    public double FontSize { get; set; } = 13;
    public bool Animations { get; set; } = true;

    public bool ShowLogo { get; set; } = true;
    public bool ShowAppName { get; set; } = true;
    public bool ShowMenus { get; set; } = true;
    public bool ShowStageButton { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowSearch { get; set; } = true;
    public bool ShowControlCenter { get; set; } = true;
    public bool ShowClock { get; set; } = true;

    /// <summary>Uma barra em cada monitor, com as mesmas configurações.</summary>
    public bool AllMonitors { get; set; }

    /// <summary>Mantém os valores dentro de limites que a interface suporta (o arquivo pode ter sido editado à mão).</summary>
    public void Normalize()
    {
        HeightDip = Math.Clamp(Finite(HeightDip, 28), 18, 64);
        LengthPercent = Math.Clamp(Finite(LengthPercent, 100), 20, 100);
        GapDip = Math.Clamp(Finite(GapDip, 0), 0, 48);
        Opacity = Math.Clamp(Finite(Opacity, 0.6), 0, 1);
        BorderOpacity = Math.Clamp(Finite(BorderOpacity, 0.12), 0, 1);
        BorderThickness = Math.Clamp(Finite(BorderThickness, 0), 0, 4);
        CornerRadius = Math.Clamp(Finite(CornerRadius, 0), 0, 24);
        ItemPadding = Math.Clamp(Finite(ItemPadding, 9), 0, 24);
        IconSize = Math.Clamp(Finite(IconSize, 16), 10, 32);
        FontSize = Math.Clamp(Finite(FontSize, 13), 9, 24);

        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Segoe UI Variable Text, Segoe UI";
        BackgroundColor = ColorOr(BackgroundColor, "#121214");
        ForegroundColor = ColorOr(ForegroundColor, "#FFFFFF");
        BorderColor = ColorOr(BorderColor, "#FFFFFF");
    }

    /// <summary>Barra que não preenche a borda inteira ou tem folga: o Windows não reserva espaço para ela.</summary>
    public bool IsFloating => LengthPercent < 99.5 || GapDip > 0.5;

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    // Aceita só #RRGGBB (6 dígitos hexadecimais): é o que o seletor de cor da interface produz.
    private static string ColorOr(string? value, string fallback) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : fallback;
}
