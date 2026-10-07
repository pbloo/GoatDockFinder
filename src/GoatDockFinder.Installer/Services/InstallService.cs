using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using GoatDock.Core.Persistence;
using Goat.Platform.Windows;
using Goat.Shared.Journal;
using Goat.Shared.Product;
using Microsoft.Win32;

namespace GoatDockFinder.Installer.Services;

/// <summary>Escolhas do usuário no instalador.</summary>
/// <param name="Componentes">O que fica instalado ao final. Componente instalado e desmarcado é removido (modo "Modificar").</param>
/// <param name="PersonalizacoesWindows">Ids de <see cref="WindowsTweakService.Catalog"/> a aplicar (opcional; desfeitas na desinstalação).</param>
public sealed record InstallOptions(
    string PastaDestino,
    IReadOnlySet<ComponentId> Componentes,
    bool AtalhoMenuIniciar,
    bool AtalhoDesktop,
    bool IniciarComWindows,
    IReadOnlyList<string>? PersonalizacoesWindows = null);

public class InstallService
{
    public const string AppName = "GoatDockFinder";
    public const string AppExeName = "GoatDock.exe";

    /// <summary>Versão única do produto (Directory.Build.props).</summary>
    public static string CurrentVersion => ProductInfo.Version;

    private const string RegUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GoatDockFinder";
    private const string RegRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly ComponentId[] TodosComponentes = [ComponentId.Dock, ComponentId.Finder];

    public string ObterDiretorioInstalacaoPadrao() => ProductInfo.DefaultInstallDirectory;

    public string ObterDiretorioDadosUsuario() => ProductInfo.DataDirectory;

    public static string PastaDoComponente(string raiz, ComponentId componente) => Path.Combine(raiz, ProductInfo.ExecutableName(componente));

    public static string ExeDoComponente(string raiz, ComponentId componente) =>
        Path.Combine(PastaDoComponente(raiz, componente), ProductInfo.ExecutableName(componente) + ".exe");

    /// <summary>Componentes cujo executável existe na pasta de instalação.</summary>
    public IReadOnlySet<ComponentId> ComponentesInstalados(string raiz) =>
        TodosComponentes.Where(c => File.Exists(ExeDoComponente(raiz, c))).ToHashSet();

