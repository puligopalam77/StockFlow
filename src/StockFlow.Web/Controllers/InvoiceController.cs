using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InvoiceController : ControllerBase
{
    private readonly IInvoiceService service;
    // ASP.NET supplies these services when it creates this class.
    public InvoiceController(IInvoiceService service)
    {
        this.service = service;
    }

    [HttpGet("api/invoices/{saleId:long}")]
    [HttpGet("api/sales/{saleId:long}/invoice")]
    public async Task<IActionResult> Get(long saleId)
    {
        if (await service.GetInvoiceAsync(saleId) is { } invoice)
        {
            return Ok(invoice);
        }
        else
        {
            return NotFound();
        }
    }
}
