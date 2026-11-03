namespace StockFlow.Application.Common;
// Request-scoped; only populated from authenticated claims and validated database membership.
public sealed class BusinessScope
{
    public string BusinessProfileCode { get; set; } = BusinessProfiles.Electrical;
    public bool BusinessProfilePersisted { get; set; }
    public long BusinessProfileVersion { get; set; }
    public bool BusinessProfileConfigurationAvailable { get; set; }
    public bool ProfileCatalogSwitchingAvailable { get; set; }
    public bool ProfileDataVisibilityAvailable { get; set; }
    public bool CatalogRegistrationEnhancementsAvailable { get; set; }
    public bool ProductImageGalleryAvailable { get; set; }
    public string DraftProfileSuffix => ProfileCatalogSwitchingAvailable ? "." + BusinessProfileCode : "";

    public long TenantId
    {
        get; set;
    }
    public int UserId
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
    public bool IsOwner
    {
        get; set;
    }
    public bool CanReviewStores
    {
        get; set;
    }
    public bool Consolidated
    {
        get; set;
    }
    public string LocationName { get; set; } = "";
}
