using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Web.Operations;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/database/operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationalReadinessController(DbConnectionFactory connections, BusinessScope scope,
    PermissionContext access, IConfiguration configuration, IWebHostEnvironment environment,
    ILogger<OperationalReadinessController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        if (!access.Can("SECURITY") || !scope.CanReviewStores || scope.TenantId <= 0) return StatusCode(403);
        var now = DateTimeOffset.UtcNow;
        // Tenant mapping is server configuration, never a caller-supplied path or tenant ID.
        var file = configuration[$"Operations:Tenants:{scope.TenantId}:RecoveryEvidenceFile"];
        if (!string.IsNullOrWhiteSpace(file))
        {
            file = Path.GetFullPath(file, environment.ContentRootPath);
            var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
            if (file.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Problem("Recovery evidence must be stored outside the public web directory.", statusCode: 503);
        }
        var recovery = RecoveryEvidence.Read(file, now,
            Math.Clamp(configuration.GetValue("Operations:BackupMaximumAgeHours", 26), 1, 8760));
        object migrations;
        var connected = false;
        try
        {
            using var c = connections.CreateConnection();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            // Capability probes, not a claim that every migration statement/checksum was applied.
            migrations = await c.QuerySingleAsync(new CommandDefinition("""
                SELECT to_regprocedure('public.sf_billing_policy()') IS NOT NULL AS "BillingPolicy082",
                    to_regprocedure('public.sf_quotation_write(uuid,bigint,uuid,text,jsonb)') IS NOT NULL AS "Quotations083"
                """, commandTimeout: 5, cancellationToken: timeout.Token));
            connected = true;
        }
        catch (Exception ex)
        {
            migrations = new { Status = "Unknown" };
            logger.LogWarning("Operational readiness database probe failed: {FailureType}; tenant {TenantId}; request {RequestId}",
                ex.GetType().Name, scope.TenantId, HttpContext.TraceIdentifier);
        }
        return StatusCode(connected ? 200 : 503, new
        {
            observedUtc = now, scope.TenantId, scope.StoreId, scope.LocationId,
            database = connected ? "Ready" : "Unavailable", migrationCapabilities = migrations,
            recovery,
            recoveryScope = "Database archive only. Application assets, secrets, offsite retention and business reconciliation require separate evidence."
        });
    }
}
