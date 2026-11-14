using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;
[ApiController,Authorize,Route("api/admin/roles")]
public sealed class RoleManagementController(ISecurityRepository repository,PermissionContext access):ControllerBase
{
 int Actor=>access.User!.UserId;
 [HttpGet("templates")] public async Task<IActionResult> Templates()=>Ok(await repository.RoleTemplatesAsync(Actor));
 [HttpGet("templates/{key}")] public async Task<IActionResult> Template(string key)=>Ok(await repository.RoleTemplateAsync(key,Actor));
 [HttpPost("templates/save")] public async Task<IActionResult> SaveTemplate(SaveRoleTemplateRequest r){try{return Ok(new{templateKey=await repository.SaveRoleTemplateAsync(r,Actor)});}catch(System.Data.DBConcurrencyException e){return Conflict(new{message=e.Message});}}
 [HttpGet("directory")] public async Task<IActionResult> Directory()=>Ok(await repository.RoleDirectoryAsync(Actor));
 [HttpGet("{id:int}/details")] public async Task<IActionResult> Details(int id)=>Ok(await repository.RoleDetailsAsync(id,Actor));
 [HttpPost("save")] public async Task<IActionResult> Save(SaveRoleRequest r){try{return Ok(new{roleId=await repository.SaveRoleAsync(r,Actor)});}catch(System.Data.DBConcurrencyException e){return Conflict(new{message=e.Message});}}
 [HttpPut("{id:int}/status")] public async Task<IActionResult> Status(int id,RoleStatusRequest r){try{await repository.SetRoleStatusAsync(id,r,Actor);return NoContent();}catch(System.Data.DBConcurrencyException e){return Conflict(new{message=e.Message});}}
}
