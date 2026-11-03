using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StockFlow.Web.Notifications;

public sealed record EmailJob(long Id,long TenantId,Guid Token,Guid RequestKey,string Recipient,string Title,string Body,string SenderEmail,string SenderName);
public sealed record EmailResult(string Status,string? ProviderId=null,string? Diagnostic=null,int DelaySeconds=60);
public sealed record SenderVerificationResult(bool Verified,string Message);
public interface INotificationEmailProvider
{
    Task<bool> VerifySenderAsync(long tenant,string sender,CancellationToken token);
    async Task<SenderVerificationResult> VerifySenderDetailsAsync(long tenant,string sender,CancellationToken token)
        => await VerifySenderAsync(tenant,sender,token)
            ? new(true,"Brevo confirmed this sender is active.")
            : new(false,"Sender verification failed. Check the saved sender in Brevo.");
    Task<EmailResult> SendAsync(EmailJob job,CancellationToken token);
}
public sealed class BrevoNotificationEmailProvider(HttpClient client,IConfiguration configuration) : INotificationEmailProvider
{
    private string? Key(long tenant)=>configuration[$"Notifications:Email:Tenants:{tenant}:ApiKey"];
    public async Task<bool> VerifySenderAsync(long tenant,string sender,CancellationToken token)
        => (await VerifySenderDetailsAsync(tenant,sender,token)).Verified;
    public async Task<SenderVerificationResult> VerifySenderDetailsAsync(long tenant,string sender,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(sender)) return new(false,"Save a sender email in Channel settings and preferences before verifying.");
        if(string.IsNullOrWhiteSpace(Key(tenant))) return new(false,$"This shop's Brevo API key is missing from server configuration. Configure Notifications:Email:Tenants:{tenant}:ApiKey, then restart StockFlow. Use an API key, not an SMTP key.");
        try
        {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.brevo.com/v3/senders");
        request.Headers.Add("api-key",Key(tenant));
        using var response=await client.SendAsync(request,token);
        if(response.StatusCode==HttpStatusCode.Unauthorized) return new(false,"Brevo rejected this shop's API key (HTTP 401). Configure a valid Brevo API key on the server and restart StockFlow.");
        if(response.StatusCode==HttpStatusCode.Forbidden) return new(false,"Brevo denied sender access (HTTP 403). Check the API key's access and account/IP restrictions in Brevo.");
        if(response.StatusCode==HttpStatusCode.TooManyRequests) return new(false,"Brevo's request limit was reached. Wait before verifying again.");
        if(!response.IsSuccessStatusCode) return new(false,$"Brevo sender verification returned HTTP {(int)response.StatusCode}. Check provider availability and account configuration, then retry.");
        using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if(data.RootElement.ValueKind!=JsonValueKind.Object || !data.RootElement.TryGetProperty("senders",out var senders) || senders.ValueKind!=JsonValueKind.Array)
            return new(false,"Brevo returned an unreadable sender list. Try verification again.");
        foreach(var entry in senders.EnumerateArray())
        {
            if(entry.ValueKind!=JsonValueKind.Object || !entry.TryGetProperty("email",out var email) || email.ValueKind!=JsonValueKind.String || !string.Equals(email.GetString(),sender.Trim(),StringComparison.OrdinalIgnoreCase)) continue;
            return entry.TryGetProperty("active",out var active) && active.ValueKind==JsonValueKind.True
                ? new(true,"Brevo confirmed this sender is active.")
                : new(false,"The saved sender exists in Brevo but is not active. Complete sender verification in Brevo, then click Verify saved sender again.");
        }
        return new(false,"The saved sender email was not found in this Brevo account. Add and verify the same address in Brevo, or save an existing verified sender address here.");
        }
        catch(OperationCanceledException) { return new(false,"Brevo sender verification timed out or was canceled. Check the connection and retry."); }
        catch(HttpRequestException) { return new(false,"StockFlow could not connect to Brevo. Check the server's internet connection and retry."); }
        catch(JsonException) { return new(false,"Brevo returned an unreadable sender list. Try verification again."); }
    }
    public async Task<EmailResult> SendAsync(EmailJob job,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(Key(job.TenantId))) return new("FAILED",Diagnostic:"Configure this shop's email API key on the server.");
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key",Key(job.TenantId));
        request.Content=JsonContent.Create(new { sender=new {email=job.SenderEmail,name=job.SenderName},
            to=new[]{new{email=job.Recipient}},subject=job.Title,textContent=job.Body,
            headers=new Dictionary<string,string>{{"Idempotency-Key",job.RequestKey.ToString()}} });
        try
        {
            using var response=await client.SendAsync(request,token);
            if(response.StatusCode==HttpStatusCode.Created)
            {
                using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                return data.RootElement.TryGetProperty("messageId",out var id) ? new("ACCEPTED",id.GetString()) : new("UNKNOWN",Diagnostic:"Provider accepted request without a message ID; confirm delivery.");
            }
            if(response.StatusCode==HttpStatusCode.TooManyRequests)
                return new("RETRY",Diagnostic:"Provider rate limit; delivery deferred.",DelaySeconds:(int)Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds??300,60,86400));
            if((int)response.StatusCode>=500) return new("UNKNOWN",Diagnostic:"Provider error; confirm acceptance before resending.");
            return new("FAILED",Diagnostic:$"Provider rejected request (HTTP {(int)response.StatusCode}). Check sender and account configuration.");
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or JsonException)
        { return new("UNKNOWN",Diagnostic:"Connection interrupted or response unreadable; confirm provider status before resending."); }
    }
}
