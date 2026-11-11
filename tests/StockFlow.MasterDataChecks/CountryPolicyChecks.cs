using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.MasterDataChecks;
internal static class CountryPolicyChecks
{
    public static async Task Run(Factory factory)
    {
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        foreach(var country in new string?[]{null,"","IN","india"})
        {
            var response=await client.PostAsJsonAsync("/api/customers",new{customerName="Indian customer",addressDetails=new{addressLine1="Market Street",country}});response.EnsureSuccessStatusCode();
            Check((await response.Content.ReadFromJsonAsync<JsonObject>())!["addressDetails"]!["country"]!.GetValue<string>()=="India","Customer country defaults/canonicalizes to India");
        }
        var supplierResponse=await client.PostAsJsonAsync("/api/suppliers",new{supplierName="Indian supplier"});supplierResponse.EnsureSuccessStatusCode();
        Check((await supplierResponse.Content.ReadFromJsonAsync<JsonObject>())!["countryCode"]!.GetValue<string>()=="IN","Supplier omission persists IN");
        Check((int)(await client.PostAsJsonAsync("/api/customers",new{customerName="Unsupported customer",addressDetails=new{country="United States",addressLine1="Foreign street"}})).StatusCode==400,"New foreign customer country rejected");
        Check((int)(await client.PostAsJsonAsync("/api/suppliers",new{supplierName="Unsupported supplier",countryCode="US"})).StatusCode==400,"New foreign supplier country rejected");
        var options=await client.GetFromJsonAsync<JsonObject>("/api/suppliers/options");Check(options!["countries"]!.AsArray().Count==1,"Supplier creation offers only India");

        // Seed pre-policy records directly in isolated fake repositories, never production data.
        var suppliers=factory.Services.GetRequiredService<ISupplierRepository>();
        var supplierId=await suppliers.CreateAsync(new Supplier{SupplierName="Historical supplier",CountryCode="US",Address="Original foreign address",IsActive=true});
        (await client.PutAsJsonAsync("/api/suppliers/"+supplierId,new{supplierName="Edited historical supplier",isActive=true})).EnsureSuccessStatusCode();
        Check((await suppliers.GetByIdAsync(supplierId))!.CountryCode=="US","Editing historical supplier preserves saved country even when omitted");
        var customers=factory.Services.GetRequiredService<ICustomerRepository>();
        var details=new AddressComponents{AddressLine1="Original street",Country="United States",PostalCode="10001"};
        var customerId=await customers.CreateAsync(new Customer{CustomerName="Historical customer",Address="Original foreign address",AddressDetailsJson=JsonSerializer.Serialize(details,new JsonSerializerOptions(JsonSerializerDefaults.Web)),IsActive=true});
        (await client.PutAsJsonAsync("/api/customers/"+customerId,new{customerName="Edited historical customer",isActive=true})).EnsureSuccessStatusCode();
        Check((await customers.GetByIdAsync(customerId))!.Address=="Original foreign address","Ordinary edit preserves historical address text");
        (await client.PutAsJsonAsync("/api/customers/"+customerId,new{customerName="Edited historical customer",isActive=true,useStructuredAddress=true,addressDetails=new{addressLine1="Edited street",postalCode="10001"}})).EnsureSuccessStatusCode();
        var saved=await client.GetFromJsonAsync<JsonObject>("/api/customers/"+customerId);
        Check(saved!["addressDetails"]!["country"]!.GetValue<string>()=="United States","Structured edits preserve historical country without applying Indian PIN rules");
        foreach(var (url,file) in new[]{("/Admin/Stores","store-management-fixtures/Stores.html"),("/Settings/Company?section=business","company-views-fixtures/business.html"),("/Settings/Company?section=billing","company-views-fixtures/billing.html")})
        {
            var html=await client.GetStringAsync(url);var output=Path.GetFullPath("tests/bin/"+file);Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllTextAsync(output,html);
        }
        Console.WriteLine("Country policy HTTP checks passed using fake repositories; no database changes.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
