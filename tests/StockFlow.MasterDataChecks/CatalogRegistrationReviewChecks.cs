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
internal static class CatalogRegistrationReviewChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    public static async Task Run()
    {
        using var factory=new Factory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var state=((FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>()).State;
        state.BusinessProfileCode=BusinessProfiles.BikeAccessories;state.BusinessProfileConfigurationAvailable=true;state.BusinessProfilePersisted=true;
        state.BusinessProfileVersion=7;state.ProfileCatalogSwitchingAvailable=true;state.ProfileDataVisibilityAvailable=true;state.CatalogRegistrationEnhancementsAvailable=true;state.ProductImageGalleryAvailable=true;
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var category=await client.PostAsJsonAsync("/api/categories",new{categoryName="Demo Brake Parts"});category.EnsureSuccessStatusCode();
        var categoryId=(await category.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
        var brand=await client.PostAsJsonAsync("/api/brands",new{brandName="Demo Parts",categoryIds=Array.Empty<int>(),aliases=new[]{"Demo Manufacturer"}});brand.EnsureSuccessStatusCode();
        var brandId=(await brand.Content.ReadFromJsonAsync<JsonObject>())!["brandId"]!.GetValue<int>();
        Check((await client.PostAsJsonAsync("/api/brands",new{brandName="DEMO MANUFACTURER",categoryIds=Array.Empty<int>()})).StatusCode==HttpStatusCode.BadRequest,"alias cannot be reused as another brand name");
        var schema=new CatalogSchema{Available=true,Version=1,Attributes=[
            new(){Code="vehicle_compatibility",Label="Verified compatible vehicles",Type="TEXT_LIST"},
            new(){Code="internal_code",Label="Internal default",Type="TEXT"}
        ],Categories=[new(){CategoryId=categoryId,Fields=[new(){Code="vehicle_compatibility",Searchable=true},new(){Code="internal_code",Required=true,Visible=false,Searchable=false,DefaultValue="BASE"}]}]};
        CatalogSchemaValidator.ValidateDefinition(schema,new HashSet<int>{categoryId});
        ((FakeCatalogSchemaRepository)factory.Services.GetRequiredService<ICatalogSchemaRepository>()).Schema=schema;
        var fitment="Honda / Activa 6G / verified generation 2020-2024 / Rear; Yamaha / FZ / verified generation 2018-2022 / Rear";
        CreateProductRequest Product(string name,string sku)=>new(){ProductName=name,CategoryId=categoryId,BrandId=brandId,TrackingType="QUANTITY",Variants=[new(){ProductId=1,VariantName="DEFAULT",Sku=sku,Attributes=new(){["vehicle_compatibility"]=fitment}}]};
        var created=await client.PostAsJsonAsync("/api/products",Product("Demo Brake Shoe","BRAKE-DEMO-1"));created.EnsureSuccessStatusCode();
        var productId=(await created.Content.ReadFromJsonAsync<JsonObject>())!["productId"]!.GetValue<int>();
        var variants=(await client.GetFromJsonAsync<JsonArray>("/api/products/variants?productId="+productId))!;
        Check(variants.Count==1&&variants[0]!["attributes"]!["internal_code"]!.GetValue<string>()=="BASE","unassigned brand accepted and hidden required default supplied server-side");
        Check(variants[0]!["attributes"]!["vehicle_compatibility"]!.GetValue<string>()==fitment,"one SKU retains several verified vehicle fitments");
        (await client.PutAsJsonAsync("/api/products/"+productId,new{productName="Demo Brake Shoe",categoryId,brandId,trackingType="QUANTITY",productStatus="DISCONTINUED",isActive=false})).EnsureSuccessStatusCode();
        var discontinued=(await client.GetFromJsonAsync<JsonObject>("/api/products/"+productId))!;
        Check(discontinued["productStatus"]!.GetValue<string>()=="DISCONTINUED"&&!discontinued["isActive"]!.GetValue<bool>(),"discontinued status preserves the product identity and deactivates it");
        (await client.PutAsJsonAsync("/api/products/"+productId,new{productName="Demo Brake Shoe",categoryId,brandId,trackingType="QUANTITY",productStatus="ACTIVE",isActive=true})).EnsureSuccessStatusCode();
        var key1=await client.PostAsJsonAsync("/api/receiving/opening/import-key",new{canonical="verified rows",locationId=1});key1.EnsureSuccessStatusCode();
        var key2=await client.PostAsJsonAsync("/api/receiving/opening/import-key",new{canonical="verified rows",locationId=1});key2.EnsureSuccessStatusCode();
        Check(await key1.Content.ReadAsStringAsync()==await key2.Content.ReadAsStringAsync(),"identical opening import yields the same tenant/profile/location request identity");
        Check((await client.PostAsJsonAsync("/api/receiving/opening/import-key",new{canonical="verified rows",locationId=2})).StatusCode==HttpStatusCode.BadRequest,"opening import identity cannot target another location");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_INFLOW:RECEIVE");
        (await client.PostAsJsonAsync("/api/receiving/opening/import-key",new{canonical="verified rows",locationId=1})).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_ADJUSTMENT:ADJUST");
        Check((await client.PostAsJsonAsync("/api/receiving/opening/import-key",new{canonical="verified rows",locationId=1})).StatusCode==HttpStatusCode.Forbidden,"opening import requires adjustment permission independently of receiving");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check((await client.PostAsJsonAsync("/api/products",Product(" DEMO   Brake Shoe ","BRAKE-OTHER"))).StatusCode==HttpStatusCode.BadRequest,"normalized duplicate product rejected before save");
        Check((await client.PostAsJsonAsync("/api/products",Product("Different item","BRAKE-DEMO-1"))).StatusCode==HttpStatusCode.BadRequest,"variant SKU collision rejected before save");
        var part=Product("Part by manufacturer code","MFR-PART-1");part.Variants![0].Attributes["manufacturer_part_number"]="MFR-001";
        (await client.PostAsJsonAsync("/api/products",part)).EnsureSuccessStatusCode();
        var repeatedPart=Product("Different name for same part","MFR-PART-2");repeatedPart.Variants![0].Attributes["manufacturer_part_number"]="mfr-001";
        Check((await client.PostAsJsonAsync("/api/products",repeatedPart)).StatusCode==HttpStatusCode.BadRequest,"same manufacturer's part cannot be duplicated using a different product name");
        var importProduct=Product("Imported Brake Shoe","IMPORT-BRAKE-1");
        var count=(await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count;
        var preview=await client.PostAsJsonAsync("/api/products/catalog/preview",new{expectedProfileVersion=7,products=new[]{importProduct}});preview.EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count==count,"CSV preview creates neither products nor stock");
        Check((await client.PostAsJsonAsync("/api/products/catalog/commit",new{expectedProfileVersion=7,products=new[]{importProduct},reviewed=false})).StatusCode==HttpStatusCode.BadRequest,"unreviewed import rejected");
        var commit=await client.PostAsJsonAsync("/api/products/catalog/commit",new{expectedProfileVersion=7,products=new[]{importProduct},reviewed=true});commit.EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products"))!.Count==count+1,"confirmed import creates product and its variant");
        Check((await client.PostAsJsonAsync("/api/products/catalog/commit",new{expectedProfileVersion=6,products=new[]{Product("Stale item","STALE-1")},reviewed=true})).StatusCode==HttpStatusCode.BadRequest,"stale profile import rejected");
        Check((await client.PostAsJsonAsync("/api/products/catalog/preview",new{expectedProfileVersion=7,products=new[]{importProduct}})).StatusCode==HttpStatusCode.OK,"retry preview reports existing rows without changing them");
        (await client.GetAsync("/api/products/catalog/export")).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Test-Deny-Action","PRODUCTS:CREATE");
        Check((await client.PostAsJsonAsync("/api/products/catalog/commit",new{expectedProfileVersion=7,products=new[]{Product("Denied item","DENIED-1")},reviewed=true})).StatusCode==HttpStatusCode.Forbidden,"catalog import requires product create permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var gallery=Product("Gallery product","GALLERY-1");gallery.ProductImages=[new(){FileName="front.png",ContentType="image/png",Data=png},new(){FileName="back.png",ContentType="image/png",Data=png}];
        var galleryResponse=await client.PostAsJsonAsync("/api/products",gallery);galleryResponse.EnsureSuccessStatusCode();
        var gallerySaved=(await galleryResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
        Check(gallerySaved.ProductImages.Count==2&&gallerySaved.ProductImages.All(image=>image.ImageId.HasValue&&image.Data is null&&image.Url!.Contains("/images/")),"multiple uploads return ordered metadata and protected URLs without image bytes");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","PRODUCTS:VIEW");
        Check((await client.GetAsync($"/api/products/{gallerySaved.ProductId}/images/{gallerySaved.ProductImages[0].ImageId}")).StatusCode==HttpStatusCode.Forbidden,"gallery image URL requires product view permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var invalidGallery=Product("Bad gallery","BAD-GALLERY");invalidGallery.ProductImages=[new(){FileName="fake.png",ContentType="image/png",Data=[1,2,3]}];
        Check((await client.PostAsJsonAsync("/api/products",invalidGallery)).StatusCode==HttpStatusCode.BadRequest,"invalid image signature rejected");
        invalidGallery.ProductImages=Enumerable.Range(0,6).Select(i=>new ProductGalleryImage{FileName=$"{i}.png",ContentType="image/png",Data=png}).ToList();
        Check((await client.PostAsJsonAsync("/api/products",invalidGallery)).StatusCode==HttpStatusCode.BadRequest,"gallery rejects more than five uploads");
        state.ProductImageGalleryAvailable=false;
        Check((await client.PostAsJsonAsync("/api/products",gallery)).StatusCode==HttpStatusCode.BadRequest,"gallery uploads are blocked before manual migration 053");
        state.ProductImageGalleryAvailable=true;
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/catalog-review-fixtures");Directory.CreateDirectory(output);
        foreach(var (url,name) in new[]{("/Products","Products"),("/Brands","Brands"),("/Categories/Attributes","Attributes"),("/Receiving","Receiving")}){
            var response=await client.GetAsync(url);response.EnsureSuccessStatusCode();await File.WriteAllTextAsync(Path.Combine(output,name+".html"),await response.Content.ReadAsStringAsync());
        }
        await File.WriteAllTextAsync(Path.Combine(output,"Schema.json"),JsonSerializer.Serialize(schema,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Console.WriteLine("PASS: catalog review server checks with fake repositories. SQL and real stock acceptance remain manual.");
    }
}
