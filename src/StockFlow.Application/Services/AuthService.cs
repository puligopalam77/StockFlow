using Microsoft.AspNetCore.Identity;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly PasswordHasher<User> _passwordHasher;
    public AuthService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<LoginResult> ValidateLoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        if (user == null)
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password."
            };
        }

        PasswordVerificationResult verification;
        try
        {
            verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        }
        catch (FormatException)
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password."
            };
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password."
            };
        }

        if (!user.IsActive || !user.RoleIsActive)
        {
            return new LoginResult { ErrorCode = LoginResult.AccountInactive, ErrorMessage = LoginResult.InactiveMessage };
        }

        return new LoginResult
        {
            Success = true,
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            RoleName = user.RoleName
        };
    }
}
