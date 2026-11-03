using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;
using StockFlow.Domain.Entities;

namespace StockFlow.MasterDataChecks;

internal static class CategoryHierarchyChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var categories=new List<Category>{new(){CategoryId=1,CategoryName="Bike Parts & Accessories"},new(){CategoryId=2,CategoryName="Helmets",ParentCategoryId=1},new(){CategoryId=3,CategoryName="Full Face Helmets",ParentCategoryId=2}};
        var schema=new CatalogSchema{Available=true,InheritanceAvailable=true,Attributes=[
            new(){Code="part_number",Label="Manufacturer part number"},new(){Code="size",Label="Size",Type="SELECT",Options=["S","M"]},
            new(){Code="color",Label="Color",Type="SELECT",Options=["Black","Red"]},new(){Code="visor",Label="Visor Type"}],
            Categories=[new(){CategoryId=1,Fields=[new(){Code="part_number"}]},
                new(){CategoryId=2,Fields=[new(){Code="size",Required=true,VariantAxis=true},new(){Code="color",Required=true,VariantAxis=true}]},
                new(){CategoryId=3,Fields=[new(){Code="visor"}]}]};
        CategoryFieldInheritance.Resolve(schema,categories);
        var fields=CategoryFieldInheritance.Fields(schema,3);
        Check(fields.Count==4&&fields.Single(f=>f.Code=="size").DefinedOnCategoryId==2,"child combines ancestor fields and retains their source");
        try{CatalogSchemaValidator.ValidateValues(schema,3,[new(){["color"]="Black"}]);throw new Exception("missing inherited field accepted");}catch(ValidationException){Check(true,"required inherited axes are validated");}
        CatalogSchemaValidator.ValidateValues(schema,3,[new(){["size"]="S",["color"]="Black",["visor"]="Clear"}]);
        categories[2].InheritsParentFields=false;CategoryFieldInheritance.Resolve(schema,categories);
        Check(CategoryFieldInheritance.Fields(schema,3).Count==1,"legacy inheritance boundary preserves existing direct rules");
        categories[2].InheritsParentFields=true;CategoryFieldInheritance.Resolve(schema,categories);
        var excessive=JsonSerializer.Deserialize<CatalogSchema>(JsonSerializer.Serialize(schema))!;
        excessive.Attributes.AddRange([new(){Code="style",Label="Style",Type="SELECT",Options=["A"]},new(){Code="finish",Label="Finish",Type="SELECT",Options=["B"]}]);
        excessive.Categories[2].Fields.AddRange([new(){Code="style",Required=true,VariantAxis=true},new(){Code="finish",Required=true,VariantAxis=true}]);
        try{CategoryFieldInheritance.Resolve(excessive,categories);throw new Exception("four inherited axes accepted");}catch(ValidationException){Check(true,"combined ancestor and child variant axes respect the limit");}
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        async Task<int> Create(string name,int? parent=null){var result=await client.PostAsJsonAsync("/api/categories",new{categoryName=name,parentCategoryId=parent});result.EnsureSuccessStatusCode();return (await result.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();}
        var electrical=await Create("Electrical models");
        var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode="BIKE_ACCESSORIES";branch.State.BusinessProfilePersisted=true;branch.State.BusinessProfileConfigurationAvailable=true;
        var root=await Create("Helmets");var helmets=root;var full=await Create("Full Face Helmets",root);
        Check((await client.PostAsJsonAsync("/api/categories",new{categoryName="Foreign parent",parentCategoryId=electrical})).StatusCode==HttpStatusCode.BadRequest,"parent from another profile is rejected");
        var row=await client.GetFromJsonAsync<JsonObject>($"/api/categories/{full}");Check(row!["categoryLevel"]!.GetValue<int>()==2&&row["isLeaf"]!.GetValue<bool>(),"category details calculate level and leaf status");
        var tree=await client.GetFromJsonAsync<JsonArray>("/api/categories/tree");Check(tree!.Count==1&&tree[0]!["children"]![0]!["categoryId"]!.GetValue<int>()==full,"tree endpoint preserves parent and child identities");
        var found=await client.GetFromJsonAsync<JsonArray>("/api/categories?term=Helmets");Check(found!.Count==2,"category search matches the full path");
        var productRepo=factory.Services.GetRequiredService<IProductRepository>();await productRepo.CreateAsync(new(){CategoryId=full,ProductName="Registered helmet",IsActive=false});
        Check((await client.PutAsJsonAsync($"/api/categories/{helmets}",new{categoryName="Helmets",parentCategoryId=(int?)full,isActive=true})).StatusCode==HttpStatusCode.BadRequest,"inactive products also protect category subtree moves");
        var schemaRepo=(FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>();
        schema.Categories=[new(){CategoryId=root,Fields=schema.Categories[0].Fields.Concat(schema.Categories[1].Fields).ToList()},new(){CategoryId=full,Fields=schema.Categories[2].Fields}];
        categories=[new(){CategoryId=root,CategoryName="Helmets"},new(){CategoryId=full,CategoryName="Full Face Helmets",ParentCategoryId=root}];
        CategoryFieldInheritance.Resolve(schema,categories);schemaRepo.Schema=schema;
        var missing=await client.PostAsJsonAsync("/api/products",new{productName="Missing inherited size",categoryId=full,trackingType="QUANTITY",variants=new[]{new{variantName="Black",sku="HELM-MISSING",attributes=new{color="Black"}}}});
        Check(missing.StatusCode==HttpStatusCode.BadRequest&&(await missing.Content.ReadAsStringAsync()).Contains("Size"),"product API enforces inherited field requirements");
        var output=Path.Combine(FindRoot(),"tests/bin/category-hierarchy-fixtures");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"Categories.html"),await client.GetStringAsync("/Categories"));
        await File.WriteAllTextAsync(Path.Combine(output,"Attributes.html"),await client.GetStringAsync("/Categories/Attributes"));
        await File.WriteAllTextAsync(Path.Combine(output,"Products.html"),await client.GetStringAsync("/Products"));
        await File.WriteAllTextAsync(Path.Combine(output,"schema.json"),JsonSerializer.Serialize(schema,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Check(true,"category hierarchy fixtures written; no SQL executed");
    }
    private static string FindRoot(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"StockFlow.sln")))dir=dir.Parent;return dir!.FullName;}
}
