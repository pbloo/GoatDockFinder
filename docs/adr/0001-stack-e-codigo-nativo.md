# ADR 0001 — Stack e código nativo

- Status: **Aceita**
- Data: 2026-10-06

## Contexto

O produto precisa de interface rica (blur, animações, janelas transparentes, 3D leve), acesso profundo ao Windows (AppBar, DWM, hooks de janela, Shell, UI Automation) e instalação simples. Já existe uma base em C#/WPF com testes. O dono do projeto aceita refatorar tudo e usar C++ onde for necessário.

## Decisão

1. **Aplicativos e lógica: C# (.NET 10) com WPF.** MVVM, xUnit para testes. Mantém tudo o que já existe e permite evoluir sem reescrever.
2. **Código nativo (C++) só quando uma API exigir.** Ele vive em `native/`, é um componente à parte, com interface pequena e estável (C ABI ou COM) consumida por `Goat.Platform` via P/Invoke. Casos que justificam: DLL carregada dentro de outro processo (hook), código que precisa rodar fora do .NET, desempenho crítico de captura/composição.
3. **Dependências nativas e de terceiros** passam por ADR (licença, assinatura, manutenção).
4. Toolchain C++ disponível na máquina de desenvolvimento: Visual Studio BuildTools (x64/x86). ARM64 e CMake não estão instalados; adicioná-los é pré-requisito de qualquer entrega ARM64.

## Alternativas consideradas

| Opção | Por que não agora |
|---|---|
| WinUI 3 | Reescreve toda a interface; ganho de visual não justifica o custo. Reavaliar se o WPF travar algum efeito |
| Tudo em C++ | Custo muito alto, perde a base testada, sem ganho onde não há API nativa necessária |
| Rust/Tauri | Muda a stack inteira; integração Win32 profunda é mais trabalhosa |

## Consequências

- Uma stack principal, uma ponte nativa opcional e isolada.
- Qualquer DLL injetada em outro processo exige assinatura, versões x64/ARM64 e manutenção por build do Windows (ver ADR 0003).
