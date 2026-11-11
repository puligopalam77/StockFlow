using System.Text.Json;
namespace StockFlow.Application.DTOs.Operations;

// Development release: deliberately no fallback to the application's live database.
public sealed class BankAccountsOptions
{
    public bool Enabled { get; set; }
    public bool PostingEnabled { get; set; }
    public string DevelopmentConnection { get; set; } = "";
    public string IdentityKey { get; set; } = "";
}
public sealed class BankOperation
{
    public Guid RequestId { get; set; }
    public JsonElement Body { get; set; }
}
