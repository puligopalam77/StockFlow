namespace StockFlow.MasterDataChecks;
internal static class ListScreenFixtures
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/list-screen-fixtures");
        Directory.CreateDirectory(output);
        foreach (var url in new[] { "/Sales", "/Purchases", "/StockInflow", "/StockOutflow", "/Inventory", "/Inventory/LowStock", "/Products", "/Products/Variants", "/Categories", "/Brands", "/Suppliers", "/Customers", "/Admin/Users", "/Admin/AuditLogs", "/Admin/Stores", "/Service", "/Service/Pending", "/Warranty/Lookup", "/Warranty/Claims", "/Warranty/Expiring", "/Transfers", "/Account/Sessions", "/Search", "/Reports/Stores", "/Reports/Activity", "/Reports/ProductSales" })
        {
            var html = await client.GetStringAsync(url);
            if (!html.Contains("sf-list-screen") || !html.Contains("list-screens.")) throw new Exception("Shared list styling missing: " + url);
            await File.WriteAllTextAsync(Path.Combine(output, url.Trim('/').Replace('/', '-') + ".html"), html);
        }
        var invoice = await client.GetStringAsync("/Sales/Invoice/3");
        await File.WriteAllTextAsync(Path.Combine(output,"Sales-Details.html"),await client.GetStringAsync("/Sales/Details/1"));
        if (invoice.Contains("sf-list-screen") || invoice.Contains("list-screens.css")) throw new Exception("Invoice print layout received list styles.");
        Console.WriteLine("PASS: operational, setup, admin, service and report lists load shared styling; invoices excluded.");
    }
}
