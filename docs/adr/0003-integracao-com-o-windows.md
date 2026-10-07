# ADR 0003 — Profundidade da integração com o Windows

- Status: **Aceita**
- Data: 2026-10-06

## Contexto

O dono quer mexer o mais fundo possível no Windows e aceita custo e risco. Para o Explorer, a pesquisa (`docs/research/referencias.md`) mostra três mecanismos de custo e risco muito diferentes.

## Decisão

Camadas, entregues em ordem. Cada camada só é liberada depois da anterior estar estável.

| Camada | O que é | Admin | Reversível | Estado |
|---|---|---|---|---|
| **A** | APIs documentadas e registro do usuário: tema claro/escuro, cor de destaque, arquivos ocultos/extensões, ícone por pasta (`desktop.ini`), Mica/Acrylic nas janelas próprias, animação de minimizar (`SPI_SETANIMATION`) | não | sim, pelo diário | **Entra no v1** |
| **B** | Código dentro do `explorer.exe` ou hooks (DLL nativa em C++): blur no Explorer, ícones de sistema trocados | normalmente sim (registro de DLL), reinício do Explorer | sim, com recuperação de falha | **Fase própria, após A**, com ADR dedicado e protótipo isolado |
| **C** | Patch de tema/DWM/uxtheme (equivalentes a SecureUxTheme e DWMBlurGlass) | sim | difícil; risco de loop de login | **Não planejada.** Só reavaliar com decisão explícita do dono |

Enquanto B não existe, o GoatFinder entrega o visual com um gerenciador de arquivos próprio (Mica/blur nativos). O Explorer segue usado em diálogos de abrir/salvar.

## Contrato de alteração do Windows (obrigatório)

Implementado uma vez em `Goat.Platform`:
1. Mostrar o que vai mudar e pedir confirmação.
2. Gravar o valor anterior em `%LOCALAPPDATA%\GoatDockFinder\changes.json` **antes** de alterar.
3. Oferecer "Desfazer tudo"; desfazer na desinstalação.
4. Se detectar alterações sem processo ativo (saída anormal), oferecer restaurar na próxima inicialização.
5. Nunca exigir administrador sem explicar o motivo.
6. O instalador nunca oculta nem desabilita a barra de tarefas por conta própria.

## Restrições de terceiros

Não copiar código ou ativos de Windhawk (GPL-3.0), ExplorerBlurMica/DWMBlurGlass (LGPL/GPL), Winaero (EULA proíbe engenharia reversa e redistribuição) nem dos ícones/temas niivu (uso pessoal; derivados de fonte da Apple). Podemos nos inspirar nas ideias e documentar o comportamento observável.

## Consequências

- Camada A é barata e segura, mas não cobre blur dentro do Explorer.
- Camada B traz assinatura de código, x64+ARM64 e manutenção a cada build do Windows; só vale depois que o resto estiver estável.
