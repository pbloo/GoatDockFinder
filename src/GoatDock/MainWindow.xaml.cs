using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GoatDock.ViewModels;
using GoatDock.Views;
using GoatDock.Core.Models;
using GoatDock.Core.Services;
using GoatDock.Core.Persistence;
using Goat.Platform.Windows;
using Goat.Shared.Ipc;
using Goat.Shared.Product;

using System.Runtime.InteropServices;

namespace GoatDock;

public partial class MainWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "RegisterShellHookWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessage")]
    public static extern uint RegisterWindowMessage(string lpString);

    private uint _shellHookMessage;
    private const int HSHELL_FLASH = 0x8006;

    private readonly MainViewModel _viewModel;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILauncherService _launcherService;
    private readonly IIconExtractionService _iconService;
    private readonly IAutostartService _autostartService;
    private Win32Hotkeys? _hotkeyService;
    private Win32TrayService? _trayService;

    private readonly DispatcherTimer _autoHideTimer;
    private bool _estaOcultoPorAutoHide;

    // A janela principal cria e controla as secundárias (uma por monitor extra); elas compartilham o mesmo ViewModel.
    private readonly MainWindow? _primary;
    private readonly IntPtr _monitor;
    private readonly List<MainWindow> _secundarias = new();
    private IpcPeer? _ipc;
    private GenieController? _genie;
    private ForegroundAppService? _foreground;
    private WindowSnapshotService? _snapshots;
    private DispatcherTimer? _publicacaoTimer;

    public void ExibirInstanciaExistente()
    {
        _viewModel.DockVisivel = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        RevelarDock();
        Activate();
    }

    private Views.Sections.SectionIniciarPesquisa? _secIniciarPesquisa;
    private Views.Sections.SectionApps? _secApps;
    private Views.Sections.SectionColecoes? _secColecoes;
    private Views.Sections.SectionItensAmbiente? _secItensAmbiente;
    private Views.Sections.SectionWidgets? _secWidgets;
    private Views.Sections.SectionRelogioControles? _secRelogioControles;
    private Views.Sections.SectionMidiaInline? _secMidiaInline;
    private Views.Sections.SectionClimaInline? _secClimaInline;
    

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        // Alertas seguem o ciclo da notificação; cliques na dock não os encerram.
    }

    public MainWindow() : this(null, IntPtr.Zero) { }

    private MainWindow(MainWindow? primary, IntPtr monitor)
    {
        InitializeComponent();
        _primary = primary;
        _monitor = monitor;

        if (primary == null)
        {
            _ipc = new IpcPeer(ComponentId.Dock);
            _settingsRepo = new JsonSettingsRepository();
            // Pastas fixadas abrem no GoatFinder quando ele está em execução; sem ele, no Explorer.
            _launcherService = new FinderAwareLauncher(new LauncherService(), () => _ipc.IsPeerPresent, caminho => _ipc.Send(new OpenFolder(caminho)));
            _iconService = new IconExtractionService();
            _autostartService = new AutostartService();
            _viewModel = new MainViewModel(_settingsRepo, _launcherService, _iconService, _autostartService);
        }
        else
        {
            _settingsRepo = primary._settingsRepo;
            _launcherService = primary._launcherService;
            _iconService = primary._iconService;
            _autostartService = primary._autostartService;
            _viewModel = primary._viewModel;
        }

        DataContext = _viewModel;
        IsVisibleChanged += (_, _) => { InformarVisibilidadeReal(); AgendarPublicacaoArea(); };
        StateChanged += (_, _) => InformarVisibilidadeReal();
        Loaded += (_, _) => InformarVisibilidadeReal();
        LocationChanged += (_, _) => AgendarPublicacaoArea();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (primary == null) ConectarCallbacksViewModel();
        ConectarEventosViewModel();

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _autoHideTimer.Tick += (s, e) => ExecutarAutoHide();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += (s, e) => ReposicionarBarra();

        MouseEnter += MainWindow_MouseEnter;
        MouseLeave += MainWindow_MouseLeave;

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        if (_primary == null)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            Microsoft.Win32.SystemEvents.TimeChanged += SystemEvents_TimeChanged;
            SystemParameters.StaticPropertyChanged += SystemParameters_Changed;
        }
    }
    private void SystemParameters_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.InvokeAsync(InformarVisibilidadeReal);
    }

    private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume)
            Dispatcher.InvokeAsync(() => _viewModel.Atividade.Suspender(e.Mode == Microsoft.Win32.PowerModes.Suspend));
    }
    private void SystemEvents_TimeChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() => { TimeZoneInfo.ClearCachedData(); _viewModel.Atividade.Atualizar("Relogio"); _viewModel.Atividade.Atualizar("Calendario"); });
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Só a janela principal reconcilia os monitores; ela reposiciona as secundárias.
        if (_primary != null) return;
        Dispatcher.Invoke(() => { AtualizarJanelasSecundarias(); ReposicionarBarra(); });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private void ConectarCallbacksViewModel()
    {
        _viewModel.MostrarAlerta = (titulo, msg) =>
        {
            System.Windows.MessageBox.Show(this, msg, titulo, MessageBoxButton.OK, MessageBoxImage.Information);
        };

        _viewModel.AtivarJanelaPrincipal = () =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // Hack para roubar o foco no Windows: simular uma tecla
            keybd_event(0, 0, 0, 0);
            SetForegroundWindow(hwnd);
            this.Activate();
            this.Focus();
        };

        _viewModel.PedirTexto = (titulo, prompt) =>
        {
            var dialog = new InputPromptDialog(titulo, prompt) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.ValorResultante : null;
        };

        _viewModel.AbrirDialogoItem = itemExistente =>
        {
            var dialog = new ItemEditDialog(itemExistente) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.ItemResultante : null;
        };

        _viewModel.AbrirJanelaAjustes = secao =>
        {
            var ajustesVm = new AjustesViewModel(_viewModel, _settingsRepo, _autostartService, secao);
            var ajustesWin = new AjustesWindow(ajustesVm) { Owner = this };
            ajustesWin.ShowDialog();
            AtualizarLayoutSecoes();
            ReposicionarBarra();
        };

        _viewModel.AbrirJanelaConfiguracoes = () =>
        {
            _viewModel.AbrirJanelaAjustes?.Invoke("Geral");
        };

        _viewModel.AbrirJanelaPersonalizacao = () =>
        {
            _viewModel.AbrirJanelaAjustes?.Invoke("Aparencia");
        };

        _viewModel.NotificarReposicionamento = () =>
        {
            Dispatcher.Invoke(ReposicionarBarra);
        };

        _viewModel.TrocarAmbienteComTransicao = amb =>
        {
            if (_viewModel.AmbienteAtivo?.Id == amb.Id) return;

            bool animar = !_viewModel.DesativarAnimacoes && SystemParameters.ClientAreaAnimation;
            if (!animar || _secItensAmbiente == null)
            {
                _viewModel.AmbienteAtivo = amb;
                return;
            }

            var duracao = _viewModel.ObterDuracaoTransicao();
            var halfDur = TimeSpan.FromMilliseconds(duracao.TotalMilliseconds / 2.0);

            var fadeOut = new DoubleAnimation(1.0, 0.0, halfDur);
            var slideOut = new DoubleAnimation(0.0, 5.0, halfDur);

            var tt = _secItensAmbiente.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                _secItensAmbiente.RenderTransform = tt;
            }

            fadeOut.Completed += (s, e) =>
            {
                _viewModel.AmbienteAtivo = amb;

                var fadeIn = new DoubleAnimation(0.0, 1.0, halfDur)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var slideIn = new DoubleAnimation(-5.0, 0.0, halfDur)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                _secItensAmbiente.BeginAnimation(OpacityProperty, fadeIn);
                tt.BeginAnimation(TranslateTransform.YProperty, slideIn);
            };

            _secItensAmbiente.BeginAnimation(OpacityProperty, fadeOut);
            tt.BeginAnimation(TranslateTransform.YProperty, slideOut);
        };

        _viewModel.SolicitarFechamento = () =>
        {
            Application.Current.Shutdown();
        };
    }

    private System.ComponentModel.PropertyChangedEventHandler? _eventosHandler;

    // Cada janela da dock (principal ou de outro monitor) reage às mudanças do ViewModel compartilhado.
    private void ConectarEventosViewModel()
    {
        _eventosHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.DockVisivel) || e.PropertyName == nameof(_viewModel.OcultoPorTelaCheia))
            {
                if (_viewModel.DockVisivel && !_viewModel.OcultoPorTelaCheia)
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                }
                else
                {
                    Hide();
                }
            }
            else if (e.PropertyName == nameof(_viewModel.OrdemSecoes) ||
                     e.PropertyName == nameof(_viewModel.Espacadores) ||
                     e.PropertyName == nameof(_viewModel.EstiloTema))
            {
                Dispatcher.Invoke(AtualizarLayoutSecoes);
            }
            else if (e.PropertyName == nameof(_viewModel.UsarComoBarraPrincipal) || e.PropertyName == nameof(_viewModel.ReservarEspacoDock))
            {
                Dispatcher.Invoke(ReposicionarBarra);
            }
            else if (_primary == null && e.PropertyName == nameof(_viewModel.EfeitoGenio))
            {
                AtualizarGenie();
            }
            else if (_primary == null && e.PropertyName == nameof(_viewModel.DockEmTodosMonitores))
            {
                Dispatcher.Invoke(() => { AtualizarJanelasSecundarias(); ReposicionarBarra(); });
            }
        };
        _viewModel.PropertyChanged += _eventosHandler;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Janelas de outros monitores só mostram a dock; bandeja, atalhos e barra nativa são da principal.
        if (_primary != null)
        {
            AtualizarLayoutSecoes();
            return;
        }

        RegisterShellHookWindow(hwnd);
        _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        AtualizarLayoutSecoes();

        if (_viewModel.UsarComoBarraPrincipal)
        {
            _viewModel.TaskbarService.OcultarBarraNativa(out var estadoAnt);
            _viewModel.Preferencias.EstadoAnteriorBarraTarefas = estadoAnt;
            _viewModel.WinKeyHookService.Iniciar();
        }

        ReposicionarBarra();

        hwnd = new WindowInteropHelper(this).Handle; InicializarHotkeys(hwnd);
        InicializarBandeja(hwnd);
        IniciarIntegracoes();
        AtualizarJanelasSecundarias();
    }

    private void InicializarHotkeys(IntPtr hwnd)
    {
        try
        {
            _hotkeyService = new Win32Hotkeys(hwnd);

            // Atalho principal para exibir/ocultar a dock: Ctrl+Alt+D
            var atalhoDock = _viewModel.Preferencias.AtalhoDock ?? new AtalhoConfig { Control = true, Alt = true, Tecla = "D" };
            var res = _hotkeyService.Registrar(1, atalhoDock, () =>
            {
                Dispatcher.Invoke(() => _viewModel.AlternarVisibilidade());
            });

            // Atalhos rÃ¡pidos para alternar ambientes: Ctrl+Alt+1, Ctrl+Alt+2, Ctrl+Alt+3
            _hotkeyService.Registrar(101, new AtalhoConfig { Control = true, Alt = true, Tecla = "1" }, () =>
            {
                Dispatcher.Invoke(() => AlternarAmbientePorIndice(0));
            });
            _hotkeyService.Registrar(102, new AtalhoConfig { Control = true, Alt = true, Tecla = "2" }, () =>
            {
                Dispatcher.Invoke(() => AlternarAmbientePorIndice(1));
            });
            _hotkeyService.Registrar(103, new AtalhoConfig { Control = true, Alt = true, Tecla = "3" }, () =>
            {
                Dispatcher.Invoke(() => AlternarAmbientePorIndice(2));
            });
        }
        catch { }
    }

    private void AlternarAmbientePorIndice(int indice)
    {
        if (indice >= 0 && indice < _viewModel.Ambientes.Count)
        {
            _viewModel.AmbienteAtivo = _viewModel.Ambientes[indice];
            if (!_viewModel.DockVisivel)
            {
                _viewModel.DockVisivel = true;
            }
        }
    }

    private void InicializarBandeja(IntPtr hwnd)
    {
        try
        {
            _trayService = new Win32TrayService(hwnd, "GoatDock â€” Produtividade");
            _trayService.DuploClique += () =>
            {
                Dispatcher.Invoke(() => _viewModel.AlternarVisibilidade());
            };
            _trayService.CliqueDireito += () =>
            {
                Dispatcher.Invoke(ExibirMenuBandeja);
            };
        }
        catch { }
    }

    private void ExibirMenuBandeja()
    {
        var menu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 34)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 58))
        };

        var itemVisibilidade = new MenuItem
        {
            Header = _viewModel.DockVisivel ? "ðŸ”½ Ocultar Dock" : "ðŸ”¼ Exibir Dock",
            FontWeight = FontWeights.Bold
        };
        itemVisibilidade.Click += (s, e) => _viewModel.AlternarVisibilidade();
        menu.Items.Add(itemVisibilidade);

        menu.Items.Add(new Separator());

        var itemGamerRgb = new MenuItem { Header = "Modo gamer RGB", IsCheckable = true, IsChecked = _viewModel.ModoGamerRgb };
        itemGamerRgb.Click += (_, _) => _viewModel.ModoGamerRgb = itemGamerRgb.IsChecked;
        menu.Items.Add(itemGamerRgb);

        // Submenu de ambientes
        var menuAmbientes = new MenuItem { Header = "ðŸ’¼ Ambientes" };
        foreach (var amb in _viewModel.Ambientes)
        {
            var itemAmb = new MenuItem
            {
                Header = amb.Nome,
                IsChecked = amb.EstaAtivo
            };
            var a = amb;
            itemAmb.Click += (s, e) => _viewModel.AmbienteAtivo = a;
            menuAmbientes.Items.Add(itemAmb);
        }
        menu.Items.Add(menuAmbientes);

        menu.Items.Add(new Separator());

        var itemAjustes = new MenuItem { Header = "âš™ï¸ Ajustes e PersonalizaÃ§Ã£o...", FontWeight = FontWeights.SemiBold };
        itemAjustes.Click += (s, e) => _viewModel.AbrirAjustesCommand.Execute(null);
        menu.Items.Add(itemAjustes);

        var itemRestaurar = new MenuItem { Header = "ðŸ”„ Restaurar Barra do Windows" };
        itemRestaurar.Click += (s, e) => _viewModel.RestaurarBarraWindowsCommand.Execute(null);
        menu.Items.Add(itemRestaurar);

        menu.Items.Add(new Separator());

        var itemSair = new MenuItem { Header = "ðŸšª Sair do GoatDock" };
        itemSair.Click += (s, e) => Application.Current.Shutdown();
        menu.Items.Add(itemSair);

        menu.IsOpen = true;
    }

    private void ReposicionarBarra()
    {
        var (areaLeft, areaWidth, screenBottom) = AreaAlvo();
        Left = areaLeft + (areaWidth - ActualWidth) / 2.0;

        double margemInferiorJanela = MainGrid?.Margin.Bottom ?? 4.0;
        double gapBordaInferior = _viewModel.UsarComoBarraPrincipal ? 4.0 : 8.0;

        if (!_estaOcultoPorAutoHide)
        {
            Top = screenBottom - ActualHeight + margemInferiorJanela - gapBordaInferior;
        }
        else
        {
            Top = screenBottom - 4.0;
        }

        // O registro de AppBar é único no processo: só a janela principal o usa, e só quando o usuário quer reservar espaço.
        if (_primary == null)
        {
            if (_viewModel.UsarComoBarraPrincipal && _viewModel.ReservarEspacoDock && !_estaOcultoPorAutoHide)
            {
                Goat.Platform.Windows.AppBarHelper.RegisterBar(this);
                Goat.Platform.Windows.AppBarHelper.UpdatePos(this);
            }
            else
            {
                Goat.Platform.Windows.AppBarHelper.RemoveBar(this);
            }

            foreach (var secundaria in _secundarias) secundaria.ReposicionarBarra();
        }

        AgendarPublicacaoArea();
    }

    // Área (em DIPs) onde esta dock se centraliza e a borda inferior onde ela encosta, conforme o monitor.
    private (double Left, double Width, double Bottom) AreaAlvo()
    {
        if (_monitor == IntPtr.Zero)
        {
            var work = SystemParameters.WorkArea;
            double bottom = _viewModel.UsarComoBarraPrincipal ? SystemParameters.PrimaryScreenHeight : work.Bottom;
            return (work.Left, work.Width, bottom);
        }

        var monitor = MonitorHelper.FromHandle(_monitor) ?? MonitorHelper.Primary();
        if (monitor == null) return (0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Bottom);

        // Em pixels físicos; o WPF multiplica pelo DPI da janela, que nasce no monitor de destino.
        double scale = monitor.DpiScale;
        var area = monitor.WorkArea;
        var bounds = monitor.Bounds;
        double bottomPx = _viewModel.UsarComoBarraPrincipal ? bounds.Y + bounds.Height : area.Y + area.Height;
        return (area.X / scale, area.Width / scale, bottomPx / scale);
    }

    public void AtualizarLayoutSecoes()
    {
        SectionsContainer.Children.Clear();

        _secIniciarPesquisa ??= new Views.Sections.SectionIniciarPesquisa();
        _secApps ??= new Views.Sections.SectionApps();
        _secColecoes ??= new Views.Sections.SectionColecoes();
        _secItensAmbiente ??= new Views.Sections.SectionItensAmbiente();
        _secWidgets ??= new Views.Sections.SectionWidgets();
        _secRelogioControles ??= new Views.Sections.SectionRelogioControles();
        _secMidiaInline ??= new Views.Sections.SectionMidiaInline();
        _secClimaInline ??= new Views.Sections.SectionClimaInline();
        

        var secoesOrdenadas = _viewModel.OrdemSecoes.OrderBy(s => s.Ordem).ToList();
        bool primeiroAdicionado = false;
        int separadorIndex = 0;

        foreach (var cfg in secoesOrdenadas)
        {
            if (!cfg.Visivel) continue;

            FrameworkElement? controle = cfg.Tipo switch
            {
                TipoSecaoDock.IniciarPesquisa => _secIniciarPesquisa,
                TipoSecaoDock.Apps => _secApps,
                TipoSecaoDock.Colecoes => _secColecoes,
                TipoSecaoDock.ItensAmbiente => _secItensAmbiente,
                TipoSecaoDock.Widgets => _secWidgets,
                TipoSecaoDock.RelogioControles => _secRelogioControles,
                TipoSecaoDock.MidiaInline => _secMidiaInline,
                TipoSecaoDock.ClimaInline => _secClimaInline,
                
                _ => null
            };

            if (controle == null) continue;

            if (primeiroAdicionado)
            {
                var sep = CriarSeparador(separadorIndex++);
                SectionsContainer.Children.Add(sep);
            }

            SectionsContainer.Children.Add(controle);
            primeiroAdicionado = true;
        }

        ReposicionarBarra();
    }

    private FrameworkElement CriarSeparador(int indiceSeparador)
    {
        var espacadores = _viewModel.Espacadores;
        var cfg = (indiceSeparador < espacadores.Count) ? espacadores[indiceSeparador] : null;

        if (cfg != null && !cfg.Visivel)
        {
            return new FrameworkElement { Width = 0, Height = 0, Visibility = Visibility.Collapsed };
        }

        var estilo = cfg?.Estilo ?? EstiloEspacador.Linha;
        double largura = cfg?.Largura ?? 8.0;
        double halfMargem = Math.Max(2, largura / 2);

        switch (estilo)
        {
            case EstiloEspacador.Espaco:
                return new Border
                {
                    Width = Math.Max(2, largura),
                    Height = 24,
                    Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center
                };

            case EstiloEspacador.Ponto:
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Margin = new Thickness(halfMargem, 0, halfMargem, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(_viewModel.SeparadorColor))
                {
                    Source = _viewModel,
                    Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("ColorToBrushConverter")
                });
                return dot;

            case EstiloEspacador.Linha:
            default:
                var sep = new Border
                {
                    Width = Math.Max(2, largura),
                    Height = 24,
                    Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return sep;
        }
    }

    private void InformarVisibilidadeReal()
    {
        if (_primary != null) return;
        _viewModel.DefinirVisibilidadeReal(IsVisible && WindowState != WindowState.Minimized && !_estaOcultoPorAutoHide);
    }

    private void MainWindow_MouseEnter(object sender, MouseEventArgs e)
        => RevelarDock();

    private void RevelarDock()
    {
        _autoHideTimer.Stop();
        if (_estaOcultoPorAutoHide)
        {
            _estaOcultoPorAutoHide = false;
            InformarVisibilidadeReal();
            double screenBottom = AreaAlvo().Bottom;
            double margemInferiorJanela = MainGrid?.Margin.Bottom ?? 4.0;
            double gapBordaInferior = _viewModel.UsarComoBarraPrincipal ? 4.0 : 8.0;
            AnimarPosicaoVertical(screenBottom - ActualHeight + margemInferiorJanela - gapBordaInferior);
        }
    }

    private void MainWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_viewModel.OcultarAutomaticamente)
        {
            _autoHideTimer.Start();
        }
    }

    private void ExecutarAutoHide()
    {
        _autoHideTimer.Stop();
        if (_viewModel.OcultarAutomaticamente && !IsMouseOver && !_estaOcultoPorAutoHide)
        {
            _estaOcultoPorAutoHide = true;
            InformarVisibilidadeReal();
            double screenBottom = AreaAlvo().Bottom;
            AnimarPosicaoVertical(screenBottom - 4.0);
        }
    }

    private void AnimarPosicaoVertical(double destinoTop)
    {
        var anim = new DoubleAnimation
        {
            To = destinoTop,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, anim);
    }

    private void BtnGerenciarAmbiente_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var arquivos = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (arquivos != null)
            {
                foreach (var caminho in arquivos)
                {
                    var isDir = Directory.Exists(caminho);
                    var novo = new ItemFixado
                    {
                        Titulo = isDir ? Path.GetFileName(caminho) : Path.GetFileNameWithoutExtension(caminho),
                        CaminhoOuUrl = caminho,
                        Tipo = isDir ? TipoItem.Pasta : (Path.GetExtension(caminho).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(caminho).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? TipoItem.Aplicativo : TipoItem.Arquivo)
                    };
                    if (string.IsNullOrWhiteSpace(novo.Titulo))
                    {
                        novo.Titulo = caminho;
                    }
                    _viewModel.AdicionarItemDireto(novo);
                }
            }
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.MenuIniciarAberto = false;
            _viewModel.Clock.CalendarioAberto = false;
            _viewModel.Pomodoro.PainelAberto = false;
            _viewModel.Calendario.PainelAberto = false;

            foreach (var col in _viewModel.TodasColecoesAtivas)
            {
                col.PainelAberto = false;
            }

            foreach (var app in _viewModel.Aplicativos)
            {
                app.MenuJanelasAberto = false;
            }

            e.Handled = true;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        _autoHideTimer.Stop();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        if (_eventosHandler != null) _viewModel.PropertyChanged -= _eventosHandler;
        PararAnimacaoAlerta();

        // Fechar uma dock de outro monitor não encerra nada do que é compartilhado.
        if (_primary != null) return;

        foreach (var secundaria in _secundarias.ToList()) secundaria.Close();
        _secundarias.Clear();
        _publicacaoTimer?.Stop();
        _genie?.Dispose();
        _foreground?.Dispose();
        _ipc?.Dispose();

        Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        Microsoft.Win32.SystemEvents.TimeChanged -= SystemEvents_TimeChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameters_Changed;
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _viewModel.WinKeyHookService.Parar();

        if (_viewModel.UsarComoBarraPrincipal)
        {
            _viewModel.TaskbarService.RestaurarBarraNativa(_viewModel.Preferencias.EstadoAnteriorBarraTarefas);
        }
        Goat.Platform.Windows.AppBarHelper.RemoveBar(this);

        _viewModel.SalvarPreferencias();
        _viewModel.Dispose();
    }

    // ---- Outros monitores ----

    // Uma dock por monitor extra quando a opção está ligada; ao desligar (ou o monitor sair), as extras fecham.
    private void AtualizarJanelasSecundarias()
    {
        if (_primary != null) return;

        var principal = MonitorHelper.FromWindow(new WindowInteropHelper(this).Handle)?.Handle ?? IntPtr.Zero;
        var desejados = _viewModel.DockEmTodosMonitores
            ? MonitorHelper.GetMonitors().Where(m => m.Handle != principal).ToList()
            : new List<MonitorDescriptor>();

        foreach (var sobra in _secundarias.Where(s => desejados.All(m => m.Handle != s._monitor)).ToList())
        {
            sobra.Close();
            _secundarias.Remove(sobra);
        }

        foreach (var monitor in desejados.Where(m => _secundarias.All(s => s._monitor != m.Handle)))
        {
            var janela = new MainWindow(this, monitor.Handle) { WindowStartupLocation = WindowStartupLocation.Manual };
            // Posição inicial dentro do monitor para a janela nascer com o DPI dele.
            janela.Left = monitor.WorkArea.X / monitor.DpiScale + 20;
            janela.Top = monitor.WorkArea.Y / monitor.DpiScale + 20;
            _secundarias.Add(janela);
            if (_viewModel.DockVisivel && !_viewModel.OcultoPorTelaCheia) janela.Show();
        }
    }

    // ---- Integrações: efeito gênio e GoatFinder ----

    private void IniciarIntegracoes()
    {
        if (_ipc != null)
        {
            _ipc.PeerPresenceChanged += _ => Dispatcher.BeginInvoke(PublicarAreaDock);
            _ipc.Start();
        }

        _snapshots = new WindowSnapshotService();
        _foreground = new ForegroundAppService();
        _foreground.Start();
        _genie = new GenieController(_snapshots, LocalizarIcone, _foreground);
        AtualizarGenie();
    }

    private void AtualizarGenie()
    {
        if (_genie == null) return;
        if (_viewModel.EfeitoGenio) _genie.Enable();
        else _genie.Disable();
    }

    private IEnumerable<MainWindow> JanelasDock() => new[] { this }.Concat(_secundarias);

    // Centro do ícone do app na dock (pixels de tela), usando a dock do mesmo monitor da janela; null se não houver dock visível.
    private (double X, double Y)? LocalizarIcone(IntPtr hwnd, string nomeProcesso, string nomeExibicao)
    {
        var monitorJanela = MonitorHelper.FromWindow(hwnd)?.Handle;
        var docks = JanelasDock().Where(d => d.IsVisible).ToList();
        var dock = docks.FirstOrDefault(d => MonitorHelper.FromWindow(new WindowInteropHelper(d).Handle)?.Handle == monitorJanela)
                   ?? docks.FirstOrDefault();
        if (dock == null) return null;

        var itens = dock._secApps?.AppsItems;
        if (itens != null)
        {
            var app = itens.Items.OfType<AppItemViewModel>().FirstOrDefault(a => a.Janelas.Any(j => j.Hwnd == hwnd))
                      ?? itens.Items.OfType<AppItemViewModel>().FirstOrDefault(a =>
                          !string.IsNullOrEmpty(nomeProcesso) && a.NomeProcesso.Equals(nomeProcesso, StringComparison.OrdinalIgnoreCase))
                      ?? itens.Items.OfType<AppItemViewModel>().FirstOrDefault(a =>
                          !string.IsNullOrEmpty(nomeExibicao) && a.Titulo.Contains(nomeExibicao, StringComparison.OrdinalIgnoreCase));

            if (app != null && itens.ItemContainerGenerator.ContainerFromItem(app) is FrameworkElement container && container.IsVisible)
            {
                var centro = container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
                return (centro.X, centro.Y);
            }
        }

        // Ícone não visivel (app não fixado, dock oculta): mira o centro da própria dock.
        var meio = dock.PointToScreen(new Point(dock.ActualWidth / 2, dock.ActualHeight / 2));
        return (meio.X, meio.Y);
    }

    // ---- Área ocupada pela dock, publicada para o GoatFinder ----

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectPx rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectPx { public int Left, Top, Right, Bottom; }

    private void AgendarPublicacaoArea()
    {
        if (_primary != null) { _primary.AgendarPublicacaoArea(); return; }
        if (_ipc == null) return;

        // Juntar rajadas de mudança (animação de auto-ocultar move a janela várias vezes por segundo).
        _publicacaoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _publicacaoTimer.Tick -= PublicarAreaTick;
        _publicacaoTimer.Tick += PublicarAreaTick;
        _publicacaoTimer.Stop();
        _publicacaoTimer.Start();
    }

    private void PublicarAreaTick(object? sender, EventArgs e)
    {
        _publicacaoTimer?.Stop();
        PublicarAreaDock();
    }

    private void PublicarAreaDock()
    {
        if (_ipc == null) return;
        var areas = new List<DockArea>();
        foreach (var dock in JanelasDock().Where(d => d.IsVisible))
        {
            var hwnd = new WindowInteropHelper(dock).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
                areas.Add(new DockArea(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        }
        _ipc.Send(new DockBounds(areas));
    }
    private System.Windows.Media.Animation.Storyboard? _alertaStoryboard;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.OcultoPorTelaCheia) && !_viewModel.OcultoPorTelaCheia)
        {
            if (_viewModel.SempreNoTopo)
            {
                Topmost = false;
                Topmost = true;
            }
        }
        
        if (e.PropertyName is nameof(MainViewModel.EstaEmAlerta) or nameof(MainViewModel.AnimacoesAtivas) or nameof(MainViewModel.AlertaChamada))
        {
            if (_viewModel.EstaEmAlerta && !_viewModel.AlertaChamada && _viewModel.AnimacoesAtivas)
            {
                IniciarAnimacaoAlerta();
            }
            else
            {
                PararAnimacaoAlerta();
            }
        }
    }

    private void IniciarAnimacaoAlerta()
    {
        if (_alertaStoryboard != null)
        {
            _alertaStoryboard.Remove(this);
        }

        try
        {
            var cor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_viewModel.CorAlerta);

            // Anima a sombra pulsando
            var animacaoSombra = new System.Windows.Media.Animation.ColorAnimation
            {
                From = System.Windows.Media.Colors.Black,
                To = cor,
                Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.35)),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(1)
            };

            var animacaoRaio = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 16.0,
                To = 30.0,
                Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.35)),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(1)
            };

            System.Windows.Media.Animation.Storyboard.SetTarget(animacaoSombra, DockShadow);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(animacaoSombra, new System.Windows.PropertyPath(System.Windows.Media.Effects.DropShadowEffect.ColorProperty));
            
            System.Windows.Media.Animation.Storyboard.SetTarget(animacaoRaio, DockShadow);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(animacaoRaio, new System.Windows.PropertyPath(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty));

            _alertaStoryboard = new System.Windows.Media.Animation.Storyboard();
            _alertaStoryboard.Children.Add(animacaoSombra);
            _alertaStoryboard.Children.Add(animacaoRaio);
            _alertaStoryboard.Begin(this, true);
        }
        catch { }
    }

    private void PararAnimacaoAlerta()
    {
        if (_alertaStoryboard != null)
        {
            _alertaStoryboard.Remove(this);
            _alertaStoryboard = null;
        }
        
        // Restaura valores originais (ClearValue restaura o Binding ou valor do XAML original)
        // Remove restaura os valores-base e conserva os bindings da sombra.
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _shellHookMessage)
        {
            if (wParam.ToInt32() == HSHELL_FLASH)
            {
                _viewModel.IncrementarNotificacaoApp(lParam);
            }
        }
        return IntPtr.Zero;
    }
}

