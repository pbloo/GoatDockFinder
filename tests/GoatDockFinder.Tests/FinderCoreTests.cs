using System.IO;
using GoatFinder.Core;

namespace GoatDockFinder.Tests;

public sealed class FinderCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "finder-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemService _files = new();

    public FinderCoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static FileEntry Entry(string name, bool dir = false, long size = 0, int day = 1) =>
        new(name, @"C:\x\" + name, dir, size, new DateTime(2026, 1, day), new DateTime(2026, 1, day), false);

    [Fact]
    public void NavigationHistory_VoltarEAvancar_SeguemAOrdemDeVisita()
    {
        var history = new NavigationHistory();
        history.Visit("A");
        history.Visit("B");
        history.Visit("C");

        Assert.Equal("B", history.Back());
        Assert.Equal("A", history.Back());
        Assert.False(history.CanGoBack);
        Assert.Equal("B", history.Forward());
    }

    [Fact]
    public void NavigationHistory_NovaVisita_DescartaOFuturo()
    {
        var history = new NavigationHistory();
        history.Visit("A");
        history.Visit("B");
        history.Back();
        history.Visit("C");

        Assert.False(history.CanGoForward);
        Assert.Equal("A", history.Back());
    }

    [Fact]
    public void NavigationHistory_VisitarAPastaAtual_NaoDuplica()
    {
        var history = new NavigationHistory();
        history.Visit("A");
        history.Visit("a");

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void FileSorter_PastasSempreVemPrimeiro_MesmoDescendente()
    {
        var items = new[] { Entry("b.txt"), Entry("Pasta", dir: true), Entry("a.txt") };

        var asc = FileSorter.Sort(items, FileSortKey.Name, true);
        var desc = FileSorter.Sort(items, FileSortKey.Name, false);

        Assert.Equal(["Pasta", "a.txt", "b.txt"], asc.Select(e => e.Name));
        Assert.Equal(["Pasta", "b.txt", "a.txt"], desc.Select(e => e.Name));
    }

    [Fact]
    public void FileSorter_PorTamanho_OrdenaArquivos()
    {
        var items = new[] { Entry("g.bin", size: 900), Entry("p.bin", size: 10) };

        var sorted = FileSorter.Sort(items, FileSortKey.Size, true);

        Assert.Equal(["p.bin", "g.bin"], sorted.Select(e => e.Name));
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void ByteSizeFormatter_FormataEmPortugues(long bytes, string esperado) =>
        Assert.Equal(esperado, ByteSizeFormatter.Format(bytes));

    [Theory]
    [InlineData("relatorio.txt", true)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("fim.", false)]
    public void FileNaming_ValidaNomes(string nome, bool valido) =>
        Assert.Equal(valido, FileNaming.IsValidName(nome, out _));

    [Fact]
    public void FileNaming_NomeUnico_AcrescentaCopiaAntesDaExtensao()
    {
        var existentes = new HashSet<string> { @"D:\a\nota.txt", @"D:\a\nota cópia.txt" };

        var nome = FileNaming.GetUniqueName(@"D:\a", "nota.txt", false, "cópia", existentes.Contains);

        Assert.Equal("nota cópia 2.txt", nome);
    }

    [Fact]
    public void FileNaming_NovaPasta_UsaNumeroSemPalavraCopia()
    {
        var existentes = new HashSet<string> { @"D:\a\Nova pasta" };

        Assert.Equal("Nova pasta 2", FileNaming.GetUniqueName(@"D:\a", "Nova pasta", true, null, existentes.Contains));
    }

    [Fact]
    public void PathSegments_DivideCaminhoEmPartesNavegaveis()
    {
        var segments = PathSegments.Build(@"C:\Users\pablo\Docs");

        Assert.Equal(["C:\\", "Users", "pablo", "Docs"], segments.Select(s => s.Name));
        Assert.Equal(@"C:\Users\pablo", segments[2].FullPath);
    }

    [Fact]
    public void FileSystemService_ListaOcultosSomenteQuandoPedido()
    {
        File.WriteAllText(Path.Combine(_root, "visivel.txt"), "x");
        var hidden = Path.Combine(_root, "oculto.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Single(_files.List(_root, includeHidden: false));
        Assert.Equal(2, _files.List(_root, includeHidden: true).Count);
    }

    [Fact]
    public void FileSystemService_CriarPastaEmSequencia_GeraNomesUnicos()
    {
        var first = _files.CreateFolder(_root);
        var second = _files.CreateFolder(_root);

        Assert.Equal("Nova pasta", Path.GetFileName(first));
        Assert.Equal("Nova pasta 2", Path.GetFileName(second));
    }

    [Fact]
    public void FileSystemService_Renomear_RejeitaNomeInvalidoEExistente()
    {
        var a = Path.Combine(_root, "a.txt");
        File.WriteAllText(a, "x");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "x");

        Assert.Throws<ArgumentException>(() => _files.Rename(a, "a/b"));
        Assert.Throws<IOException>(() => _files.Rename(a, "b.txt"));
        Assert.Equal(Path.Combine(_root, "c.txt"), _files.Rename(a, "c.txt"));
    }

    [Fact]
    public void FileSystemService_Renomear_SoMudandoACaixa_Funciona()
    {
        var path = Path.Combine(_root, "doc.txt");
        File.WriteAllText(path, "x");

        var renamed = _files.Rename(path, "Doc.txt");

        Assert.Contains(_files.List(_root, false), e => e.Name == "Doc.txt");
        Assert.Equal(Path.Combine(_root, "Doc.txt"), renamed);
    }

    [Fact]
    public void FileSystemService_CopiarParaAMesmaPasta_CriaCopiaComNomeUnico()
    {
        var path = Path.Combine(_root, "nota.txt");
        File.WriteAllText(path, "conteudo");

        var created = _files.Copy([path], _root);

        Assert.Equal("nota cópia.txt", Path.GetFileName(created[0]));
        Assert.Equal("conteudo", File.ReadAllText(created[0]));
    }

    [Fact]
    public void FileSystemService_MoverPastaParaDentroDelaMesma_Falha()
    {
        var folder = Path.Combine(_root, "pai");
        var inner = Path.Combine(folder, "filho");
        Directory.CreateDirectory(inner);

        Assert.Throws<IOException>(() => _files.Move([folder], inner));
    }

    [Fact]
    public void FileSystemService_CopiarPasta_CopiaConteudoRecursivamente()
    {
        var folder = Path.Combine(_root, "origem");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "a.txt"), "ok");
        var destination = Path.Combine(_root, "destino");
        Directory.CreateDirectory(destination);

        _files.Copy([folder], destination);

        Assert.True(File.Exists(Path.Combine(destination, "origem", "sub", "a.txt")));
    }

    [Fact]
    public void FileSystemService_Comprimir_GeraZipComOsArquivos()
    {
        var path = Path.Combine(_root, "dados.txt");
        File.WriteAllText(path, "abc");

        var zip = _files.Compress([path], _root);

        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.Name == "dados.txt");
    }

    [Fact]
    public async Task FileSystemService_Busca_EncontraEmSubpastasIgnorandoCaixa()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        File.WriteAllText(Path.Combine(_root, "a", "b", "Relatorio-Final.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "outro.txt"), "x");

        var results = await _files.SearchAsync(_root, "relatorio", false, 50, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Relatorio-Final.txt", results[0].Name);
    }

    [Fact]
    public async Task FileSystemService_Busca_RespeitaCancelamento()
    {
        File.WriteAllText(Path.Combine(_root, "x.txt"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _files.SearchAsync(_root, "x", false, 10, cts.Token));
    }

    [Fact]
    public void FinderSettingsStore_SalvaECarrega_EAceitaArquivoCorrompido()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new FinderSettingsStore(path);

        store.Save(new FinderSettings { IconView = true, SortKey = FileSortKey.Size, SortAscending = false });
        var loaded = store.Load();
        Assert.True(loaded.IconView);
        Assert.Equal(FileSortKey.Size, loaded.SortKey);
        Assert.False(loaded.SortAscending);

        File.WriteAllText(path, "{ nao e json");
        Assert.False(store.Load().IconView);
    }

    [Fact]
    public void FileTypeCatalog_ClassificaPreviewEDescricao()
    {
        Assert.Equal(PreviewKind.Image, FileTypeCatalog.GetPreviewKind("png"));
        Assert.Equal(PreviewKind.Text, FileTypeCatalog.GetPreviewKind("cs"));
        Assert.Equal(PreviewKind.None, FileTypeCatalog.GetPreviewKind("exe"));
        Assert.Equal("Pasta", Entry("x", dir: true).Kind);
        Assert.Equal("Documento PDF", Entry("a.pdf").Kind);
    }
}
