using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Auth;

public class LoginRequest
{
    [Required, StringLength(50)]
    public string TenantCode { get; set; } = "DEFAULT";

    [Required(ErrorMessage = "Username is required."), StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required."), StringLength(128)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe
    {
        get; set;
    }
}
