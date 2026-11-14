using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
namespace StockFlow.MasterDataChecks;
internal static class CategorySimplificationChecks
{
 public static async Task Run(Factory factory)
 {
  using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  async Task<int> Create(string name,int? parent=null,string? prefix=null){var response=await client.PostAsJsonAsync("/api/categories",new{categoryName=name,parentCategoryId=parent,skuPrefix=prefix});response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();}
  var parent=await Create("Helmets",prefix:"HLM");var child=await Create("Full Face",parent);var other=await Create("Lighting");
  var categories=factory.Services.GetRequiredService<ICategoryRepository>();var products=factory.Services.GetRequiredService<IProductRepository>();
  var p1=await products.CreateAsync(new(){ProductName="Direct helmet",CategoryId=parent,IsActive=true,Sku="HLM-0001",ParentProductCode="HLM-0001",Barcode="8901234567890"});await products.CreateAsync(new(){ProductName="Archived full face",CategoryId=child,IsActive=false});await products.CreateAsync(new(){ProductName="Other category",CategoryId=other,IsActive=true});
  (await categories.GetByIdAsync(parent))!.ProductCount=1;(await categories.GetByIdAsync(child))!.ProductCount=1;
  var search=await client.GetFromJsonAsync<JsonArray>("/api/categories?listSearch=true&term=Full");var root=search!.Single(c=>c!["categoryId"]!.GetValue<int>()==parent)!;Check(root["productCount"]!.GetValue<int>()==1&&root["subtreeProductCount"]!.GetValue<int>()==2,"filtered category response retains direct and complete hierarchy counts");
  var listed=await client.GetFromJsonAsync<JsonArray>($"/api/categories/{parent}/products");Check(listed!.Count==2&&listed.Any(p=>p!["productId"]!.GetValue<int>()==p1),"View Products includes direct and child assignments without unrelated products");
  var productList=await client.GetFromJsonAsync<JsonArray>($"/api/products?categoryId={parent}&includeDescendants=true");Check(productList!.Count==2,"Product List category navigation includes direct and descendant records");
  var directList=await client.GetFromJsonAsync<JsonArray>($"/api/products?categoryId={parent}&includeDescendants=false");Check(directList!.Count==1,"Product List direct category filter excludes descendants");
  var activeList=await client.GetFromJsonAsync<JsonArray>($"/api/products?categoryId={parent}&includeDescendants=true&activeOnly=true");Check(activeList!.Count==1,"Product List category filter composes with status");
  var searchList=await client.GetFromJsonAsync<JsonArray>($"/api/products?categoryId={parent}&includeDescendants=true&term=Archived");Check(searchList!.Count==1,"Product List category filter composes with search");
  Check((await client.GetAsync("/api/products?categoryId=0")).StatusCode==HttpStatusCode.BadRequest,"Invalid Product List category filter fails closed");
  client.DefaultRequestHeaders.Add("Test-Deny-Action","CATEGORIES:VIEW");Check((await client.GetAsync($"/api/products?categoryId={parent}")).StatusCode==HttpStatusCode.Forbidden,"Category drilldown independently requires category view authorization");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
  client.DefaultRequestHeaders.Add("Test-Deny-Action","PRODUCTS:VIEW");Check((await client.GetAsync($"/api/categories/{parent}/products")).StatusCode==HttpStatusCode.Forbidden,"View Products independently requires product authorization");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
  var saved=await client.GetFromJsonAsync<JsonObject>($"/api/categories/{parent}");Check((await client.PutAsJsonAsync($"/api/categories/{parent}",new{categoryName="Road Helmets",skuPrefix="HLM",isActive=true})).IsSuccessStatusCode,"rename preserves category identity and product assignments");var renamed=await client.GetFromJsonAsync<JsonObject>($"/api/categories/{parent}");Check(renamed!["categoryCode"]!.GetValue<string>()==saved!["categoryCode"]!.GetValue<string>()&&(await products.GetByIdAsync(p1))!.CategoryId==parent,"existing code and product category ID remain unchanged");
  var intact=(await products.GetByIdAsync(p1))!;Check(intact.Sku=="HLM-0001"&&intact.ParentProductCode=="HLM-0001"&&intact.Barcode=="8901234567890","category rename preserves product SKU, parent code and barcode");
  Check((await client.PostAsJsonAsync("/api/categories",new{categoryName="Other prefix",skuPrefix="hlm"})).StatusCode==HttpStatusCode.BadRequest,"assigned prefix duplicate rejected before repository write");
  var unused=await Create("Rear View Mirrors",prefix:"REA");
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Sport & Custom Mirrors",skuPrefix="REA",expectedSkuPrefix="REA",isActive=true})).IsSuccessStatusCode&&(await categories.GetByIdAsync(unused))!.SkuPrefix=="REA","rename alone keeps unused prefix");
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Sport & Custom Mirrors",skuPrefix="SPO",isActive=true})).StatusCode==HttpStatusCode.BadRequest,"prefix replacement requires an expected current value");
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Sport & Custom Mirrors",skuPrefix="SPO",expectedSkuPrefix="REA",isActive=true})).IsSuccessStatusCode,"explicit unused prefix replacement allowed");
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Stale edit",skuPrefix="OLD",expectedSkuPrefix="REA",isActive=true})).StatusCode==HttpStatusCode.BadRequest&&(await categories.GetByIdAsync(unused))!.SkuPrefix=="SPO","stale prefix expectation rejected without overwrite");
  foreach(var invalid in new[]{"AB","1ABC","TOOLONG99","A B"})Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Invalid prefix",skuPrefix=invalid,expectedSkuPrefix="SPO",isActive=true})).StatusCode==HttpStatusCode.BadRequest,"invalid prefix rejected: "+invalid);
  (await categories.GetByIdAsync(unused))!.SkuPrefixUsed=true;
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Sport & Custom Mirrors",skuPrefix="NEW",isActive=true})).StatusCode==HttpStatusCode.BadRequest,"previously issued prefix locked even with zero assigned products");
  Check((await client.PutAsJsonAsync($"/api/categories/{unused}",new{categoryName="Renamed issued category",skuPrefix="SPO",isActive=true})).IsSuccessStatusCode&&(await categories.GetByIdAsync(unused))!.SkuPrefixUsed,"issued category rename keeps prefix and usage flag");
  (await categories.GetByIdAsync(parent))!.SkuPrefixUsed=true;Check((await client.PutAsJsonAsync($"/api/categories/{parent}",new{categoryName="Road Helmets",skuPrefix="NEW",isActive=true})).StatusCode==HttpStatusCode.BadRequest,"issued SKU prefix cannot be changed through the API");
  Check((await client.PostAsJsonAsync($"/api/categories/{child}/status",new{isActive=false})).IsSuccessStatusCode,"child soft deactivation remains available");await client.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=false});
  Check((await client.PostAsJsonAsync($"/api/categories/{child}/status",new{isActive=true})).StatusCode==HttpStatusCode.BadRequest,"status API blocks child activation under inactive parent");Check((await client.PutAsJsonAsync($"/api/categories/{child}",new{categoryName="Full Face",parentCategoryId=parent,isActive=true})).StatusCode==HttpStatusCode.BadRequest,"edit API cannot bypass parent activation rule");
  await client.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=true});Check((await client.PostAsJsonAsync($"/api/categories/{child}/status",new{isActive=true})).IsSuccessStatusCode,"child activation succeeds after parent activation");
  var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode="BIKE_ACCESSORIES";branch.State.BusinessProfileConfigurationAvailable=true;branch.State.BusinessProfilePersisted=true;Check((await client.GetAsync($"/api/categories/{parent}/products")).StatusCode==HttpStatusCode.NotFound,"another shop profile cannot list products using an unauthorized category ID");
  Check((await client.GetAsync($"/api/products?categoryId={parent}")).StatusCode==HttpStatusCode.NotFound,"Product List category filter cannot read another profile category");
  Console.WriteLine("Category simplification API checks passed; fake repositories, no database accessed.");
 }
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
