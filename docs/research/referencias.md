# Pesquisa de referências (2026-10-06)

Fontes consultadas nesta sessão. Tudo marcado como "não verificado" precisa ser confirmado antes de virar implementação.

## 1. MyDockFinder

**Fontes:** página Steam (app 1787090), repositório público `mydockfinder/mydockfinder-for-Win10-Win11` (só traduções e issues, sem código), site oficial (renderizado por JavaScript, quase sem texto útil).

**O que é:** um único desenvolvedor, lançado em 25/11/2021, versão 1.18 em 25/09/2026, vendido na Steam. Interface em WinUI com GPU. Pelo próprio texto da Steam: "Bezier curve rounded corners", blur com intensidade ajustável, 4K e múltiplos monitores, tema claro/escuro do sistema, backup de configuração na nuvem da Steam. Requisitos: Windows 10 1809+, .NET Framework 4.8, VC++ 2019.

**MyDock (dock):** ícones que iniciam apps; ícone editável manualmente; animação de minimizar com três efeitos; painel "Launchpad"; avisos de mensagens de programas; ver arquivos de uma pasta aberta; clima; pré-visualização de janelas; barra de progresso de download e mídia no ícone; máscara unificada nos ícones; vários modos de ocultar, configuráveis por aplicativo.

**MyFinder (barra superior):** desligar/reiniciar/sair; monitor de CPU, memória, disco, rede e temperatura; Wi-Fi; Bluetooth; bandeja do sistema; brilho do monitor (inclusive externo); volume, troca de saída e volume por programa; controle de mídia; aviso de volume; "exibição imersiva" integrada ao fundo da janela. O botão direito de cada botão abre a função de sistema correspondente.

**O que aprendemos das issues abertas (620 abertas):**
- A bandeja conflita com menus de apps de terceiros e chegou a deixar janelas com DWM `cloaked=2`.
- Animação de minimizar 2 a 3 vezes mais lenta, e a "animação de abertura" abre janela duplicada no Firefox.
- Ícones que somem após reiniciar com dois monitores; "Smart Hide" não detecta a área de trabalho no monitor secundário.
- Pedidos de usuários: metadados de mídia no Finder, botões do Finder com clique esquerdo/direito configurável, ícones do Finder dentro da Dock, ícones de apps em todas as docks.
- "WinUI failed" depois de suspender/acordar o PC.

**Limite da pesquisa:** não há documentação pública das configurações (alturas, transparência, cores). Para isso precisamos de capturas de tela dos painéis de configuração. Não recomendo desmontar o programa (viola o contrato de uso e não é necessário).

## 2. Ferramentas de referência

