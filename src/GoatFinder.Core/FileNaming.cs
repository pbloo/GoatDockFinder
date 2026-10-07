namespace GoatFinder.Core;

public static class FileNaming
{
    public static bool IsValidName(string? name, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "O nome não pode ficar vazio.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "O nome não pode conter \\ / : * ? \" < > |";
            return false;
        }
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            error = "O nome não pode terminar com ponto ou espaço.";
            return false;
        }
        return true;
    }

    // copyWord != null gera "nome cópia", "nome cópia 2"; null gera "nome 2", "nome 3".
    public static string GetUniqueName(string directory, string name, bool isDirectory, string? copyWord, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        if (!exists(Path.Combine(directory, name))) return name;

        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 1; ; i++)
        {
            var label = copyWord is null
                ? $"{stem} {i + 1}"
                : i == 1 ? $"{stem} {copyWord}" : $"{stem} {copyWord} {i}";
            var candidate = label + ext;
            if (!exists(Path.Combine(directory, candidate))) return candidate;
        }
    }
}
