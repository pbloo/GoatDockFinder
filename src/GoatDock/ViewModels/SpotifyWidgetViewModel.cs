using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GoatDock.Common;
using GoatDock.Core.Widgets;

namespace GoatDock.ViewModels;

/// <summary>
/// Widget do Spotify. Usa as sessões de mídia do Windows (SMTC), então não exige conta nem credenciais:
/// vale para o aplicativo desktop do Spotify. Volume não é controlado (o SMTC não expõe volume).
/// </summary>
public sealed class SpotifyWidgetViewModel : ObservableObject, IAtividadeWidget
{
    private const string Verde = "#1DB954", Cinza = "#8E8E93";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _habilitado, _visual, _escutar, _iniciando, _disposed;
    private int _revisao;

    private string _titulo = "Spotify", _artista = "Spotify fechado", _posicao = "—", _duracao = "—";
    private ImageSource? _capa;
    private double _progresso;
    private bool _tocando, _aleatorio;
    private MediaPlaybackAutoRepeatMode _repeticao = MediaPlaybackAutoRepeatMode.None;

    public SpotifyWidgetViewModel()
    {
        _timer.Tick += (_, _) => AtualizarTimeline();
        PlayPauseCommand = new RelayCommand(() => _ = Executar(s => s.TryTogglePlayPauseAsync()));
        AnteriorCommand = new RelayCommand(() => _ = Executar(s => s.TrySkipPreviousAsync()));
        ProximoCommand = new RelayCommand(() => _ = Executar(s => s.TrySkipNextAsync()));
        AleatorioCommand = new RelayCommand(() => _ = Executar(s => s.TryChangeShuffleActiveAsync(!_aleatorio)));
        RepeticaoCommand = new RelayCommand(() => _ = Executar(s => s.TryChangeAutoRepeatModeAsync(ProximaRepeticao(_repeticao))));
        AbrirSpotifyCommand = new RelayCommand(AbrirSpotify);
    }

    public bool? EmExecucao => !_disposed && _escutar && (_iniciando || _manager != null);

    public bool Habilitado { get => _habilitado; set => SetProperty(ref _habilitado, value); }
    public bool SpotifyAberto => _session != null;
    public string Titulo { get => _titulo; private set => SetProperty(ref _titulo, value); }
    public string Artista { get => _artista; private set => SetProperty(ref _artista, value); }
    public ImageSource? Capa { get => _capa; private set { if (SetProperty(ref _capa, value)) OnPropertyChanged(nameof(TemCapa)); } }
    public bool TemCapa => _capa != null;
    public double Progresso { get => _progresso; private set => SetProperty(ref _progresso, value); }
    public string Posicao { get => _posicao; private set => SetProperty(ref _posicao, value); }
    public string Duracao { get => _duracao; private set => SetProperty(ref _duracao, value); }

    public bool EstaTocando { get => _tocando; private set { if (SetProperty(ref _tocando, value)) { OnPropertyChanged(nameof(GlifoPlay)); AtualizarTimer(); } } }
    public string GlifoPlay => _tocando ? "\uE103" : "\uE102";

    public bool Aleatorio { get => _aleatorio; private set { if (SetProperty(ref _aleatorio, value)) OnPropertyChanged(nameof(CorAleatorio)); } }
    public string CorAleatorio => _aleatorio ? Verde : Cinza;

    public bool RepeticaoAtiva => _repeticao != MediaPlaybackAutoRepeatMode.None;
    public string CorRepeticao => RepeticaoAtiva ? Verde : Cinza;
    public string GlifoRepeticao => _repeticao == MediaPlaybackAutoRepeatMode.Track ? "\uE8ED" : "\uE8EE";

    public ICommand PlayPauseCommand { get; }
    public ICommand AnteriorCommand { get; }
    public ICommand ProximoCommand { get; }
    public ICommand AleatorioCommand { get; }
    public ICommand RepeticaoCommand { get; }
    public ICommand AbrirSpotifyCommand { get; }

    public void DefinirAtividade(EstadoAtividade estado)
    {
        var eraVisual = _visual;
        _visual = estado.Visual;
        _escutar = estado.Habilitado && (estado.Visual || estado.SegundoPlano);

        if (!_escutar) Desconectar();
        else if (_manager == null) _ = InicializarAsync();
        else if (_visual && !eraVisual) { _ = AtualizarPropriedadesAsync(); AtualizarPlayback(); }
        AtualizarTimer();
    }

