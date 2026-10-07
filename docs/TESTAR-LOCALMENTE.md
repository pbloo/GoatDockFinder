# Testar localmente, sem instalar

Há três formas, da mais simples à mais completa. Nenhuma instala nada no Windows nem mexe no registro por conta própria.

## 1. Abrir os componentes já compilados

```powershell
cd C:\Users\pablo\projetos\GoatDockFinder
.\tools\dev.ps1 -Both      # compila e abre GoatDock e GoatFinder
.\tools\dev.ps1 -Dock      # só o GoatDock
.\tools\dev.ps1 -Finder    # só o GoatFinder
.\tools\dev.ps1 -Stop      # encerra os dois
```

Se o PowerShell bloquear o script, rode `powershell -ExecutionPolicy Bypass -File .\tools\dev.ps1 -Both` (vale só para esse comando).

Sem o script: `dotnet build GoatDockFinder.slnx -c Release` e abra `src\GoatDock\bin\Release\net10.0-windows10.0.19041.0\GoatDock.exe` e o equivalente do `GoatFinder`.

## 2. Pelo VS Code, com depuração

Abra a pasta no VS Code, vá em **Executar e Depurar** e escolha `GoatDock`, `GoatFinder` ou `GoatDock + GoatFinder`. Pontos de parada funcionam.

## 3. Pasta portátil a partir do instalador (sem instalar)

O `release\GoatDockFinder-Setup.exe` instala de verdade. Para só olhar os arquivos, publique os componentes numa pasta:

```powershell
dotnet publish src\GoatDock\GoatDock.csproj -c Release -r win-x64 --self-contained false -o C:\Temp\GoatDockFinder\GoatDock
dotnet publish src\GoatFinder\GoatFinder.csproj -c Release -r win-x64 --self-contained false -o C:\Temp\GoatDockFinder\GoatFinder
C:\Temp\GoatDockFinder\GoatDock\GoatDock.exe
```

## O que conferir (roteiro rápido)

1. **GoatFinder sozinho:** a barra aparece no topo; o menu do aplicativo em foco muda ao trocar de janela. Logo da barra → "Configurações do GoatFinder…" e mexa nos controles (a barra reage na hora). Aba "Stage Manager e Windows" → ligue o Stage Manager e clique numa miniatura: a janela deve vir para a frente.
2. **GoatDock sozinho:** a dock aparece no rodapé. Em Ajustes → Comportamento teste "Reservar espaço", "Dock em todos os monitores" e "Efeito gênio" (minimize uma janela). Adicione o widget Spotify pela loja de widgets, com o Spotify aberto.
3. **Os dois juntos:** o menu do logo do Finder mostra "GoatDock conectado". Fixe uma pasta na dock e clique: ela abre numa janela do GoatFinder.
4. **Segurança:** se a barra do Windows sumir ou algo prender a tela, `tools\restaurar-barra-windows.bat` restaura. Encerre com `.\tools\dev.ps1 -Stop`.

## Testes automáticos

```powershell
dotnet test tests\GoatDockFinder.Tests\GoatDockFinder.Tests.csproj -c Release --filter "Category!=SystemIntegration"
```

## Ver o site localmente

```powershell
cd vercel
npx serve .          # ou: python -m http.server 8000
```

Abra o endereço mostrado (ou `vercel\index.html` direto no navegador).
