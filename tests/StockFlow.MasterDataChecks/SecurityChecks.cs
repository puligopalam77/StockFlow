using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Web.Security;
namespace StockFlow.MasterDataChecks;
static class SecurityChecks
{
    public static async Task Fixtures(Factory factory)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/security-ui-fixtures");Directory.CreateDirectory(output);
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var page in new[]{"Users","Roles","AuditLogs"})await File.WriteAllTextAsync(Path.Combine(output,page+".html"),await client.GetStringAsync("/Admin/"+page));
        var modules=PermissionCatalog.Modules.Select((p,index)=>new StockFlow.Application.DTOs.Security.ModulePermission{ModuleId=index+1,ModuleCode=p.Key,ModuleName=p.Key,SupportedActions=p.Value,IsActive=true,Actions=[]}).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output,"Modules.json"),System.Text.Json.JsonSerializer.Serialize(modules,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        using(var anonymous=factory.CreateClient(new(){AllowAutoRedirect=false}))
        {
            var loginHtml=await anonymous.GetStringAsync("/Account/Login");
            Check(!loginHtml.Contains("Input.UseEmailOtp") && !loginHtml.Contains(">Business code<")&&System.Text.RegularExpressions.Regex.IsMatch(loginHtml,"<input(?=[^>]*type=\"hidden\")(?=[^>]*name=\"Input.TenantCode\")(?=[^>]*value=\"DEFAULT\")[^>]*>"),"login hides the default business code");
        }
        var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Sales");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        Check((int)(await client.PostAsJsonAsync("/api/products",new{})).StatusCode==403,"default Sales product creation denied");
        client.DefaultRequestHeaders.Add("Test-Grant-Create","PRODUCTS");
        Check((int)(await client.PostAsJsonAsync("/api/products",new{})).StatusCode==400,"custom Sales product creation reaches validation without Admin role");
        client.DefaultRequestHeaders.Remove("Test-Grant-Create");client.DefaultRequestHeaders.Add("Test-Deny","PRODUCTS");
        Check((int)(await client.GetAsync("/api/products")).StatusCode==403,"revoked view permission blocks API");
        Check((await client.GetAsync("/Products")).Headers.Location?.OriginalString=="/Account/AccessDenied","revoked view permission blocks page");
        client.DefaultRequestHeaders.Remove("Test-Deny");client.DefaultRequestHeaders.Add("Test-Force-Password","true");
        Check((int)(await client.GetAsync("/api/products")).StatusCode==403,"forced password change blocks API");
        Check((await client.GetAsync("/Products")).Headers.Location?.OriginalString=="/Account/ChangePassword","forced password change redirects page");
        Check((int)(await client.GetAsync("/Account/ChangePassword")).StatusCode==200,"forced password change form accessible");
        client.DefaultRequestHeaders.Remove("Test-Force-Password");client.DefaultRequestHeaders.Add("Test-Version","0");
        Check((int)(await client.GetAsync("/api/products")).StatusCode==401,"old session version rejected");
        client.DefaultRequestHeaders.Remove("Test-Version");client.DefaultRequestHeaders.Add("Test-Inactive","true");
        Check((int)(await client.GetAsync("/api/products")).StatusCode==401,"deactivated account session rejected");
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var page in new[]{"Users","Roles","AuditLogs","Security"})Check((int)(await admin.GetAsync("/Admin/"+page)).StatusCode==200,"administration page renders: "+page);
        var dev = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName="Development" };
        var lifetime=new DevelopmentLoginLifetime(dev);
        var ticket=new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
        Check(!lifetime.Accepts(ticket),"development rejects cookies without a current debug stamp");
        lifetime.Stamp(ticket);
        Check(lifetime.Accepts(ticket),"development accepts cookies from the current debug run");
        Check(!new DevelopmentLoginLifetime(dev).Accepts(ticket),"development restart rejects previous debug cookies");
        Check(new DevelopmentLoginLifetime(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName="Production" }).Accepts(ticket),"production restart preserves normal session validity");
        var usersHtml=await admin.GetStringAsync("/Admin/Users");
        Check(!usersHtml.Contains("id=\"login-email-otp\"") && usersHtml.Contains("id=\"primary-store\"")&&!usersHtml.Contains("id=\"permission-matrix\"") && !usersHtml.Contains("Authenticator settings (optional)")&&!usersHtml.Contains("Select all view permissions"),"user creation has assigned shop without authenticator or screen access editors");
        Check(usersHtml.Contains("id=\"admin-page-title\">Staff accounts & access")&&usersHtml.Contains("id=\"new-user\" type=\"button\" class=\"btn btn-primary\"")&&!usersHtml.Contains("aria-label=\"Administration sections\"")&&!usersHtml.Contains("class=\"admin-stat-grid\""),"user heading is first and duplicate navigation and summary cards are removed");
        var adminToken=await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",adminToken!["requestToken"]!.GetValue<string>());
        foreach(var endpoint in new[]{"/api/admin/users", "/api/admin/users/2"})
        foreach(var missing in new[]{"email","mobileNumber"})
        {
            var body=new JsonObject{["fullName"]="Example User",["username"]="example.user",["roleId"]=2,["email"]="staff@example.com",["mobileNumber"]="9876543210"};
            body[missing]=" ";
            var response=endpoint.EndsWith("/2")?await admin.PutAsJsonAsync(endpoint,body):await admin.PostAsJsonAsync(endpoint,body);
            Check((int)response.StatusCode==400,"staff create/edit requires "+missing);
        }
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","STORES:MANAGE_ACCESS");
        Check(!(await admin.GetStringAsync("/Admin/Users")).Contains("id=\"new-user\""),"new account shortcut requires permission to assign a store");
        admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check(PermissionMiddleware.Requirement("/api/admin/users/1/status","PUT")==("USER_MANAGEMENT","DELETE"),"user status requires deactivate permission");
        Check(PermissionMiddleware.Requirement("/api/purchases/1/complete","POST")==("PURCHASES","APPROVE"),"purchase completion requires approve permission");
        Check(PermissionMiddleware.Requirement("/api/purchases/1/receipts","POST")==("STOCK_INFLOW","RECEIVE"),"partial receiving requires receive permission");
        Check(PermissionMiddleware.Requirement("/api/operations/purchases/1","GET")==("PURCHASES","VIEW"),"purchase account summary requires purchase view permission");
        Check(PermissionMiddleware.Requirement("/api/operations/purchases/1/payments","POST")==("PURCHASES","PAY"),"supplier payment requires purchase pay permission");
        Check(PermissionMiddleware.Requirement("/api/operations/purchases/1/returns","POST")==("PURCHASES","RETURN"),"supplier return requires purchase return permission");
        Check(PermissionMiddleware.Requirement("/api/products/variants/1/barcode","POST")==("BARCODES","GENERATE"),"label generation requires barcode generate permission");
        foreach(var weak in new[]{"short","abc123!","aaaaaaaa","        "}){bool rejected=false;try{SecurityRules.Password(weak,weak);}catch(System.ComponentModel.DataAnnotations.ValidationException){rejected=true;}Check(rejected,"reject weak password");}
        SecurityRules.Password("Abcd123!","Abcd123!");
        Check(true,"accept eight-character password");
        SecurityRules.Password("Example passphrase 123","Example passphrase 123");
    }
}

