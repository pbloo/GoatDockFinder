using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoatFinder.Core;
using Goat.Ui;

namespace GoatFinder.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private const long MaxThumbnailBytes = 25 * 1024 * 1024;
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Func<string, bool, Task<ImageSource?>> _iconLoader;
    private ImageSource? _icon, _thumbnail;
    private bool _iconRequested, _thumbnailRequested, _isSelected, _isCut;

    public FileItemViewModel(FileEntry entry, Func<string, bool, Task<ImageSource?>> iconLoader)
    {
        Entry = entry;
        _iconLoader = iconLoader;
    }

    public FileEntry Entry { get; }
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsHidden => Entry.IsHidden;
    public string Kind => Entry.Kind;
    public string SizeText => Entry.IsDirectory ? "--" : ByteSizeFormatter.Format(Entry.Size);
    public string ModifiedText => Entry.Modified.ToString("d MMM yyyy HH:mm", Portuguese);
    public string ParentFolder => Path.GetDirectoryName(Entry.FullPath) ?? string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsCut
    {
        get => _isCut;
        set => SetProperty(ref _isCut, value);
    }

    // O ícone só é pedido ao Shell quando o item aparece na tela (a lista é virtualizada).
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }
            return _icon;
        }
    }

    // Visão em ícones: miniatura da própria imagem quando existir, senão o ícone do Shell.
    public ImageSource? Visual
    {
        get
        {
            _ = Icon;
            if (!_thumbnailRequested && !IsDirectory && FileTypeCatalog.GetPreviewKind(Entry.Extension) == PreviewKind.Image)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }
            return _thumbnail ?? _icon;
        }
    }

    private async Task LoadIconAsync()
    {
        try
        {
            var icon = await _iconLoader(FullPath, IsDirectory);
            if (icon == null) return;
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(Visual));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
    }

    private async Task LoadThumbnailAsync()
    {
        var path = FullPath;
        var image = await Task.Run<ImageSource?>(() =>
        {
            try
            {
                if (new FileInfo(path).Length > MaxThumbnailBytes) return null;
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = 160;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        });

        if (image == null) return;
        _thumbnail = image;
        OnPropertyChanged(nameof(Visual));
    }
}