    /// <summary>Move a reprodução para a fração (0 a 1) da faixa; o Spotify só aceita se a faixa permitir busca.</summary>
    public void Buscar(double fracao)
    {
        if (_session is not { } sessao || double.IsNaN(fracao)) return;
        try
        {
            var linha = sessao.GetTimelineProperties();
            var total = linha.EndTime - linha.StartTime;
            if (total <= TimeSpan.Zero) return;
            var alvo = linha.StartTime + TimeSpan.FromTicks((long)(total.Ticks * Math.Clamp(fracao, 0, 1)));
            _ = sessao.TryChangePlaybackPositionAsync(alvo.Ticks);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private static MediaPlaybackAutoRepeatMode ProximaRepeticao(MediaPlaybackAutoRepeatMode atual) => atual switch
    {
        MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
        MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
        _ => MediaPlaybackAutoRepeatMode.None,
    };

    private async Task Executar(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> acao)
    {
        if (_session is not { } sessao) { AbrirSpotify(); return; }
        try { await acao(sessao); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private static void AbrirSpotify()
    {
        // O esquema "spotify:" é registrado pelo aplicativo; sem ele instalado, o Windows apenas não abre nada.
        try { Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private async Task InicializarAsync()
    {
        if (_disposed || _iniciando) return;
        _iniciando = true;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed || !_escutar) return;
            _manager = manager;
            _manager.SessionsChanged += OnSessionsChanged;
            UsarSessao(EncontrarSpotify());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { _iniciando = false; }
    }

    private GlobalSystemMediaTransportControlsSession? EncontrarSpotify() =>
        _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        NaInterface(() => { if (!_disposed && _escutar) UsarSessao(EncontrarSpotify()); });

    private void UsarSessao(GlobalSystemMediaTransportControlsSession? sessao)
    {
        if (ReferenceEquals(sessao, _session)) return;
        SoltarSessao();
        _session = sessao;
        OnPropertyChanged(nameof(SpotifyAberto));

        if (sessao == null)
        {
            Titulo = "Spotify";
            Artista = "Spotify fechado";
            Capa = null;
            EstaTocando = false;
            Progresso = 0;
            Posicao = Duracao = "—";
            return;
        }

        sessao.MediaPropertiesChanged += OnPropriedadesMudaram;
        sessao.PlaybackInfoChanged += OnPlaybackMudou;
        sessao.TimelinePropertiesChanged += OnTimelineMudou;
        _ = AtualizarPropriedadesAsync();
        AtualizarPlayback();
    }

    private void SoltarSessao()
    {
        if (_session == null) return;
        _session.MediaPropertiesChanged -= OnPropriedadesMudaram;
        _session.PlaybackInfoChanged -= OnPlaybackMudou;
        _session.TimelinePropertiesChanged -= OnTimelineMudou;
        _session = null;
    }

    private void OnPropriedadesMudaram(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        NaInterface(() => { if (ReferenceEquals(sender, _session)) _ = AtualizarPropriedadesAsync(); });

    private void OnPlaybackMudou(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        NaInterface(() => { if (ReferenceEquals(sender, _session)) AtualizarPlayback(); });

    private void OnTimelineMudou(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        NaInterface(() => { if (ReferenceEquals(sender, _session)) AtualizarTimeline(); });

    private async Task AtualizarPropriedadesAsync()
    {
        var sessao = _session;
        if (sessao == null || !_visual) return;
        var revisao = Interlocked.Increment(ref _revisao);
        try
        {
            var propriedades = await sessao.TryGetMediaPropertiesAsync();
            ImageSource? capa = null;
            if (propriedades.Thumbnail is { } miniatura) capa = await LerCapaAsync(miniatura);

            // Se outra atualização começou enquanto esta esperava, esta resposta já está velha.
            if (_disposed || revisao != _revisao || !ReferenceEquals(sessao, _session)) return;
            Titulo = string.IsNullOrWhiteSpace(propriedades.Title) ? "Spotify" : propriedades.Title;
            Artista = string.IsNullOrWhiteSpace(propriedades.Artist) ? propriedades.AlbumTitle : propriedades.Artist;
            Capa = capa;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException) { }
    }

    private static async Task<ImageSource?> LerCapaAsync(IRandomAccessStreamReference referencia)
    {
        using var origem = await referencia.OpenReadAsync();
        if (origem.Size == 0 || origem.Size > 4 * 1024 * 1024) return null;

        var bytes = new byte[origem.Size];
        using (var leitor = new DataReader(origem))
        {
            await leitor.LoadAsync((uint)origem.Size);
            leitor.ReadBytes(bytes);
        }

        var imagem = new BitmapImage();
        using var memoria = new MemoryStream(bytes);
        imagem.BeginInit();
        imagem.CacheOption = BitmapCacheOption.OnLoad;
        imagem.DecodePixelWidth = 96;
        imagem.StreamSource = memoria;
        imagem.EndInit();
        imagem.Freeze();
        return imagem;
    }

    private void AtualizarPlayback()
    {
        if (_session == null) return;
        try
        {
            var info = _session.GetPlaybackInfo();
            EstaTocando = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Aleatorio = info.IsShuffleActive == true;
            _repeticao = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
            OnPropertyChanged(nameof(RepeticaoAtiva));
            OnPropertyChanged(nameof(CorRepeticao));
            OnPropertyChanged(nameof(GlifoRepeticao));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private void AtualizarTimeline()
    {
        if (_session == null) { Progresso = 0; Posicao = Duracao = "—"; return; }
        try
        {
            var linha = _session.GetTimelineProperties();
            var total = linha.EndTime - linha.StartTime;
            if (total <= TimeSpan.Zero) { Progresso = 0; Posicao = Duracao = "—"; return; }

            var posicao = linha.Position - linha.StartTime;
            if (EstaTocando) posicao += DateTimeOffset.UtcNow - linha.LastUpdatedTime;
            var segundos = Math.Clamp(posicao.TotalSeconds, 0, total.TotalSeconds);
            Progresso = segundos / total.TotalSeconds;
            Posicao = $"{(int)(segundos / 60)}:{(int)segundos % 60:00}";
            Duracao = $"{(int)total.TotalMinutes}:{total.Seconds:00}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { Progresso = 0; }
    }

    // O relógio só roda com a dock visível e música tocando: sem isso não há motivo para acordar a cada segundo.
    private void AtualizarTimer()
    {
        _timer.Stop();
        if (_disposed || !_visual) return;
        AtualizarTimeline();
        if (EstaTocando) _timer.Start();
    }

    private static void NaInterface(Action acao)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        if (dispatcher.CheckAccess()) acao();
        else dispatcher.InvokeAsync(acao);
    }

    private void Desconectar()
    {
        Interlocked.Increment(ref _revisao);
        _timer.Stop();
        if (_manager != null) { _manager.SessionsChanged -= OnSessionsChanged; _manager = null; }
        SoltarSessao();
        OnPropertyChanged(nameof(SpotifyAberto));
    }

    public void Dispose()
    {
        _disposed = true;
        _visual = _escutar = false;
        Desconectar();
    }
}
