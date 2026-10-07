using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Goat.Platform.Windows;

/// <param name="Image">Imagem congelada (pode ser usada em qualquer thread).</param>
/// <param name="ScreenBoundsPx">Retângulo visível da janela, em pixels de tela, no momento da captura.</param>
public sealed record WindowSnapshot(BitmapSource Image, Int32Rect ScreenBoundsPx, DateTime CapturedAt);

/// <summary>
/// Tira "fotos" de janelas com PrintWindow e guarda a última de cada uma. Uma janela minimizada
/// não pode ser fotografada, então o efeito gênio e o Stage Manager usam a última foto guardada.
/// </summary>
public sealed class WindowSnapshotService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    private const uint PW_RENDERFULLCONTENT = 0x2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int MaxCachedWindows = 16;
    private const int MaxImageWidth = 1600;

    private readonly ConcurrentDictionary<IntPtr, WindowSnapshot> _cache = new();

    public WindowSnapshot? GetCached(IntPtr hwnd) => _cache.TryGetValue(hwnd, out var s) ? s : null;

    public void Forget(IntPtr hwnd) => _cache.TryRemove(hwnd, out _);

    public Task<WindowSnapshot?> CaptureAsync(IntPtr hwnd) => Task.Run(() => Capture(hwnd));

    private WindowSnapshot? Capture(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || IsIconic(hwnd) || IsHungAppWindow(hwnd)) return null;
        if (!GetWindowRect(hwnd, out var outer)) return null;

        int width = outer.right - outer.left, height = outer.bottom - outer.top;
        if (width < 32 || height < 32) return null;

        // O retângulo "visível" exclui a sombra invisível que o Windows 10/11 adiciona ao redor da janela.
        var frame = outer;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var ext, Marshal.SizeOf<RECT>()) == 0) frame = ext;

        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);
            old = SelectObject(memory, bitmap);
            if (!PrintWindow(hwnd, memory, PW_RENDERFULLCONTENT)) return null;

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // O HBITMAP não preenche o canal alfa; converter para Bgr32 evita uma imagem "transparente".
            source = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);

            var crop = new Int32Rect(
                Math.Clamp(frame.left - outer.left, 0, width - 1),
                Math.Clamp(frame.top - outer.top, 0, height - 1),
                Math.Max(1, Math.Min(frame.right - frame.left, width)),
                Math.Max(1, Math.Min(frame.bottom - frame.top, height)));
            crop.Width = Math.Min(crop.Width, width - crop.X);
            crop.Height = Math.Min(crop.Height, height - crop.Y);

            BitmapSource result = new CroppedBitmap(source, crop);
            if (result.PixelWidth > MaxImageWidth)
            {
                double scale = (double)MaxImageWidth / result.PixelWidth;
                result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            }
            result.Freeze();

            var snapshot = new WindowSnapshot(result, new Int32Rect(frame.left, frame.top, frame.right - frame.left, frame.bottom - frame.top), DateTime.UtcNow);
            Store(hwnd, snapshot);
            return snapshot;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void Store(IntPtr hwnd, WindowSnapshot snapshot)
    {
        _cache[hwnd] = snapshot;
        if (_cache.Count <= MaxCachedWindows) return;

        // Remove as fotos mais antigas e as de janelas que já foram fechadas.
        foreach (var stale in _cache.Where(kv => !IsWindow(kv.Key)).Select(kv => kv.Key).ToList())
            _cache.TryRemove(stale, out _);
        while (_cache.Count > MaxCachedWindows)
        {
            var oldest = _cache.OrderBy(kv => kv.Value.CapturedAt).First().Key;
            _cache.TryRemove(oldest, out _);
        }
    }
}
