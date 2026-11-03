using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/purchases/{id:long}/receipts")]
public class PurchaseReceiptController : ControllerBase
{
    private readonly IPurchaseReceiptRepository repository;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public PurchaseReceiptController(IPurchaseReceiptRepository repository, StockFlow.Web.Security.PermissionContext access)
    {
        this.repository = repository;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> Get(long id)
    {
        return Ok(await repository.GetAsync(id));
    }

    [HttpPost]
    public async Task<IActionResult> Receive(long id, ReceivePurchaseRequest request)
    {
        if (!access.Can("PURCHASES", "APPROVE"))
        {
            return Forbid();
        }

        return Ok(new
        {
            receiptId = await repository.ReceiveAsync(id, request, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!))
        });
    }
}
