using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Security;

public class BranchLocation
{
    public long LocationId
    {
        get; set;
    }
    public long? StoreId
    {
        get; set;
    }
    public string StoreName { get; set; } = "Central warehouse";
    public string LocationName { get; set; } = "";
    public string LocationType { get; set; } = "";
    public bool IsDefault
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
}

public class StoreRequest
{
    [StringLength(250)] public string? AddressLine1 { get; set; }
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? District { get; set; }
    [RegularExpression("^[1-9][0-9]{5}$")] public string? PinCode { get; set; }
    [EmailAddress,StringLength(150)] public string? Email { get; set; }
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone)] public string? AlternatePhone { get; set; }
    [Required, StringLength(50)]
    public string StoreCode { get; set; } = "";

    [Required, StringLength(150)]
    public string StoreName { get; set; } = "";

    [StringLength(1000)]
    public string? Address
    {
        get; set;
    }

    [StringLength(100)]
    public string? City
    {
        get; set;
    }

    [StringLength(100)]
    public string? State
    {
        get; set;
    }

    [StringLength(10)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone, ErrorMessage = StockFlow.Application.Common.FieldPatterns.PhoneMessage)]
    public string? MobileNumber
    {
        get; set;
    }

    [StringLength(15)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Gst, ErrorMessage = StockFlow.Application.Common.FieldPatterns.GstMessage)]
    public string? GstNumber
    {
        get; set;
    }
}

public class LocationRequest
{
    public long? StoreId
    {
        get; set;
    }

    [Required, StringLength(150)]
    public string LocationName { get; set; } = "";
    public string LocationType { get; set; } = "STOCKROOM";
}

public class StoreAccessRequest
{
    public List<long> StoreIds { get; set; } = [];
    public List<long> WarehouseLocationIds { get; set; } = [];
    public long? DefaultStoreId
    {
        get; set;
    }
}

public class BranchUserState
{
    public string BusinessProfileCode { get; set; } = StockFlow.Application.Common.BusinessProfiles.Electrical;
    public bool BusinessProfilePersisted { get; set; }
    public long BusinessProfileVersion { get; set; }
    public bool BusinessProfileConfigurationAvailable { get; set; }
    public bool ProfileCatalogSwitchingAvailable { get; set; }
    public bool ProfileDataVisibilityAvailable { get; set; }
    public bool CatalogRegistrationEnhancementsAvailable { get; set; }
    public bool ProductImageGalleryAvailable { get; set; }

    public bool IsOwner
    {
        get; set;
    }
    public bool IsAdmin
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
}

public class StockComparison
{
    public long VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string VariantName { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public long CurrentStock
    {
        get; set;
    }
    public string LocationType { get; set; } = "";
}
