using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Products;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/receiving/unit-identifiers")]
public sealed class UnitIdentifiersController(DbConnectionFactory factory,PermissionContext access) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Reserve(UnitIdentifierReservation request)
    {
        if(!access.Can("PURCHASES","APPROVE") && !access.Can("STOCK_INFLOW","RECEIVE") && !access.Can("STOCK_ADJUSTMENT","ADJUST")) return Forbid();
        return Ok(await new UnitIdentifierAllocator(factory).ReserveAsync(request));
    }
}
