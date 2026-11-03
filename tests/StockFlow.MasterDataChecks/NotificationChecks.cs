using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

static class NotificationChecks
{
    public static async Task Run(Factory factory)
    {
        var repository=new FakeNotifications();
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<INotificationRepository>();s.AddSingleton<INotificationRepository>(repository);}));
        using var anonymous=app.CreateClient(new(){AllowAutoRedirect=false});
        Check((int)(await anonymous.GetAsync("/api/notifications")).StatusCode==401,"anonymous feed denied");
        using var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var settings=await client.GetStringAsync("/Configuration/Notifications");
        Check(settings.Contains("notification-bell")&&settings.Contains("Input.Rules[0].Title")&&settings.Contains("Notification settings")&&settings.Contains("Delivery history"),"configuration renders settings and history");
        var html=await client.GetStringAsync("/Notifications");
        Check(html.Contains("data-sf-pagination-link")&&html.Contains("data-size=\"10\""),"history exposes shared pagination with default page size");
        var paged=await client.GetStringAsync("/Notifications?PageNumber=999&PageSize=50");
        Check(paged.Contains("data-page=\"1\"")&&paged.Contains("data-size=\"50\""),"history honors page size and clamps an out-of-range page");
        Check(paged.Contains("name=\"PageSize\" value=\"50\""),"mark-read forms preserve pagination context");
        Check(html.Contains("&lt;script&gt;")&&!html.Contains("<script>alert(1)</script>"),"notification text safely encoded");
        Check(html.Contains("/Sales/Details/1")&&html.Contains("Mark all as read"),"history shows document links and read controls");
        var output=Path.Combine(AppContext.BaseDirectory,"notification-ui-fixtures");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"settings.html"),settings);await File.WriteAllTextAsync(Path.Combine(output,"history.html"),html);
        Check((int)(await client.PostAsJsonAsync("/api/notifications/read",new {id=1})).StatusCode==204&&repository.Reads==1,"read API accepts CSRF-protected request");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Check((int)(await client.PostAsJsonAsync("/api/notifications/read",new {id=1})).StatusCode==400,"read API requires CSRF");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token["requestToken"]!.GetValue<string>());
        client.DefaultRequestHeaders.Add("Test-Deny-Action","COMPANY_SETTINGS:EDIT");
        Check((int)(await client.GetAsync("/Configuration/Notifications")).StatusCode==200,"view-only configuration allowed");
        Check((await client.PostAsync("/Configuration/Notifications",new FormUrlEncodedContent([]))).Headers.Location?.OriginalString=="/Account/AccessDenied"&&repository.Saves==0,"view-only staff cannot save settings");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny","COMPANY_SETTINGS");
        Check((await client.GetAsync("/Configuration/Notifications")).Headers.Location?.OriginalString=="/Account/AccessDenied","configuration permission enforced");
        Check((int)(await client.GetAsync("/api/notifications")).StatusCode==200,"own feed does not require configuration access");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        var fields=new Dictionary<string,string>{{"Input.Enabled","true"},{"Input.RecipientIds","1"}};
        var rules=NotificationRule.Defaults();for(var i=0;i<rules.Count;i++)
        {
            fields[$"Input.Rules[{i}].Event"]=rules[i].Event;fields[$"Input.Rules[{i}].Title"]=rules[i].Title;
            fields[$"Input.Rules[{i}].Body"]=rules[i].Body;fields[$"Input.Rules[{i}].Enabled"]="true";
        }
        var save=await client.PostAsync("/Configuration/Notifications",new FormUrlEncodedContent(fields));
        Check((int)save.StatusCode==302&&repository.Saves==1,"settings save validates independently of consent form");
        fields["Input.RecipientIds"]="invalid";
        Check((int)(await client.PostAsync("/Configuration/Notifications",new FormUrlEncodedContent(fields))).StatusCode==200&&repository.Saves==1,"malformed settings do not save");
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Consent",new FormUrlEncodedContent(new Dictionary<string,string>{{"Consent.CustomerId","1"},{"Consent.Channel","email"},{"Consent.Allowed","true"},{"Consent.Evidence","Written consent today"}}))).StatusCode==302&&repository.Consents==1,"consent form saves independently of settings");
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Consent",new FormUrlEncodedContent(new Dictionary<string,string>{{"Consent.CustomerId","bad"},{"Consent.Channel","email"},{"Consent.Evidence","Written consent today"}}))).StatusCode==200&&repository.Consents==1,"malformed customer ID does not save consent");
        var deliveryFields=new Dictionary<string,string>{{"Delivery.DigestEnabled","true"},{"Delivery.DigestHour","9"},{"Delivery.TimeZone","Asia/Kolkata"},{"Delivery.EmailEnabled","true"},{"Delivery.SenderEmail","shop@example.test"},{"Delivery.SenderName","Shop"},{"Delivery.DailyLimit","250"},{"Delivery.AdministratorEmails",""}};
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Delivery",new FormUrlEncodedContent(deliveryFields))).StatusCode==302&&repository.DeliverySaves==1,"delivery form saves independently with blank administrator emails");
        deliveryFields["Delivery.AdministratorEmails"]="bad-email";
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Delivery",new FormUrlEncodedContent(deliveryFields))).StatusCode==200&&repository.DeliverySaves==1,"optional administrator emails still reject invalid addresses");
        deliveryFields.Remove("Delivery.AdministratorEmails");
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Delivery",new FormUrlEncodedContent(deliveryFields))).StatusCode==302&&repository.DeliverySaves==2,"omitted administrator emails do not block email settings");
        deliveryFields["Delivery.DailyLimit"]="invalid";
        Check((int)(await client.PostAsync("/Configuration/Notifications?handler=Delivery",new FormUrlEncodedContent(deliveryFields))).StatusCode==200&&repository.DeliverySaves==2,"invalid quota rejected");
        Check((int)(await anonymous.PostAsJsonAsync("/api/notification-jobs/failure",new{tenantId=1,runId=Guid.NewGuid(),job="backup"})).StatusCode==401,"backup failure endpoint requires secret authentication");
        repository.Ready=false;
        Check((await client.GetStringAsync("/Configuration/Notifications")).Contains("migration 039"),"pending migration shown clearly");
        Check((int)(await client.GetAsync("/api/notifications")).StatusCode==503,"pending schema feed fails gracefully");
        Console.WriteLine("Notification fixtures: "+output);
    }
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name); }
    internal sealed class FakeNotifications : INotificationRepository
    {
        public bool Ready=true;public int Reads;public int Saves;public int Consents;public int DeliverySaves;
        public Task<bool> DeliveryReadyAsync()=>Task.FromResult(Ready);
        public Task<bool> TestEmailReadyAsync()=>Task.FromResult(Ready);
        public Task<NotificationDeliverySettings> DeliverySettingsAsync()=>Task.FromResult(new NotificationDeliverySettings());
        public Task SaveDeliverySettingsAsync(NotificationDeliverySettings settings){DeliverySaves++;return Task.CompletedTask;}
        public Task<bool> ReadyAsync()=>Task.FromResult(Ready);
        public Task<NotificationSettings> SettingsAsync()=>Task.FromResult(new NotificationSettings());
        public Task SaveSettingsAsync(NotificationSettings settings){Saves++;return Task.CompletedTask;}
        public Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync()=>Task.FromResult<IReadOnlyList<NotificationRecipient>>([new(1,"Owner")]);
        public Task<IReadOnlyList<ConsentCustomer>> CustomersAsync(string term)=>Task.FromResult<IReadOnlyList<ConsentCustomer>>([]);
        public Task SaveConsentAsync(CustomerConsent consent){Consents++;return Task.CompletedTask;}
        public Task<NotificationFeed> FeedAsync(int page)=>Task.FromResult(new NotificationFeed(1,1,[new(1,"Invoice completed","<script>alert(1)</script>","/Sales/Details/1",DateTime.UtcNow,null)]));
        public Task MarkReadAsync(long? id){Reads++;return Task.CompletedTask;}
        public Task<IReadOnlyList<NotificationJob>> JobsAsync()=>Task.FromResult<IReadOnlyList<NotificationJob>>([]);
    }
}
