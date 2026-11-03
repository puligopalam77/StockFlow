using Microsoft.AspNetCore.Identity;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly PasswordHasher<User> _passwordHasher;
    public UserService(IUserRepository repository)
    {
        _repository = repository;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<int> CreateUserAsync(User user, string password)
    {
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.IsActive = true;
        return await _repository.CreateAsync(user);
    }
}
