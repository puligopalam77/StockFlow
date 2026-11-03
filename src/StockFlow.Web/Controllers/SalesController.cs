using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/sales"), Authorize]
public class SalesController : ControllerBase
{
    private readonly ISalesService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public SalesController(ISalesService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(string? term, int? customerId, int offset = 0, DateTime? from = null, DateTime? to = null)
    {
        return Ok(await service.GetAllAsync(term, customerId, offset, from, to));
    }

    [HttpGet("gst-context")]
    public async Task<IActionResult> GstContext([FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        await using var c=factory.CreateConnection();
        if(!await Dapper.SqlMapper.ExecuteScalarAsync<bool>(c,"SELECT to_regprocedure('public.sf_gst_sale_context()') IS NOT NULL")) return Ok(new{mapped=false});
        try{return Content((await Dapper.SqlMapper.ExecuteScalarAsync<string>(c,"SELECT public.sf_gst_sale_context()::text"))!,"application/json");}
        catch(Npgsql.PostgresException ex)when(ex.SqlState==Npgsql.PostgresErrorCodes.CheckViolation){return BadRequest(new{message=ex.MessageText});}
    }

    [HttpGet("manual-tax-rules")]
    public async Task<IActionResult> ManualTaxRules([FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        if(!access.Can("SALES","CREATE") || !access.Can("SALES","MANUAL_ITEM")) return Forbid();
        await using var c=factory.CreateConnection();
        if(!await Dapper.SqlMapper.ExecuteScalarAsync<bool>(c,"SELECT to_regprocedure('public.sf_pos_manual_tax(bigint)') IS NOT NULL"))
            return BadRequest(new {message="Quick Add Item requires database migration 074."});
        var rows=await Dapper.SqlMapper.QueryAsync<string>(c,"SELECT value::text FROM public.sf_pos_manual_tax() value");
        return Content("["+string.Join(",",rows)+"]","application/json");
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        if (await service.GetByIdAsync(id) is { } sale)
        {
            return Ok(sale);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(string? term, int? customerId, int offset = 0, int limit = 10, DateTime? from = null, DateTime? to = null, string? status = null, string sort = "date", bool ascending = false, string? paymentStatus = null, string? paymentMethod = null)
        => Ok(await service.GetListAsync(term,customerId,offset,limit,from,to,status,sort,ascending,paymentStatus,paymentMethod));

    private void SetPermissions(CreateSaleRequest request)
    {
        request.CanOverridePrice = access.Can("SALES", "PRICE_OVERRIDE");
        request.CanDiscount = access.Can("SALES", "DISCOUNT");
        request.CanApproveDiscount = access.Can("SALES", "DISCOUNT_APPROVE");
        request.CanTradeIn = access.Can("SALES", "FULFIL");
    }

    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CreateSaleRequest request)
    {
        SetPermissions(request);
        return Ok(await service.QuoteAsync(request));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateSaleRequest request)
    {
        request.DraftEditorId=Guid.TryParse(Request.Headers["X-Draft-Editor"],out var editor)?editor:null;
        request.DraftRevision=long.TryParse(Request.Headers["X-Draft-Revision"],out var revision)?revision:null;
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        SetPermissions(request);
        var id = await service.CreateSaleAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, new
        {
            saleId = id
        });
    }

    [HttpPost("recover")]
    public async Task<IActionResult> Recover(CreateSaleRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        var id = await service.RecoverCompletedAsync(request, userId);
        if (id.HasValue)
        {
            return Ok(new
            {
                saleId = id.Value
            });
        }
        else
        {
            return NotFound(new
            {
                message = "No matching completed invoice was found for this account and location. Ask your administrator to reconcile the previous sale before re-entering it."
            });
        }
    }
}
