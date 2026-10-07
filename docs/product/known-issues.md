# Problemas conhecidos

Revalidar antes de tratar como atuais. Itens "herdados" vêm do projeto de origem.

## Verificado na 1.0.0

- **Testes:** 214 de 214 aprovados em Release, filtro `Category!=SystemIntegration` (184 herdados + 30 novos: arquitetura, IPC, diário e personalização do Windows, manifesto, configurações do Finder, launcher). Os 3 testes que falhavam no projeto de origem (clima e RGB) passaram; sugerem dependência de rede, tempo ou ordem.
- **Instalador e barra nativa:** o instalador nunca ativa o modo barra principal; na atualização apenas restaura a barra quando o modo estava ativo.
- **Nome antigo:** nenhuma ocorrência fora do aviso de copyright em `LICENSE`.

## Não validado em máquina real (precisa de teste manual)

| Tema | O que conferir |
|---|---|
| Instalação | Instalar, atualizar, "Modificar" (adicionar/remover componente) e desinstalar em Windows 10 e 11 |
| Dock em vários monitores | DPIs diferentes, monitor removido com a dock aberta, auto-ocultar por monitor |
| Efeito gênio | Janelas translúcidas, multi-monitor, apps que minimizam de forma própria; com e sem a animação nativa desligada |
| Stage Manager | Ativar janela de app elevado (o Windows bloqueia por UIPI), jogos em tela cheia |
| Barra do Finder | Barra flutuante, bottom, uma por monitor, material Mica/Acrílico no Windows 10 (sem backdrop do sistema usa fundo opaco) |
| Personalização do Windows | Cada opção da camada A no Windows 11: o Explorer pode exigir reabrir janelas para refletir |
| Spotify | Faixas sem busca, Spotify fechado, sessão do navegador |

## Limitações

- **Assinatura:** executáveis sem assinatura de código podem ser bloqueados pelo Controle de Aplicativo; é o principal bloqueio para distribuir publicamente.
- **Desfoque:** o Windows só oferece materiais prontos (Mica, Acrílico); a "intensidade" na barra do Finder é a opacidade da cor sobre o material.
- **Spotify:** não controla volume (a API de mídia do Windows não expõe volume).
- **Elevação:** a dock não aparece sobre janelas de apps rodando como administrador sem UIAccess (roadmap).
- **Componente nativo (C++):** não faz parte da 1.0.0.

## Herdados do projeto de origem

| Prioridade | Tema | Detalhe |
|---|---|---|
| Alta | Teste de autostart real | Teste em `SystemIntegration` escreve no perfil do usuário; isolar com diretório e caminho injetados |
| Alta | Migrações de schema | Versão atual única, migrações independentes e em sequência, com testes de entrada em cada versão |
| Média | Apps globais e duplicação | Contrato de itens globais e identidade de itens/processos |
| Média | Integrações demonstrativas | Discord e OBS não executam ações reais; Teams e WhatsApp dependem de heurísticas e de permissão de notificações |
| Média | Validação de URL iCalendar | Allowlist de protocolos; tratamento de URLs com token |
| Média | Ciclo de vida e descarte | Timers/serviços sem descarte uniforme; medir CPU e RAM reais |
| Média | Codificação | Texto com mojibake em alguns pontos (bandeja, "Sair do GoatDock", comentários): revisão UTF-8 |
| Média | Magnificação e geometria | Vizinhos por índice, não por distância; sobreposição com ícones grandes |
| Média | Sincronização dos ajustes | Interruptores podem ficar desatualizados quando a mudança vem da dock ou da bandeja |
| Baixa | Ícone | Provisório (um "G"); criar identidade própria |
| Baixa | Bandeja nativa | Depende do botão do Explorer via acessibilidade; confirmar abertura real |
