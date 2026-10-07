using System.Windows;
using System.Windows.Threading;
using Goat.Platform.Windows;

namespace GoatDock.Genie;

/// <summary>
/// Liga o efeito gênio: guarda fotos recentes da janela ativa e, quando alguma minimiza ou restaura,
/// toca a animação em direção ao ícone dela na dock.
/// </summary>
public sealed class GenieController : IDisposable
{
    private static readonly TimeSpan TargetCacheTime = TimeSpan.FromSeconds(3);

    private readonly WindowStateWatcher _watcher = new();
    private readonly WindowSnapshotService _snapshots;
    private readonly Func<IntPtr, string, string, (double X, double Y)?> _locateIcon;
    private readonly ForegroundAppService _foreground;
    private readonly DispatcherTimer _captureTimer;
    private readonly Dictionary<string, (Point Target, DateTime At)> _targetCache = new(StringComparer.OrdinalIgnoreCase);
    private GenieOverlayWindow? _overlay;
    private bool _enabled;

    /// <param name="locateIcon">Dada a janela, o processo e o nome do app, devolve o centro do ícone na tela (pixels) ou null. Roda na thread de interface.</param>
    public GenieController(WindowSnapshotService snapshots, Func<IntPtr, string, string, (double X, double Y)?> locateIcon, ForegroundAppService foreground)
    {
        _snapshots = snapshots;
        _locateIcon = locateIcon;
        _foreground = foreground;
        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _captureTimer.Tick += (_, _) => CaptureForeground();
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        _watcher.Minimizing += OnMinimizing;
        _watcher.Restoring += OnRestoring;
        _foreground.ForegroundChanged += OnForegroundChanged;
        _watcher.Start();
        _captureTimer.Start();
        CaptureForeground();
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;
        _watcher.Minimizing -= OnMinimizing;
        _watcher.Restoring -= OnRestoring;
        _foreground.ForegroundChanged -= OnForegroundChanged;
        _captureTimer.Stop();
        _overlay?.Close();
        _overlay = null;
    }

    private async void OnForegroundChanged(ForegroundAppInfo info)
    {
        if (info.IsOwnProcess || info.IsDesktop) return;
        // Dá tempo da janela terminar de desenhar antes da foto.
        await Task.Delay(300);
        await _snapshots.CaptureAsync(info.Hwnd);
    }

    private void CaptureForeground()
    {
        var current = _foreground.Current;
        if (current is { IsOwnProcess: false, IsDesktop: false }) _ = _snapshots.CaptureAsync(current.Hwnd);
    }

    private async void OnMinimizing(IntPtr hwnd) => await PlayAsync(hwnd, reverse: false);

    private async void OnRestoring(IntPtr hwnd) => await PlayAsync(hwnd, reverse: true);

    private async Task PlayAsync(IntPtr hwnd, bool reverse)
    {
        try
        {
            var snapshot = _snapshots.GetCached(hwnd);
            if (snapshot == null) return;

            var target = ResolveTarget(hwnd, snapshot);
            if (!_enabled) return;

            _overlay ??= new GenieOverlayWindow();
            await _overlay.PlayAsync(snapshot, target, reverse);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A animação é só enfeite: nunca deve derrubar o shell.
        }
    }

    private Point ResolveTarget(IntPtr hwnd, WindowSnapshot snapshot)
    {
        var info = _foreground.Describe(hwnd);
        var key = info?.ProcessName ?? hwnd.ToString();
        if (_targetCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TargetCacheTime) return cached.Target;

        (double X, double Y)? found = info == null ? null : _locateIcon(hwnd, info.DisplayName, info.ProcessName);
        Point target;
        if (found is { } f)
        {
            target = new Point(f.X, f.Y);
        }
        else
        {
            // Sem dock ou sem o ícone: mira o centro inferior do monitor.
            var monitor = MonitorHelper.GetMonitorRectAt(snapshot.ScreenBoundsPx.X + snapshot.ScreenBoundsPx.Width / 2, snapshot.ScreenBoundsPx.Y + snapshot.ScreenBoundsPx.Height / 2);
            target = new Point(monitor.X + monitor.Width / 2.0, monitor.Y + monitor.Height - 30);
        }
        _targetCache[key] = (target, DateTime.UtcNow);
        return target;
    }

    public void Dispose()
    {
        Disable();
        _watcher.Dispose();
    }
}
