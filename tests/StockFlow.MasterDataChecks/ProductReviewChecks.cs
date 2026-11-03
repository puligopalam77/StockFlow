using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
namespace StockFlow.MasterDataChecks;
internal static class ProductReviewChecks
{
 public static async Task Run(Factory factory)
 {
  var state=((FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>()).State;state.BusinessProfileCode=BusinessProfiles.BikeAccessories;state.BusinessProfilePersisted=true;state.BusinessProfileConfigurationAvailable=true;state.CatalogRegistrationEnhancementsAvailable=true;state.ProductImageGalleryAvailable=true;
  ((FakeBrandRepository)factory.Services.GetRequiredService<IBrandRepository>()).DetailsAvailable=true;
  using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  async Task<int> Category(string name,int? parent=null){var response=await client.PostAsJsonAsync("/api/categories",new{categoryName=name,parentCategoryId=parent});response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();}
  var root=await Category("Bike Parts & Accessories");var helmets=await Category("Helmets",root);var parts=await Category("Head Lights",root);
  var brandResponse=await client.PostAsJsonAsync("/api/brands",new{brandName="Helmet Brand",brandCode="HBR",categoryIds=new[]{helmets}});if(!brandResponse.IsSuccessStatusCode)throw new Exception(await brandResponse.Content.ReadAsStringAsync());var brand=(await brandResponse.Content.ReadFromJsonAsync<JsonObject>())!["brandId"]!.GetValue<int>();
  JsonObject Body(string code,int categoryId)=>new(){["productName"]="Product "+code,["parentProductCode"]=code,["categoryId"]=categoryId,["trackingType"]="QUANTITY",["productStatus"]="INACTIVE",["description"]="Short description",["detailedDescription"]="Detailed specifications",["variants"]=new JsonArray(new JsonObject{["variantName"]="DEFAULT",["sku"]=code+"-01",["purchasePrice"]=0,["sellingPrice"]=0,["isActive"]=false,["barcodeSource"]=null,["attributes"]=new JsonObject()})};
  var body=Body("DRAFT-001",helmets);var result=await client.PostAsJsonAsync("/api/products",body);if(!result.IsSuccessStatusCode)throw new Exception(await result.Content.ReadAsStringAsync());var product=(await result.Content.ReadFromJsonAsync<JsonObject>())!;Check(product["parentProductCode"]!.GetValue<string>()=="DRAFT-001"&&product["isActive"]!.GetValue<bool>()==false,"inactive zero-priced product retains parent code and descriptions");
  var duplicate=Body("DRAFT-001",parts);Check((int)(await client.PostAsJsonAsync("/api/products",duplicate)).StatusCode==400,"duplicate parent product code rejected");
  body=Body("LIGHT-001",parts);body["brandId"]=brand;Check((int)(await client.PostAsJsonAsync("/api/products",body)).StatusCode==400,"unassociated brand requires explicit confirmation");body["allowUnassociatedBrand"]=true;Check((int)(await client.PostAsJsonAsync("/api/products",body)).StatusCode==201,"show all brands permits a validated brand exception");
  body=Body("WARRANTY-001",helmets);body["warrantyMonths"]=12;body["warrantyProvider"]="MANUFACTURER";body["variants"]![0]!["warrantyMonths"]=12;Check((int)(await client.PostAsJsonAsync("/api/products",body)).StatusCode==201,"bike accessory warranty defaults accepted");
  body=Body("LONG-SKU",helmets);body["variants"]![0]!["sku"]=new string('A',41);Check((int)(await client.PostAsJsonAsync("/api/products",body)).StatusCode==400,"new variant SKU limited to 40 characters");
  body=Body("BAD-IMAGE",helmets);body["productImages"]=new JsonArray(new JsonObject{["fileName"]="x.png",["contentType"]="image/png",["data"]="AQI=",["variantName"]="Other product variant"});Check((int)(await client.PostAsJsonAsync("/api/products",body)).StatusCode==400,"unrelated variant image rejected");
  var mode=(await factory.Services.GetRequiredService<ICompanySettingsRepository>().GetAsync()).DefaultTaxMode=="INCLUSIVE";
  body=Body("TAX-MODE-066",helmets);body["registrationDefaults"]=new JsonObject{["purchasePrice"]=10,["sellingPrice"]=20,["catalogPricesIncludeTax"]=!mode};var staleTax=await client.PostAsJsonAsync("/api/products",body);Check((int)staleTax.StatusCode==400&&(await staleTax.Content.ReadAsStringAsync()).Contains("shop tax mode changed"),"registration rejects stale shop tax mode before saving prices");
  var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"StockFlow.sln")))dir=dir.Parent;var output=Path.Combine(dir!.FullName,"tests/bin/product-review-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Products.html"),await client.GetStringAsync("/Products"));
 }
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
