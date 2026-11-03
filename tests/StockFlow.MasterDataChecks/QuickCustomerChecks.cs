using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;

internal static class QuickCustomerChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        const string guarded="/api/customers?preventIdentifierDuplicates=true";
        Check((int)(await client.PostAsJsonAsync(guarded+"&requireMobile=true",new{customerName="Checkout no mobile"})).StatusCode==400,"checkout quick-add requires mobile without changing general customer creation");
        Check((int)(await client.PostAsJsonAsync(guarded+"&requireMobile=true",new{customerName="Checkout business",mobileNumber="9123456701",gstNumber="29ABCDE1234F1Z5"})).StatusCode==400,"checkout rejects GSTIN with an invalid checksum");
        Check((int)(await client.PostAsJsonAsync(guarded+"&requireMobile=true",new{customerName="Checkout business",mobileNumber="9123456701",email="invalid"})).StatusCode==400,"checkout validates optional email on the server");
        var optional=await client.PostAsJsonAsync(guarded+"&requireMobile=true",new{customerName="Checkout business",mobileNumber="9123456701",email="accounts@example.test",gstNumber="29ABCDE1234F1ZW",address="Main Road, Bengaluru, Karnataka 560001"});
        var optionalRecord=await optional.Content.ReadFromJsonAsync<JsonObject>();
        Check((int)optional.StatusCode==201&&optionalRecord!["email"]!.GetValue<string>()=="accounts@example.test"&&optionalRecord["gstNumber"]!.GetValue<string>()=="29ABCDE1234F1ZW"&&optionalRecord["address"]!.GetValue<string>().Contains("560001"),"quick customer keeps optional email, reviewed GSTIN format and billing address in the authoritative customer record");
        async Task<int> Post(object body)=>(int)(await client.PostAsJsonAsync(guarded,body)).StatusCode;
        Check(await Post(new{customerName="",mobileNumber=(string?)null})==400,"quick customer requires a valid name");
        Check(await Post(new{customerName="12345"})==400,"customer name still requires letters");
        Check(await Post(new{customerName="Counter",mobileNumber="123"})==400,"optional mobile is validated when supplied");
        Check(await Post(new{customerName="Counter",gstNumber="invalid"})==400,"optional GSTIN is validated when supplied");
        Check(await Post(new{customerName="Name only"})==201,"name-only customer remains valid");
        var body=new{customerName="Existing customer",mobileNumber="9123456780",gstNumber="29ABCDE1234F1Z5",address="Main Road"};
        var response=await client.PostAsJsonAsync(guarded,body);var record=await response.Content.ReadFromJsonAsync<JsonObject>();
        Check((int)response.StatusCode==201&&record!["customerId"]!.GetValue<int>()>0,"registration returns a real server customer ID after creation");
        Check(await Post(new{customerName="Duplicate phone",mobileNumber=body.mobileNumber})==400,"quick-add rejects an existing mobile");
        Check(await Post(new{customerName="Duplicate GST",gstNumber=body.gstNumber})==400,"quick-add rejects an existing GSTIN");
        var matches=await client.GetFromJsonAsync<JsonArray>("/api/customers/search?gstNumber=29abcde1234f1z5");
        Check(matches!.Count==1&&matches[0]!["customerName"]!.GetValue<string>()==body.customerName,"exact GST lookup is case-insensitive");
        matches=await client.GetFromJsonAsync<JsonArray>("/api/customers/search?mobileNumber=9123456780");Check(matches!.Count==1,"exact mobile lookup finds the existing customer");
        Check((int)(await client.GetAsync("/api/customers/search?mobileNumber=123")).StatusCode==400,"invalid exact lookup cannot become a broad customer query");
        await client.DeleteAsync("/api/customers/"+record!["customerId"]!.GetValue<int>());
        matches=await client.GetFromJsonAsync<JsonArray>("/api/customers/search?mobileNumber=9123456780");
        Check(matches!.Count==1&&!matches[0]!["isActive"]!.GetValue<bool>(),"inactive matches are returned for duplicate warning, not recreated");
        Check(await Post(body)==400,"inactive identifier still blocks quick duplicate creation");
        Check((int)(await client.PostAsJsonAsync("/api/customers",body)).StatusCode==201,"other customer registration retains its existing behavior");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","CUSTOMERS:CREATE");Check(await Post(new{customerName="Forbidden"})==403,"quick registration requires customer create permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny","CUSTOMERS");Check((int)(await client.GetAsync("/api/customers/search?mobileNumber=9123456780")).StatusCode==403,"matching does not bypass customer lookup permission");
        Console.WriteLine("Quick Customer API checks passed; no business database accessed.");
    }
}
