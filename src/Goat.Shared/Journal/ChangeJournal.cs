using System.Text.Json;
using System.Text.Json.Serialization;
using Goat.Shared.Product;

namespace Goat.Shared.Journal;

public enum ChangeKind
{
    /// <summary>Valor do registro em HKCU.</summary>
    RegistryValue,
    /// <summary>Arquivo desktop.ini criado ou alterado numa pasta.</summary>
    DesktopIni,
    /// <summary>Parâmetro global de interface (SystemParametersInfo).</summary>
    SystemParameter,
}

/// <summary>Uma alteração feita no Windows, com o valor anterior guardado para desfazer.</summary>
public sealed class ChangeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Identificador estável da opção (ex.: "explorer.show-extensions").</summary>
    public string TweakId { get; set; } = string.Empty;
    public ChangeKind Kind { get; set; }

    /// <summary>Chave do registro, pasta ou nome do parâmetro.</summary>
    public string Target { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo do valor no registro ("DWord" ou "String").</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>Valor antes da alteração. Nulo significa que não existia.</summary>
    public string? Previous { get; set; }
    public string? Applied { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Diário de alterações do Windows (changes.json). Regra: a entrada é gravada ANTES da alteração,
/// então uma queda no meio do caminho nunca deixa algo alterado sem registro de como voltar.
/// </summary>
public sealed class ChangeJournal
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<ChangeEntry> _entries;

    public ChangeJournal(string? directory = null)
    {
        _path = Path.Combine(directory ?? ProductInfo.DataDirectory, "changes.json");
        _entries = Read();
    }

    public IReadOnlyList<ChangeEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public ChangeEntry? FindByTweak(string tweakId, string name)
    {
        lock (_gate)
            return _entries.FirstOrDefault(e => e.TweakId == tweakId && e.Name == name);
    }

    /// <summary>
    /// Registra a alteração. Se a mesma opção já tinha sido alterada antes, mantém o valor ORIGINAL
    /// (o de antes da primeira vez), para que desfazer sempre volte ao estado do usuário.
    /// </summary>
    public ChangeEntry Record(ChangeEntry entry)
    {
        lock (_gate)
        {
            var existing = _entries.FirstOrDefault(e => e.TweakId == entry.TweakId && e.Target == entry.Target && e.Name == entry.Name);
            if (existing != null)
            {
                existing.Applied = entry.Applied;
                existing.AppliedAtUtc = entry.AppliedAtUtc;
                Save();
                return existing;
            }

            _entries.Add(entry);
            Save();
            return entry;
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) > 0) Save();
        }
    }

    private List<ChangeEntry> Read()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<ChangeEntry>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
