# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento semântico; a versão vem de `Directory.Build.props`.

## [1.0.0] - 2026-10-06

Primeira versão do GoatDockFinder: um produto, dois componentes independentes.

### Estrutura
- Projeto novo, criado a partir de uma cópia do código anterior (o original não foi alterado).
- Camadas: `Goat.Shared`, `Goat.Platform`, `Goat.Ui`, `GoatDock.Core`/`GoatDock`, `GoatFinder.Core`/`GoatFinder`, `GoatDockFinder.Installer`.
- Regras de dependência entre os componentes verificadas por testes automáticos.
- Versão única do produto (antes havia três valores diferentes).

### Instalador
- Instalação por componentes: GoatDock, GoatFinder ou os dois; reabrir o instalador permite adicionar ou remover um componente.
- Pacotes separados (`dock.zip`, `finder.zip`), manifesto `components.json` e pasta própria por componente.
- Personalização opcional do Windows na instalação, com valor anterior guardado e desfeita ao desinstalar.
- Atalhos e inicialização com o Windows por componente.

### GoatDock
- Dock sobre as janelas sem reservar espaço (opção "Reservar espaço na tela").
- Dock em todos os monitores, com as mesmas configurações.
- Efeito gênio (minimizar e restaurar em direção ao ícone da dock), mirando o ícone real da dock.
- Widget do Spotify (capa, controles, aleatório, repetição e busca na faixa) pela API de mídia do Windows.
- Pastas fixadas abrem no GoatFinder quando ele está em execução.
- Opção para desligar a animação nativa de minimizar, com desfazer.

### GoatFinder
- Barra de menu totalmente configurável ao vivo: altura, largura, posição, folga, reserva de espaço, material do fundo, cor e intensidade, bordas, espaçamento, tamanho dos ícones, fonte, animações, elementos visíveis e uma barra por monitor.
- Stage Manager redesenhado; corrigidos o clique nas miniaturas (a área clicável só incluía o ícone) e a ativação de janelas de outros processos (restrição de foco do Windows).
- Janela de configurações com personalização do Windows (camada A) e "desfazer tudo".

### Integração
- Comunicação entre Dock e Finder por pipe nomeado, com protocolo versionado, reconexão automática e funcionamento sem o outro componente.

### Removido
- Dados pessoais do autor original na interface e nomes legados.
- Localizador do ícone por UI Automation (o Dock agora conhece os próprios ícones).

### Limitações conhecidas
Ver `docs/product/known-issues.md`. Principais: executáveis sem assinatura de código; componente nativo em C++ (camada B) fora desta versão; widget do Spotify não controla volume; o Windows não permite ajustar a força do desfoque.
