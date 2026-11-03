using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly ISecurityRepository repository;
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public AdminController(ISecurityRepository repository, PermissionContext access)
    {
        this.repository = repository;
        this.access = access;
    }

    int Actor => access.User!.UserId;

    [HttpGet("users")]
    public async Task<IActionResult> Users(string? term, int offset = 0)
    {
        return Ok(await repository.GetUsersAsync(term, offset));
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> UserById(int id)
    {
        if (await repository.GetUserAsync(id) is { } user)
        {
            return Ok(user);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPost("users")]
    public async Task<IActionResult> Create(SaveSecurityUserRequest r)
    {
        return Ok(new
        {
            id = await repository.SaveUserAsync(null, r, Actor)
        });
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> Update(int id, SaveSecurityUserRequest r)
    {
        return Ok(new
        {
            id = await repository.SaveUserAsync(id, r, Actor)
        });
    }

    [HttpPut("users/{id:int}/status")]
    public async Task<IActionResult> Status(int id, UserStatusUpdate r)
    {
        await repository.SetStatusAsync(id, r, Actor);
        return NoContent();
    }

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> Reset(int id, ResetPasswordRequest r)
    {
        await repository.ResetPasswordAsync(id, r, Actor);
        return NoContent();
    }

    [HttpGet("users/{id:int}/permissions")]
    public async Task<IActionResult> Permissions(int id)
    {
        if (await repository.GetUserAsync(id) is { } user)
        {
            return Ok(new
            {
                user.SecurityVersion,
                user.Permissions
            });
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPut("users/{id:int}/permissions")]
    public async Task<IActionResult> Permissions(int id, PermissionUpdate r)
    {
        await repository.SetPermissionsAsync(id, r, Actor);
        return NoContent();
    }

    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        return Ok(await repository.GetModulesAsync());
    }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        return Ok(await repository.GetRolesAsync());
    }

    [HttpGet("roles/{id:int}/permissions")]
    public async Task<IActionResult> RolePermissions(int id)
    {
        return Ok(await repository.GetRolePermissionsAsync(id));
    }

    [HttpPut("roles/{id:int}/permissions")]
    public async Task<IActionResult> RolePermissions(int id, List<ModulePermission> r)
    {
        await repository.SetRolePermissionsAsync(id, r, Actor);
        return NoContent();
    }

    [HttpGet("auditlogs")]
    public async Task<IActionResult> Audit([FromQuery] AuditSearch s)
    {
        return Ok(await repository.GetAuditAsync(s));
    }

    [HttpGet("auditlogs/export")]
    public async Task<IActionResult> ExportAudit([FromQuery] AuditSearch s)
    {
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await repository.GetAuditAsync(s, 1000)),
"application/json",
"stockflow-audit.json");
    }
}
