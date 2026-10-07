# Inventário de recursos existentes

Nada desta lista pode ser perdido em refatorações. Ao mover código, conferir o item correspondente e os testes.
Fonte: código herdado e a documentação anterior. "Disponível no código" não significa validado em todas as instalações.

## GoatDock

- **Ambientes** independentes (Trabalho, Estudos, Pessoal): apps fixados, widgets, estilos e visibilidade por ambiente; itens globais quando configurados.
- **Dock personalizável:** temas, cores, altura, transparência, cantos arredondados, divisores, coleções, ocultação automática, expansão (magnificação) dos ícones.
- **Interação com apps:** miniaturas de janelas via DWM, ativar e fechar janelas, foco por teclado, instância única.
- **Launchpad** com os aplicativos do Menu Iniciar (pasta shell:AppsFolder, inclui apps da Loja).
- **Dock em todos os monitores** (opcional) e **dock sobre as janelas sem reservar espaço** (opcional).
- **Efeito gênio** ao minimizar e restaurar, em direção ao ícone da dock.
- **Pastas fixadas abrem no GoatFinder** quando ele está em execução.
- **Modo barra principal** (opcional): substitui a barra do Windows, com restauração (`tools/restaurar-barra-windows.*`).
- **Lixeira integrada** (oculta o ícone do desktop quando ativada).
- **Controles rápidos:** atalhos de configurações e ações do Windows; bloquear teclado por 30 s; bloquear tela; suspender com confirmação; seta de ícones ocultos.
- **Prévias de pasta:** até 100 itens, imagens e trechos de texto.
- **Abrir painéis por clique ou mouse**, por ambiente.
- **Alertas visuais** WhatsApp (verde) e Teams (violeta) com prioridade sobre RGB; **RGB musical** e **modo gamer**.
- **Ajustes** adaptativos com loja local de widgets e menu "Personalizar".
- **Persistência:** configurações JSON com backup e migrações.

### Widgets

Relógio (digital/analógico, mundiais, cronômetro, temporizador), Pomodoro, Calendário e reuniões (iCalendar), Mídia (SMTC, capa, cores derivadas), Clima (wttr.in), Monitor do sistema (CPU, RAM, rede, armazenamento), Bateria, GitHub (contribuições + animações arcade), WhatsApp, Teams, Notas, Discord (atalho), OBS (atalho), Spotify (capa, controles, aleatório, repetição, busca; sem volume).

## GoatFinder

- **Barra de menu** (AppBar): menu do app em foco lido via HMENU com fallback "Janela"; menus próprios. Configurável ao vivo: posição (topo ou base), altura, largura, folga, reserva de espaço, material do fundo, cor, intensidade, borda, espaçamento, ícones, fonte, animações, elementos visíveis e uma barra por monitor.
- **Gerenciador de arquivos:** barra lateral, visões lista e ícones, busca, painel de prévia, arrastar e soltar, área de transferência de arquivos, menus centralizados (`MenuBuilder`/`FinderMenus`).
- **Stage Manager**: snapshots por PrintWindow, miniaturas inclinadas 3D em cartões, animação de entrada, ativação robusta de janelas de outros processos.
- **Janela de configurações** do Finder, com a personalização do Windows (camada A) e "desfazer tudo".

## Instalação

- Instalador por componentes (GoatDock, GoatFinder ou ambos), modo "Modificar", atualização, atalhos e inicialização por componente, desinstalação, backup preventivo de configurações, restauração segura da barra e desfazer das personalizações do Windows.
- Workflow de build do Windows (compila, testa, empacota).
- Ferramentas: `build-installer.ps1`, gerador de ícone, restauração da barra, sondas de notificação e harness visual.
