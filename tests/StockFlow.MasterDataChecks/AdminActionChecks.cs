using System.ComponentModel.DataAnnotations;
using System.Text;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Web.Security;
namespace StockFlow.MasterDataChecks;
internal static class AdminActionChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        // RFC 6238 SHA-1 vectors, reduced to six digits.
        var secret=Encoding.ASCII.GetBytes("12345678901234567890");
        foreach(var (time,expected) in new[]{(59L,"287082"),(1111111109L,"081804"),(1111111111L,"050471"),(1234567890L,"005924"),(2000000000L,"279037"),(20000000000L,"353130")})
            Check(TotpCodes.Generate(secret,time/30)==expected,"RFC TOTP vector "+time);
        Check(TotpCodes.Base32(secret)=="GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ","Authenticator Base32 encoding");
        Check(TotpCodes.Verify(secret,"287082",1,1)==null,"Used TOTP step rejected");
        Check(TotpCodes.Verify(secret,"287082",5,null)==null,"Expired TOTP rejected");
        Check(TotpCodes.Verify(secret,"287082",2,null)==1,"One-step clock tolerance");
        var dashboard=new ModulePermission{ModuleId=1,ModuleCode="DASHBOARD",SupportedActions=PermissionCatalog.Modules["DASHBOARD"],Actions=["VIEW","CREATE"]};
        bool rejected=false;try{SecurityRules.Permissions([dashboard]);}catch(ValidationException){rejected=true;}Check(rejected,"Unsupported dashboard action rejected");
        var approve=new ModulePermission{ModuleId=2,ModuleCode="PURCHASES",SupportedActions=PermissionCatalog.Modules["PURCHASES"],Actions=["VIEW","APPROVE"]};
        SecurityRules.Permissions([approve]);Check(approve.Allows("APPROVE")&&!approve.Allows("EDIT"),"Approval independent from editing");
        approve.Actions=["APPROVE"];rejected=false;try{SecurityRules.Permissions([approve]);}catch(ValidationException){rejected=true;}Check(rejected,"View dependency enforced");
        Check(PermissionMiddleware.Requirement("/api/transfers/1/dispatch","POST")==("STOCK_TRANSFER","DISPATCH"),"Dispatch action is separate");
        Check(PermissionMiddleware.Requirement("/api/admin/users/1/reset-password","POST")==("USER_MANAGEMENT","RESET_PASSWORD"),"Password reset is separate");
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        admin.DefaultRequestHeaders.Add("Test-2FA-Required","true");
        Check((await admin.GetAsync("/Products")).IsSuccessStatusCode,"Admin skips required MFA enrollment");
        admin.DefaultRequestHeaders.Add("Test-2FA-Enabled","true");
        Check((await admin.GetAsync("/Products")).IsSuccessStatusCode,"Enrolled Admin skips MFA challenge");
        Check((await admin.GetAsync("/api/products")).IsSuccessStatusCode,"Admin can access APIs without MFA");
        Check((await admin.GetAsync("/Account/TwoFactor")).Headers.Location?.OriginalString=="/Account/Home","Admin leaves existing MFA page");
        admin.DefaultRequestHeaders.Add("Test-Force-Password","true");
        Check((await admin.GetAsync("/Products")).Headers.Location?.OriginalString=="/Account/ChangePassword","Admin still must change temporary password");
        Check(SecurityRules.IsMfaExempt("admin")&&!SecurityRules.IsMfaExempt("Store Manager"),"MFA exemption is limited to Admin role");
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Sales");
        client.DefaultRequestHeaders.Add("Test-2FA-Required","true");
        Check((await client.GetAsync("/Products")).Headers.Location?.OriginalString=="/Account/TwoFactor","Required enrollment gates business pages");
        Check((int)(await client.GetAsync("/api/products")).StatusCode==403,"Required enrollment gates APIs");
        foreach(var asset in new[]{("/css/stockflow.css","text/css"),("/js/stockflow-shell.js","text/javascript"),("/images/icons.svg","image/svg+xml")})
        {
            using var response=await client.GetAsync(asset.Item1);
            Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType==asset.Item2,"MFA enrollment can load asset "+asset.Item1);
        }
        client.DefaultRequestHeaders.Add("Test-Force-Password","true");
        Check((await client.GetAsync("/Account/TwoFactor")).Headers.Location?.OriginalString=="/Account/ChangePassword","Temporary password precedes enrollment");
        client.DefaultRequestHeaders.Add("Test-2FA-Enabled","true");
        Check((await client.GetAsync("/Account/ChangePassword")).Headers.Location?.OriginalString=="/Account/TwoFactor","Enrolled account verifies MFA before changing password");
        Console.WriteLine("Action and MFA gate checks passed without database access.");
    }
}
