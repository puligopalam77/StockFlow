using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace StockFlow.Web.Notifications;

public sealed record MetaWhatsAppAccount(string Token,string DisplayNumber,string BusinessName);
public sealed record MetaWhatsAppTemplate(string Id,string Name,string Language,string Body,string Status);
public sealed class MetaWhatsAppClient(HttpClient client,IOptions<WhatsAppOptions> options)
{
    public const string InvoiceBody="Your invoice {{1}} for INR {{2}} from {{3}} is ready. Thank you. Reply STOP to stop notifications.";
    public const string PaymentBody="Payment {{1}} of INR {{2}} was recorded for invoice {{3}}. Thank you. Reply STOP to stop notifications.";
    public static string TemplateName(string kind)=>kind=="invoice.completed"?"stockflow_invoice_v1":"stockflow_payment_v1";
    public static string TemplateBody(string kind)=>kind=="invoice.completed"?InvoiceBody:PaymentBody;
    private WhatsAppOptions Settings=>options.Value;
    private string Url(string path)=>$"https://graph.facebook.com/{Settings.GraphVersion}/{path}";
    private static string Id(string value)=>Regex.IsMatch(value,"^[0-9]{5,30}$")?value:throw new ValidationException("Invalid Meta account identifier.");
    private string Proof(string token)=>Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Settings.AppSecret),Encoding.UTF8.GetBytes(token)));
    private async Task<JsonDocument> Graph(HttpMethod method,string path,string token,object? payload,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,Url(path+(path.Contains('?')?"&":"?")+"appsecret_proof="+Proof(token)));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(payload is not null) request.Content=JsonContent.Create(payload);
        using var response=await client.SendAsync(request,ct);
        if(!response.IsSuccessStatusCode) throw new ValidationException($"Meta returned HTTP {(int)response.StatusCode}. Check account access, approvals and billing in WhatsApp Manager.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<MetaWhatsAppAccount> Connect(string code,string waba,string phone,string? registrationPin,bool businessApp,CancellationToken ct)
    {
        if(!Settings.Configured) throw new ValidationException("WhatsApp server setup is incomplete.");
        Id(waba);Id(phone);
        // This HttpClient has all loggers removed: OAuth query strings contain secrets.
        using var exchange=new HttpRequestMessage(HttpMethod.Get,Url("oauth/access_token")+"?client_id="+Id(Settings.AppId)+"&client_secret="+Uri.EscapeDataString(Settings.AppSecret)+"&code="+Uri.EscapeDataString(code));
        using var response=await client.SendAsync(exchange,ct);
        if(!response.IsSuccessStatusCode) throw new ValidationException("Meta could not exchange the signup code. Start Connect WhatsApp again.");
        using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token=data.RootElement.GetProperty("access_token").GetString();
        if(string.IsNullOrWhiteSpace(token)) throw new ValidationException("Meta returned no account authorization.");
        using var debug=await Graph(HttpMethod.Get,"debug_token?input_token="+Uri.EscapeDataString(token),Settings.AppId+"|"+Settings.AppSecret,null,ct);
        var details=debug.RootElement.GetProperty("data");
        if(!details.TryGetProperty("is_valid",out var valid) || !valid.GetBoolean() || details.GetProperty("app_id").GetString()!=Settings.AppId)
            throw new ValidationException("The returned authorization does not belong to StockFlow's Meta app.");
        var scopes=details.GetProperty("scopes").EnumerateArray().Select(x=>x.GetString()).ToHashSet();
        if(!scopes.Contains("whatsapp_business_management") || !scopes.Contains("whatsapp_business_messaging")) throw new ValidationException("Grant WhatsApp account management and messaging access during signup.");
        // Verify the browser's WABA/number pair against Meta using the exchanged authorization.
        using var phones=await Graph(HttpMethod.Get,Id(waba)+"/phone_numbers?fields=id,display_phone_number,verified_name,code_verification_status,platform_type&limit=100",token,null,ct);
        var selected=phones.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault(x=>x.GetProperty("id").GetString()==phone);
        if(selected.ValueKind==JsonValueKind.Undefined || !selected.TryGetProperty("code_verification_status",out var status) || status.GetString()!="VERIFIED")
            throw new ValidationException("Select a verified phone number belonging to the authorized WhatsApp account. Complete number setup in Meta first.");
        if(!businessApp && (registrationPin is not null || !selected.TryGetProperty("platform_type",out var platform) || platform.GetString()!="CLOUD_API"))
        {
            if(registrationPin is null || !Regex.IsMatch(registrationPin,"^[0-9]{6}$")) throw new ValidationException("This number needs Cloud API registration. Enter your six-digit registration PIN and restart signup.");
            using var registered=await Graph(HttpMethod.Post,Id(phone)+"/register",token,new{messaging_product="whatsapp",pin=registrationPin},ct);
            if(!registered.RootElement.TryGetProperty("success",out var registeredOk) || !registeredOk.GetBoolean()) throw new ValidationException("Meta did not confirm Cloud API registration.");
        }
        using var subscribed=await Graph(HttpMethod.Post,Id(waba)+"/subscribed_apps",token,new{},ct);
        if(!subscribed.RootElement.TryGetProperty("success",out var success) || !success.GetBoolean()) throw new ValidationException("Meta did not confirm webhook subscription.");
        return new(token,selected.GetProperty("display_phone_number").GetString()??phone,selected.GetProperty("verified_name").GetString()??"Business");
    }
    public async Task CreateTemplate(string waba,string token,string kind,CancellationToken ct)
    {
        using var response=await Graph(HttpMethod.Post,Id(waba)+"/message_templates",token,new{name=TemplateName(kind),language="en",category="UTILITY",
            components=new[]{new{type="BODY",text=TemplateBody(kind),example=new{body_text=new[]{kind=="invoice.completed"?new[]{"INV-1001","100.00","Sample Shop"}:new[]{"1001","100.00","INV-1001"}}}}}},ct);
    }
    public async Task<IReadOnlyList<MetaWhatsAppTemplate>> Templates(string waba,string token,CancellationToken ct)
    {
        var result=new List<MetaWhatsAppTemplate>();string? after=null;
        for(var page=0;page<10;page++)
        {
            using var response=await Graph(HttpMethod.Get,Id(waba)+"/message_templates?fields=id,name,language,status,category,components&limit=100"+(after is null?"":"&after="+Uri.EscapeDataString(after)),token,null,ct);
            foreach(var row in response.RootElement.GetProperty("data").EnumerateArray())
            {
                var name=row.GetProperty("name").GetString()??"";
                if(name!=TemplateName("invoice.completed") && name!=TemplateName("payment.recorded")) continue;
                var components=row.GetProperty("components").EnumerateArray().ToArray();
                var body=components.FirstOrDefault(c=>c.GetProperty("type").GetString()=="BODY");
                var content=body.ValueKind==JsonValueKind.Undefined?"":body.GetProperty("text").GetString()??"";
                var approved=row.GetProperty("status").GetString()=="APPROVED" && row.GetProperty("category").GetString()=="UTILITY"
                    && row.GetProperty("language").GetString()=="en" && components.All(c=>c.GetProperty("type").GetString() is "BODY" or "FOOTER")
                    && content==(name==TemplateName("invoice.completed")?InvoiceBody:PaymentBody);
                result.Add(new(row.GetProperty("id").GetString()!,name,row.GetProperty("language").GetString()!,content,approved?"APPROVED":"UNAVAILABLE"));
            }
            after=response.RootElement.TryGetProperty("paging",out var paging) && paging.TryGetProperty("next",out _) && paging.TryGetProperty("cursors",out var cursors) && cursors.TryGetProperty("after",out var cursor)?cursor.GetString():null;
            if(after is null) return result;
        }
        throw new ValidationException("Too many templates to inspect safely. Contact your administrator.");
    }
    public async Task<WhatsAppResult> Send(WhatsAppJob job,string token,CancellationToken ct)
    {
        using var values=JsonDocument.Parse(job.Parameters);
        using var request=new HttpRequestMessage(HttpMethod.Post,Url(Id(job.PhoneId)+"/messages?appsecret_proof="+Proof(token)));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Content=JsonContent.Create(new{messaging_product="whatsapp",recipient_type="individual",to=job.Recipient,type="template",biz_opaque_callback_data=job.RequestKey.ToString(),
            template=new{name=job.TemplateName,language=new{code=job.Language},components=new[]{new{type="body",parameters=values.RootElement.EnumerateArray().Select(v=>new{type="text",text=v.GetString()}).ToArray()}}}});
        try
        {
            using var response=await client.SendAsync(request,ct);
            if(response.IsSuccessStatusCode)
            {
                using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if(result.RootElement.TryGetProperty("messages",out var messages) && messages.GetArrayLength()>0 && messages[0].TryGetProperty("id",out var id) && !string.IsNullOrWhiteSpace(id.GetString())) return new("ACCEPTED",id.GetString());
                return new("UNKNOWN",Diagnostic:"Meta returned no message identifier. Reconcile before resending.");
            }
            if(response.StatusCode==HttpStatusCode.TooManyRequests) return new("RETRY",Diagnostic:"Meta rate limit; send deferred.",DelaySeconds:(int)Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds??300,60,86400));
            if((int)response.StatusCode>=500) return new("UNKNOWN",Diagnostic:"Meta server error; reconcile before resending.");
            return new("FAILED",Diagnostic:$"Meta rejected the message (HTTP {(int)response.StatusCode}). Review account, template and billing in WhatsApp Manager.");
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or JsonException)
        { return new("UNKNOWN",Diagnostic:"Send outcome uncertain. Do not resend without provider reconciliation."); }
    }
}
