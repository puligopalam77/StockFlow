using StockFlow.Application.DTOs.Security;

namespace StockFlow.Application.Common;

public static class StoreContextRules
{
    public static BranchLocation? DefaultLocation(IEnumerable<BranchLocation> locations, bool canReviewStores)
    {
        var active = locations.Where(l => l.IsActive && l.LocationType != "TRANSIT").ToList();
        var stores = active.Where(l => l.StoreId.HasValue).ToList();
        var defaults = stores.Where(l => l.IsDefault).Select(l => l.StoreId).Distinct().ToList();
        var store = defaults.Count == 1 ? defaults[0] : stores.Select(l => l.StoreId).Distinct().Count() == 1 ? stores.First().StoreId : null;
        var salesLocation = stores.Where(l => (canReviewStores || l.StoreId == store) && l.LocationType == "SALES_FLOOR").OrderByDescending(l => l.StoreId == store)
.ThenBy(l => l.LocationId)
.FirstOrDefault();
        if (salesLocation is not null)
        {
            return salesLocation;
        }

        // Warehouse-only staff retain receiving/transfer access; retail staff need a sales floor.
        if (canReviewStores || stores.Count == 0)
        {
            return active.OrderByDescending(l => l.IsDefault).ThenBy(l => l.LocationId).FirstOrDefault();
        }
        else
        {
            return null;
        }
    }
}
