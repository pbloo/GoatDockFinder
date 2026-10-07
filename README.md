# GoatDockFinder

Experiência estilo macOS para Windows 10 e 11, em dois componentes independentes:

- **GoatDock**: dock, launchpad, widgets (incluindo Spotify) e ambientes (Trabalho, Estudos, Pessoal), com efeito gênio e suporte a vários monitores.
- **GoatFinder**: barra de menu totalmente configurável, gerenciador de arquivos e Stage Manager.

Funcionam sozinhos, podem ser instalados separadamente e se integram quando os dois estão em execução (por exemplo, pastas fixadas na dock abrem no GoatFinder). C#, .NET 10 e WPF, interface em português do Brasil, sem conta nem telemetria.

> Versão 1.0.0. Os executáveis ainda não são assinados; o Windows pode pedir confirmação ao executar o instalador.

## Instalar

Requisitos: Windows 10/11 x64 e o .NET Desktop Runtime 10 x64.

1. Gere ou baixe `GoatDockFinder-Setup.exe` (ver "Gerar o instalador").
2. Escolha os componentes (GoatDock, GoatFinder ou os dois), os atalhos e, se quiser, as personalizações do Windows. Não exige administrador.
3. Para adicionar ou remover um componente depois, execute o instalador de novo (modo "Modificar").
4. Desinstalar restaura a barra do Windows e desfaz as personalizações feitas pelo produto.

Se algo prender a barra de tarefas, `tools/restaurar-barra-windows.bat` restaura a barra nativa.

## Desenvolvimento

Requisitos: Windows 10/11 x64 e o SDK .NET indicado em `global.json`.

```powershell
dotnet build GoatDockFinder.slnx -c Release
.\tools\dev.ps1 -Dock      # só o GoatDock
.\tools\dev.ps1 -Finder    # só o GoatFinder
.\tools\dev.ps1 -Both      # os dois juntos
.\tools\dev.ps1 -Stop      # encerra
```

No VS Code, use as configurações de depuração `GoatDock`, `GoatFinder` ou o conjunto `GoatDock + GoatFinder`.

Testes (sem os que alteram o sistema):

```powershell
dotnet test tests/GoatDockFinder.Tests/GoatDockFinder.Tests.csproj -c Release --filter "Category!=SystemIntegration"
```

### Gerar o instalador

```powershell
.\build_release.ps1
```

Publica GoatDock e GoatFinder, empacota `dock.zip` e `finder.zip` dentro do instalador e grava `release/GoatDockFinder-Setup.exe`. Para assinar (veja [docs/ASSINATURA-DIGITAL.md](docs/ASSINATURA-DIGITAL.md)), use `-Assinar -CertificadoThumbprint <hash> -SignToolPath <signtool.exe> -TimestampUrl <url>` (certificado RSA de assinatura de código).

## Arquitetura

```
GoatDockFinder
├── GoatDock    (+ GoatDock.Core)     dock, launchpad, widgets, gênio
├── GoatFinder  (+ GoatFinder.Core)   barra de menu, arquivos, Stage Manager
├── Goat.Shared                       contratos, IPC, diário de alterações do Windows
├── Goat.Platform                     Win32/DWM usado pelos dois
├── Goat.Ui                           MVVM comum
└── GoatDockFinder.Installer          instalação por componentes
```

`GoatDock` e `GoatFinder` nunca se referenciam; conversam por um pipe nomeado. As regras são verificadas por testes. Detalhes em [docs/adr/](docs/adr/).

## Documentação

- [docs/TESTAR-LOCALMENTE.md](docs/TESTAR-LOCALMENTE.md): como testar sem instalar.
- [docs/PUBLICAR.md](docs/PUBLICAR.md): GitHub, release e site na Vercel.
- [docs/ASSINATURA-DIGITAL.md](docs/ASSINATURA-DIGITAL.md): como assinar o instalador.
- [AGENTS.md](AGENTS.md): regras de trabalho e estrutura.
- [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) e [SECURITY.md](SECURITY.md).
- [vercel/](vercel/): site de apresentação.
- [CHANGELOG.md](CHANGELOG.md): o que mudou.
- [docs/adr/](docs/adr/): decisões de arquitetura.
- [docs/product/roadmap.md](docs/product/roadmap.md): entregue, próximos passos, custos e riscos.
- [docs/product/feature-inventory.md](docs/product/feature-inventory.md): o que já existe.
- [docs/product/known-issues.md](docs/product/known-issues.md): problemas e o que validar manualmente.
- [docs/research/referencias.md](docs/research/referencias.md): pesquisa de referências.

## Licença

Ver [LICENSE](LICENSE) e [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
