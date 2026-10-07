namespace GoatFinder.Core;

public enum PreviewKind { None, Image, Text }

public static class FileTypeCatalog
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "ico", "webp"
    };

    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "json", "xml", "csv", "log", "cs", "js", "ts", "tsx", "jsx", "css", "html", "htm",
        "yml", "yaml", "ini", "cfg", "config", "sql", "py", "java", "c", "cpp", "h", "sh", "ps1", "bat",
        "toml", "xaml", "csproj", "slnx", "gitignore"
    };

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "Documento PDF",
        ["doc"] = "Documento do Word",
        ["docx"] = "Documento do Word",
        ["xls"] = "Planilha",
        ["xlsx"] = "Planilha",
        ["ppt"] = "Apresentação",
        ["pptx"] = "Apresentação",
        ["zip"] = "Arquivo compactado",
        ["rar"] = "Arquivo compactado",
        ["7z"] = "Arquivo compactado",
        ["exe"] = "Aplicativo",
        ["msi"] = "Instalador",
        ["lnk"] = "Atalho",
        ["mp3"] = "Áudio",
        ["wav"] = "Áudio",
        ["flac"] = "Áudio",
        ["m4a"] = "Áudio",
        ["mp4"] = "Vídeo",
        ["mkv"] = "Vídeo",
        ["avi"] = "Vídeo",
        ["mov"] = "Vídeo",
    };

    public static PreviewKind GetPreviewKind(string extension)
    {
        if (Images.Contains(extension)) return PreviewKind.Image;
        if (Texts.Contains(extension)) return PreviewKind.Text;
        return PreviewKind.None;
    }

    public static string Describe(bool isDirectory, string extension)
    {
        if (isDirectory) return "Pasta";
        if (string.IsNullOrEmpty(extension)) return "Arquivo";
        if (Images.Contains(extension)) return $"Imagem {extension.ToUpperInvariant()}";
        if (Texts.Contains(extension)) return "Documento de texto";
        if (Kinds.TryGetValue(extension, out var kind)) return kind;
        return $"Arquivo {extension.ToUpperInvariant()}";
    }
}
