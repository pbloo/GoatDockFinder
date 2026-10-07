using System.IO;
using System.Windows;
using GoatFinder.ViewModels;

namespace GoatFinder.Views;

public partial class FinderSettingsWindow : Window
{
    private readonly FinderSettingsViewModel _viewModel;

    public FinderSettingsWindow(FinderSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (PanelBar == null) return; // Checked dispara durante o InitializeComponent
        PanelBar.Visibility = TabBar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelItems.Visibility = TabItems.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelWindows.Visibility = TabWindows.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => _viewModel.ResetBar();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTweakClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TweakItemViewModel tweak }) return;

        var action = tweak.IsApplied ? "Desfazer" : "Aplicar";
        var message = $"{action}: {tweak.Title}\n\n{tweak.Description}\n\nO valor atual fica guardado e pode ser restaurado depois.";
        if (MessageBox.Show(this, message, "Personalização do Windows", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        try { _viewModel.Toggle(tweak); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            MessageBox.Show(this, "Não foi possível alterar esta opção: " + ex.Message, "Personalização do Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnUndoAllClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Desfazer todas as alterações do Windows feitas pelo GoatDockFinder?", "Personalização do Windows",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _viewModel.UndoAllTweaks();
    }
}
