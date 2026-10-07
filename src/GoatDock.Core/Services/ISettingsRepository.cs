using GoatDock.Core.Models;

namespace GoatDock.Core.Services;

public interface ISettingsRepository
{
    Preferencias Carregar();
    void Salvar(Preferencias prefs);
    Task SalvarAsync(Preferencias prefs);
    string ObterCaminhoConfiguracoes();
}