| Ferramenta | O que faz | Mecanismo | Admin | Risco | Veredito |
|---|---|---|---|---|---|
| Windhawk | Loja de "mods" (código C++ compilado na hora) que mudam programas do Windows | Injeta um motor em quase todos os processos e faz hooks de funções; exclui processos críticos e jogos conhecidos | não verificado | Médio: incompatível com antivírus e anti-cheat | Não reproduzir. Referência de funcionalidades |
| Mod "MacOS Minimize Animation" | Genie ao minimizar/restaurar | Hooks em `ShowWindow` e similares, foto da janela, malha Direct2D numa janela fantasma, desliga transições do DWM só naquela janela, mira o botão da barra por UI Automation | n/a | Médio; falhas conhecidas em multi-monitor e janelas translúcidas | Copiar a ideia, não a injeção |
| Mod "Resource Redirect" | Troca ícones/recursos carregados (imageres.dll etc.) sem alterar arquivos do sistema | Hooks em `LoadImageW`, `LoadIconW`, `PrivateExtractIconsW`; `theme.ini` com regras de redirecionamento; há pacotes de ícones estilo macOS | não verificado | Médio | Só via Windhawk; não reproduzir |
| Mod "Windows 11 File Explorer Styler" | Estiliza o Explorer | XAML diagnostics dentro do explorer.exe (só um consumidor por vez: conflita com ExplorerBlurMica e TranslucentTB) | não verificado | Médio | Não reproduzir |
| ExplorerBlurMica | Blur, Acrylic ou Mica no Explorer | DLL registrada com `regsvr32` (admin) e hook em explorer.exe; config em arquivo `.ini` (tipo de efeito, cor RGBA clara/escura, limpar barra de endereço) | Sim | Médio: se o Explorer cair, segurar ESC abre o Explorer para desinstalar | Não embutir. Oferecer como integração opcional guiada |
| DWMBlurGlass | Efeitos globais na barra de título (Blur, Aero, Acrylic, Mica) | Engenharia reversa do DWM + minhook; também modo `SystemBackdrop` (API pública) | Provável (não confirmado) | Alto: mexe no DWM; há versões falsas com malware | Não reproduzir |
| SecureUxTheme | Permite temas visuais (`.msstyles`) não assinados | Remove a verificação de assinatura na memória; sem driver, compatível com Secure Boot | Sim (instalação em nível de sistema) | Alto: tema quebrado pode causar loop de login | Fora do escopo |
| StartAllBack | Barra de tarefas/Explorer/menu de contexto/Iniciar clássicos e estilizados | Produto comercial fechado; `winget ... --scope machine` | Sim | Médio | Não reproduzir |
| Winaero Tweaker | Dezenas de ajustes de aparência e comportamento | Principalmente registro e APIs; **não consegui abrir a página nesta sessão** | Parte sim | Baixo a médio | Referência de lista de ajustes; precisa ser reverificado |
| Golden Gate (niivu e EvpatKa) | Tema visual macOS para Windows 11 | `.msstyles` + dependências (SecureUxTheme etc.) | Sim | Alto | Só referência visual |
| macOS Sidebar Icons (niivu) | Ícones do painel de navegação estilo macOS | **Não encontrei a página**; provavelmente pacote de ícones | ? | ? | Peça o link ou uma captura |

Nota de licença: ExplorerBlurMica e DWMBlurGlass usam LGPL/GPL; Windhawk, GPL-3.0; temas da niivu são "uso pessoal, sem redistribuição". Embutir ou redistribuir exige análise de licença.

## 3. Personalização do Explorer: o que é viável

**Camada A (segura, por usuário, sem admin, reversível) — recomendada para o v1:**
- Tema claro/escuro e cor de destaque (valores em `HKCU`, avisar o Windows com `WM_SETTINGCHANGE`).
- Mostrar/ocultar arquivos ocultos e extensões, e Acesso Rápido (`HKCU\...\Explorer\Advanced`).
- Ícone por pasta com `desktop.ini` (`IconResource`), reversível apagando a entrada; atualizar o cache de ícones com `SHChangeNotify`.
- Painel de navegação por usuário (CLSID com `System.IsPinnedToNameSpaceTree`); **precisa de teste prático antes de prometer**.
- Efeito Mica/Acrylic nas **nossas** janelas via `DWMWA_SYSTEMBACKDROP_TYPE` (API documentada).
- Animação nativa de minimizar ligada/desligada com `SystemParametersInfo(SPI_SETANIMATION)` (documentada; **confirmar o efeito no Windows 11 em teste**).

**Camada B (precisa de injeção, admin ou terceiros) — só como integração opcional guiada:** blur dentro do Explorer, ícones de sistema trocados em todos os programas, estilização do Explorer.
Quem quiser isso instala o Windhawk/ExplorerBlurMica por conta própria; o Goat apenas detecta, orienta e avisa dos riscos.

**Camada C (patch de tema/sistema) — fora do escopo:** SecureUxTheme, DWMBlurGlass, substituição de arquivos de recursos.

**Estratégia:** o GoatFinder é um gerenciador de arquivos próprio com Mica/blur nativo. Isso entrega o visual sem tocar no Explorer. O Explorer continua sendo usado em diálogos de abrir/salvar.

**Regras para qualquer mudança no Windows:** backup do valor anterior antes de alterar, lista de alterações feitas pelo Goat, botão "Desfazer tudo", restauração ao desinstalar e na inicialização se detectar saída anormal, e confirmação explícita do usuário.

## 4. Spotify

