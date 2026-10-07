using System.IO;
using System.Reflection;
using Goat.Platform.Windows;
using Goat.Shared.Ipc;
using Goat.Shared.Journal;
using Goat.Shared.Product;
using GoatDock.Core.Models;
using GoatDock.Core.Services;
using GoatDock.Platform;
using GoatDockFinder.Installer.Services;
using GoatFinder.Core;

namespace GoatDockFinder.Tests;

/// <summary>Regras de dependência entre os componentes (ADR 0002): verificadas nas referências reais dos assemblies.</summary>
public sealed class ArchitectureTests
{
    private static IEnumerable<string> GoatReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Goat", StringComparison.Ordinal)).ToList();

    [Fact]
    public void GoatDock_NaoReferenciaOGoatFinder()
    {
        var refs = GoatReferences(typeof(GoatDock.MainWindow).Assembly);
        Assert.DoesNotContain(refs, n => n.StartsWith("GoatFinder", StringComparison.Ordinal));
    }

    [Fact]
    public void GoatFinder_NaoReferenciaOGoatDock()
    {
        var refs = GoatReferences(typeof(GoatFinder.Services.FinderShell).Assembly);
        Assert.DoesNotContain(refs, n => n.StartsWith("GoatDock", StringComparison.Ordinal));
    }

    [Fact]
    public void GoatShared_NaoReferenciaNenhumOutroProjeto()
    {
        Assert.Empty(GoatReferences(typeof(ProductInfo).Assembly));
    }

    [Fact]
    public void GoatUi_NaoReferenciaNenhumOutroProjeto()
    {
        Assert.Empty(GoatReferences(typeof(Goat.Ui.ObservableObject).Assembly));
    }

    [Fact]
    public void GoatPlatform_SoDependeDoShared()
    {
        Assert.Equal(["Goat.Shared"], GoatReferences(typeof(MonitorHelper).Assembly).ToArray());
    }

    [Fact]
    public void GoatFinderCore_NaoReferenciaNenhumOutroProjeto()
    {
        Assert.Empty(GoatReferences(typeof(FinderSettings).Assembly));
    }

    [Fact]
    public void GoatDockCore_SoDependeDoShared()
    {
        Assert.Equal(["Goat.Shared"], GoatReferences(typeof(Preferencias).Assembly).ToArray());
    }
}

