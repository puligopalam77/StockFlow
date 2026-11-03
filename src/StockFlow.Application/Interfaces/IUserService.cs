using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface IUserService
{
    Task<int> CreateUserAsync(User user, string password);
}
