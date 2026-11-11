using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
namespace StockFlow.Web.Pages.Settings.BankAccounts;
public sealed class IndexModel(IOptions<BankAccountsOptions> options,BusinessScope scope):PageModel
{
    public Guid? AccountId { get; private set; }
    public bool PostingEnabled=>options.Value.PostingEnabled;
    public IActionResult OnGet(Guid? accountId)
    {
        if(!options.Value.Enabled)return NotFound();
        if(!scope.CanReviewStores)return Forbid();
        AccountId=accountId;ViewData["BankAccountDetail"]=accountId.HasValue;
        return Page();
    }
}
