using StockFlow.Application.DTOs.Auth;

namespace StockFlow.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResult> ValidateLoginAsync(LoginRequest request);
}
