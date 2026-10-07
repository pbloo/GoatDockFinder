namespace GoatFinder.Core;

public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime Modified,
    DateTime Created,
    bool IsHidden)
{
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();

    public string Kind => FileTypeCatalog.Describe(IsDirectory, Extension);
}
