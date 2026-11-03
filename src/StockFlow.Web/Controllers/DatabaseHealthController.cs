using Dapper;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Web.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
[Route("api/database")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DatabaseHealthController : ControllerBase
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly StockFlow.Web.Security.PermissionContext access;
    private readonly StockFlow.Application.Common.BusinessScope scope;
    private readonly ILogger<DatabaseHealthController> logger;
    public DatabaseHealthController(DbConnectionFactory connectionFactory, StockFlow.Web.Security.PermissionContext access,
        StockFlow.Application.Common.BusinessScope scope, ILogger<DatabaseHealthController> logger)
    {
        _connectionFactory = connectionFactory;
        this.access = access;
        this.scope = scope;
        this.logger = logger;
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        if (!access.Can("SECURITY") || !scope.CanReviewStores) return StatusCode(403);
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1;", commandTimeout: 5, cancellationToken: timeout.Token));
            return Ok(new
            {
                status = "Connected",
                result
            });
        }
        catch (Exception ex)
        {
            // A provider exception can include server names or credentials. Log only its type.
            logger.LogWarning("Database readiness failed: {FailureType}; request {RequestId}", ex.GetType().Name, HttpContext.TraceIdentifier);
            return StatusCode(503, new
            {
                status = "Failed",
                error = "Unable to connect to the database."
            });
        }
    }
}
