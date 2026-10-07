using GoatDock.Core.Models;
using System.Windows.Media;

namespace GoatDock.Platform;

public static class IconExtractionExtensions
{
    /// <summary>Atalho para o ícone de um item fixado na dock.</summary>
    public static ImageSource? ObterIcone(this IIconExtractionService icones, ItemFixado item) =>
        icones.ObterIcone(item.CaminhoOuUrl, item.Tipo);
}
