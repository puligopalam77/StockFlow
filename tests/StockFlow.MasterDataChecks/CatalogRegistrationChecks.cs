using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
namespace StockFlow.MasterDataChecks;
internal static class CatalogRegistrationChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        async Task<int> Category(string name, int? parent = null)
        {
            var response = await client.PostAsJsonAsync("/api/categories", new { categoryName = name, parentCategoryId = parent, description = "  Details  " });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonObject>())!["categoryId"]!.GetValue<int>();
        }
        async Task Reject(HttpResponseMessage response, string description) { Check((int)response.StatusCode == 400, description + " " + await response.Content.ReadAsStringAsync()); }
        var clothing = await Category(" Clothing "); var electronics = await Category("Electronics");
        var mens = await Category("Men's Wear", clothing); var shirts = await Category("Shirts", clothing);
        await Category("Accessories", clothing); await Category("Accessories", electronics);
        var row = await client.GetFromJsonAsync<JsonObject>($"/api/categories/{shirts}");
        Check(row!["categoryPath"]!.GetValue<string>() == "Clothing / Shirts", "Full category path");
        Check(row["description"]!.GetValue<string>() == "Details", "Trimmed category description");
        await Reject(await client.PostAsJsonAsync("/api/categories", new { categoryName = " accessories ", parentCategoryId = clothing }), "Duplicate siblings rejected");
        await Reject(await client.PostAsJsonAsync("/api/categories", new { categoryName = "clothing" }), "Duplicate roots rejected");
        await Reject(await client.PostAsJsonAsync("/api/categories", new { categoryName = "Third level", parentCategoryId = shirts }), "Third level rejected");
        await Reject(await client.PutAsJsonAsync($"/api/categories/{clothing}", new { categoryName = "Clothing", parentCategoryId = shirts }), "Cycle rejected");
        await Reject(await client.PutAsJsonAsync($"/api/categories/{clothing}", new { categoryName = "Clothing", parentCategoryId = clothing }), "Self-parent rejected");
        await Reject(await client.PutAsJsonAsync($"/api/categories/{clothing}", new { categoryName = "Clothing", parentCategoryId = electronics }), "Subtree depth enforced on move");
        await Reject(await client.PostAsJsonAsync("/api/categories", new { categoryName = "Unknown parent", parentCategoryId = 99999 }), "Unknown parent rejected");
        (await client.DeleteAsync($"/api/categories/{clothing}")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/categories/{clothing}/status",new{isActive=true})).EnsureSuccessStatusCode();
        foreach (var module in new[] { "categories", "brands" })
        {
            var name = module == "categories" ? "categoryName" : "brandName";
            foreach (var invalid in new[] { " ", "x", "!!!", new string('x', 101) })
                await Reject(await client.PostAsJsonAsync("/api/" + module, new JsonObject { [name] = invalid }), "Invalid " + module + " name");
            await Reject(await client.PostAsJsonAsync("/api/" + module, new JsonObject { [name] = "Valid Name", ["description"] = new string('x', 501) }), "Description limit");
        }
        var brandResponse = await client.PostAsJsonAsync("/api/brands", new { brandName = " H&M ", description = new string('x', 500) });
        brandResponse.EnsureSuccessStatusCode(); var brand = (await brandResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        int brandId = brand["brandId"]!.GetValue<int>();
        Check(brand["brandName"]!.GetValue<string>() == "H&M" && brand["description"]!.GetValue<string>().Length == 500, "Brand punctuation, trimming and description round-trip");
        await Reject(await client.PostAsJsonAsync("/api/brands", new { brandName = "h&m" }), "Brand duplicates rejected");
        var products = factory.Services.GetRequiredService<IProductRepository>();
        var product = new Product { ProductName = "Test product", CategoryId = shirts, BrandId = brandId, IsActive = true };
        await products.CreateAsync(product);
        (await client.DeleteAsync($"/api/categories/{shirts}")).EnsureSuccessStatusCode();
        Check((await products.GetByIdAsync(product.ProductId))!.CategoryId==shirts,"Category deactivation retains active product relationship");
        await Reject(await client.DeleteAsync($"/api/brands/{brandId}"), "Active products block brand deactivation");
        await Reject(await client.PutAsJsonAsync($"/api/brands/{brandId}", new { brandName = "H&M", isActive = false }), "PUT cannot bypass deactivation protection");
        product.IsActive = false; await products.UpdateAsync(product);
        (await client.DeleteAsync($"/api/categories/{shirts}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/brands/{brandId}")).EnsureSuccessStatusCode();
        Check((await products.GetByIdAsync(product.ProductId))!.BrandId == brandId, "Inactive product relationships retained");
        await Reject(await client.PostAsJsonAsync("/api/categories", new { categoryName = "Inactive parent", parentCategoryId = shirts }), "Inactive parent rejected");
        Console.WriteLine("Catalog service/API checks passed using fake repositories. No database scripts executed.");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
}