public sealed class IpcTests
{
    private static async Task<T> Esperar<T>(Task<T> tarefa)
    {
        var concluida = await Task.WhenAny(tarefa, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(tarefa, concluida);
        return await tarefa;
    }

    [Fact]
    public async Task Framing_IdaEVolta_PreservaAMensagem()
    {
        using var stream = new MemoryStream();
        await IpcFraming.WriteAsync(stream, new DockBounds([new DockArea(10, 20, 300, 80)]), CancellationToken.None);
        stream.Position = 0;

        var lida = Assert.IsType<DockBounds>(await IpcFraming.ReadAsync(stream, CancellationToken.None));
        Assert.Equal(new DockArea(10, 20, 300, 80), Assert.Single(lida.Areas));
    }

    [Fact]
    public async Task Framing_TipoDesconhecido_EIgnoradoSemQuebrarAConexao()
    {
        using var stream = new MemoryStream();
        var json = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"mensagem-do-futuro\",\"x\":1}");
        stream.Write(BitConverter.GetBytes(json.Length));
        stream.Write(json);
        stream.Position = 0;

        Assert.IsType<UnknownMessage>(await IpcFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Framing_QuadroGrandeDemais_EhRejeitado()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(IpcProtocol.MaxFrameBytes + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Peers_DockEnviaOpenFolder_FinderRecebeEAmbosSeReconhecem()
    {
        var escopo = "teste-" + Guid.NewGuid().ToString("N");
        using var dock = new IpcPeer(ComponentId.Dock, escopo);
        using var finder = new IpcPeer(ComponentId.Finder, escopo);

        var finderViuDock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dockViuFinder = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recebida = new TaskCompletionSource<OpenFolder>(TaskCreationOptions.RunContinuationsAsynchronously);
        finder.PeerPresenceChanged += presente => { if (presente) finderViuDock.TrySetResult(true); };
        dock.PeerPresenceChanged += presente => { if (presente) dockViuFinder.TrySetResult(true); };
        finder.MessageReceived += mensagem => { if (mensagem is OpenFolder pasta) recebida.TrySetResult(pasta); };

        dock.Start();
        finder.Start();
        dock.Send(new OpenFolder(@"C:\Users"));

        Assert.True(await Esperar(finderViuDock.Task));
        Assert.True(await Esperar(dockViuFinder.Task));
        Assert.Equal(@"C:\Users", (await Esperar(recebida.Task)).Path);
    }

    [Fact]
    public void Peer_SemOOutroLado_NaoBloqueiaNemLanca()
    {
        using var sozinho = new IpcPeer(ComponentId.Dock, "teste-" + Guid.NewGuid().ToString("N"));
        sozinho.Start();
        sozinho.Send(new DockBounds([]));
        Assert.False(sozinho.IsPeerPresent);
    }
}

public sealed class JournalAndTweaksTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "goat-journal-" + Guid.NewGuid().ToString("N"));

    public JournalAndTweaksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            foreach (var arquivo in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(arquivo, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
    }

    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<(string Key, string Name), string> Values { get; } = [];
        public string? Read(string key, string name) => Values.TryGetValue((key, name), out var v) ? v : null;
        public void WriteDword(string key, string name, int value) => Values[(key, name)] = value.ToString();
        public void Delete(string key, string name) => Values.Remove((key, name));
    }

    private sealed class FakeParameters : ISystemParameters
    {
        public bool MinimizeAnimation { get; set; } = true;
        public bool GetMinimizeAnimation() => MinimizeAnimation;
        public void SetMinimizeAnimation(bool enabled) => MinimizeAnimation = enabled;
    }

    private WindowsTweakService Service(ChangeJournal journal, FakeRegistry registry, FakeParameters? parameters = null) =>
        new(journal, "teste", registry, parameters ?? new FakeParameters(), _ => { }, () => { });

    [Fact]
    public void Aplicar_GravaODiarioEDesfazerRestauraOValorAnterior()
    {
        var registry = new FakeRegistry();
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        registry.Values[(key, "HideFileExt")] = "1";
        var journal = new ChangeJournal(_dir);
        var service = Service(journal, registry);

        service.Apply("explorer.show-extensions");
        Assert.Equal("0", registry.Values[(key, "HideFileExt")]);
        Assert.True(service.IsApplied("explorer.show-extensions"));

        // O diário persiste: uma nova instância enxerga a alteração (e permite desfazer após reiniciar).
        Assert.True(Service(new ChangeJournal(_dir), registry).IsApplied("explorer.show-extensions"));

        service.Undo("explorer.show-extensions");
        Assert.Equal("1", registry.Values[(key, "HideFileExt")]);
        Assert.False(service.IsApplied("explorer.show-extensions"));
    }

    [Fact]
    public void Desfazer_ValorQueNaoExistia_RemoveOValor()
    {
        var registry = new FakeRegistry();
        var service = Service(new ChangeJournal(_dir), registry);

        service.Apply("explorer.show-hidden");
        Assert.Single(registry.Values);
        service.Undo("explorer.show-hidden");
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void AplicarDuasVezes_MantemOValorOriginalDoUsuario()
    {
        var registry = new FakeRegistry();
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        registry.Values[(key, "LaunchTo")] = "2";
        var service = Service(new ChangeJournal(_dir), registry);

        service.Apply("explorer.open-this-pc");
        service.Apply("explorer.open-this-pc");
        service.Undo("explorer.open-this-pc");

        Assert.Equal("2", registry.Values[(key, "LaunchTo")]);
    }

    [Fact]
    public void DesfazerTudo_ReverteTodasAsOpcoesEAnimacaoNativa()
    {
        var registry = new FakeRegistry();
        var parametros = new FakeParameters { MinimizeAnimation = true };
        var service = Service(new ChangeJournal(_dir), registry, parametros);

        service.Apply("windows.dark-mode");
        service.Apply("windows.minimize-animation-off");
        Assert.False(parametros.MinimizeAnimation);

        service.UndoAll();
        Assert.Empty(registry.Values);
        Assert.True(parametros.MinimizeAnimation);
        Assert.Empty(new ChangeJournal(_dir).Entries);
    }

    [Fact]
    public void OpcaoDesconhecida_Lanca()
    {
        var service = Service(new ChangeJournal(_dir), new FakeRegistry());
        Assert.Throws<ArgumentException>(() => service.Apply("nao.existe"));
    }

    [Fact]
    public void IconeDePasta_CriaEDesfazDesktopIni()
    {
        var pasta = Path.Combine(_dir, "minha-pasta");
        Directory.CreateDirectory(pasta);
        var service = Service(new ChangeJournal(_dir), new FakeRegistry());

        service.SetFolderIcon(pasta, @"C:\Windows\System32\shell32.dll", 4);
        var ini = Path.Combine(pasta, "desktop.ini");
        Assert.Contains("IconResource=C:\\Windows\\System32\\shell32.dll,4", File.ReadAllText(ini));
        Assert.True(File.GetAttributes(pasta).HasFlag(FileAttributes.ReadOnly));

        service.ClearFolderIcon(pasta);
        Assert.False(File.Exists(ini));
        Assert.False(File.GetAttributes(pasta).HasFlag(FileAttributes.ReadOnly));
    }
}

public sealed class ProductAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "goat-prod-" + Guid.NewGuid().ToString("N"));

