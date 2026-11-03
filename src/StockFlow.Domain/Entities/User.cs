namespace StockFlow.Domain.Entities;

public class User
{
    public bool RoleIsActive
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public int RoleId
    {
        get; set;
    }
    public string RoleName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? MobileNumber
    {
        get; set;
    }
    public string? Email
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
