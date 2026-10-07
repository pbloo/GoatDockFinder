# Roadmap, custos e riscos

Tamanho é relativo (P, M, G, GG), não uma estimativa de prazo. Risco: B (baixo), M (médio), A (alto), MA (muito alto).
Regra: cada fase compila, passa nos testes e não remove nenhum item de `feature-inventory.md`.

## Entregue na 1.0.0

| Item | Estado |
|---|---|
| Repositório novo a partir do original, sem alterá-lo | Feito |
| Renomeação do nome anterior para Goat e remoção de dados pessoais da interface | Feito |
| Camadas `Goat.Shared`, `Goat.Platform`, `Goat.Ui`, Core por componente | Feito |
| Comunicação Dock ↔ Finder (pipe nomeado) e testes de arquitetura | Feito |
| Instalador por componentes, manifesto, modo "Modificar" | Feito |
| Dock sobre as janelas sem reservar espaço | Feito (configurável) |
| Dock em todos os monitores | Feito (validar com monitores de DPI diferentes) |
| Configurações do GoatFinder ao vivo, barra por monitor | Feito |
| Stage Manager redesenhado, clique e foco corrigidos | Feito (validar foco em apps elevados) |
| Efeito gênio no GoatDock | Feito (validar multi-monitor e janelas translúcidas) |
| Widget do Spotify | Feito (sem volume) |
| Apps do Menu Iniciar no launchpad | Já existia (`InstalledAppsScanner`, `shell:AppsFolder`) |
| Personalização do Explorer, camada A, com desfazer | Feito (instalador e janela do Finder) |
| Comportamento da barra do Windows (ocultar / manter / reservar) | Feito (ocultar e reservar são opções independentes) |

## Próximas versões

| # | Item | Dono | Tam. | Risco | Custos e riscos |
|---|---|---|---|---|---|
| 1 | Assinatura de código dos executáveis | Instalador | M | M | Certificado pago e renovável; sem ele o Controle de Aplicativo do Windows pode bloquear. É o maior bloqueio para distribuir publicamente |
| 2 | Dock sobre janelas elevadas (UIAccess) | Dock | M | M | Exige executável assinado e instalado em local seguro, ou seja, instalação com administrador |
| 3 | Volume por aplicativo no widget do Spotify | Dock | M | B | Core Audio (COM); o SMTC não expõe volume |
| 4 | Instalador em MSIX/WiX | Instalador | G | M | Atualização mais robusta; exige certificado |
| 5 | Componente nativo C++ (camada B) | native | GG | MA | Ver abaixo |

## Camada B: custo e risco

Código dentro do `explorer.exe` ou em hooks globais:

- **Assinatura de código** com certificado pago, renovação periódica; sem ela o Windows e antivírus bloqueiam.
- **Administrador** para registrar a DLL e reiniciar o Explorer.
- **x64 e ARM64**, cada um com toolchain própria (ARM64 não está instalado hoje).
- **Manutenção a cada build do Windows**: funções internas mudam; o que funciona hoje pode quebrar numa atualização.
- **Falha derruba o Explorer** (barra, área de trabalho). Exige modo de recuperação e teste em máquina virtual.
- **Antivírus e anti-cheat** podem tratar injeção como ameaça.
- **Conflitos** com Windhawk e similares (somente um consumidor por ponto de extensão).
- Licenças: não copiar código GPL/LGPL.

Só começar depois do item 1, com ADR dedicado e protótipo isolado em máquina virtual.

## Fora do escopo (decisão em ADR 0003)

Patch de tema e do DWM (equivalentes ao SecureUxTheme e ao DWMBlurGlass).

## Backlog solto

- Substituir o ícone provisório por identidade própria.
- Decidir a licença do código novo (ADR 0004).
- Importar uma vez as configurações do app antigo (pasta do nome anterior em `%LOCALAPPDATA%`)? Decisão pendente.
- Renomear para inglês os identificadores em português do GoatDock (refatoração própria).
- Wi-Fi/Bluetooth reais, saída de áudio, microfone, área de transferência, Downloads e arquivos recentes (ideias do projeto de origem).
