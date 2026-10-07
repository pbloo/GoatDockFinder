using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Goat.Platform.Windows;

namespace GoatFinder.Views;

/// <summary>Caixa de texto simples (escura) para renomear e "Ir para a pasta".</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    private PromptWindow(string title, string message, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("Win.Bg");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Win.Fg");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("UiFont");
        SourceInitialized += (_, _) => WindowBackdrop.UseDarkTitleBar(this);

        _input = new TextBox { Text = initial, Style = (Style)Application.Current.FindResource("DarkTextBox"), Margin = new Thickness(0, 8, 0, 14) };
        _input.Loaded += (_, _) =>
        {
            _input.Focus();
            // Seleciona só o nome, sem a extensão, como o Finder faz ao renomear.
            var dot = initial.LastIndexOf('.');
            _input.Select(0, dot > 0 ? dot : initial.Length);
        };

        var ok = new Button { Content = "OK", Width = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancelar", Width = 84, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_input);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public static string? Show(Window owner, string title, string message, string initial)
    {
        var dialog = new PromptWindow(title, message, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._input.Text : null;
    }
}
