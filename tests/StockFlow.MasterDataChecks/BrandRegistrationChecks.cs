using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;
using StockFlow.Application.DTOs.Brands;
namespace StockFlow.MasterDataChecks;

internal static class BrandRegistrationChecks
{
    public static async Task Run(){
        using var factory=new Factory();using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode="BIKE_ACCESSORIES";branch.State.BusinessProfilePersisted=true;branch.State.BusinessProfileConfigurationAvailable=true;branch.State.CatalogRegistrationEnhancementsAvailable=true;
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        static void Check(bool yes,string message){if(!yes)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        async Task<int> Category(string name,int? parent=null){var response=await client.PostAsJsonAsync("/api/categories",new{categoryName=name,parentCategoryId=parent});response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();}
        var helmets=await Category("Helmets");var full=await Category("Full Face",helmets);var half=await Category("Half Face",helmets);var lights=await Category("Lighting");var headlight=await Category("Headlights",lights);
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="Gated",brandCode="GAT"})).StatusCode==HttpStatusCode.BadRequest,"new brand details gated before 058");
        var repository=(FakeBrandRepository)factory.Services.GetRequiredService<IBrandRepository>();repository.DetailsAvailable=true;
        var logo=new BrandLogo{FileName="logo.png",ContentType="image/png",Data=Convert.ToBase64String(new byte[]{137,80,78,71,13,10,26,10,0})};
        var request=new CreateBrandRequest{BrandName="  Bosch   Parts  ",BrandCode="BOS",Description="Manufacturer",OfficialWebsite="HTTPS://example.com/",Logo=logo,CategoryIds=[helmets,lights]};
        var created=await client.PostAsJsonAsync("/api/brands",request);created.EnsureSuccessStatusCode();var brand=(await created.Content.ReadFromJsonAsync<BrandResponse>())!;
        Check(brand.BrandName=="Bosch Parts"&&brand.BrandCode=="BOS"&&brand.OfficialWebsite=="https://example.com/"&&brand.LogoUrl is not null,"brand details and logo round-trip");
        Check(brand.ApplicableCategoryIds.Contains(full)&&brand.ApplicableCategoryIds.Contains(half)&&brand.ApplicableCategoryIds.Contains(headlight),"whole-category recommendations include descendants across multiple categories");
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="bosch    parts",categoryIds=new[]{full}})).StatusCode==HttpStatusCode.BadRequest,"duplicate brand blocked across category assignments ignoring spaces/case");
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="Other",brandCode="bos"})).StatusCode==HttpStatusCode.BadRequest,"duplicate brand code blocked");
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="Bad website",officialWebsite="javascript:alert(1)"})).StatusCode==HttpStatusCode.BadRequest,"non-http website rejected");
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="Bad logo",logo=new{fileName="x.png",contentType="image/png",data=Convert.ToBase64String(new byte[]{1,2,3})}})).StatusCode==HttpStatusCode.BadRequest,"invalid image signature rejected");
        Check((await client.GetAsync(brand.LogoUrl)).StatusCode==HttpStatusCode.OK,"authorized brand logo endpoint returns uploaded image");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BRANDS:VIEW");Check((await client.GetAsync(brand.LogoUrl)).StatusCode==HttpStatusCode.Forbidden,"logo endpoint requires brand view permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        (await client.PutAsJsonAsync("/api/brands/"+brand.BrandId,new{brandName="Bosch Parts",description="Updated",isActive=true})).EnsureSuccessStatusCode();var preserved=(await client.GetFromJsonAsync<BrandResponse>("/api/brands/"+brand.BrandId))!;
        Check(preserved.BrandCode=="BOS"&&preserved.LogoUrl is not null&&preserved.OfficialWebsite=="https://example.com/"&&preserved.CategoryIds.Length==2,"legacy basic edit preserves code, logo, website and category links");
        (await client.PutAsJsonAsync("/api/brands/"+brand.BrandId,new{brandName="Bosch Parts",categoryIds=new[]{full},removeLogo=true,officialWebsite="",isActive=true})).EnsureSuccessStatusCode();var changed=(await client.GetFromJsonAsync<BrandResponse>("/api/brands/"+brand.BrandId))!;
        Check(changed.LogoUrl is null&&changed.OfficialWebsite is null&&changed.ApplicableCategoryIds.SequenceEqual(new[]{full}),"logo removal and specific-subcategory recommendation persist");
        var schema=(FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>();schema.Schema.RegistrationAvailable=true;
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"StockFlow.sln")))directory=directory.Parent;
        var output=Path.Combine(directory!.FullName,"tests/bin/brand-registration-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Brands.html"),await client.GetStringAsync("/Brands"));
        Console.WriteLine("PASS: brand registration API checks and Razor fixture. Fake repositories only; no SQL executed.");
    }
}
