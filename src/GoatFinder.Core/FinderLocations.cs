namespace GoatFinder.Core;

public sealed record FinderLocation(string Name, string Path, string Glyph);

public static class FinderLocations
{
    public static IReadOnlyList<FinderLocation> Favorites()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            new FinderLocation("Área de Trabalho", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE7F4"),
            new FinderLocation("Downloads", Path.Combine(home, "Downloads"), "\uE896"),
            new FinderLocation("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5"),
            new FinderLocation("Imagens", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uE91B"),
            new FinderLocation("Músicas", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "\uE8D6"),
            new FinderLocation("Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "\uE714"),
            new FinderLocation(Environment.UserName, home, "\uE80F"),
        };
        return candidates.Where(l => !string.IsNullOrEmpty(l.Path) && Directory.Exists(l.Path)).ToList();
    }

    public static IReadOnlyList<FinderLocation> Drives()
    {
        var list = new List<FinderLocation>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? (drive.DriveType == DriveType.Removable ? "Disco removível" : "Disco local")
                    : drive.VolumeLabel;
                list.Add(new FinderLocation($"{label} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})", drive.RootDirectory.FullName, "\uEDA2"));
            }
            catch (IOException) { }
        }
        return list;
    }
}
