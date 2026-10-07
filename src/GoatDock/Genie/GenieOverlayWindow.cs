using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Goat.Platform.Windows;

namespace GoatDock.Genie;

/// <summary>
/// Janela transparente que cobre o monitor e desenha o "efeito gênio": a foto da janela é uma malha 3D
/// em que cada linha converge para o ícone da dock em momentos diferentes, formando o funil.
/// </summary>
public sealed class GenieOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int Rows = 40;
    private const double DurationMs = 440;
    private const double Lag = 0.6;            // quanto o topo atrasa em relação à base
    private const double MinWidthPx = 36;      // largura da janela ao chegar no ícone
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MeshGeometry3D _mesh = new();
    private readonly ImageBrush _brush = new() { Stretch = Stretch.Fill };
    private readonly OrthographicCamera _camera = new() { LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0) };
    private readonly Viewport3D _viewport = new() { IsHitTestVisible = false };
    private readonly Stopwatch _clock = new();

    private Int32Rect _monitor, _source;
    private Point _target;
    private bool _reverse, _running;
    private TaskCompletionSource? _completion;

    public GenieOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = Height = 100;

        var indices = new Int32Collection();
        var coordinates = new PointCollection();
        for (int i = 0; i <= Rows; i++)
        {
            double v = i / (double)Rows;
            coordinates.Add(new Point(0, v));
            coordinates.Add(new Point(1, v));
            if (i == Rows) continue;
            int a = 2 * i, b = a + 1, c = a + 2, d = a + 3;
            indices.Add(a); indices.Add(c); indices.Add(b);
            indices.Add(b); indices.Add(c); indices.Add(d);
        }
        _mesh.TriangleIndices = indices;
        _mesh.TextureCoordinates = coordinates;

        var material = new DiffuseMaterial(_brush);
        var group = new Model3DGroup();
        group.Children.Add(new GeometryModel3D(_mesh, material) { BackMaterial = material });
        group.Children.Add(new AmbientLight(Colors.White));
        _viewport.Children.Add(new ModelVisual3D { Content = group });
        _viewport.Camera = _camera;
        Content = _viewport;

        SourceInitialized += (_, _) => WindowStyles.Apply(this, noActivate: true, toolWindow: true, clickThrough: true);
    }

    /// <param name="targetPx">Ponto de destino em pixels de tela (o ícone na dock).</param>
    public Task PlayAsync(WindowSnapshot snapshot, Point targetPx, bool reverse)
    {
        Stop();

        _source = snapshot.ScreenBoundsPx;
        _target = targetPx;
        _reverse = reverse;
        _monitor = MonitorHelper.GetMonitorRectAt(_source.X + _source.Width / 2, _source.Y + _source.Height / 2);
        _brush.ImageSource = snapshot.Image;

        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(hwnd, HwndTopmost, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        // Câmera ortográfica em pixels: 1 unidade do mundo = 1 pixel de tela, independente de DPI.
        _camera.Width = _monitor.Width;
        _camera.Position = new Point3D(_monitor.Width / 2.0, _monitor.Height / 2.0, 100);

        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _clock.Restart();
        UpdateFrame(_reverse ? 1 : 0);
        if (!_running)
        {
            CompositionTarget.Rendering += OnRendering;
            _running = true;
        }
        return _completion.Task;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double t = Math.Clamp(_clock.Elapsed.TotalMilliseconds / DurationMs, 0, 1);
        double eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        UpdateFrame(_reverse ? 1 - eased : eased);
        if (t >= 1) Stop();
    }

    private void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
        if (IsVisible) Hide();
        _completion?.TrySetResult();
        _completion = null;
    }

    // progress: 0 = janela inteira no lugar; 1 = tudo colapsado no ícone.
    private void UpdateFrame(double progress)
    {
        double left = _source.X - _monitor.X, top = _source.Y - _monitor.Y;
        double width = _source.Width, height = _source.Height;
        double centerX = left + width / 2;
        double targetX = _target.X - _monitor.X, targetY = _target.Y - _monitor.Y;
        bool leadAtBottom = targetY >= top + height / 2;

        var positions = new Point3DCollection((Rows + 1) * 2);
        for (int i = 0; i <= Rows; i++)
        {
            double v = i / (double)Rows;
            double lead = leadAtBottom ? v : 1 - v;              // 1 = linha mais próxima do ícone
            double s = Math.Clamp(progress * (1 + Lag) - (1 - lead) * Lag, 0, 1);
            double e = s * s * (3 - 2 * s);                       // smoothstep

            double y = Lerp(top + v * height, targetY, e);
            double center = Lerp(centerX, targetX, e);
            double half = Lerp(width, MinWidthPx, e) / 2;
            double worldY = _monitor.Height - y;                  // eixo Y do 3D aponta para cima

            positions.Add(new Point3D(center - half, worldY, 0));
            positions.Add(new Point3D(center + half, worldY, 0));
        }
        _mesh.Positions = positions;
        _viewport.Opacity = 1 - Math.Clamp((progress - 0.88) / 0.12, 0, 1);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
