using System.IO;
using GoatDock.Core.Models;
using GoatDock.Core.Services;

namespace GoatDock.Platform;

/// <summary>Abre pastas fixadas no GoatFinder quando ele está em execução; sem ele, usa o launcher normal (Explorer).</summary>
public sealed class FinderAwareLauncher : ILauncherService
{
    private readonly ILauncherService _inner;
    private readonly Func<bool> _finderPresent;
    private readonly Action<string> _openInFinder;

    public FinderAwareLauncher(ILauncherService inner, Func<bool> finderPresent, Action<string> openInFinder)
    {
        _inner = inner;
        _finderPresent = finderPresent;
        _openInFinder = openInFinder;
    }

    public LaunchResult Executar(ItemFixado item)
    {
        if (item.Tipo == TipoItem.Pasta && _finderPresent())
        {
            var caminho = Environment.ExpandEnvironmentVariables(item.CaminhoOuUrl);
            if (Path.IsPathFullyQualified(caminho) && Directory.Exists(caminho))
            {
                _openInFinder(caminho);
                return LaunchResult.Ok();
            }
        }

        return _inner.Executar(item);
    }

    public LaunchResult AbrirLocal(ItemFixado item) => _inner.AbrirLocal(item);

    public LaunchResult ExecutarCaminho(string caminho, string? argumentos = null) => _inner.ExecutarCaminho(caminho, argumentos);
}
