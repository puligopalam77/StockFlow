using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/bankaccounts")]
public sealed class BankAccountsController(IBankAccountsRepository repository,IOptions<BankAccountsOptions> options,BusinessScope scope,BankAccountProtection protection):ControllerBase
{
    [HttpGet]
    [HttpGet("{accountId:guid}")]
    public async Task<IActionResult> Read(Guid? accountId,DateTime? date,int page=1,string? search=null)
    {
        if(!options.Value.Enabled)return NotFound();
        return Ok(await repository.ReadAsync(accountId,date??DateTime.Today,page,search));
    }
    [HttpPost("{accountId:guid}/{operation}")]
    public async Task<IActionResult> Write(Guid accountId,string operation,BankOperation request)
    {
        var action=operation;
        if(!options.Value.Enabled)return NotFound();
        if(action is not("create" or "edit" or "assign" or "default" or "status" or "open" or "link" or "verify" or "reverse"))return NotFound();
        if(request.Body.ValueKind!=JsonValueKind.Object)throw new ValidationException("Enter valid bank details.");
        var body=JsonNode.Parse(request.Body.GetRawText())!.AsObject();
        if(action is "create" or "edit")protection.Protect(body,scope.TenantId,accountId);
        return Ok(await repository.WriteAsync(action,accountId,request.RequestId,JsonSerializer.SerializeToElement(body)));
    }
}
