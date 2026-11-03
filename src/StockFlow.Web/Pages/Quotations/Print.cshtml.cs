using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
namespace StockFlow.Web.Pages.Quotations;

[Authorize]
public sealed class PrintModel(IQuotationRepository repository):PageModel
{
    public QuotationDetail Document {get;private set;}=null!;
    public async Task<IActionResult> OnGetAsync(Guid id,int revision)
    {
        await repository.RequireAsync("PRINT");Document=await repository.GetAsync(id,revision);return Page();
    }
}
