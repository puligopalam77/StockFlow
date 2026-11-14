using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Security;

public sealed class StoreDirectoryQuery
{
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=10;
    [StringLength(150)] public string? Search { get; set; }
    public string Status { get; set; }="ALL";
}
public class StoreDirectoryRow
{
    public long StoreId { get; set; }
    public string StoreCode { get; set; }="";
    public string StoreName { get; set; }="";
    public bool IsActive { get; set; }
    public bool IsHeadOffice { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? MobileNumber { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? District { get; set; }
    public string? PinCode { get; set; }
    public string? Email { get; set; }
    public string? AlternatePhone { get; set; }
    public long Version { get; set; }
    public long LocationCount { get; set; }
    public long SalesFloorCount { get; set; }
    public string BusinessProfileCode { get; set; }="ELECTRICAL";
    public string GstStatus { get; set; }="Not assessed";
    public string? GstRegistrationType { get; set; }
    public long? GstBusinessId { get; set; }
    public string SetupReadiness { get; set; }="Review existing setup in GST Settings";
}
public sealed record StoreDirectoryPage(IEnumerable<StoreDirectoryRow> Rows,long Total,int Page,int PageSize,long StoreCount,long LocationCount,long WarehouseCount,long MissingSalesFloorCount,bool ManagementAvailable,bool CanManage,bool CanCreate,bool CanAssignAccess,string BusinessProfileCode,long ActiveStoreCount=0,long InactiveStoreCount=0,bool ShopSetupAvailable=false);
public sealed record StoreDetails(StoreDirectoryRow Store,IEnumerable<BranchLocation> Locations);
public sealed class StoreUpdateRequest:StoreRequest
{
    public long ExpectedVersion { get; set; }
}