    public bool DetectarInstalacaoExistente(out string versaoInstalada, out string pastaInstalada, out bool iniciaComWindows)
    {
        versaoInstalada = string.Empty;
        pastaInstalada = ObterDiretorioInstalacaoPadrao();
        iniciaComWindows = false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegUninstallKey);
            if (key != null)
            {
                var ver = key.GetValue("DisplayVersion") as string;
                var loc = key.GetValue("InstallLocation") as string;

                if (!string.IsNullOrWhiteSpace(ver)) versaoInstalada = ver;
                if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc)) pastaInstalada = loc;
            }

            bool existeArquivo = ComponentesInstalados(pastaInstalada).Count > 0;
            if (existeArquivo && string.IsNullOrEmpty(versaoInstalada))
            {
                var raiz = pastaInstalada;
                var exe = TodosComponentes.Select(c => ExeDoComponente(raiz, c)).First(File.Exists);
                try
                {
                    var fileVer = FileVersionInfo.GetVersionInfo(exe);
                    versaoInstalada = !string.IsNullOrWhiteSpace(fileVer.ProductVersion) ? fileVer.ProductVersion : "1.0.0";
                }
                catch
                {
                    versaoInstalada = "1.0.0";
                }
            }

            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            iniciaComWindows = TodosComponentes.Any(c => File.Exists(Path.Combine(startupFolder, ProductInfo.ExecutableName(c) + ".lnk")));
            if (!iniciaComWindows)
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RegRunKey);
                if (runKey != null)
                    iniciaComWindows = runKey.GetValue("GoatDockFinder") != null;
            }

            return existeArquivo || !string.IsNullOrEmpty(versaoInstalada);
        }
        catch
        {
            return false;
        }
    }

    public bool ExecutarInstalacao(
        string pastaDestino,
        bool criarAtalhoIniciar,
        bool criarAtalhoDesktop,
        bool iniciarComWindows,
        Action<string> notificarProgresso)
    {
        return ExecutarInstalacao(pastaDestino, criarAtalhoIniciar, criarAtalhoDesktop, iniciarComWindows, notificarProgresso, out _);
    }

    /// <summary>Instala GoatDock e GoatFinder (compatível com o fluxo anterior de um único instalador).</summary>
    public bool ExecutarInstalacao(
        string pastaDestino,
        bool criarAtalhoIniciar,
        bool criarAtalhoDesktop,
        bool iniciarComWindows,
        Action<string> notificarProgresso,
        out string? mensagemErro)
    {
        var opcoes = new InstallOptions(pastaDestino, TodosComponentes.ToHashSet(), criarAtalhoIniciar, criarAtalhoDesktop, iniciarComWindows);
        return ExecutarInstalacao(opcoes, notificarProgresso, out mensagemErro);
    }

    public bool ExecutarInstalacao(InstallOptions opcoes, Action<string> notificarProgresso, out string? mensagemErro)
    {
        mensagemErro = null;
        var pastaDestino = opcoes.PastaDestino;
        try
        {
            if (opcoes.Componentes.Count == 0)
            {
                mensagemErro = "Selecione ao menos um componente.";
                return false;
            }

            bool ehAtualizacao = DetectarInstalacaoExistente(out var versaoAntiga, out _, out _);
            notificarProgresso(ehAtualizacao
                ? $"Instalação anterior detectada ({versaoAntiga}). Preparando atualização para {CurrentVersion}..."
                : "Preparando diretório para nova instalação...");

            // 1. Atualizando com o modo de substituição ativo: restaurar a barra nativa com segurança antes de trocar arquivos
            if (ehAtualizacao)
            {
                try
                {
                    notificarProgresso("Garantindo restauração segura da barra de tarefas do Windows...");
                    var dirDados = ObterDiretorioDadosUsuario();
                    var arquivoConfig = Path.Combine(dirDados, "settings.json");
                    if (File.Exists(arquivoConfig))
                    {
                        var arquivoBackup = Path.Combine(dirDados, $"settings.json.pre-update-{DateTime.Now:yyyyMMddHHmmss}.bak");
                        File.Copy(arquivoConfig, arquivoBackup, overwrite: true);

                        var prefs = new JsonSettingsRepository().Carregar();
                        if (prefs.UsarComoBarraPrincipal)
                        {
                            new Win32TaskbarService().RestaurarBarraNativa(prefs.EstadoAnteriorBarraTarefas);
                        }
                    }
                }
                catch { }
            }

            // 2. Encerrar instâncias ativas de forma controlada
            notificarProgresso("Encerrando instâncias em execução do GoatDockFinder...");
            FecharProcessosDock(pastaDestino);

            // 3. Componentes desmarcados que já estavam instalados são removidos
            foreach (var componente in ComponentesInstalados(pastaDestino).Where(c => !opcoes.Componentes.Contains(c)))
            {
                notificarProgresso($"Removendo {ProductInfo.ExecutableName(componente)}...");
                RemoverComponente(pastaDestino, componente);
            }

            // 4. Extrair os pacotes dos componentes escolhidos (substituição limpa por componente)
            Directory.CreateDirectory(pastaDestino);
            foreach (var componente in opcoes.Componentes.OrderBy(c => c))
            {
                notificarProgresso(ehAtualizacao
                    ? $"Atualizando {ProductInfo.ExecutableName(componente)} para a versão {CurrentVersion}..."
                    : $"Copiando {ProductInfo.ExecutableName(componente)}...");
                ExtrairComponente(componente, PastaDoComponente(pastaDestino, componente));
            }

            // 5. Copiar este instalador como Uninstall.exe (também serve de "Modificar")
            notificarProgresso("Atualizando utilitário de desinstalação...");
            var exeAtual = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exeAtual) && File.Exists(exeAtual))
            {
                try { CopiarArquivoComRetry(exeAtual, Path.Combine(pastaDestino, "Uninstall.exe")); }
                catch { }
            }

            // 6. Atalhos e inicialização, por componente
            var menuIniciar = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            foreach (var componente in TodosComponentes)
            {
                var nome = ProductInfo.ExecutableName(componente);
                var exe = ExeDoComponente(pastaDestino, componente);
                var selecionado = opcoes.Componentes.Contains(componente);

                if (selecionado && opcoes.AtalhoMenuIniciar)
                    ShortcutService.CriarAtalho(Path.Combine(menuIniciar, nome + ".lnk"), exe, Path.GetDirectoryName(exe)!, DescricaoDoComponente(componente));
                else
                    ShortcutService.RemoverAtalho(Path.Combine(menuIniciar, nome + ".lnk"));

                if (selecionado && opcoes.AtalhoDesktop)
                    ShortcutService.CriarAtalho(Path.Combine(desktop, nome + ".lnk"), exe, Path.GetDirectoryName(exe)!, DescricaoDoComponente(componente));
                else
                    ShortcutService.RemoverAtalho(Path.Combine(desktop, nome + ".lnk"));

                ConfigurarInicializacao(componente, exe, selecionado && opcoes.IniciarComWindows);
            }

            // 7. Manifesto de componentes e registro do painel de controle
            notificarProgresso("Atualizando registro do sistema...");
            GravarManifesto(pastaDestino, opcoes.Componentes);
            RegistrarNoPainelControle(pastaDestino);

            // 8. Personalização do Windows escolhida (camada A, com diário para desfazer)
            if (opcoes.PersonalizacoesWindows is { Count: > 0 })
            {
                notificarProgresso("Aplicando personalizações do Windows escolhidas...");
                var tweaks = new WindowsTweakService(new ChangeJournal(), "Installer");
                foreach (var id in opcoes.PersonalizacoesWindows)
                {
                    try { tweaks.Apply(id); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception or ArgumentException)
                    {
                        notificarProgresso($"Não foi possível aplicar '{id}': {ex.Message}");
                    }
                }
            }

            // Instalar/atualizar preserva preferências e a barra nativa; o modo de barra principal exige ação explícita no GoatDock.
            notificarProgresso(ehAtualizacao ? "GoatDockFinder atualizado com sucesso!" : "Instalação concluída com sucesso!");
            return true;
        }
        catch (Exception ex)
        {
            mensagemErro = ex.Message;
            notificarProgresso($"Erro durante a instalação/atualização: {ex.Message}");
            return false;
        }
    }

    public bool ExecutarDesinstalacao(bool removerDadosPessoais, Action<string> notificarProgresso)
    {
        try
        {
            notificarProgresso("Encerrando instâncias ativas do GoatDockFinder...");
            FecharProcessosDock();

            // 1. Restaurar a barra de tarefas do Windows se estiver oculta
            notificarProgresso("Restaurando barra de tarefas do Windows...");
            try
            {
                var prefs = new JsonSettingsRepository().Carregar();
                new Win32TaskbarService().RestaurarBarraNativa(prefs.EstadoAnteriorBarraTarefas);
            }
            catch { }

            // 2. Desfazer as alterações do Windows registradas no diário
            notificarProgresso("Desfazendo personalizações do Windows...");
            try { new WindowsTweakService(new ChangeJournal(), "Installer").UndoAll(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
            {
                notificarProgresso($"Algumas personalizações do Windows não puderam ser desfeitas: {ex.Message}");
            }

            // 3. Atalhos e inicialização de cada componente
            notificarProgresso("Removendo atalhos do sistema...");
            var menuIniciar = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            foreach (var componente in TodosComponentes)
            {
                var arquivo = ProductInfo.ExecutableName(componente) + ".lnk";
                ShortcutService.RemoverAtalho(Path.Combine(menuIniciar, arquivo));
                ShortcutService.RemoverAtalho(Path.Combine(desktop, arquivo));
                ShortcutService.RemoverAtalho(Path.Combine(startup, arquivo));
            }
            ShortcutService.RemoverAtalho(Path.Combine(menuIniciar, $"{AppName}.lnk"));
            ShortcutService.RemoverAtalho(Path.Combine(desktop, $"{AppName}.lnk"));

            notificarProgresso("Removendo inicialização automática...");
            LimparChaveRunLegada();

            // 4. Registro no Windows
            notificarProgresso("Removendo registro do aplicativo no sistema...");
            DetectarInstalacaoExistente(out _, out var pastaApp, out _);
            try { Registry.CurrentUser.DeleteSubKeyTree(RegUninstallKey, throwOnMissingSubKey: false); }
            catch { }

            // 5. Dados pessoais (o manifesto sempre sai; as configurações só se o usuário pedir)
            new ComponentManifestStore().Delete();
            if (removerDadosPessoais)
            {
                notificarProgresso("Removendo preferências e configurações pessoais...");
                var dirDados = ObterDiretorioDadosUsuario();
                if (Directory.Exists(dirDados))
                {
                    try { Directory.Delete(dirDados, recursive: true); } catch { }
                }
            }

            // 6. Agendar exclusão da pasta do programa
            notificarProgresso("Limpando arquivos do programa...");
            if (Directory.Exists(pastaApp))
            {
                AgendarExclusaoPasta(pastaApp);
            }

            notificarProgresso("GoatDockFinder foi desinstalado com sucesso!");
            return true;
        }
        catch (Exception ex)
        {
            notificarProgresso($"Erro durante a desinstalação: {ex.Message}");
            return false;
        }
    }

    private static string DescricaoDoComponente(ComponentId componente) => componente == ComponentId.Dock
        ? "GoatDock — dock, launchpad e widgets"
        : "GoatFinder — barra de menu, arquivos e Stage Manager";

    private void RemoverComponente(string raiz, ComponentId componente)
    {
        var nome = ProductInfo.ExecutableName(componente);
        var arquivo = nome + ".lnk";
        ShortcutService.RemoverAtalho(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), arquivo));
        ShortcutService.RemoverAtalho(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), arquivo));
        ShortcutService.RemoverAtalho(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), arquivo));

        var pasta = PastaDoComponente(raiz, componente);
        if (Directory.Exists(pasta))
        {
            try { Directory.Delete(pasta, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void GravarManifesto(string raiz, IReadOnlySet<ComponentId> componentes)
    {
        var manifesto = new ComponentManifest { InstallDirectory = raiz };
        foreach (var componente in componentes)
            manifesto.Components[componente] = new InstalledComponent { Version = CurrentVersion, RelativeDirectory = ProductInfo.ExecutableName(componente) };
        new ComponentManifestStore().Save(manifesto);
    }

    private void ExtrairComponente(ComponentId componente, string destino)
    {
        Directory.CreateDirectory(destino);
        var nome = ProductInfo.ExecutableName(componente);
        var recurso = $"GoatDockFinder.Installer.Resources.{nome.ToLowerInvariant().Replace("goat", string.Empty)}.zip";

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso);
        if (stream != null)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var arquivoDestino = CaminhoPacoteSeguro.Resolver(destino, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(arquivoDestino);
                    continue;
                }

                var pastaPai = Path.GetDirectoryName(arquivoDestino);
                if (!string.IsNullOrEmpty(pastaPai)) Directory.CreateDirectory(pastaPai);
                ExtrairArquivoComRetry(entry, arquivoDestino);
            }
            return;
        }

        // Sem o pacote embutido (execução local de desenvolvimento): usa os arquivos já compilados do componente.
        var pastaBase = AppDomain.CurrentDomain.BaseDirectory;
        const string tfm = "net10.0-windows10.0.19041.0";
        var candidatos = new[]
        {
            Path.Combine(pastaBase, nome.ToLowerInvariant().Replace("goat", string.Empty)),
            Path.GetFullPath(Path.Combine(pastaBase, $@"..\..\..\..\{nome}\bin\Release\{tfm}")),
            Path.GetFullPath(Path.Combine(pastaBase, $@"..\..\..\..\{nome}\bin\Debug\{tfm}")),
            Path.GetFullPath(Path.Combine(pastaBase, $@"..\..\..\..\..\src\{nome}\bin\Release\{tfm}")),
            Path.GetFullPath(Path.Combine(pastaBase, $@"..\..\..\..\..\src\{nome}\bin\Debug\{tfm}")),
        };

        foreach (var pasta in candidatos)
        {
            if (Directory.Exists(pasta) && File.Exists(Path.Combine(pasta, nome + ".exe")))
            {
                CopiarDiretorioRecursivo(pasta, destino);
                return;
            }
        }

        throw new FileNotFoundException($"Arquivos do {nome} não foram encontrados para instalação.");
    }

    private static void ExtrairArquivoComRetry(ZipArchiveEntry entry, string destinoArquivo, int maxTentativas = 6)
    {
        for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
        {
            try
            {
                entry.ExtractToFile(destinoArquivo, overwrite: true);
                return;
            }
            catch (IOException)
            {
                if (tentativa < maxTentativas)
                {
                    Thread.Sleep(250 * tentativa);
                }
                else
                {
                    // Fallback para Windows: se o arquivo estiver bloqueado, renomeia para .old temporário
                    try
                    {
                        if (File.Exists(destinoArquivo))
                        {
                            var arquivoOld = destinoArquivo + ".old-" + Guid.NewGuid().ToString("N")[..6];
                            File.Move(destinoArquivo, arquivoOld);
                            entry.ExtractToFile(destinoArquivo, overwrite: true);
                            try { File.Delete(arquivoOld); } catch { }
                            return;
                        }
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
        }
    }

    private static void CopiarArquivoComRetry(string origem, string destino, int maxTentativas = 6)
    {
        for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
        {
            try
            {
                File.Copy(origem, destino, true);
                return;
            }
            catch (IOException)
            {
                if (tentativa < maxTentativas)
                {
                    Thread.Sleep(250 * tentativa);
                }
                else
                {
                    try
                    {
                        if (File.Exists(destino))
                        {
                            var arquivoOld = destino + ".old-" + Guid.NewGuid().ToString("N")[..6];
                            File.Move(destino, arquivoOld);
                            File.Copy(origem, destino, true);
                            try { File.Delete(arquivoOld); } catch { }
                            return;
                        }
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
        }
    }

    private static void CopiarDiretorioRecursivo(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var arquivo in Directory.GetFiles(origem))
        {
            var nomeArquivo = Path.GetFileName(arquivo);
            CopiarArquivoComRetry(arquivo, Path.Combine(destino, nomeArquivo));
        }

        foreach (var sub in Directory.GetDirectories(origem))
        {
            var nomeSub = Path.GetFileName(sub);
            CopiarDiretorioRecursivo(sub, Path.Combine(destino, nomeSub));
        }
    }

    public static void FecharProcessosDock(string? pastaDestino = null)
    {
        var currentPid = Environment.ProcessId;

        // 1. Procurar e encerrar por nomes conhecidos de processo
        var nomes = new[] { "GoatDock", "GoatFinder" };
        foreach (var nome in nomes)
        {
            try
            {
                var procs = Process.GetProcessesByName(nome);
                foreach (var p in procs)
                {
                    if (p.Id == currentPid) continue;
                    try
                    {
                        p.CloseMainWindow();
                        if (!p.WaitForExit(1500))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 2. Se informada a pasta de destino, verificar qualquer processo aberto nela
        if (!string.IsNullOrEmpty(pastaDestino) && Directory.Exists(pastaDestino))
        {
            try
            {
                var todosProcs = Process.GetProcesses();
                foreach (var p in todosProcs)
                {
                    if (p.Id == currentPid) continue;
                    try
                    {
                        var caminhoProc = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(caminhoProc) && caminhoProc.StartsWith(pastaDestino, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 3. Pausa de estabilização do kernel do Windows para desmapeamento de arquivos executáveis
        Thread.Sleep(300);
    }


    private static void ConfigurarInicializacao(ComponentId componente, string caminhoExe, bool habilitar)
    {
        try
        {
            var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ProductInfo.ExecutableName(componente) + ".lnk");
            if (habilitar && File.Exists(caminhoExe))
                ShortcutService.CriarAtalho(shortcutPath, caminhoExe, Path.GetDirectoryName(caminhoExe) ?? string.Empty, DescricaoDoComponente(componente));
            else
                ShortcutService.RemoverAtalho(shortcutPath);
            LimparChaveRunLegada();
        }
        catch { }
    }

    // Instalações antigas usavam a chave Run do registro.
    private static void LimparChaveRunLegada()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegRunKey, writable: true);
            key?.DeleteValue("GoatDockFinder", throwOnMissingValue: false);
        }
        catch { }
    }

    private void RegistrarNoPainelControle(string pastaDestino)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegUninstallKey);
            if (key == null) return;

            var icone = TodosComponentes.Select(c => ExeDoComponente(pastaDestino, c)).FirstOrDefault(File.Exists) ?? string.Empty;
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", CurrentVersion);
            key.SetValue("Publisher", AppName);
            key.SetValue("InstallLocation", pastaDestino);
            key.SetValue("UninstallString", $"\"{Path.Combine(pastaDestino, "Uninstall.exe")}\" --uninstall");
            key.SetValue("DisplayIcon", $"\"{icone}\",0");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            long tamanhoBytes = 0;
            if (Directory.Exists(pastaDestino))
            {
                foreach (var file in Directory.GetFiles(pastaDestino, "*.*", SearchOption.AllDirectories))
                    tamanhoBytes += new FileInfo(file).Length;
            }
            key.SetValue("EstimatedSize", (int)(tamanhoBytes / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void AgendarExclusaoPasta(string pasta)
    {
        try
        {
            var cmd = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{pasta}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }
}






