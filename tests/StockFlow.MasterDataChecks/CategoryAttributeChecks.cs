using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;

namespace StockFlow.MasterDataChecks;
internal static class CategoryAttributeChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Invalid(Action action,string message){try{action();}catch(ValidationException){Check(true,message);return;}throw new Exception("FAIL: "+message);}
    public static async Task Run()
    {
        var doc=new CatalogSchema {Attributes=[
            new(){Code="size",Label="Size",Type="SELECT",Options=["S","M","L"]},
            new(){Code="color",Label="Color",Type="SELECT",Options=["Black","Blue"]},
            new(){Code="material",Label="Material",Type="TEXT"},
            new(){Code="weight",Label="Weight",Type="NUMBER",Minimum=0,Maximum=10},
            new(){Code="release_date",Label="Release date",Type="DATE"},
            new(){Code="certified",Label="Certified",Type="BOOLEAN"}
        ],Categories=[new(){CategoryId=1,Fields=[new(){Code="size",Required=true,VariantAxis=true},new(){Code="color",Required=true,VariantAxis=true},new(){Code="material",Required=true},new(){Code="weight"},new(){Code="release_date"},new(){Code="certified"}]}]};
        CatalogSchemaValidator.ValidateDefinition(doc,new HashSet<int>{1});
        Dictionary<string,string> Values(string size="S")=>new(){["size"]=size,["color"]="Black",["material"]="Cotton",["weight"]="1.5",["release_date"]="2026-10-27",["certified"]="true"};
        CatalogSchemaValidator.ValidateValues(doc,1,[Values(),Values("M")]);
        Check(true,"typed definitions and distinct combinations accepted");
        foreach(var (key,bad) in new[]{("size","XL"),("weight","11"),("weight","NaN"),("release_date","2026-02-30"),("certified","yes"),("material","")}){
            var values=Values();values[key]=bad;Invalid(()=>CatalogSchemaValidator.ValidateValues(doc,1,[values]),"reject invalid/required "+key);
        }
        Invalid(()=>CatalogSchemaValidator.ValidateValues(doc,1,[Values(),Values()]),"duplicate combinations rejected");
        var many=Copy(doc);many.Attributes[0].Options=Enumerable.Range(1,31).Select(i=>"Size"+i).ToList();
        var manyRows=many.Attributes[0].Options.Select(size=>Values(size)).ToList();
        CatalogSchemaValidator.ValidateValues(many,1,manyRows.Take(30).ToList());
        Invalid(()=>CatalogSchemaValidator.ValidateValues(many,1,manyRows),"31 distinct configured variants rejected; 30 accepted");
        var mismatch=Values("M");mismatch["material"]="Wool";Invalid(()=>CatalogSchemaValidator.ValidateValues(doc,1,[Values(),mismatch]),"common values must match across variants");
        foreach(var mutate in new Action<CatalogSchema>[] {s=>s.Attributes.Add(Copy(s.Attributes[0])),s=>s.Categories[0].CategoryId=999,s=>s.Categories[0].Fields[0].Required=false,s=>s.Attributes[0].Type="TEXT",s=>s.Attributes[0].Options=["S","s"],s=>s.Attributes[0].Code="Invalid Code"}){
            var invalid=Copy(doc);mutate(invalid);Invalid(()=>CatalogSchemaValidator.ValidateDefinition(invalid,new HashSet<int>{1}),"invalid definition or assignment rejected");
        }
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/category-attributes-fixtures");Directory.CreateDirectory(output);
        foreach(var profile in new[]{BusinessProfiles.Clothing,BusinessProfiles.Footwear,BusinessProfiles.BikeAccessories}){
            using var factory=new Factory();using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode=profile;branch.State.BusinessProfilePersisted=true;branch.State.BusinessProfileConfigurationAvailable=true;branch.State.BusinessProfileVersion=1;
            var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
            var schemaRepo=(FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>();
            Check(!(await client.GetFromJsonAsync<CatalogSchema>("/api/categories/schema"))!.Available,"setup state before manual 048");
            var catResponse=await client.PostAsJsonAsync("/api/categories",new{categoryName="Configured items"});catResponse.EnsureSuccessStatusCode();var cat=(await catResponse.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
            var secondResponse=await client.PostAsJsonAsync("/api/categories",new{categoryName="Other items"});secondResponse.EnsureSuccessStatusCode();var second=(await secondResponse.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
            schemaRepo.Schema.Available=true;var schema=Copy(doc);schema.Categories[0].CategoryId=cat;
            (await client.PutAsJsonAsync("/api/categories/schema",schema)).EnsureSuccessStatusCode();
            Check((await client.PutAsJsonAsync("/api/categories/schema",schema)).StatusCode==HttpStatusCode.BadRequest,"stale configuration save rejected");
            var request=new CreateProductRequest{ProductName="Configured product",CategoryId=cat,TrackingType="QUANTITY",HasVariants=true,Variants=[new(){VariantName="S / Black",Sku="ITEM-S-BLK",Attributes=Values()},new(){VariantName="M / Black",Sku="ITEM-M-BLK",Attributes=Values("M")}]};
            var invalid=Copy(request);invalid.Variants![0].Attributes.Remove("size");Check((await client.PostAsJsonAsync("/api/products",invalid)).StatusCode==HttpStatusCode.BadRequest,"required fields enforced by product API: "+profile);
            var created=await client.PostAsJsonAsync("/api/products",request);created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonObject>())!["productId"]!.GetValue<int>();
            var variants=(await client.GetFromJsonAsync<List<VariantResponse>>("/api/products/variants?productId="+id))!;Check(variants.Count==2&&variants.All(v=>v.Attributes["material"]=="Cotton"),"configured variants persist with stable attributes: "+profile);
            var update=JsonSerializer.Deserialize<UpdateProductRequest>(JsonSerializer.Serialize(request))!;update.IsActive=true;for(var i=0;i<2;i++)update.Variants![i].VariantId=variants[i].VariantId;
            update.Variants![0].Attributes["size"]="L";Check((await client.PutAsJsonAsync("/api/products/"+id,update)).StatusCode==HttpStatusCode.BadRequest,"saved variant option identity protected");update.Variants[0].Attributes["size"]="S";
            update.CategoryId=second;Check((await client.PutAsJsonAsync("/api/products/"+id,update)).StatusCode==HttpStatusCode.BadRequest,"configured category changes rejected");update.CategoryId=cat;
            (await client.PutAsJsonAsync("/api/products/"+id,update)).EnsureSuccessStatusCode();
            var direct=new VariantRequest{ProductId=id,VariantName="L / Black",Sku="ITEM-L-BLK",Attributes=Values("L")};direct.Attributes.Remove("size");Check((await client.PostAsJsonAsync("/api/products/variants",direct)).StatusCode==HttpStatusCode.BadRequest,"direct variant API cannot bypass required fields");
            client.DefaultRequestHeaders.Add("Test-Deny-Action","CATEGORIES:EDIT");Check((await client.PutAsJsonAsync("/api/categories/schema",schema)).StatusCode==HttpStatusCode.Forbidden,"category edit permission required");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
            await File.WriteAllTextAsync(Path.Combine(output,profile+".html"),await client.GetStringAsync("/Products"));
            await File.WriteAllTextAsync(Path.Combine(output,"Settings-"+profile+".html"),await client.GetStringAsync("/Categories/Attributes"));
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((await client.PutAsJsonAsync("/api/categories/schema",schema)).StatusCode==HttpStatusCode.BadRequest,"schema saves require CSRF token");
        }
        Console.WriteLine("PASS: category attribute server checks. Fake repositories only; SQL and PostgreSQL trigger checks remain manual.");
    }
}
