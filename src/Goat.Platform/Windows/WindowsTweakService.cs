using System.IO;
using System.Runtime.InteropServices;
using Goat.Shared.Journal;
using Microsoft.Win32;

namespace Goat.Platform.Windows;

/// <summary>Acesso ao registro do usuário (HKCU). Existe para que os testes não toquem no registro real.</summary>
public interface IUserRegistry
{
    /// <summary>Valor atual como texto, ou null se não existir.</summary>
    string? Read(string key, string name);
    void WriteDword(string key, string name, int value);
    void Delete(string key, string name);
}

public sealed class HkcuRegistry : IUserRegistry
{
    public string? Read(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name)?.ToString();
    }

    public void WriteDword(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void Delete(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>Parâmetros globais de interface. Existe para os testes não mudarem o Windows de verdade.</summary>
public interface ISystemParameters
{
    bool GetMinimizeAnimation();
    void SetMinimizeAnimation(bool enabled);
}

public sealed class Win32SystemParameters : ISystemParameters
{
    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpifUpdateIniFileAndBroadcast = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint CbSize;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo info, uint flags);

    public bool GetMinimizeAnimation()
    {
        var info = new AnimationInfo { CbSize = (uint)Marshal.SizeOf<AnimationInfo>() };
        return SystemParametersInfo(SpiGetAnimation, info.CbSize, ref info, 0) && info.MinAnimate != 0;
    }

    public void SetMinimizeAnimation(bool enabled)
    {
        var info = new AnimationInfo { CbSize = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = enabled ? 1 : 0 };
        if (!SystemParametersInfo(SpiSetAnimation, info.CbSize, ref info, SpifUpdateIniFileAndBroadcast))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}

public sealed record TweakDefinition(string Id, string Title, string Description);

/// <summary>
/// Personalização do Windows e do Explorer pela camada A (ADR 0003): só APIs documentadas e registro do usuário,
/// sem administrador. Toda alteração passa pelo diário e pode ser desfeita.
/// </summary>
public sealed class WindowsTweakService
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ExplorerRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string MinimizeAnimationName = "MinAnimate";

    private sealed record RegistryStep(string Key, string Name, int Value);

    private sealed record Tweak(TweakDefinition Definition, RegistryStep[] Steps, bool ChangesMinimizeAnimation = false, bool BroadcastTheme = false);

    private static readonly Tweak[] Tweaks =
    [
        new(new("windows.dark-mode", "Tema escuro do Windows", "Aplicativos e sistema usam o tema escuro."),
            [new(Personalize, "AppsUseLightTheme", 0), new(Personalize, "SystemUsesLightTheme", 0)], BroadcastTheme: true),
        new(new("explorer.show-extensions", "Mostrar extensões de arquivo", "Exibe .txt, .png etc. nos nomes de arquivo."),
            [new(ExplorerAdvanced, "HideFileExt", 0)]),
        new(new("explorer.show-hidden", "Mostrar arquivos ocultos", "Exibe arquivos e pastas ocultos no Explorer."),
            [new(ExplorerAdvanced, "Hidden", 1)]),
        new(new("explorer.no-recent", "Esconder recentes e frequentes", "Remove arquivos recentes e pastas frequentes do Acesso rápido."),
            [new(ExplorerRoot, "ShowRecent", 0), new(ExplorerRoot, "ShowFrequent", 0)]),
        new(new("explorer.open-this-pc", "Abrir o Explorer em Este Computador", "O Explorer começa em Este Computador, não no Acesso rápido."),
            [new(ExplorerAdvanced, "LaunchTo", 1)]),
        new(new("windows.minimize-animation-off", "Desligar a animação nativa de minimizar", "Evita animação dupla quando o efeito gênio do GoatDock está ligado."),
            [], ChangesMinimizeAnimation: true),
    ];

    private readonly ChangeJournal _journal;
    private readonly IUserRegistry _registry;
    private readonly ISystemParameters _parameters;
    private readonly string _owner;
    private readonly Action<string> _broadcast;
    private readonly Action _associationsChanged;

    public WindowsTweakService(ChangeJournal journal, string owner, IUserRegistry? registry = null, ISystemParameters? parameters = null, Action<string>? broadcast = null, Action? associationsChanged = null)
    {
        _journal = journal;
        _owner = owner;
        _registry = registry ?? new HkcuRegistry();
        _parameters = parameters ?? new Win32SystemParameters();
        _broadcast = broadcast ?? NativeShell.BroadcastSettingChange;
        _associationsChanged = associationsChanged ?? NativeShell.NotifyAssociationsChanged;
    }

    public static IReadOnlyList<TweakDefinition> Catalog { get; } = Tweaks.Select(t => t.Definition).ToList();

    public bool IsApplied(string tweakId) => _journal.Entries.Any(e => e.TweakId == tweakId);

    public void Apply(string tweakId)
    {
        var tweak = Find(tweakId);
        foreach (var step in tweak.Steps)
        {
            var previous = _registry.Read(step.Key, step.Name);
            // O diário é gravado antes da alteração: se algo falhar no meio, ainda sabemos como voltar.
            _journal.Record(new ChangeEntry
            {
                TweakId = tweakId, Kind = ChangeKind.RegistryValue, Target = step.Key, Name = step.Name,
                ValueType = "DWord", Previous = previous, Applied = step.Value.ToString(), Description = tweak.Definition.Title, Owner = _owner,
            });
            _registry.WriteDword(step.Key, step.Name, step.Value);
        }

        if (tweak.ChangesMinimizeAnimation)
        {
            _journal.Record(new ChangeEntry
            {
                TweakId = tweakId, Kind = ChangeKind.SystemParameter, Target = "SPI_ANIMATION", Name = MinimizeAnimationName,
                ValueType = "Bool", Previous = _parameters.GetMinimizeAnimation() ? "1" : "0", Applied = "0",
                Description = tweak.Definition.Title, Owner = _owner,
            });
            _parameters.SetMinimizeAnimation(false);
        }

        if (tweak.BroadcastTheme) _broadcast("ImmersiveColorSet");
        else _broadcast("Environment");
    }

    public void Undo(string tweakId)
    {
        foreach (var entry in _journal.Entries.Where(e => e.TweakId == tweakId).ToList())
            Revert(entry);
    }

    public void UndoAll()
    {
        foreach (var entry in _journal.Entries.ToList())
            Revert(entry);
    }

    /// <summary>Define o ícone de uma pasta via desktop.ini (reversível).</summary>
    public void SetFolderIcon(string folder, string iconFile, int index = 0)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        var previous = File.Exists(ini) ? File.ReadAllText(ini) : null;
        var attributes = File.GetAttributes(folder);

        _journal.Record(new ChangeEntry
        {
            TweakId = "folder-icon", Kind = ChangeKind.DesktopIni, Target = folder, Name = "desktop.ini",
            ValueType = ((int)attributes).ToString(), Previous = previous, Applied = $"{iconFile},{index}",
            Description = "Ícone de pasta", Owner = _owner,
        });

        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllText(ini, $"[.ShellClassInfo]\r\nIconResource={iconFile},{index}\r\n");
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        // O Explorer só respeita desktop.ini em pastas marcadas como somente leitura ou sistema.
        File.SetAttributes(folder, attributes | FileAttributes.ReadOnly);
        _associationsChanged();
    }

    public void ClearFolderIcon(string folder)
    {
        foreach (var entry in _journal.Entries.Where(e => e.TweakId == "folder-icon" && e.Target == folder).ToList())
            Revert(entry);
    }

    private void Revert(ChangeEntry entry)
    {
        switch (entry.Kind)
        {
            case ChangeKind.RegistryValue:
                if (entry.Previous is null) _registry.Delete(entry.Target, entry.Name);
                else _registry.WriteDword(entry.Target, entry.Name, int.Parse(entry.Previous, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case ChangeKind.SystemParameter:
                _parameters.SetMinimizeAnimation(entry.Previous == "1");
                break;
            case ChangeKind.DesktopIni:
                RevertDesktopIni(entry);
                break;
        }

        _journal.Remove(entry.Id);
        _broadcast("ImmersiveColorSet");
    }

    private void RevertDesktopIni(ChangeEntry entry)
    {
        var ini = Path.Combine(entry.Target, "desktop.ini");
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        if (entry.Previous is null)
        {
            File.Delete(ini);
        }
        else
        {
            File.WriteAllText(ini, entry.Previous);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        }

        if (Directory.Exists(entry.Target) && int.TryParse(entry.ValueType, out var attributes))
            File.SetAttributes(entry.Target, (FileAttributes)attributes);
        _associationsChanged();
    }

    private static Tweak Find(string id) =>
        Tweaks.FirstOrDefault(t => t.Definition.Id == id) ?? throw new ArgumentException($"Opção desconhecida: {id}", nameof(id));
}

internal static class NativeShell
{
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint ShcneAssocChanged = 0x08000000;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void BroadcastSettingChange(string area) =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, area, SmtoAbortIfHung, 1000, out _);

    public static void NotifyAssociationsChanged() => SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
}
