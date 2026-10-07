# Contribuindo para o GoatDockFinder

Obrigado por querer ajudar! O projeto tem dois componentes independentes, **GoatDock** e **GoatFinder**, e uma camada compartilhada. Antes de começar, leia o [AGENTS.md](AGENTS.md) (estrutura e regras) e os [ADRs](docs/adr/).

## Como contribuir

1. Faça um fork e crie uma branch a partir de `main`: `git checkout -b feat/minha-ideia`.
2. Faça mudanças pequenas e focadas. Mudança estrutural grande (nova biblioteca, nova camada, novo padrão) começa por uma issue e vira um ADR em `docs/adr/`.
3. Rode build e testes (comandos abaixo) antes de abrir o pull request.
4. Commits no padrão Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`.
5. Abra o pull request preenchendo o modelo. Explique o que mudou, por que e como testar.

## Ambiente

Windows 10/11 x64, SDK .NET indicado em `global.json` (a versão fica em `Directory.Build.props`).

```powershell
dotnet build GoatDockFinder.slnx -c Release
dotnet test tests/GoatDockFinder.Tests/GoatDockFinder.Tests.csproj -c Release --filter "Category!=SystemIntegration"
.\tools\dev.ps1 -Both     # abre os dois componentes sem instalar
```

`tools/instalar-ferramentas-dev.bat` instala Git, .NET SDK e Visual Studio pelo winget, se faltar algo.

## Regras de código

- C# com Nullable habilitado e sem warnings novos. WPF com MVVM.
- `GoatDock` e `GoatFinder` nunca se referenciam; só se falam por IPC. Há testes de arquitetura que quebram se isso mudar.
- Lógica fora de WPF e de Win32; chamadas do Windows ficam em `Goat.Platform` (ou na pasta `Platform/` do componente) atrás de interfaces testáveis.
- Tudo que altera o Windows (registro, `desktop.ini`, animações, barra) passa pelo diário de alterações, com confirmação do usuário e opção de desfazer.
- Código e identificadores novos em inglês; textos da interface em português do Brasil.
- Sem bibliotecas novas quando dá para fazer com o que já existe. Dependência nova exige ADR (licença, manutenção).
- Não copie código ou ativos de terceiros com licença incompatível.

## Relatando bugs

Use as Issues. Inclua a versão do Windows, a versão do GoatDockFinder, qual componente (Dock, Finder ou instalador), os passos para reproduzir e, se houver, a mensagem de erro. Não cole segredos, tokens ou dados pessoais.

## Segurança

Veja o [SECURITY.md](SECURITY.md).
