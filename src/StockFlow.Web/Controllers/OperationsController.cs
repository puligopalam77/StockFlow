using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/operations"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OperationsController : ControllerBase
{
    private readonly IOperationsRepository repository;
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public OperationsController(IOperationsRepository repository, PermissionContext access)
    {
        this.repository = repository;
        this.access = access;
    }

    [HttpGet("sales/{id:long}")]
    public async Task<IActionResult> Sale(long id)
    {
        if (access.Can("SALES", "VIEW"))
        {
            return Ok(await repository.SaleAsync(id));
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("sales/{id:long}/payments")]
    public async Task<IActionResult> CustomerPayment(long id, MoneyReceiptRequest request)
    {
        if (access.Can("SALES", "COLLECT"))
        {
            return Ok(new
            {
                id = await repository.AddCustomerPaymentAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("sales/{id:long}/returns")]
    public async Task<IActionResult> SaleReturn(long id, SaleReturnRequest request)
    {
        if (access.Can("SALES", "RETURN"))
        {
            return Ok(new
            {
                id = await repository.ReturnSaleAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("sales/{id:long}/fulfilments")]
    public async Task<IActionResult> Fulfilment(long id, FulfilmentRequest request)
    {
        if (access.Can("SALES", "FULFIL"))
        {
            return Ok(new
            {
                id = await repository.AddFulfilmentAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("sales/{id:long}/trade-ins")]
    public async Task<IActionResult> TradeIn(long id, TradeInRequest request)
    {
        if (access.Can("SALES", "FULFIL"))
        {
            return Ok(new
            {
                id = await repository.AddTradeInAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpGet("purchases/{id:long}")]
    public async Task<IActionResult> Purchase(long id)
    {
        if (access.Can("PURCHASES", "VIEW"))
        {
            return Ok(await repository.PurchaseAsync(id));
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("purchases/{id:long}/payments")]
    public async Task<IActionResult> SupplierPayment(long id, MoneyReceiptRequest request)
    {
        if (access.Can("PURCHASES", "PAY"))
        {
            return Ok(new
            {
                id = await repository.AddSupplierPaymentAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("purchases/{id:long}/returns")]
    public async Task<IActionResult> PurchaseReturn(long id, PurchaseReturnRequest request)
    {
        if (access.Can("PURCHASES", "RETURN"))
        {
            return Ok(new
            {
                id = await repository.ReturnPurchaseAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("stock-counts")]
    public async Task<IActionResult> StockCount(StockCountRequest request)
    {
        if (access.Can("STOCK_ADJUSTMENT", "ADJUST"))
        {
            return Ok(new
            {
                id = await repository.PostStockCountAsync(request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpGet("service/{id:long}")]
    public async Task<IActionResult> Service(long id)
    {
        if (access.Can("SERVICE", "VIEW"))
        {
            return Ok(await repository.ServiceAsync(id));
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("service/{id:long}/payments")]
    public async Task<IActionResult> ServicePayment(long id, MoneyReceiptRequest request)
    {
        if (access.Can("SERVICE", "COLLECT"))
        {
            return Ok(new
            {
                id = await repository.AddServicePaymentAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpGet("warranty-claims/{id:long}/rmas")]
    public async Task<IActionResult> Rmas(long id)
    {
        if (access.Can("WARRANTY_CLAIMS", "VIEW"))
        {
            return Ok(await repository.WarrantyRmasAsync(id));
        }
        else
        {
            return Forbid();
        }
    }

    [HttpPost("warranty-claims/{id:long}/rmas")]
    public async Task<IActionResult> Rma(long id, WarrantyRmaRequest request)
    {
        if (access.Can("WARRANTY_CLAIMS", "UPDATE"))
        {
            return Ok(new
            {
                id = await repository.AddWarrantyRmaAsync(id, request, UserId())
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpGet("reports/product-sales")]
    public async Task<IActionResult> ProductSales(DateTime from, DateTime to)
    {
        if (access.Can("REPORTS", "VIEW") && access.Can("SALES", "VIEW"))
        {
            return Ok(await repository.ProductSalesAsync(from, to));
        }
        else
        {
            return Forbid();
        }
    }

    private int UserId()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0)
        {
            return id;
        }
        else
        {
            throw new UnauthorizedAccessException();
        }
    }
}
