namespace GoatFinder.Core;

public sealed record PathSegment(string Name, string FullPath);

public static class PathSegments
{
    public static IReadOnlyList<PathSegment> Build(string path)
    {
        var list = new List<PathSegment>();
        if (string.IsNullOrWhiteSpace(path)) return list;

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (root.Length > 0) list.Add(new PathSegment(root, root));

        var parts = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            list.Add(new PathSegment(part, current));
        }
        return list;
    }
}
