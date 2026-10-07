using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GoatDock.ViewModels;

namespace GoatDock.Views.Sections;

public partial class SectionSpotifyInline : UserControl
{
    public SectionSpotifyInline() => InitializeComponent();

    private void OnProgressoClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement barra || barra.ActualWidth <= 0) return;
        vm.Spotify.Buscar(e.GetPosition(barra).X / barra.ActualWidth);
    }
}
