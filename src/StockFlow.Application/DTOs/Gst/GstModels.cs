using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Gst;

public sealed class GstBusiness
{
    [StringLength(250)] public string? AddressLine1 { get; set; }
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? District { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long BusinessId { get; set; }
    public int? CompanySettingsId { get; set; }
    [Required, StringLength(200, MinimumLength = 2)] public string LegalName { get; set; } = "";
    [StringLength(200)] public string? TradeName { get; set; }
    [StringLength(80)] public string? Constitution { get; set; }
    [StringLength(10)] public string? Pan { get; set; }
    [StringLength(1000)] public string Address { get; set; } = "";
    [Required] public string StateCode { get; set; } = "";
    [Required] public string PinCode { get; set; } = "";
    [RegularExpression("^[6-9][0-9]{9}$")] public string? Mobile { get; set; }
    [EmailAddress, StringLength(150)] public string? Email { get; set; }
}

public sealed class GstRegistration
{
    public DateTime? UpdatedAt { get; set; }
    public long RegistrationId { get; set; }
    public long BusinessId { get; set; }
    public string? Gstin { get; set; }
    public string RegistrationType { get; set; } = "REGULAR";
    [StringLength(100)] public string? OtherType { get; set; }
    [Required] public string StateCode { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    [StringLength(200)] public string? AuthorizedContact { get; set; }
    [Required, StringLength(1000, MinimumLength = 3)] public string Address { get; set; } = "";
}

public sealed class GstMapping
{
    public DateTime? UpdatedAt { get; set; }
    public long MappingId { get; set; }
    public long RegistrationId { get; set; }
    public string BusinessProfileCode { get; set; } = "";
    public long StoreId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public sealed class GstTaxConfiguration
{
    public long? StoreId { get; set; }
    public long? VariantId { get; set; }
    public long? RuleId { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public long TaxConfigId { get; set; }
    public int ProductId { get; set; }
    public string Classification { get; set; } = "GOODS";
    [Required] public string HsnSac { get; set; } = "";
    [Required] public string Uqc { get; set; } = "NOS";
    public string Treatment { get; set; } = "TAXABLE";
    public decimal TaxRate { get; set; }
    public decimal CessRate { get; set; }
    public bool ReverseCharge { get; set; }
    public bool? PricesIncludeTax { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    [Required, StringLength(300, MinimumLength = 3)] public string ReviewReference { get; set; } = "";
}

public sealed record GstShop(long StoreId, string StoreName);
public sealed record GstProduct(int ProductId, string ProductName, string? HsnCode, string UnitOfMeasure, decimal TaxPercentage);
public sealed record GstAudit(long AuditId, string Entity, long RecordId, string Action, int ActorId, DateTime CreatedAt, string? OldValue, string? NewValue);
public sealed class GstSettings
{
    public bool CatalogAvailable { get; set; }
    public string Today { get; set; } = "";
    public StockFlow.Application.DTOs.Invoices.CompanySettings? Company { get; set; }
    public bool Available { get; set; }
    public IEnumerable<GstBusiness> Businesses { get; set; } = [];
    public IEnumerable<GstRegistration> Registrations { get; set; } = [];
    public IEnumerable<GstMapping> Mappings { get; set; } = [];
    public IEnumerable<GstTaxConfiguration> Taxes { get; set; } = [];
    public IEnumerable<GstShop> Shops { get; set; } = [];
    public IEnumerable<GstProduct> Products { get; set; } = [];
    public IEnumerable<GstAudit> History { get; set; } = [];
}

public sealed record GstSetupCheck(string Key,string Label,string Status,string Message,string Step);
public sealed class GstSetupReadiness
{
    public long RegistrationId { get; set; }
    public long StoreId { get; set; }
    public string BusinessProfileCode { get; set; } = "";
    public string Today { get; set; } = "";
    public bool Ready { get; set; }
    public bool CanActivate { get; set; }
    public string Fingerprint { get; set; } = "";
    public List<GstSetupCheck> Checks { get; set; } = [];
}
public sealed record GstActivateSetup(long RegistrationId,string Fingerprint);
public sealed class GstAuditQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long? RegistrationId { get; set; }
    public long? StoreId { get; set; }
    public int? ActorId { get; set; }
    public string? Action { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public sealed class GstAuditEntry
{
    public long AuditId { get; set; }
    public string Entity { get; set; } = "";
    public long RecordId { get; set; }
    public string Action { get; set; } = "";
    public int ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
public sealed record GstAuditPage(long Total,IEnumerable<GstAuditEntry> Rows);