    public ProductAndSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Versao_VemDeUmaFonteUnica()
    {
        Assert.Equal("1.0.0", ProductInfo.Version);
        Assert.Equal(ProductInfo.Version, InstallService.CurrentVersion);
    }

    [Fact]
    public void Mutex_EhUmPorComponenteEUsuario()
    {
        Assert.StartsWith(@"Local\GoatDock.", ProductInfo.MutexName(ComponentId.Dock));
        Assert.StartsWith(@"Local\GoatFinder.", ProductInfo.MutexName(ComponentId.Finder));
        Assert.NotEqual(ProductInfo.MutexName(ComponentId.Dock), ProductInfo.MutexName(ComponentId.Finder));
    }

    [Fact]
    public void Manifesto_GravaELeOsComponentesInstalados()
    {
        var store = new ComponentManifestStore(_dir);
        var manifesto = new ComponentManifest { InstallDirectory = @"C:\GoatDockFinder" };
        manifesto.Components[ComponentId.Finder] = new InstalledComponent { Version = "1.0.0", RelativeDirectory = "GoatFinder" };
        store.Save(manifesto);

        var lido = store.Load();
        Assert.True(lido.IsInstalled(ComponentId.Finder));
        Assert.False(lido.IsInstalled(ComponentId.Dock));
        store.Delete();
        Assert.Empty(store.Load().Components);
    }

    [Fact]
    public void Instalador_CadaComponenteTemPastaEExeProprios()
    {
        Assert.Equal(@"C:\X\GoatDock\GoatDock.exe", InstallService.ExeDoComponente(@"C:\X", ComponentId.Dock));
        Assert.Equal(@"C:\X\GoatFinder\GoatFinder.exe", InstallService.ExeDoComponente(@"C:\X", ComponentId.Finder));
    }

