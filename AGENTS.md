# AGENTS.md — instruções para agentes de IA

Fonte única de regras para Claude Code, GitHub Copilot e qualquer outro agente.
`CLAUDE.md` e `.github/copilot-instructions.md` apontam para este arquivo.

## Projeto

**GoatDockFinder**: experiência estilo macOS para Windows 10/11 em um único produto, com dois componentes independentes:

- **GoatDock**: dock, launchpad, widgets (relógio, clima, mídia, monitor, bateria etc.), ambientes.
- **GoatFinder**: barra de menu superior, gerenciador de arquivos, Stage Manager.

Os dois funcionam sozinhos, podem ser instalados separadamente e se integram quando ambos existem.
Objetivo de longo prazo: reunir em um só produto o que hoje exige vários aplicativos de personalização do Windows, mexendo fundo no sistema quando fizer sentido (inclusive com código nativo C++), sempre com rollback.

## Leia antes de mudar algo estrutural

- `docs/adr/`: decisões de arquitetura numeradas. Nova decisão (stack, biblioteca, estrutura, padrão) vira ADR.
- `docs/product/roadmap.md`: fases, custos e riscos.
- `docs/product/feature-inventory.md`: tudo o que já existe (nada pode se perder).
- `docs/product/known-issues.md`: problemas conhecidos herdados.
- `docs/research/referencias.md`: pesquisa de produtos de referência e do que é viável no Windows.

## Estrutura

| Caminho | Responsabilidade |
|---|---|
| `src/Goat.Shared` | Contratos sem WPF: produto/versão/mutex, IPC (pipe nomeado), diário de alterações do Windows, manifesto de componentes, `JanelaInfo`/`TipoItem` |
| `src/Goat.Platform` | Win32/DWM/COM usado pelos dois componentes: AppBar, monitores, backdrop, janelas, ícones, bateria, barra do Windows, personalização do Windows |
| `src/Goat.Ui` | MVVM comum (`ObservableObject`, `RelayCommand`) |
| `src/GoatDock.Core` | Modelos, validação, widgets e persistência do Dock |
| `src/GoatDock` | Dock: WPF, ViewModels, `Platform/` (serviços só do Dock), `Genie/` (efeito gênio) |
| `src/GoatFinder.Core` | Gerenciador de arquivos e configurações do Finder (`MenuBarSettings`) |
| `src/GoatFinder` | Finder: barra de menu, arquivos, Stage Manager, janela de configurações, `Platform/` (só do Finder) |
| `src/GoatDockFinder.Installer` | Instalação por componentes, atualização, desinstalação |
| `tests/GoatDockFinder.Tests` | Testes automatizados, incluindo os de arquitetura |
| `tools/` | Scripts de desenvolvimento, build e restauração |

Regras (verificadas em `ReleaseArchitectureTests`): `GoatDock` e `GoatFinder` nunca se referenciam e só se falam por IPC; `Goat.Shared` e `Goat.Ui` não referenciam ninguém; `Goat.Platform`, `GoatDock.Core` e `GoatFinder.Core` só dependem de `Goat.Shared` (o Finder Core, de nada). O instalador pode usar `GoatDock.Core`, `Goat.Platform` e `Goat.Shared`.

## Comandos

```powershell
dotnet build GoatDockFinder.slnx -c Release
dotnet test tests/GoatDockFinder.Tests/GoatDockFinder.Tests.csproj -c Release --filter "Category!=SystemIntegration"
.\tools\dev.ps1 -Dock      # só o GoatDock
.\tools\dev.ps1 -Finder    # só o GoatFinder
.\tools\dev.ps1 -Both      # os dois
.\tools\dev.ps1 -Stop      # encerra os dois
```

Se o PowerShell bloquear scripts: execute os comandos `dotnet build`/`Start-Process` manualmente ou use as configurações de depuração do VS Code (`.vscode/launch.json`).

## Convenções

- Código e identificadores novos em inglês. Texto de interface em português do Brasil. Nomes em português que já existem no GoatDock permanecem até uma refatoração própria.
- Commits no padrão Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`).
- Nullable habilitado. Sem warnings novos.
- Lógica de negócio fora de WPF e de Win32. Chamadas Win32/COM em `Goat.Platform`, atrás de interfaces testáveis.
- Nada de trabalho pesado na thread de UI.
- Considere DPI (100% a 200%) e múltiplos monitores em qualquer código de posicionamento.
- Projetos WPF removem `System.IO` dos usings implícitos: declare `using System.IO;`.
- Namespaces que mudaram de projeto entram como `<Using>` no `.csproj` de quem os usa (por exemplo `Goat.Shared.Windows`, `GoatDock.Platform`).

## Segurança e Windows

- Código que mexe em barra de tarefas, autostart, registro, `desktop.ini`, animações ou tema precisa de **backup do valor anterior, desfazer e restauração** (diário de alterações), e de confirmação do usuário.
- Testes que mexem no sistema ficam em `Category=SystemIntegration` e não rodam por padrão.
- Não execute `build_release.ps1` nem scripts de `tools/` que alterem o sistema sem pedir confirmação.
- Nunca commite segredos, certificados ou URLs com token. Sem telemetria nem rede sem ADR.
- Não copie código, ativos ou ícones de terceiros com licença incompatível (GPL/LGPL, uso pessoal, comercial). Engenharia reversa de software de terceiros é proibida pelas licenças de vários deles (ex.: Winaero Tweaker).
- Não remover o aviso de copyright do arquivo `LICENSE` sem autorização escrita do autor do código original (condição da licença MIT).

## Como trabalhar (acordos com o dono do projeto)

1. O dono pediu: implemente tudo o que for solicitado, em etapas, sem pedir confirmação prévia (exceto exclusões e ações destrutivas). Atue como mentor: explique o que, por que e como. Se ele disser "só o código pronto", entregue só o código.
2. Em toda alteração, informar: o que mudou, por que, como foi feito, quais arquivos, como as partes se relacionam e como testar. Explicar a arquitetura, não só o código final.
3. Mudança estrutural grande: analisar a arquitetura atual e explicar a proposta antes. Havendo mais de uma solução, apresentar as opções e recomendar uma.
4. Não deixar código morto nem estruturas antigas só por compatibilidade.
5. Rode build e testes antes de dizer que terminou; relate o resultado real.
6. Não invente APIs do Windows; cite a documentação a conferir. Não assuma que algo é possível: pesquise fontes atuais e verificáveis.
7. Alterou o Windows? Considerar backup, rollback, restauração, segurança, compatibilidade, impacto de atualizações do Windows e privilégio de administrador.
8. Toda ideia solta do dono entra no backlog (`docs/product/roadmap.md`) na mesma resposta.
9. O contexto vive no repositório (este arquivo, `docs/`, ADRs), não na conversa.
