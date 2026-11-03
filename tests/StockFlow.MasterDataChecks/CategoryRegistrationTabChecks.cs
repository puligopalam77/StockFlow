using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.DTOs.Categories;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

internal static class CategoryRegistrationTabChecks
{
    public static async Task Run()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"StockFlow.sln")))directory=directory.Parent;
        var output=Path.Combine(directory!.FullName,"tests/bin/category-tabs-fixtures");Directory.CreateDirectory(output);
        foreach(var profile in new[]{"BIKE_ACCESSORIES","ELECTRICAL","CLOTHING","FOOTWEAR"})
        {
            using var factory=new Factory();using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode=profile;branch.State.BusinessProfilePersisted=true;branch.State.BusinessProfileConfigurationAvailable=true;branch.State.CatalogRegistrationEnhancementsAvailable=true;
            var schemaRepo=(FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>();schemaRepo.Schema.Available=true;schemaRepo.Schema.InheritanceAvailable=true;
            var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
            var rootResponse=await client.PostAsJsonAsync("/api/categories",new{categoryName="Retail products"});rootResponse.EnsureSuccessStatusCode();var parent=(await rootResponse.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
            var configuration=new CategoryConfiguration{Attributes=[new(){Code="helmet_type",Label="Helmet Type",Type="SELECT",Options=["Full Face","Half Face"]},new(){Code="material",Label="Material",Type="SELECT",Options=["ABS","Fiberglass"]},new(){Code="certification",Label="Safety Certification"},new(){Code="visor",Label="Visor Type",Type="SELECT",Options=["Clear","Tinted"]},new(){Code="size",Label="Size",Type="SELECT",Options=["S","M","L","XL"]},new(){Code="color",Label="Color",Type="SELECT",Options=["Black","Red","Blue"]}],
                Fields=[new(){Code="helmet_type",Required=true},new(){Code="material"},new(){Code="certification",Required=true},new(){Code="visor"},new(){Code="size",Required=true,VariantAxis=true},new(){Code="color",Required=true,VariantAxis=true}],
                Inventory=new(){StockUnit=profile=="FOOTWEAR"?"PAIR":"PIECE",TrackingMethod=profile=="ELECTRICAL"?"SERIAL":"QUANTITY",AllowProductOverride=false,ReorderLevel=5,EnableWarranty=profile=="ELECTRICAL"}};
            var request=new CreateCategoryRequest{CategoryName="Configured items",ParentCategoryId=parent,Configuration=configuration};
            Check((await client.PostAsJsonAsync("/api/categories",request)).StatusCode==HttpStatusCode.BadRequest,"configuration is gated until manual 056: "+profile);
            schemaRepo.Schema.RegistrationAvailable=true;
            var result=await client.PostAsJsonAsync("/api/categories",request);result.EnsureSuccessStatusCode();var category=(await result.Content.ReadFromJsonAsync<CategoryResponse>())!;
            Check(category.RegistrationDefaults?.ReorderLevel==5&&category.ParentCategoryId==parent,"category and inventory defaults round-trip: "+profile);
            var schema=(await client.GetFromJsonAsync<CatalogSchema>("/api/categories/schema"))!;
            Check(schema.Categories.Single(c=>c.CategoryId==category.CategoryId).Fields.Count==6&&schema.EffectiveCategories!.Single(c=>c.CategoryId==category.CategoryId).Fields.Count(f=>f.VariantAxis)==2,"fields and variant axes use reusable schema: "+profile);
            Check((await client.PostAsJsonAsync("/api/categories",new CreateCategoryRequest{CategoryName="Stale save",Configuration=configuration})).StatusCode==HttpStatusCode.BadRequest,"stale configuration rejected before category creation: "+profile);
            Check((await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!.Count==2,"failed save leaves category count unchanged: "+profile);
            var product=new CreateProductRequest{ProductName="Configured product",CategoryId=category.CategoryId,UnitOfMeasure=configuration.Inventory.StockUnit,HasVariants=true,Variants=[new(){VariantName="Black M",Sku="CONFIG-M",Attributes=new(){["color"]="Black",["size"]="M"}}]};
            Check((await client.PostAsJsonAsync("/api/products",product)).StatusCode==HttpStatusCode.BadRequest,"configured required fields control product registration: "+profile);
            product.Variants![0].Attributes["helmet_type"]="Full Face";product.Variants[0].Attributes["certification"]="Verified label";
            var created=await client.PostAsJsonAsync("/api/products",product);created.EnsureSuccessStatusCode();
            product.ProductName="Wrong unit";product.UnitOfMeasure="BOX";
            Check((await client.PostAsJsonAsync("/api/products",product)).StatusCode==HttpStatusCode.BadRequest,"category unit/tracking override policy enforced: "+profile);
            (await client.PutAsJsonAsync("/api/categories/"+category.CategoryId,new{categoryName="Configured items updated",parentCategoryId=parent,isActive=true})).EnsureSuccessStatusCode();
            Check((await client.GetFromJsonAsync<CategoryResponse>("/api/categories/"+category.CategoryId))!.RegistrationDefaults?.ReorderLevel==5,"basic edits retain saved rules: "+profile);
            var inactive=await client.PostAsJsonAsync("/api/categories",new{categoryName="Inactive category",isActive=false});inactive.EnsureSuccessStatusCode();Check(!(await inactive.Content.ReadFromJsonAsync<CategoryResponse>())!.IsActive,"new category status is respected: "+profile);
            client.DefaultRequestHeaders.Add("Test-Deny-Action","CATEGORIES:EDIT");configuration.SchemaVersion=schema.Version;
            Check((await client.PostAsJsonAsync("/api/categories",new CreateCategoryRequest{CategoryName="No edit permission",Configuration=configuration})).StatusCode==HttpStatusCode.Forbidden,"configuring category rules requires edit permission: "+profile);client.DefaultRequestHeaders.Remove("Test-Deny-Action");
            Check((await client.PutAsJsonAsync("/api/categories/"+parent,new{categoryName="Retail products",categoryCode="ROOT-01",displayOrder=8,isActive=true})).StatusCode==HttpStatusCode.BadRequest,"editable metadata is gated before 057: "+profile);
            schemaRepo.Schema.CategoryDetailsAvailable=true;
            (await client.PutAsJsonAsync("/api/categories/"+parent,new{categoryName="Retail products",categoryCode="ROOT-01",displayOrder=8,isActive=true})).EnsureSuccessStatusCode();
            var details=await client.GetFromJsonAsync<CategoryResponse>("/api/categories/"+parent);Check(details?.CategoryCode=="ROOT-01"&&details.DisplayOrder==8,"editable category code and display order persist: "+profile);
            Check((await client.PostAsJsonAsync("/api/categories",new{categoryName="Duplicate code",categoryCode="root-01"})).StatusCode==HttpStatusCode.BadRequest,"duplicate category codes rejected: "+profile);
            (await client.PutAsJsonAsync("/api/categories/"+parent,new{categoryName="Retail products",isActive=true})).EnsureSuccessStatusCode();Check((await client.GetFromJsonAsync<CategoryResponse>("/api/categories/"+parent))?.CategoryCode=="ROOT-01","legacy edits preserve custom category codes: "+profile);
            schemaRepo.Schema.CategoryDetailsAvailable=false;
            await File.WriteAllTextAsync(Path.Combine(output,profile+"-Categories.html"),await client.GetStringAsync("/Categories"));
            await File.WriteAllTextAsync(Path.Combine(output,profile+"-Products.html"),await client.GetStringAsync("/Products"));
            if(profile=="BIKE_ACCESSORIES"){
                var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
                await File.WriteAllTextAsync(Path.Combine(output,"schema.json"),JsonSerializer.Serialize(schema,json));
                await File.WriteAllTextAsync(Path.Combine(output,"categories.json"),JsonSerializer.Serialize(await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"),json));
            }
        }
        Console.WriteLine("PASS: four-tab category configuration API checks. Fake repositories; PostgreSQL transaction/trigger acceptance remains manual.");
    }
}
