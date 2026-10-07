using System.Security.Principal;

namespace Goat.Shared.Product;

/// <summary>Constantes do produto compartilhadas por Dock, Finder e instalador.</summary>
public static class ProductInfo
{
    public const string Name = "GoatDockFinder";
    public const string DockName = "GoatDock";
    public const string FinderName = "GoatFinder";

    /// <summary>Versão única do produto: vem de Directory.Build.props, via metadados do assembly.</summary>
    public static string Version
    {
        get
        {
            var v = typeof(ProductInfo).Assembly.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>%LOCALAPPDATA%\GoatDockFinder: configurações, diário de alterações e manifesto de componentes.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    /// <summary>%LOCALAPPDATA%\Programs\GoatDockFinder: pasta de instalação padrão.</summary>
    public static string DefaultInstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);

    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    /// <summary>Nome do mutex de instância única do componente (isolado por sessão e por usuário).</summary>
    public static string MutexName(ComponentId component) => $@"Local\{ExecutableName(component)}.{UserSid}";

    public static string ExecutableName(ComponentId component) => component switch
    {
        ComponentId.Dock => DockName,
        ComponentId.Finder => FinderName,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