- **Caminho principal: SMTC** (`GlobalSystemMediaTransportControlsSessionManager`, Windows 10 1809+). Funciona sem conta nem credencial, com o app desktop ou o player web. Entrega título, artista, álbum, capa, linha do tempo, play/pause/anterior/próxima, aleatório e repetição, e permite mudar a posição. A dock atual já usa essa API no widget de mídia; faltam seek e o visual dedicado.
- **Volume:** o SMTC não controla volume. Usar o volume por aplicativo do Core Audio (sessão do processo do Spotify).
- **Abrir:** URI `spotify:`.
- **Web API (opcional e avançada):** o app em modo de desenvolvimento exige dono com Premium e no máximo 5 usuários na lista; o modo estendido só é concedido a organizações com pelo menos 250 mil usuários ativos (regra desde 15/05/2025). Logo não serve como padrão de um app público. Só como opção em que cada usuário cria seu próprio app (PKCE, sem segredo), para curtir, fila e playlists.

Fontes: developer.spotify.com (quota-modes e get-the-users-currently-playing-track) e learn.microsoft.com (classe `GlobalSystemMediaTransportControlsSessionManager`).

## 5. Levantamento local (somente leitura, 2026-10-06)

Metodologia: listagem de pastas, leitura de arquivos de texto/configuração e dos módulos carregados no `explorer.exe`. Nenhum binário foi aberto, executado ou analisado.

- Windows 11 Home build 26200.
- **Windhawk 1.7.3** ativo, com os mods *Resource Redirect* e *UXTheme hook* dentro do `explorer.exe`. O *UXTheme hook* cumpre o papel do SecureUxTheme (que não está instalado).
- **OldNewExplorer** está carregado no `explorer.exe` a partir de `Downloads`. Mover ou apagar essa pasta quebra o carregamento.
- **ExplorerBlurMica**: arquivos em `Downloads`, **não carregado** (o registro exige `regsvr32` como administrador e reiniciar o Explorer).
- **DWMBlurGlass 2.3.1**: extraído em `C:\DWMBlurGlass`. Traz `dbghelp.dll` e `symsrv.dll`, o que sugere (inferência, não confirmado) que resolve funções internas do DWM por símbolos da Microsoft, ou seja, depende da versão exata do Windows.
- **StartAllBack**: não encontrado.
- **Winaero Tweaker 1.65**: instalado. A licença (EULA) **proíbe engenharia reversa, redistribuição e inclusão em outro software**; usamos só a lista pública de recursos.
- **MyDockFinder**: `UiAccess.exe` em Program Files com `config.ini` apontando para um build em `Downloads`. O nome sugere o recurso *UIAccess* do Windows (executável assinado em local seguro, para desenhar sobre janelas elevadas); não confirmado.

## 6. Como essas ferramentas mexem no Explorer

| Mecanismo | Exemplo | O que exige de nós |
|---|---|---|
| Registro e APIs documentadas | Winaero (muitos ajustes), tema claro/escuro, ícones por pasta | C# comum, reversibilidade |
| Código dentro do `explorer.exe` | ExplorerBlurMica (extensão COM de pasta, desde a v2.0), OldNewExplorer, mods do Windhawk | DLL **nativa** (C++), assinada, x64 e ARM64, admin para registrar, reiniciar o Explorer, manutenção a cada build do Windows (o changelog do ExplorerBlurMica é uma lista de correções por versão) |
| Hooks no DWM ou no `uxtheme` | DWMBlurGlass, SecureUxTheme, mod UXTheme hook | Tudo acima, mais dependência de funções internas e risco de loop de login |

O ExplorerBlurMica deixou de publicar o código-fonte completo (só cabeçalhos) desde a 2.0, por violações da licença LGPL. Não podemos copiar o código deles.

## 7. Restrições de licença e ativos

- **macOS Sidebar Icons (niivu):** 64 ícones em 14 cores x 2 modos, **portados da fonte SF Pro da Apple**, uso pessoal, sem redistribuição. Não podemos embutir; precisamos de um conjunto de ícones original.
- **Temas Golden Gate:** uso pessoal, sem redistribuição. Só referência visual.
- **Windhawk:** GPL-3.0. **ExplorerBlurMica/DWMBlurGlass:** LGPL-3.0/GPL-3.0. Copiar código deles impõe as regras dessas licenças.