    [Fact]
    public void Instalador_SemComponentes_FalhaComMensagem()
    {
        var servico = new InstallService();
        var opcoes = new InstallOptions(Path.Combine(_dir, "destino"), new HashSet<ComponentId>(), false, false, false);

        Assert.False(servico.ExecutarInstalacao(opcoes, _ => { }, out var erro));
        Assert.Contains("componente", erro, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_dir, "destino")));
    }

    [Fact]
    public void ConfiguracoesDaBarra_ValoresAbsurdosSaoNormalizados()
    {
        var barra = new MenuBarSettings
        {
            HeightDip = 5000, LengthPercent = -4, GapDip = double.NaN, Opacity = 7, FontSize = 1,
            BackgroundColor = "vermelho", ForegroundColor = "#abc", FontFamily = "  ",
        };
        barra.Normalize();

        Assert.Equal(64, barra.HeightDip);
        Assert.Equal(20, barra.LengthPercent);
        Assert.Equal(0, barra.GapDip);
        Assert.Equal(1, barra.Opacity);
        Assert.Equal(9, barra.FontSize);
        Assert.Equal("#121214", barra.BackgroundColor);
        Assert.Equal("#FFFFFF", barra.ForegroundColor);
        Assert.False(string.IsNullOrWhiteSpace(barra.FontFamily));
    }

    [Fact]
    public void ConfiguracoesDaBarra_FlutuanteQuandoCurtaOuComFolga()
    {
        Assert.False(new MenuBarSettings().IsFloating);
        Assert.True(new MenuBarSettings { LengthPercent = 60 }.IsFloating);
        Assert.True(new MenuBarSettings { GapDip = 6 }.IsFloating);

        Assert.True(new BarPlacement().UsesAppBar);
        Assert.False(new BarPlacement { LengthPercent = 60 }.UsesAppBar);
        Assert.False(new BarPlacement { ReserveSpace = false }.UsesAppBar);
    }

    [Fact]
    public void ConfiguracoesDoFinder_PersistemAAparenciaDaBarra()
    {
        var caminho = Path.Combine(_dir, "finder-settings.json");
        var store = new FinderSettingsStore(caminho);
        var configuracoes = new FinderSettings();
        configuracoes.Bar.Position = BarPosition.Bottom;
        configuracoes.Bar.HeightDip = 36;
        configuracoes.Bar.ShowClock = false;
        configuracoes.Bar.AllMonitors = true;
        store.Save(configuracoes);

        var lidas = store.Load().Bar;
        Assert.Equal(BarPosition.Bottom, lidas.Position);
        Assert.Equal(36, lidas.HeightDip);
        Assert.False(lidas.ShowClock);
        Assert.True(lidas.AllMonitors);
    }

    [Fact]
    public void ConfiguracoesDoFinder_ArquivoCorrompidoVoltaAoPadrao()
    {
        var caminho = Path.Combine(_dir, "finder-settings.json");
        File.WriteAllText(caminho, "{ isto nao e json");
        Assert.Equal(28, new FinderSettingsStore(caminho).Load().Bar.HeightDip);
    }
}

public sealed class FinderAwareLauncherTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "goat-launcher-" + Guid.NewGuid().ToString("N"));

    public FinderAwareLauncherTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, true);

    private sealed class FakeLauncher : ILauncherService
    {
        public List<string> Chamadas { get; } = [];
        public LaunchResult Executar(ItemFixado item) { Chamadas.Add(item.CaminhoOuUrl); return LaunchResult.Ok(); }
        public LaunchResult AbrirLocal(ItemFixado item) => LaunchResult.Ok();
        public LaunchResult ExecutarCaminho(string caminho, string? argumentos = null) => LaunchResult.Ok();
    }

    [Fact]
    public void Pasta_ComFinderPresente_AbreNoFinder()
    {
        var interno = new FakeLauncher();
        var enviados = new List<string>();
        var launcher = new FinderAwareLauncher(interno, () => true, enviados.Add);

        launcher.Executar(new ItemFixado { CaminhoOuUrl = _pasta, Tipo = TipoItem.Pasta });

        Assert.Equal([_pasta], enviados);
        Assert.Empty(interno.Chamadas);
    }

    [Fact]
    public void Pasta_SemFinder_UsaOLauncherNormal()
    {
        var interno = new FakeLauncher();
        var enviados = new List<string>();
        var launcher = new FinderAwareLauncher(interno, () => false, enviados.Add);

        launcher.Executar(new ItemFixado { CaminhoOuUrl = _pasta, Tipo = TipoItem.Pasta });

        Assert.Empty(enviados);
        Assert.Equal([_pasta], interno.Chamadas);
    }

    [Fact]
    public void Aplicativo_NuncaVaiParaOFinder()
    {
        var interno = new FakeLauncher();
        var enviados = new List<string>();
        var launcher = new FinderAwareLauncher(interno, () => true, enviados.Add);

        launcher.Executar(new ItemFixado { CaminhoOuUrl = "notepad.exe", Tipo = TipoItem.Aplicativo });

        Assert.Empty(enviados);
        Assert.Single(interno.Chamadas);
    }
}
