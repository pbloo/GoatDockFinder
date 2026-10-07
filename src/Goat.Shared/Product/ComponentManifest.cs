using System.Text.Json;
using System.Text.Json.Serialization;

namespace Goat.Shared.Product;

/// <summary>Registro do que está instalado (components.json), escrito pelo instalador e lido pelos componentes.</summary>
public sealed class ComponentManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string InstallDirectory { get; set; } = string.Empty;
    public Dictionary<ComponentId, InstalledComponent> Components { get; set; } = [];

    public bool IsInstalled(ComponentId component) => Components.ContainsKey(component);
}

public sealed class InstalledComponent
{
    public string Version { get; set; } = string.Empty;
    public string RelativeDirectory { get; set; } = string.Empty;
}

public sealed class ComponentManifestStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public ComponentManifestStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? ProductInfo.DataDirectory, "components.json");
    }

    public string FilePath => _path;

    public ComponentManifest Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<ComponentManifest>(File.ReadAllText(_path), Json) ?? new ComponentManifest();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new ComponentManifest();
    }

    public void Save(ComponentManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, Json));
        File.Move(temp, _path, overwrite: true);
    }

    public void Delete()
    {
        try { File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
