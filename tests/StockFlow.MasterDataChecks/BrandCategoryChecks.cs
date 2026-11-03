using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;
internal static class BrandCategoryChecks
{
 public static async Task Run(Factory factory)
 {
  using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
  var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  async Task<int> Create(string module,object body,string key){var response=await client.PostAsJsonAsync("/api/"+module,body);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())![key]!.GetValue<int>();}
  async Task Reject(HttpResponseMessage response,string name){Check((int)response.StatusCode==400,name+" "+await response.Content.ReadAsStringAsync());}
  var battery=await Create("categories",new{categoryName="Battery"},"categoryId");
  var inverter=await Create("categories",new{categoryName="Inverter"},"categoryId");
  var brand=await Create("brands",new{brandName="Shared brand",categoryIds=new[]{battery,inverter}},"brandId");
  var batteryBrand=await Create("brands",new{brandName="Battery brand",categoryIds=new[]{battery}},"brandId");
  var batteryBrands=await client.GetFromJsonAsync<JsonArray>("/api/brands?activeOnly=true&categoryId="+battery);
  var inverterBrands=await client.GetFromJsonAsync<JsonArray>("/api/brands?activeOnly=true&categoryId="+inverter);
  Check(batteryBrands!.Count==2&&inverterBrands!.Count==1&&inverterBrands[0]!["brandId"]!.GetValue<int>()==brand,"brand filters follow category and support multiple assignments");
  await Reject(await client.PostAsJsonAsync("/api/brands",new{brandName="Unknown category brand",categoryIds=new[]{99999}}),"unknown or foreign categories rejected");
  await Reject(await client.PostAsJsonAsync("/api/brands",new{brandName="Empty category brand",categoryIds=Array.Empty<int>()}),"explicit empty category list rejected");
  await Reject(await client.PostAsJsonAsync("/api/brands",new{brandName="Duplicate category brand",categoryIds=new[]{battery,battery}}),"duplicate category assignments rejected");
  await Reject(await client.PostAsJsonAsync("/api/products",new{productName="Wrong brand inverter",categoryId=inverter,brandId=batteryBrand}),"product API rejects incompatible category/brand");
  var product=await Create("products",new{productName="Valid battery",categoryId=battery,brandId=brand},"productId");
  await Reject(await client.PutAsJsonAsync("/api/brands/"+brand,new{brandName="Shared brand",categoryIds=new[]{inverter},isActive=true}),"removing a category used by active products is rejected");
  (await client.PutAsJsonAsync("/api/brands/"+brand,new{brandName="Updated shared brand",isActive=true})).EnsureSuccessStatusCode();
  var updated=await client.GetFromJsonAsync<JsonObject>("/api/brands/"+brand);
  Check(updated!["categoryIds"]!.AsArray().Count==2,"legacy metadata updates preserve category assignments");
  Check((await client.GetFromJsonAsync<JsonObject>("/api/products/"+product))!["brandId"]!.GetValue<int>()==brand,"valid product retains brand");
  client.DefaultRequestHeaders.Add("Test-Deny","CATEGORIES");
  Check((await client.GetAsync("/api/brands/categories")).IsSuccessStatusCode && (int)(await client.GetAsync("/api/categories")).StatusCode==403,"brand category lookup works without granting category administration access");
  Console.WriteLine("Brand/category API checks passed with fake repositories.");
 }
 static void Check(bool passed,string name){if(!passed)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
}
