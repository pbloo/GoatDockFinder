using System;
using System.Collections.Generic;
namespace Goat.Shared.Windows;

public interface IWindowTrackingService : IDisposable
{
    event Action? JanelasAlteradas;
    event Action<IntPtr>? JanelaAtivada;
    event Action<bool>? TelaCheiaAlterada;

    IReadOnlyList<JanelaInfo> ObterJanelasAbertas();
    IntPtr ObterJanelaAtiva();
    void Iniciar();
    void Parar();
    bool AtivarJanela(IntPtr hWnd);
    bool MinimizarJanela(IntPtr hWnd);
    bool FecharJanela(IntPtr hWnd);
}
