namespace StockFlow.MasterDataChecks;

internal static class CaptionHelpFixtures
{
    public static async Task Write(Factory factory)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/caption-help-fixtures");
        Directory.CreateDirectory(output);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        foreach (var route in new[] { "/Products", "/Suppliers", "/Customers", "/Receiving", "/Purchases/Create", "/Sales/Create", "/Inventory", "/Transfers", "/Service/Create", "/Warranty/Details/1", "/Admin/Users", "/Admin/Roles", "/Admin/Security", "/Settings/Company", "/Configuration/Notifications", "/Account/TwoFactor" })
        {
            var response = await client.GetAsync(route);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            if (!html.Contains("/js/common/caption-help.") || !html.Contains("/css/caption-help."))
                throw new Exception("Shared caption help is missing on " + route);
            await File.WriteAllTextAsync(Path.Combine(output, route.Trim('/').Replace('/', '-') + ".html"), html);
        }
        Console.WriteLine("PASS: caption help is available across 16 module and account screens; browser fixtures rendered.");
    }
}
