using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using GoatFinder.ViewModels;
using Goat.Platform.Windows;

namespace GoatFinder.Views;

public partial class StageManagerWindow : Window
{
    private readonly StageManagerViewModel _viewModel;

    public StageManagerWindow(StageManagerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        SourceInitialized += (_, _) => WindowStyles.Apply(this, noActivate: true, toolWindow: true);
        viewModel.PropertyChanged += OnViewModelChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        };

        Reposition();
        UpdateVisibility();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StageManagerViewModel.IsVisible)) UpdateVisibility();
    }

    // A área de trabalho muda quando a barra de menus (AppBar) é registrada ou a resolução muda.
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(Reposition);
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left;
        Top = area.Top;
        Height = area.Height;
    }

    private void UpdateVisibility()
    {
        if (_viewModel.IsVisible)
        {
            if (!IsVisible) Show();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StageItemViewModel item })
            _viewModel.Activate(item);
    }
}
