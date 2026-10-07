using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Goat.Ui;
using Goat.Platform.Windows;

namespace GoatFinder.ViewModels;

public sealed class StageItemViewModel : ObservableObject
{
    private string _title = string.Empty;
    private ImageSource? _icon;
    private BitmapSource? _thumbnail;

    public StageItemViewModel(IntPtr hwnd) => Hwnd = hwnd;

    public IntPtr Hwnd { get; }
    public DateTime LastCapture { get; set; } = DateTime.MinValue;
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    public BitmapSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
}

/// <summary>Mantém a lista de janelas fora do palco (todas, menos a ativa) com miniatura e ícone.</summary>
public sealed class StageManagerViewModel : ObservableObject, IDisposable
{
    private const int MaxItems = 6;
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromSeconds(2.5);

    private readonly IWindowTrackingService _tracking;
    private readonly IconExtractionService _icons;
    private readonly WindowSnapshotService _snapshots;
    private readonly DispatcherTimer _timer;
    private bool _refreshing, _suppressed;

    public StageManagerViewModel(IWindowTrackingService tracking, IconExtractionService icons, WindowSnapshotService snapshots)
    {
        _tracking = tracking;
        _icons = icons;
        _snapshots = snapshots;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<StageItemViewModel> Items { get; } = [];

    // Aplicativos em tela cheia (jogos, vídeo) escondem a faixa para não atrapalhar.
    public bool Suppressed
    {
        get => _suppressed;
        set { if (SetProperty(ref _suppressed, value)) OnPropertyChanged(nameof(IsVisible)); }
    }

    public bool IsVisible => !Suppressed && Items.Count > 0;

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var windows = _tracking.ObterJanelasAbertas().Where(w => !w.EstaAtiva).Take(MaxItems).ToList();
            Reconcile(windows);

            foreach (var item in Items.ToList())
            {
                var window = windows.FirstOrDefault(w => w.Hwnd == item.Hwnd);
                if (window == null) continue;

                // Janela minimizada não pode ser fotografada: usa a última foto guardada.
                if (window.EstaMinimizada)
                {
                    item.Thumbnail ??= _snapshots.GetCached(item.Hwnd)?.Image;
                }
                else if (DateTime.UtcNow - item.LastCapture > CaptureInterval)
                {
                    var snapshot = await _snapshots.CaptureAsync(item.Hwnd);
                    item.LastCapture = DateTime.UtcNow;
                    if (snapshot != null) item.Thumbnail = snapshot.Image;
                }
            }
        }
        finally { _refreshing = false; }
    }

    private void Reconcile(List<JanelaInfo> windows)
    {
        foreach (var gone in Items.Where(i => windows.All(w => w.Hwnd != i.Hwnd)).ToList())
        {
            Items.Remove(gone);
            _snapshots.Forget(gone.Hwnd);
        }

        for (int index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var existing = Items.FirstOrDefault(i => i.Hwnd == window.Hwnd);
            if (existing == null)
            {
                existing = new StageItemViewModel(window.Hwnd) { Icon = _icons.ObterIconeJanela(window.Hwnd) };
                Items.Insert(Math.Min(index, Items.Count), existing);
            }
            existing.Title = window.Titulo;
        }
        OnPropertyChanged(nameof(IsVisible));
    }

    // Traz o app escolhido para o palco e manda o que estava nele para a faixa (minimizando).
    public bool Activate(StageItemViewModel item)
    {
        // A janela ativa vem do rastreador (nunca a área de trabalho ou uma janela nossa), para não minimizar o shell.
        var previous = _tracking.ObterJanelasAbertas().FirstOrDefault(w => w.EstaAtiva && w.Hwnd != item.Hwnd);
        if (previous != null) _ = _snapshots.CaptureAsync(previous.Hwnd);

        var activated = WindowCommands.Activate(item.Hwnd);
        if (activated && previous != null) WindowCommands.Minimize(previous.Hwnd);
        _ = RefreshAsync();
        return activated;
    }

    public void Dispose() => _timer.Stop();
}
