using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Security;

public class BusinessProfileChange
{
    [Required] public string Code { get; set; } = "";
    [Range(1, long.MaxValue)] public long ExpectedVersion { get; set; }
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
    public bool CreateStarterCategories { get; set; }
    public bool Acknowledged { get; set; }
}

public class BusinessProfileImpact
{
    public int ProductCount { get; set; }
    public int TransactionCount { get; set; }
    public int CategoryCount { get; set; }
}
