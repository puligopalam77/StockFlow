using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class SecurityAuthService : IAuthService
{
    private readonly ISecurityRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public SecurityAuthService(ISecurityRepository repository)
    {
        this.repository = repository;
    }

    public Task<LoginResult> ValidateLoginAsync(LoginRequest request)
    {
        return repository.LoginAsync(request);
    }
}
