using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

static class BankPresentationChecks
{
    public static async Task Run()
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        static bool Denied(HttpResponseMessage response)=>response.StatusCode==HttpStatusCode.Forbidden||response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location?.OriginalString=="/Account/AccessDenied";
        using var factory=new Factory();
        using var owner=factory.CreateClient(new(){AllowAutoRedirect=false});owner.DefaultRequestHeaders.Add("Test-Role","Admin");
        // Existing settings VIEW authorizes information only, not operational bank access.
        owner.DefaultRequestHeaders.Add("Test-Deny","BANK_ACCOUNTS");
        var response=await owner.GetAsync("/Settings/BankAccountsSetup");var html=await response.Content.ReadAsStringAsync();
        Check(response.StatusCode==HttpStatusCode.OK,"Owner can open setup information with bank feature disabled");
        Check(html.Contains("href=\"/Settings/BankAccountsSetup\""),"actual Business Setup navigation exposes Bank Accounts information");
        Check(html.Contains("Bank account setup pending")&&html.Contains("No bank account data is loaded"),"pending state does not claim empty database or fabricate records");
        Check(html.Contains("disabled aria-describedby=\"bank-setup-status\"")&&html.Contains("financial posting are disabled"),"setup and posting actions remain disabled");
        Check(!html.Contains("/js/bank-accounts.")&&!html.Contains("id=\"bank-workspace\""),"informational page cannot load bank API or financial UI");
        Check((await owner.GetAsync("/api/bankaccounts")).StatusCode==HttpStatusCode.Forbidden,"setup visibility does not grant operational bank API permission");
        using var denied=factory.CreateClient(new(){AllowAutoRedirect=false});denied.DefaultRequestHeaders.Add("Test-Role","Admin");denied.DefaultRequestHeaders.Add("Test-Deny","COMPANY_SETTINGS");
        var noSettings=await denied.GetAsync("/Settings/BankAccountsSetup");Check(Denied(noSettings),"existing Business Setup permission is required: "+noSettings.StatusCode+" "+noSettings.Headers.Location);
        var branches=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branches.State.IsOwner=false;
        Check(Denied(await owner.GetAsync("/Settings/BankAccountsSetup")),"non-Owner cannot open setup information");
        using var guest=factory.CreateClient(new(){AllowAutoRedirect=false});
        var anonymous=await guest.GetAsync("/Settings/BankAccountsSetup");Check(anonymous.StatusCode==HttpStatusCode.Unauthorized||anonymous.StatusCode==HttpStatusCode.Redirect&&anonymous.Headers.Location?.OriginalString.Contains("/Account/Login")==true,"anonymous access is rejected");
        Console.WriteLine("Bank presentation checks passed: no bank repository or database accessed.");
    }
}
