namespace StockFlow.Application.DTOs.Security;

public sealed class StaffRegistrationOptions
{
    public string BusinessName { get; set; } = "";
    public bool CanManageStaff { get; set; }
    public bool CanCreateShopAdmin { get; set; }
    public bool CanUseAdvancedAccess { get; set; }
    public List<SecurityRole> Roles { get; set; } = [];
    public List<StaffShopOption> Stores { get; set; } = [];
    public List<BranchLocation> Warehouses { get; set; } = [];
}
public sealed class StaffShopOption
{
    public long StoreId { get; set; }
    public string StoreName { get; set; } = "";
}
