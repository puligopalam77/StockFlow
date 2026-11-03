using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StockFlow.Web.Controllers;

[ApiController,Route("api/notification-jobs"),AllowAnonymous,IgnoreAntiforgeryToken]
public sealed class NotificationJobsController(IConfiguration configuration) : ControllerBase
{
    public sealed record Failure(long TenantId,Guid RunId,string Job);
    [HttpPost("failure")]
    public async Task<IActionResult> Failed(Failure input,CancellationToken token)
    {
        if(input.TenantId<=0 || input.RunId==Guid.Empty || input.Job!="backup") return BadRequest();
        var expected=configuration[$"Notifications:Jobs:Tenants:{input.TenantId}:Token"];
        var supplied=Request.Headers.Authorization.ToString();
        if(string.IsNullOrEmpty(expected) || expected.Length<32 || !supplied.StartsWith("Bearer ",StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied[7..])))) return Unauthorized();
        var cs=new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("NotificationWorker")??configuration.GetConnectionString("DefaultConnection"))
        {Options="-c role=stockflow_notification_worker -c statement_timeout=10000",MaxPoolSize=2};
        await using var c=new NpgsqlConnection(cs.ConnectionString);
        await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_notification_failure(@TenantId,@RunId,@Job)",input,commandTimeout:15,cancellationToken:token));
        return Accepted();
    }
}
