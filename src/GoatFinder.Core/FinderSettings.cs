using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatFinder.Core;

public sealed class FinderSettings
{
    public bool StageManagerEnabled { get; set; } = true;
    public bool ShowHidden { get; set; }
    public bool PreviewPaneVisible { get; set; } = true;
    public bool IconView { get; set; }
    public FileSortKey SortKey { get; set; } = FileSortKey.Name;
    public bool SortAscending { get; set; } = true;
    public string? LastFolder { get; set; }

    public MenuBarSettings Bar { get; set; } = new();

    public void Normalize()
    {
        Bar ??= new MenuBarSettings();
        Bar.Normalize();
    }
}

public sealed class FinderSettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public FinderSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoatDockFinder", "finder-settings.json");
    }

    public FinderSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<FinderSettings>(File.ReadAllText(_path), Json) ?? new FinderSettings();
                loaded.Normalize();
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new FinderSettings();
    }

    public void Save(FinderSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Escrita atômica: grava num temporário e troca, para não corromper o arquivo em caso de falha.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
