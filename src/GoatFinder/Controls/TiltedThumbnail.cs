using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GoatFinder.Controls;

/// <summary>
/// Mostra uma imagem como um painel inclinado em perspectiva (o visual das miniaturas do Stage Manager).
/// O WPF não tem transformação de perspectiva 2D, então a imagem vira textura de um plano 3D girado.
/// </summary>
public sealed class TiltedThumbnail : Border
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(TiltedThumbnail), new PropertyMetadata(null, (d, _) => ((TiltedThumbnail)d).Refresh()));

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(TiltedThumbnail), new PropertyMetadata(26.0, (d, e) => ((TiltedThumbnail)d)._rotation.Angle = (double)e.NewValue));

    private static readonly Brush Placeholder = CreatePlaceholder();

    private readonly MeshGeometry3D _mesh = new();
    private readonly GeometryModel3D _model;
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), 26);
    private readonly ImageBrush _imageBrush = new() { Stretch = Stretch.Fill };

    public TiltedThumbnail()
    {
        _mesh.TriangleIndices = [0, 1, 2, 0, 2, 3];
        _mesh.TextureCoordinates = [new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)];

        _model = new GeometryModel3D(_mesh, new DiffuseMaterial(Placeholder));
        _model.BackMaterial = _model.Material;

        var group = new Model3DGroup { Transform = new RotateTransform3D(_rotation) };
        group.Children.Add(_model);
        group.Children.Add(new AmbientLight(Colors.White));

        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0, 0, 4.4), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 32),
            IsHitTestVisible = false,
        };
        viewport.Children.Add(new ModelVisual3D { Content = group });
        Child = viewport;

        Refresh();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // Graus de rotação em torno do eixo vertical; positivo afasta a borda direita.
    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    private void Refresh()
    {
        var source = Source;
        double aspect = source is { Height: > 0 } ? Math.Clamp(source.Width / source.Height, 0.6, 2.0) : 1.6;
        _mesh.Positions = [new Point3D(-aspect, -1, 0), new Point3D(aspect, -1, 0), new Point3D(aspect, 1, 0), new Point3D(-aspect, 1, 0)];

        if (source == null)
        {
            _model.Material = _model.BackMaterial = new DiffuseMaterial(Placeholder);
            return;
        }

        _imageBrush.ImageSource = source;
        _model.Material = _model.BackMaterial = new DiffuseMaterial(_imageBrush);
    }

    private static Brush CreatePlaceholder()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
        brush.Freeze();
        return brush;
    }
}
