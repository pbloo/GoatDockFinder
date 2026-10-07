namespace GoatFinder.Core;

public enum FileSortKey { Name, Modified, Created, Size, Kind }

public static class FileSorter
{
    // Pastas sempre vêm primeiro, como no Finder; o desempate final é o nome.
    public static List<FileEntry> Sort(IEnumerable<FileEntry> items, FileSortKey key, bool ascending)
    {
        var names = StringComparer.CurrentCultureIgnoreCase;
        var ordered = items.OrderBy(e => e.IsDirectory ? 0 : 1);

        ordered = key switch
        {
            FileSortKey.Modified => ascending ? ordered.ThenBy(e => e.Modified) : ordered.ThenByDescending(e => e.Modified),
            FileSortKey.Created => ascending ? ordered.ThenBy(e => e.Created) : ordered.ThenByDescending(e => e.Created),
            FileSortKey.Size => ascending ? ordered.ThenBy(e => e.Size) : ordered.ThenByDescending(e => e.Size),
            FileSortKey.Kind => ascending ? ordered.ThenBy(e => e.Kind, names) : ordered.ThenByDescending(e => e.Kind, names),
            _ => ascending ? ordered.ThenBy(e => e.Name, names) : ordered.ThenByDescending(e => e.Name, names),
        };

        return ordered.ThenBy(e => e.Name, names).ToList();
    }
}
