using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Web.Notifications;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[Authorize,ApiController,Route("api/configuration/whatsapp"),RequestSizeLimit(16*1024)]
public sealed class WhatsAppController(WhatsAppService service,BusinessScope scope,PermissionContext access,IOptions<WhatsAppOptions> options):ControllerBase
{
    [HttpPost("signup")]
    public async Task<IActionResult> Begin()
    {
        if(!scope.IsOwner || !access.Can("COMPANY_SETTINGS","EDIT")) return Forbid();
        if(!await service.Ready()) return StatusCode(503,new{message="Apply migration 076 before connecting WhatsApp."});
        try {return Ok(new{ticket=await service.Begin(),options.Value.AppId,configurationId=options.Value.SignupConfigurationId,options.Value.GraphVersion,options.Value.CoexistenceEnabled});}
        catch(ValidationException e){return BadRequest(new{message=e.Message});}
    }
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(WhatsAppSignupCompletion input,CancellationToken token)
    {
        if(!scope.IsOwner || !access.Can("COMPANY_SETTINGS","EDIT")) return Forbid();
        if(!await service.Ready()) return StatusCode(503);
        try {await service.Complete(input,token);return Ok(new{message="WhatsApp connected. Complete billing and template setup before enabling notifications."});}
        catch(ValidationException e){return BadRequest(new{message=e.Message});}
        catch(Exception e) when(e is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {return BadRequest(new{message="Meta onboarding could not be completed. Check Meta setup and start Connect WhatsApp again."});}
        catch(PostgresException e) when(e.SqlState is "23505" or "23514" or "42501")
        {return BadRequest(new{message="This WhatsApp account cannot be assigned to the selected shop. Check account ownership and shop access."});}
    }
}

[AllowAnonymous,IgnoreAntiforgeryToken,ApiController,Route("api/whatsapp/webhook")]
public sealed class WhatsAppWebhookController(WhatsAppWorkerStore store,IOptions<WhatsAppOptions> options):ControllerBase
{
    [HttpGet]
    public IActionResult Verify([FromQuery(Name="hub.mode")]string? mode,[FromQuery(Name="hub.verify_token")]string? token,[FromQuery(Name="hub.challenge")]string? challenge)
    {
        var expected=options.Value.WebhookVerifyToken;
        if(expected.Length<32 || mode!="subscribe" || token is null || challenge?.Length>1000) return Unauthorized();
        if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected)),System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))) return Unauthorized();
        return Content(challenge??"","text/plain");
    }
    [HttpPost,RequestSizeLimit(1024*1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if(Request.ContentLength>1024*1024) return StatusCode(413);
        using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await Request.Body.ReadAsync(buffer,ct))>0){if(bytes.Length+read>1024*1024)return StatusCode(413);await bytes.WriteAsync(buffer.AsMemory(0,read),ct);}
        if(!WhatsAppWebhookSecurity.Verify(bytes.GetBuffer().AsSpan(0,(int)bytes.Length),Request.Headers["X-Hub-Signature-256"].ToString(),options.Value.AppSecret)) return Unauthorized();
        JsonDocument document;
        try {document=JsonDocument.Parse(bytes.GetBuffer().AsMemory(0,(int)bytes.Length),new JsonDocumentOptions{MaxDepth=32});}
        catch(JsonException){return BadRequest();}
        using(document)
        {
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object) return BadRequest();
            if(String(root,"object")!="whatsapp_business_account") return Ok();
            if(!root.TryGetProperty("entry",out var entries) || entries.ValueKind!=JsonValueKind.Array) return BadRequest();
            await using var c=store.OpenConnection();await c.OpenAsync(ct);
            if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT to_regprocedure('public.sf_wa_status(text,text,text,uuid)') IS NOT NULL",cancellationToken:ct))) return StatusCode(503);
            await using var tx=await c.BeginTransactionAsync(ct);
            foreach(var entry in entries.EnumerateArray())
            {
                if(!entry.TryGetProperty("changes",out var changes) || changes.ValueKind!=JsonValueKind.Array) continue;
                foreach(var change in changes.EnumerateArray())
                {
                    if(!change.TryGetProperty("field",out var field) || field.GetString()!="messages" || !change.TryGetProperty("value",out var value) || !value.TryGetProperty("metadata",out var metadata)) continue;
                    var phone=String(metadata,"phone_number_id");if(phone.Length is <5 or >30) continue;
                    if(value.TryGetProperty("statuses",out var statuses) && statuses.ValueKind==JsonValueKind.Array)
                    foreach(var status in statuses.EnumerateArray())
                    {
                        var state=String(status,"status") switch{"sent"=>"ACCEPTED","delivered"=>"DELIVERED","read"=>"READ","failed"=>"FAILED",_=>null};if(state is null)continue;
                        var message=String(status,"id");if(message.Length is 0 or >250)continue;
                        Guid? correlation=Guid.TryParse(String(status,"biz_opaque_callback_data"),out var id)?id:null;
                        await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_status(@Phone,@Message,@State,@Correlation)",new{Phone=phone,Message=message,State=state,Correlation=correlation},tx,cancellationToken:ct));
                    }
                    if(value.TryGetProperty("messages",out var messages) && messages.ValueKind==JsonValueKind.Array)
                    foreach(var message in messages.EnumerateArray())
                    {
                        if(String(message,"type")!="text" || !message.TryGetProperty("text",out var text) || !WhatsAppWebhookSecurity.IsOptOut(String(text,"body"))) continue;
                        await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_optout(@Phone,@Sender,@Message)",new{Phone=phone,Sender=String(message,"from"),Message=String(message,"id")},tx,cancellationToken:ct));
                    }
                }
            }
            await tx.CommitAsync(ct);
        }
        return Ok();
    }
    private static string String(JsonElement element,string key)=>element.ValueKind==JsonValueKind.Object && element.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
}
