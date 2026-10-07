using System.IO.Compression;

namespace GoatFinder.Core;

public sealed class FileSystemService
{
    public IReadOnlyList<FileEntry> List(string directory, bool includeHidden)
    {
        var result = new List<FileEntry>();
        foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            try
            {
                var attrs = info.Attributes;
                bool hidden = (attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                if (hidden && !includeHidden) continue;

                bool isDir = (attrs & FileAttributes.Directory) != 0;
                long size = !isDir && info is FileInfo file ? file.Length : 0;
                result.Add(new FileEntry(info.Name, info.FullName, isDir, size, info.LastWriteTime, info.CreationTime, hidden));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    public Task<IReadOnlyList<FileEntry>> SearchAsync(string root, string query, bool includeHidden, int maxResults, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            var results = new List<FileEntry>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // Pular reparse points evita loops infinitos com junctions e links simbólicos.
                AttributesToSkip = FileAttributes.ReparsePoint | (includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System),
            };

            foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (info.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) is false) continue;

                try
                {
                    bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    long size = !isDir && info is FileInfo file ? file.Length : 0;
                    bool hidden = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                    results.Add(new FileEntry(info.Name, info.FullName, isDir, size, info.LastWriteTime, info.CreationTime, hidden));
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (results.Count >= maxResults) break;
            }
            return results;
        }, ct);
    }

    public string CreateFolder(string parent, string baseName = "Nova pasta")
    {
        var name = FileNaming.GetUniqueName(parent, baseName, true, null);
        var full = Path.Combine(parent, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public string Rename(string path, string newName)
    {
        if (!FileNaming.IsValidName(newName, out var error)) throw new ArgumentException(error, nameof(newName));

        var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new IOException("Caminho inválido.");
        var destination = Path.Combine(parent, newName);
        if (string.Equals(path, destination, StringComparison.Ordinal)) return path;

        bool onlyCaseChanged = string.Equals(path, destination, StringComparison.OrdinalIgnoreCase);
        if (!onlyCaseChanged && Exists(destination)) throw new IOException("Já existe um item com esse nome.");

        bool isDir = Directory.Exists(path);
        if (onlyCaseChanged)
        {
            // NTFS ignora maiúsculas/minúsculas; renomear só a caixa exige passar por um nome temporário.
            var temp = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            MoveItem(path, temp, isDir);
            MoveItem(temp, destination, isDir);
        }
        else
        {
            MoveItem(path, destination, isDir);
        }
        return destination;
    }

    public IReadOnlyList<string> Copy(IEnumerable<string> sources, string destinationDirectory)
    {
        var created = new List<string>();
        foreach (var source in sources)
        {
            bool isDir = Directory.Exists(source);
            GuardNotInside(source, destinationDirectory, isDir);
            var name = FileNaming.GetUniqueName(destinationDirectory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)), isDir, "cópia");
            var target = Path.Combine(destinationDirectory, name);

            if (isDir) CopyDirectory(source, target);
            else File.Copy(source, target);
            created.Add(target);
        }
        return created;
    }

    public IReadOnlyList<string> Move(IEnumerable<string> sources, string destinationDirectory)
    {
        var moved = new List<string>();
        foreach (var source in sources)
        {
            var parent = Path.GetDirectoryName(source.TrimEnd(Path.DirectorySeparatorChar));
            if (string.Equals(parent, destinationDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;

            bool isDir = Directory.Exists(source);
            GuardNotInside(source, destinationDirectory, isDir);
            var name = FileNaming.GetUniqueName(destinationDirectory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)), isDir, "cópia");
            var target = Path.Combine(destinationDirectory, name);

            try
            {
                MoveItem(source, target, isDir);
            }
            catch (IOException) when (!SameRoot(source, target))
            {
                // Entre volumes diferentes não existe "mover" atômico: copia e depois apaga.
                if (isDir) { CopyDirectory(source, target); Directory.Delete(source, true); }
                else { File.Copy(source, target); File.Delete(source); }
            }
            moved.Add(target);
        }
        return moved;
    }

    public string Compress(IReadOnlyList<string> sources, string destinationDirectory)
    {
        var baseName = sources.Count == 1 ? Path.GetFileName(sources[0].TrimEnd(Path.DirectorySeparatorChar)) : "Arquivo";
        var zipName = FileNaming.GetUniqueName(destinationDirectory, baseName + ".zip", false, null);
        var zipPath = Path.Combine(destinationDirectory, zipName);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var source in sources)
        {
            if (File.Exists(source))
            {
                zip.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
                continue;
            }

            var root = source.TrimEnd(Path.DirectorySeparatorChar);
            var folderName = Path.GetFileName(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                zip.CreateEntryFromFile(file, Path.Combine(folderName, relative), CompressionLevel.Optimal);
            }
        }
        return zipPath;
    }

    public void DeletePermanently(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool SameRoot(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static void MoveItem(string source, string target, bool isDirectory)
    {
        if (isDirectory) Directory.Move(source, target);
        else File.Move(source, target);
    }

    private static void GuardNotInside(string source, string destinationDirectory, bool isDirectory)
    {
        if (!isDirectory) return;
        var src = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dst = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (dst.StartsWith(src, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Não é possível copiar ou mover uma pasta para dentro dela mesma.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
