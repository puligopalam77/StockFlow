using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Interfaces;

public interface IScreenLanguageRepository
{
    Task<bool> IsAvailableAsync();
    Task<List<ScreenLanguageSetting>> GetAsync();
    Task SaveAsync(ScreenLanguageSetting setting);
}
