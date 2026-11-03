using Microsoft.Extensions.Configuration;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class DatabaseReadChecks
{
    public static async Task Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName, "src", "StockFlow.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true).AddEnvironmentVariables().Build();
        var factory = new DbConnectionFactory(config);
        var inventory = new InventoryRepository(factory);
        await inventory.GetBalancesAsync(null, false, true);
        await inventory.GetHistoryAsync(null, null, null, 0);
        await inventory.GetUnitsAsync(null, null, false, 0);
        Console.WriteLine("PASS: inventory balances, history and units SQL executed (read only).");
        await new ScanRepository(factory).FindByCodeAsync("__phase8_readonly_probe__");
        Console.WriteLine("PASS: unified scan SQL executed against PostgreSQL (read only).");
        var categories = new CategoryRepository(factory);
        var brands = new BrandRepository(factory);
        var suppliers = new SupplierRepository(factory);
        var customers = new CustomerRepository(factory);
        var products = new ProductRepository(factory);
        await categories.GetAllAsync(); await categories.GetAllAsync(true, "Battery"); await categories.GetByIdAsync(-1); await categories.NameExistsAsync("Battery");
        await brands.GetAllAsync(); await brands.GetAllAsync(true, "test"); await brands.GetByIdAsync(-1); await brands.NameExistsAsync("test");
        await suppliers.GetAllAsync(); await suppliers.GetAllAsync(true, "test"); await suppliers.GetByIdAsync(-1);
        await customers.GetAllAsync(); await customers.GetAllAsync(true, "9876"); await customers.GetByIdAsync(-1);
        await products.GetAllAsync(); await products.GetAllAsync(true, "test"); await products.GetByIdAsync(-1);
        await products.GetByBarcodeAsync("__read_only_check__"); await products.SkuExistsAsync("__read_only_check__"); await products.BarcodeExistsAsync("__read_only_check__");
        Console.WriteLine("PASS: all five PostgreSQL repositories executed list, search, lookup, and duplicate-check queries. No writes performed.");
    }
}
