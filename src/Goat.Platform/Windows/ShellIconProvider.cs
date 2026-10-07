using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Goat.Platform.Windows;

/// <summary>
/// Ícones do Shell para arquivos e pastas (os mesmos do Explorer). Roda numa thread STA própria
/// porque o Shell espera STA, e guarda em cache por extensão para listas grandes não explodirem a memória.
/// </summary>
public sealed class ShellIconProvider : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int imageList, ref Guid riid, out IntPtr ppv);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_GetIcon(IntPtr himl, int index, int flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_SYSICONINDEX = 0x4000, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int SHIL_EXTRALARGE = 0x2; // 48x48
    private const int ILD_TRANSPARENT = 0x1;
    private const int MaxCacheEntries = 600;

    // Para estas extensões o ícone é do próprio arquivo, então a chave do cache é o caminho completo.
    private static readonly HashSet<string> PerFileIcons = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".msi", ".url" };

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<(string Path, bool IsDirectory, TaskCompletionSource<ImageSource?> Result)> _queue = new();
    private readonly Thread _worker;

    public ShellIconProvider()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "ShellIconProvider" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public Task<ImageSource?> GetIconAsync(string path, bool isDirectory)
    {
        var key = CacheKey(path, isDirectory);
        if (_cache.TryGetValue(key, out var cached)) return Task.FromResult(cached);

        var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { _queue.Add((path, isDirectory, tcs)); }
        catch (InvalidOperationException) { tcs.TrySetResult(null); }
        return tcs.Task;
    }

    private static string CacheKey(string path, bool isDirectory)
    {
        if (isDirectory) return "<dir>";
        var ext = Path.GetExtension(path);
        return PerFileIcons.Contains(ext) ? path : string.IsNullOrEmpty(ext) ? "<file>" : ext;
    }

    private void Run()
    {
        foreach (var (path, isDirectory, result) in _queue.GetConsumingEnumerable())
        {
            try
            {
                var key = CacheKey(path, isDirectory);
                if (!_cache.TryGetValue(key, out var icon))
                {
                    icon = Load(path, isDirectory, key);
                    if (_cache.Count < MaxCacheEntries) _cache[key] = icon;
                }
                result.TrySetResult(icon);
            }
            catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException)
            {
                result.TrySetResult(null);
            }
        }
    }

    private static ImageSource? Load(string path, bool isDirectory, string key)
    {
        // Ícone genérico (por extensão/tipo) usa só os atributos; ícones por arquivo leem o próprio arquivo.
        bool generic = key != path;
        var info = new SHFILEINFO();
        uint flags = SHGFI_SYSICONINDEX | (generic ? SHGFI_USEFILEATTRIBUTES : 0);
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        if (SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero) return null;

        var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IID_IImageList
        if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out var imageList) != 0 || imageList == IntPtr.Zero) return null;

        var hIcon = ImageList_GetIcon(imageList, info.iIcon, ILD_TRANSPARENT);
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(hIcon); }
    }

    public void Dispose() => _queue.CompleteAdding();
}
