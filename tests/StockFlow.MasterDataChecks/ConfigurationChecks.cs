namespace StockFlow.MasterDataChecks;

internal static class ConfigurationChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/configuration-ui-fixtures");
        Directory.CreateDirectory(output);
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        Check((await admin.GetAsync("/Configuration")).Headers.Location?.OriginalString == "/Categories", "Configuration opens first permitted module in registration order");
        foreach (var module in new[] { "Products", "Categories", "Brands", "Suppliers" })
        {
            var html = await admin.GetStringAsync("/" + module);
            Check(html.Contains("<dialog id=\"editor\"") && html.Contains("sf-catalog-nav") && !html.Contains("configuration-sidebar") && !html.Contains("class=\"configuration-menu\""), module + " renders main sidebar navigation and registration popup without an inner menu");
            await File.WriteAllTextAsync(Path.Combine(output, module + ".html"), html);
        }
        var products = await admin.GetStringAsync("/Products");
        var receiving = await admin.GetStringAsync("/Purchases/Create");
        const string sharedFields = "<fieldset id=\"purchase-registration-fields\">[\\s\\S]*?</fieldset>";
        var setupFields = System.Text.RegularExpressions.Regex.Match(products, sharedFields).Value;
        Check(setupFields.Length > 0 && !receiving.Contains("id=\"purchase-registration\"") && receiving.Contains("id=\"goods-register\"") && receiving.Contains("href=\"/Products/Create\""),
            "Receive Goods navigates to the existing product registration screen");
        Check(products.Contains("/js/product-registration-form.") && !receiving.Contains("/js/product-registration-form."),
            "Product registration stays in Products without a receiving-specific editor");
        Check(System.Text.RegularExpressions.Regex.Matches(products, "href=\"/Products\"").Count == 1 && !products.Contains("configuration-submenu"), "Products appears once in the Configuration menu");
        var catalogGroups = System.Text.RegularExpressions.Regex.Matches(products, "<details class=\"sf-nav-group sf-catalog-nav\"[\\s\\S]*?</details>").Select(match => match.Value).ToArray();
        var catalogMenu = string.Join("", catalogGroups);
        Check(catalogGroups.Length == 1 && catalogGroups[0].Contains("Product Catalog") && !products.Contains("Business Partners"), "Only Product Catalog has a nested subgroup");
        Check(!catalogGroups[0].Contains("href=\"/Suppliers\"") && !catalogGroups[0].Contains("href=\"/Customers\""), "Suppliers and Customers are direct workflow links, outside Product Catalog");
        var orderedRoutes = new[] { "/Products", "/Categories", "/Brands" };
        var positions = orderedRoutes.Select(route => catalogMenu.IndexOf("href=\"" + route + "\"", StringComparison.Ordinal)).ToArray();
        Check(positions.All(position => position >= 0) && positions.SequenceEqual(positions.Order()), "Product Catalog lists Products, Categories and Brands in order");
        Check(products.IndexOf(catalogGroups[0], StringComparison.Ordinal) < products.IndexOf("id=\"main-content\"", StringComparison.Ordinal), "Catalog submenu belongs to the main sidebar");
        using var reader = factory.CreateClient(new() { AllowAutoRedirect = false });
        await File.WriteAllTextAsync(Path.Combine(output, "Products-Variants.html"), await admin.GetStringAsync("/Products/Variants"));
        reader.DefaultRequestHeaders.Add("Test-Role", "Sales");
        reader.DefaultRequestHeaders.Add("Test-Deny", "PRODUCTS,BRANDS");
        Check((await reader.GetAsync("/Configuration")).Headers.Location?.OriginalString == "/Categories", "Configuration skips unauthorized modules");
        var readOnly = await reader.GetStringAsync("/Categories");
        Check(!readOnly.Contains("id=\"add-record\"") && !readOnly.Contains("href=\"/Suppliers\"") && !readOnly.Contains("href=\"/Products\"") && !readOnly.Contains("href=\"/Brands\""), "Read-only user cannot add or navigate to restricted catalog modules");
        await File.WriteAllTextAsync(Path.Combine(output, "Categories-readonly.html"), readOnly);
        reader.DefaultRequestHeaders.Remove("Test-Deny");
        reader.DefaultRequestHeaders.Add("Test-Deny", "PRODUCTS,CATEGORIES,BRANDS,SUPPLIERS,CUSTOMERS");
        Check((await reader.GetAsync("/Configuration")).Headers.Location?.OriginalString == "/Account/AccessDenied", "Configuration denied without any child permission");
        Check((await reader.GetAsync("/Products")).Headers.Location?.OriginalString == "/Account/AccessDenied", "Direct module URL still protected");
        using var customerOnly = factory.CreateClient(new() { AllowAutoRedirect = false });
        customerOnly.DefaultRequestHeaders.Add("Test-Role", "Admin");
        customerOnly.DefaultRequestHeaders.Add("Test-Deny", "PRODUCTS,CATEGORIES,BRANDS,SUPPLIERS");
        Check((await customerOnly.GetAsync("/Configuration")).Headers.Location?.OriginalString == "/Customers", "Customer-only user can open Configuration");
        var customers = await customerOnly.GetStringAsync("/Customers");
        Check(customers.Contains("configuration-layout") && customers.Contains("class=\"sf-nav-child active\" href=\"/Customers\"") && customers.Contains("/css/configuration.") && !customers.Contains("/css/operational-screens."), "Customers retains its full-width layout and active workflow link");
        Check(!customers.Contains("href=\"/Suppliers\""), "Customer-only Configuration hides unauthorized modules");
        Check(!customers.Contains("aria-label=\"Product Catalog\"") && !customers.Contains("Business Partners") && customers.Contains("data-nav-group=\"Customer Relationships\""), "Customer-only access uses Customer Relationships without an empty catalog");
        await File.WriteAllTextAsync(Path.Combine(output, "Customers.html"), customers);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Check((int)(await anonymous.GetAsync("/Configuration")).StatusCode == 401, "Configuration requires sign-in");
    }
}
