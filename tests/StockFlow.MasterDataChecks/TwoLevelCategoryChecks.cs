using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class TwoLevelCategoryChecks
{
 public static async Task Run(Factory factory)
 {
  using var c=factory.CreateClient(new(){AllowAutoRedirect=false});c.DefaultRequestHeaders.Add("Test-Role","Admin");var token=await c.GetFromJsonAsync<JsonObject>("/api/antiforgery");c.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  async Task<int> Create(string name,int? parent=null){var r=await c.PostAsJsonAsync("/api/categories",new{categoryName=name,parentCategoryId=parent});if(!r.IsSuccessStatusCode)throw new Exception(await r.Content.ReadAsStringAsync());return (await r.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();}
  int parent=await Create("  Bike   Spare Parts "),other=await Create("Lighting"),child=await Create("Filters",parent),inactive=await Create("Inactive type",parent);
  Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName="Third",parentCategoryId=child})).StatusCode==400,"API rejects third level");
  Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName="bike spare  parts"})).StatusCode==400,"normalized duplicate parent rejected");
  Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName=" filters ",parentCategoryId=parent})).StatusCode==400,"duplicate sibling rejected");await Create("Filters",other);
  var before=await c.GetFromJsonAsync<JsonObject>($"/api/categories/{child}");
  const string descriptionOnly="Archived description-only token";
  Check((await c.PutAsJsonAsync($"/api/categories/{child}",new{categoryName="Fuel Filters",parentCategoryId=parent,isActive=true,description=descriptionOnly})).IsSuccessStatusCode,"subcategory metadata edit allowed");
  var after=await c.GetFromJsonAsync<JsonObject>($"/api/categories/{child}");Check(after!["categoryId"]!.GetValue<int>()==child&&after["categoryCode"]!.GetValue<string>()==before!["categoryCode"]!.GetValue<string>(),"edit preserves category ID and code");
  async Task<JsonArray> ListSearch(string term,bool activeOnly=false)=> (await c.GetFromJsonAsync<JsonArray>("/api/categories?listSearch=true&activeOnly="+activeOnly+"&term="+Uri.EscapeDataString(term)))!;
  bool Has(JsonArray rows,int id)=>rows.Any(x=>x!["categoryId"]!.GetValue<int>()==id);
  Check(Has(await ListSearch("  bIkE  "),parent),"category-list search trims and partially matches parent names case-insensitively");
  var childSearch=await ListSearch("  fUeL  ");Check(Has(childSearch,child)&&Has(childSearch,parent)&&childSearch.Count==2,"subcategory search includes its parent without unrelated records");
  var childCode=after!["categoryCode"]!.GetValue<string>();
  Check(Has(await ListSearch(childCode.ToLowerInvariant()),child)&&Has(await ListSearch(childCode[^3..]),child),"category-list search supports full and partial category codes");
  Check((await ListSearch(descriptionOnly)).Count==0,"category-list search excludes description-only matches");
  Check(Has((await c.GetFromJsonAsync<JsonArray>("/api/categories?term="+Uri.EscapeDataString(descriptionOnly)))!,child),"existing non-list API description search remains compatible");
  Check(after["description"]!.GetValue<string>()==descriptionOnly,"category description remains saved and available to edit forms");
  Check((int)(await c.PutAsJsonAsync($"/api/categories/{parent}",new{categoryName="Bike Spare Parts",parentCategoryId=other,isActive=true})).StatusCode==400,"parent cannot be subordinated");
  Check((await c.PostAsJsonAsync($"/api/categories/{inactive}/status",new{isActive=false})).IsSuccessStatusCode,"subcategory deactivation");
  Check(!Has(await ListSearch("Inactive type",true),inactive)&&Has(await ListSearch("Inactive type"),inactive),"category-list active filtering retains existing status semantics");
  Check((await c.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=false})).IsSuccessStatusCode,"parent deactivation preserves active children");
  var hidden=await c.GetFromJsonAsync<JsonObject>($"/api/categories/{child}");Check(hidden!["isActive"]!.GetValue<bool>()&&!hidden["isEffectivelyActive"]!.GetValue<bool>(),"child status retained but effective assignment disabled");
  var choices=await c.GetFromJsonAsync<JsonArray>("/api/categories?activeOnly=true");Check(!choices!.Any(x=>x!["categoryId"]!.GetValue<int>()==child),"new assignment options exclude child of inactive parent");
  Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName="New under inactive",parentCategoryId=parent})).StatusCode==400,"inactive parent cannot accept new subcategories");
  Check((await c.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=true})).IsSuccessStatusCode,"parent activation");
  Check(!(await c.GetFromJsonAsync<JsonObject>($"/api/categories/{inactive}"))!["isActive"]!.GetValue<bool>(),"independently inactive child stays inactive");
  c.DefaultRequestHeaders.Add("Test-Deny-Action","CATEGORIES:DEACTIVATE");Check((int)(await c.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=false})).StatusCode==403,"status endpoint independently enforces permission");c.DefaultRequestHeaders.Remove("Test-Deny-Action");
  Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName="Foreign parent",parentCategoryId=int.MaxValue})).StatusCode==400,"unknown parent rejected");
  var branches=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();
  var originalProfile=branches.State.BusinessProfileCode;
  var originalAvailable=branches.State.BusinessProfileConfigurationAvailable;var originalPersisted=branches.State.BusinessProfilePersisted;
  try {
   branches.State.BusinessProfileConfigurationAvailable=true;branches.State.BusinessProfilePersisted=true;
   branches.State.BusinessProfileCode="BIKE_ACCESSORIES";
   Check(!(await c.GetFromJsonAsync<JsonArray>("/api/categories"))!.Any(x=>x!["categoryId"]!.GetValue<int>()==parent),"other shop profile cannot list the original category");
   Check((await ListSearch("Fuel")).Count==0&&(await ListSearch(childCode)).Count==0,"category-list name/code search cannot return another shop's categories");
   Check((int)(await c.GetAsync($"/api/categories/{parent}")).StatusCode==404,"other shop profile cannot open the original category");
   Check((int)(await c.PostAsJsonAsync($"/api/categories/{parent}/status",new{isActive=false})).StatusCode==404,"other shop profile cannot change category status");
   Check((int)(await c.PostAsJsonAsync("/api/categories",new{categoryName="Foreign shop child",parentCategoryId=parent})).StatusCode==400,"other shop profile cannot attach a child to the original parent");
  } finally {branches.State.BusinessProfileCode=originalProfile;branches.State.BusinessProfileConfigurationAvailable=originalAvailable;branches.State.BusinessProfilePersisted=originalPersisted;}
  Check((await c.GetFromJsonAsync<JsonObject>($"/api/categories/{child}"))!["categoryCode"]!.GetValue<string>()==before!["categoryCode"]!.GetValue<string>(),"returning to the original shop retains category identity and code");
 }
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
