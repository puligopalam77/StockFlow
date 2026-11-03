using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Gst;

public sealed class GstTaxRule
{
    public long RuleId { get; set; }
    [Required] public string RuleName { get; set; } = "";
    [Required] public string Description { get; set; } = "";
    [Required] public GstTaxConfiguration Tax { get; set; } = new();
    public string ReviewStatus { get; set; } = "DRAFT";
    public string Status { get; set; } = "ACTIVE";
    public string? Notes { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public long ProductsAssigned { get; set; }
}
public sealed class GstCatalogQuery
{
    public string? Search { get; set; }
    public int? ParentCategoryId { get; set; }
    public int? CategoryId { get; set; }
    [Required] public int[] CategoryIds { get; set; } = [];
    public int? BrandId { get; set; }
    public string? Treatment { get; set; }
    public decimal? Rate { get; set; }
    public string? Status { get; set; }
    public DateTime? Date { get; set; }
    public int? ProductId { get; set; }
    public bool Variants { get; set; }
    public bool ReviewedOnly { get; set; }
    public bool CurrentOnly { get; set; }
    public bool NeedsAttention { get; set; }
    public string Sort { get; set; } = "name";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public sealed class GstCatalogRow
{
    public int ProductId { get; set; }
    public long? VariantId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? ProductSku { get; set; }
    public string? Sku { get; set; }
    public string? Category { get; set; }
    public string? ParentCategory { get; set; }
    public string? Brand { get; set; }
    public string? ProductHsn { get; set; }
    public string? HsnSac { get; set; }
    public string? Treatment { get; set; }
    public decimal? TaxRate { get; set; }
    public string? RuleName { get; set; }
    public long? RuleId { get; set; }
    public long? TaxConfigId { get; set; }
    public string MappingStatus { get; set; } = "Missing";
    public string MappingSource { get; set; } = "Product default";
    public int VariantCount { get; set; }
    public int OverrideCount { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool? PricesIncludeTax { get; set; }
    public string? ReviewReference { get; set; }
}
public sealed record GstCatalogSummary(long Total,long Configured,long NeedsReview,long Missing);
public sealed record GstCatalogPage(long Total,IEnumerable<GstCatalogRow> Rows,GstCatalogSummary Summary);
public sealed record GstChoice(int Id,string Name,int? ParentId = null);
public sealed record GstCatalogOptions(IEnumerable<GstChoice> Categories,IEnumerable<GstChoice> Brands,Dictionary<string,bool> Permissions,string PricingScope);
public sealed record GstRulePage(long Total,IEnumerable<GstTaxRule> Rows);
public sealed record GstTarget(int ProductId,long? VariantId=null);
public sealed class GstAssignment
{
    public Guid RequestId { get; set; }
    public long RuleId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool OverrideExisting { get; set; }
    public string? Reason { get; set; }
    public bool AllMatching { get; set; }
    public bool ConfirmAllMatching { get; set; }
    [Required] public GstCatalogQuery Filter { get; set; } = new();
    [Required] public List<GstTarget> Targets { get; set; } = [];
    public List<GstImportRow>? Import { get; set; }
}
public sealed class GstImportRow
{
    public const string TemplateHeader="ProductId,VariantId,ProductCode,ProductName,ProductSku,VariantSku,RuleId,HsnSac,Treatment,TaxRate,PricesIncludeTax,EffectiveFrom,EffectiveTo,ReviewReference";
    public int Row { get; set; }
    public int ProductId { get; set; }
    public long? VariantId { get; set; }
    public string? VariantSku { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductSku { get; set; }
    public bool? PricesIncludeTax { get; set; }
    public long RuleId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? HsnSac { get; set; }
    public string? Treatment { get; set; }
    public decimal? TaxRate { get; set; }
    public string? ReviewReference { get; set; }
    public string? Error { get; set; }
}
public sealed record GstOperationRow(int Row,int ProductId,long? VariantId,string ProductName,string Status,string Message,string? BeforeValue,string? ProposedValue);
public sealed record GstOperation(Guid OperationId,string Status,long Total,Dictionary<string,long> Counts,IEnumerable<GstOperationRow> Rows,string ScopeLabel,string? SelectionSummary=null,long? FilteredTotal=null);
public sealed record GstOperationConfirm(Guid OperationId,bool Confirm);
public sealed record GstRuleReview(long RuleId);
public sealed record GstRuleEnd(long RuleId,DateTime EffectiveTo,string Reason,bool Confirm);
public sealed record GstMappingHistory(IEnumerable<GstTaxConfiguration> Versions,IEnumerable<GstAuditEntry> Audit);
public sealed record GstOperationSummary(Guid OperationId,string Status,long Total,long Updated,long Failed,DateTime CreatedAt);
public sealed record GstOperationPage(long Total,IEnumerable<GstOperationSummary> Rows);
