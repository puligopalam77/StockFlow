using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;

namespace StockFlow.Web.Pages.CashInHand;
public sealed class IndexModel(BusinessScope scope, StockFlow.Application.Interfaces.IBranchRepository branches) : PageModel
{
    public string ShopContext { get; private set; } = "";
    public async Task<IActionResult> OnGetAsync()
    {
        if(!scope.CanReviewStores || scope.LocationId<=0 || scope.Consolidated)return StatusCode(403);
        var location=(await branches.LocationsAsync()).SingleOrDefault(l=>l.LocationId==scope.LocationId && l.StoreId==scope.StoreId);
        if(location is null)return StatusCode(403);
        ShopContext=location.StoreName+" · "+location.LocationName;
        return Page();
    }
}
