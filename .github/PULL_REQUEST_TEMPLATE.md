## O que mudou e por quê

## Como testar

## Checklist

- [ ] `dotnet build GoatDockFinder.slnx -c Release` sem erros
- [ ] `dotnet test ... --filter "Category!=SystemIntegration"` aprovado
- [ ] GoatDock e GoatFinder continuam sem se referenciar (testes de arquitetura)
- [ ] Alterações no Windows passam pelo diário, com confirmação e desfazer
- [ ] Documentação e CHANGELOG atualizados, se aplicável
