using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;

namespace StockFlow.MasterDataChecks;
internal static class BikeDemoSetupChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
    public static async Task Run()
    {
        using var factory=new Factory();using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
        var state=((FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>()).State;state.BusinessProfileConfigurationAvailable=true;state.BusinessProfileVersion=1;state.BusinessProfilePersisted=true;
        var existingCategory=await client.PostAsJsonAsync("/api/categories",new{categoryName="Existing batteries"});existingCategory.EnsureSuccessStatusCode();var cat=(await existingCategory.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
        for(var i=0;i<3;i++)(await client.PostAsJsonAsync("/api/products",new{productName="Existing electrical item "+i,categoryId=cat,trackingType="SERIAL",warrantyMonths=12,sku="KEEP-"+i})).EnsureSuccessStatusCode();
        var repo=(FakeProductRepository)factory.Services.GetRequiredService<IProductRepository>();var before=JsonSerializer.Serialize(repo.AllRows);
        ((FakeBusinessProfileRepository)factory.Services.GetRequiredService<IBusinessProfileRepository>()).Transactions=33;
        BusinessProfileChange Change(string code,long version)=>new(){Code=code,ExpectedVersion=version,Reason="Apply demo shop configuration",Acknowledged=true,CreateStarterCategories=true};
        Check((await client.PutAsJsonAsync("/api/business-profile",Change("BIKE_ACCESSORIES",1))).StatusCode==HttpStatusCode.BadRequest,"populated switching still blocked without manual 050 capability");
        state.ProfileCatalogSwitchingAvailable=true;
        var pendingHtml=await client.GetStringAsync("/Products");
        Check(pendingHtml.Contains("Apply migration 051"),"pre-051 banner clearly identifies pending transaction filtering");
        state.ProfileDataVisibilityAvailable=true;
        var settings=(await client.GetFromJsonAsync<JsonObject>("/api/business-profile"))!;
        Check(settings["dataVisibilityAvailable"]!.GetValue<bool>(),"051 capability reaches authenticated configuration API");
        Check(settings["catalogSwitchingAvailable"]!.GetValue<bool>()&&settings["profiles"]!.AsArray().Count==2,"050 exposes Electrical / Bike setup choices only");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/bike-demo-fixtures");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"Setup.html"),await client.GetStringAsync("/Settings/BusinessProfile"));
        (await client.PutAsJsonAsync("/api/business-profile",Change("BIKE_ACCESSORIES",1))).EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count==0,"Electrical products are excluded from Bike catalog");
        var bikeHtml=await client.GetStringAsync("/Products");
        Check(bikeHtml.Contains("Products, transactions and totals follow this setup")&&bikeHtml.Contains("Keep one product record for each bike part or accessory model.")&&!bikeHtml.Contains("Keep one product record for each battery or geyser model."),"Bike screen advertises profile filtering and displays bike-specific guidance");
        Check((await client.GetAsync("/api/products/1")).StatusCode==HttpStatusCode.NotFound,"old Electrical item cannot be edited in Bike setup");
        Check(JsonSerializer.Serialize(repo.AllRows)==before,"all three original product records remain byte-for-byte unchanged in fake storage");
        var categories=(await client.GetFromJsonAsync<JsonArray>("/api/categories"))!;
        Check(categories.Count==10&&!categories.Any(c=>c!["categoryName"]!.GetValue<string>().Contains("batteries")),"bike categories include spare parts, guards and lights without relabeling old categories");
        var migration=await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/050_bike_catalog_setup_switch.sql"));
        var jsonOptions=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var schema=new CatalogSchema{Available=true,Version=1,Attributes=JsonSerializer.Deserialize<List<CatalogAttribute>>(Regex.Match(migration,@"defaults:='(\[.*?\])'::jsonb;",RegexOptions.Singleline).Groups[1].Value,jsonOptions)!};
        var fallback=Regex.Match(migration,@"ELSE '(\[.*?\])'::jsonb END;",RegexOptions.Singleline).Groups[1].Value;
        foreach(var c in categories){var name=c!["categoryName"]!.GetValue<string>();var match=Regex.Match(migration,@"WHEN '"+Regex.Escape(name)+@"' THEN '(\[.*?\])'::jsonb",RegexOptions.Singleline);schema.Categories.Add(new(){CategoryId=c["categoryId"]!.GetValue<int>(),Fields=JsonSerializer.Deserialize<List<CategoryAttributeRule>>(match.Success?match.Groups[1].Value:fallback,jsonOptions)!});}
        CatalogSchemaValidator.ValidateDefinition(schema,categories.Select(c=>c!["categoryId"]!.GetValue<int>()).ToHashSet());
        ((FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>()).Schema=schema;
        await File.WriteAllTextAsync(Path.Combine(output,"Schema.json"),JsonSerializer.Serialize(schema,jsonOptions));await File.WriteAllTextAsync(Path.Combine(output,"Categories.json"),categories.ToJsonString());
        Check(schema.Categories.All(c=>c.Fields.Count>0),"shipped SQL bike templates pass application definition validation (SQL not executed)");
        await File.WriteAllTextAsync(Path.Combine(output,"BikeProducts.html"),await client.GetStringAsync("/Products"));
        await File.WriteAllTextAsync(Path.Combine(output,"BikeSetup.html"),await client.GetStringAsync("/Settings/BusinessProfile"));
        var helmet=categories.Single(c=>c!["categoryName"]!.GetValue<string>()=="Helmets")!["categoryId"]!.GetValue<int>();
        (await client.PostAsJsonAsync("/api/products",new CreateProductRequest{ProductName="Demo Helmet",CategoryId=helmet,TrackingType="QUANTITY",Variants=[new(){Sku="DEMO-HELMET-M",VariantName="DEFAULT",Attributes=new(){["helmet_size"]="M",["color"]="Black"}}]})).EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count==1&&repo.AllRows.Count==4,"new Bike product is separate from retained Electrical products");
        Check((await client.PutAsJsonAsync("/api/business-profile",Change("ELECTRICAL",1))).StatusCode==HttpStatusCode.Conflict,"stale setup version rejected");
        Check((await client.PutAsJsonAsync("/api/business-profile",Change("CLOTHING",2))).StatusCode==HttpStatusCode.BadRequest,"other populated profile conversions remain blocked");
        (await client.PutAsJsonAsync("/api/business-profile",Change("ELECTRICAL",2))).EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count==3,"switching back restores original Electrical catalog");
        Check((await client.GetStringAsync("/Products")).Contains("Keep one product record for each battery or geyser model."),"Electrical guidance is restored with original catalog");
        Check(JsonSerializer.Serialize(repo.AllRows.Take(3))==before,"original IDs, serial settings, warranty and SKU survive both setup changes");
        await File.WriteAllTextAsync(Path.Combine(output,"ElectricalProducts.html"),await client.GetStringAsync("/Products"));
        client.DefaultRequestHeaders.Add("Test-Deny-Action","COMPANY_SETTINGS:EDIT");Check((await client.PutAsJsonAsync("/api/business-profile",Change("BIKE_ACCESSORIES",3))).StatusCode==HttpStatusCode.Forbidden,"setup remains administrator/edit protected");
        Console.WriteLine("PASS: populated Bike setup/API preservation checks. Fakes and SQL-template parsing only; no database script executed.");
    }
}
