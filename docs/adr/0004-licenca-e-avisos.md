# ADR 0004 — Licença e avisos de terceiros

- Status: **Aceita (provisória)**
- Data: 2026-10-06

## Contexto

O código inicial do GoatDockFinder foi copiado de um projeto MIT cujo autor autorizou verbalmente o uso e a modificação. A MIT permite usar, copiar, modificar, renomear, vender e relicenciar, com uma condição: o aviso de copyright e o texto da licença permanecem em todas as cópias ou partes substanciais. Uma autorização verbal não substitui isso.

## Decisão

1. O arquivo `LICENSE` traz dois avisos de copyright: o do autor original (obrigatório enquanto existir código derivado) e o do mantenedor deste projeto, pelo código novo. Nenhum dos dois aparece na interface nem no nome do produto.
2. Os avisos de dependências ficam em `THIRD-PARTY-NOTICES.md` (Ical.Net MIT, NodaTime Apache-2.0, outros que surgirem).
3. O código novo escrito para o GoatDockFinder pode ter licença própria; a escolha fica pendente e não bloqueia o desenvolvimento.
4. Caminhos para remover o aviso original: (a) autorização escrita do autor, (b) reescrever os trechos derivados até não restar nada substancial. Nenhum agente deve apagar o aviso sem uma dessas duas condições.

Isto não é aconselhamento jurídico.

## Consequências

Nenhum impacto no uso pessoal. Para distribuir publicamente ou vender, resolver antes a licença própria e a situação do aviso original.
