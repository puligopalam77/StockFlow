using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[Authorize, ApiController, Route("api/notifications")]
public sealed class NotificationsController(INotificationRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int page=1)
    {
        if (!await repository.ReadyAsync()) return StatusCode(503,new { message="Notifications are awaiting database setup." });
        return Ok(await repository.FeedAsync(page));
    }
    [HttpPost("read")]
    public async Task<IActionResult> Read([FromBody] ReadRequest request)
    {
        if (request.Id is <= 0) return BadRequest();
        if (!await repository.ReadyAsync()) return StatusCode(503);
        await repository.MarkReadAsync(request.Id); return NoContent();
    }
    public sealed record ReadRequest(long? Id);
}
