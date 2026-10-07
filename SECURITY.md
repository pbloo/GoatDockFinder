# Segurança

## Versões com suporte

Apenas a versão mais recente (1.x) recebe correções.

## Como relatar uma vulnerabilidade

Não abra uma issue pública. Use a aba **Security** do repositório no GitHub (Report a vulnerability) ou entre em contato com o mantenedor pelo perfil. Inclua o componente afetado, os passos para reproduzir e o impacto. Você receberá uma resposta assim que possível.

## O que o projeto faz e não faz

- Não coleta telemetria nem exige conta; configurações e notas ficam em `%LOCALAPPDATA%\GoatDockFinder`.
- Alguns widgets consultam a internet por ação do usuário (clima, contribuições do GitHub, calendários por URL).
- Alterações no Windows ficam registradas em `changes.json` e podem ser desfeitas pelo próprio aplicativo ou ao desinstalar.
- Os executáveis atuais ainda não têm assinatura de código; confira o hash SHA-256 publicado junto do instalador.
