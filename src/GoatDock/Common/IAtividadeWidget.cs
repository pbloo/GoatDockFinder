using GoatDock.Core.Widgets;

namespace GoatDock.Common;

public interface IAtividadeWidget : IDisposable
{
    void DefinirAtividade(EstadoAtividade estado);
    SaudeWidget Saude => SaudeWidget.Disponivel;
    string? MotivoEstado => null;
    bool? EmExecucao => null;
}
