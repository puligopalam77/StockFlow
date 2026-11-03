namespace StockFlow.Application.DTOs.Auth;

public class LoginResult
{
    public const string AccountInactive = "ACCOUNT_INACTIVE";
    public const string InactiveMessage = "Your account is inactive. Please contact your administrator.";
    public string? ErrorCode { get; set; }
    public long TenantId
    {
        get; set;
    }
    public long StoreId
    {
        get; set;
    }
    public long LocationId
    {
        get; set;
    }
    public bool MustChangePassword
    {
        get; set;
    }
    public long SecurityVersion
    {
        get; set;
    }
    public bool Success
    {
        get; set;
    }
    public string? ErrorMessage
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}
