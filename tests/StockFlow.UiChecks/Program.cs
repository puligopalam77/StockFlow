using Microsoft.Playwright;

var baseUrl = Environment.GetEnvironmentVariable("STOCKFLOW_UI_URL") ?? "http://localhost:5231";
var username = Environment.GetEnvironmentVariable("STOCKFLOW_UI_USERNAME") ?? "admin";
var password = Environment.GetEnvironmentVariable("STOCKFLOW_UI_PASSWORD") ?? throw new InvalidOperationException("Set STOCKFLOW_UI_PASSWORD for the test account.");
var output = Path.GetFullPath("tests/bin/ui-screens");
Directory.CreateDirectory(output);
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
var page = await browser.NewPageAsync();
var errors = new List<string>();
page.PageError += (_, error) => errors.Add(error);
var sizes = new[] { (1672, 940), (1440, 900), (1024, 768), (768, 1024), (390, 844), (320, 700) };
foreach (var (width, height) in sizes)
{
    await page.SetViewportSizeAsync(width, height);
    await page.GotoAsync(baseUrl + "/Account/Login");
    await CheckLayout("login", width);
}
await page.Locator("#Input_Username").FillAsync(username);
await page.Locator("#Input_Password").FillAsync(password);
await page.Locator("#password-toggle").ClickAsync();
if (await page.Locator("#Input_Password").GetAttributeAsync("type") != "text") throw new Exception("Password toggle failed");
await page.Locator("#password-toggle").ClickAsync();
await page.Locator("button[type=submit]").ClickAsync();
await page.WaitForURLAsync(baseUrl + "/");
if ((await page.Locator("body").InnerTextAsync()).Contains("temporarily unavailable")) throw new Exception("Dashboard data unavailable");
if (args.Contains("--invoices"))
{
    await InvoiceUiChecks.Run(page,baseUrl);
    if(errors.Count>0) throw new Exception(string.Join("\n",errors));
    return;
}
if (args.Contains("--sales"))
{
    await SalesUiChecks.Run(page, baseUrl);
    if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
    return;
}
foreach (var (width, height) in sizes)
{
    await page.SetViewportSizeAsync(width, height);
    await CheckLayout("dashboard", width);
    if (width < 992)
    {
        await page.Locator("#sidebar-toggle").ClickAsync();
        if (await page.Locator("#sidebar-toggle").GetAttributeAsync("aria-expanded") != "true") throw new Exception("Menu failed to open");
        await page.Keyboard.PressAsync("Escape");
    }
}
await page.GotoAsync(baseUrl + "/Inventory");
await page.Locator("#balances tr").First.WaitForAsync();
if (!string.IsNullOrWhiteSpace(await page.Locator("#inventory-message").InnerTextAsync())) throw new Exception("Inventory data unavailable");
foreach (var (width, height) in sizes)
{
    await page.SetViewportSizeAsync(width, height);
    await CheckLayout("inventory", width);
}
// Mock only posting: browser checks never modify the configured application's inventory.
var adjustmentPosts = new List<string>();
var failNextAdjustment = false;
await page.RouteAsync("**/api/inventory/adjustments", async route =>
{
    if (!route.Request.Headers.ContainsKey("x-csrf-token")) throw new Exception("Adjustment missing CSRF header");
    adjustmentPosts.Add(route.Request.PostData ?? "");
    if (failNextAdjustment) { failNextAdjustment = false; await route.AbortAsync(); return; }
    await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"adjustmentId\":123}" });
});
var productOptions = page.Locator("#adjust-product option");
if (await productOptions.CountAsync() > 1)
{
    var productValue = await productOptions.Nth(1).GetAttributeAsync("value");
    await page.Locator("#adjust-product").SelectOptionAsync(productValue!);
    await page.Locator("#adjust-reason").FillAsync("Browser test (mocked, no write)");
    await page.Locator("#post-adjustment").ClickAsync();
    await page.GetByText("Adjustment #123 posted.", new() { Exact = true }).WaitForAsync();
    if (await page.EvaluateAsync<string?>("sessionStorage.getItem('stockflow-pending-adjustment')") is not null) throw new Exception("Confirmed adjustment retry state was not cleared");
    failNextAdjustment = true;
    await page.Locator("#adjust-reason").FillAsync("Uncertain response test");
    await page.Locator("#post-adjustment").ClickAsync();
    await page.GetByText("Retry the same values if the result is uncertain.", new() { Exact = false }).WaitForAsync();
    await page.ReloadAsync();
    await page.GetByText("An adjustment is awaiting confirmation.", new() { Exact = false }).WaitForAsync();
    await page.Locator("#post-adjustment").ClickAsync();
    await page.GetByText("Adjustment #123 posted.", new() { Exact = true }).WaitForAsync();
    if (adjustmentPosts[^1] != adjustmentPosts[^2]) throw new Exception("Reloaded retry changed the request or its idempotency key");
}
Console.WriteLine("PASS: inventory loading, responsive layout, adjustment CSRF, confirmed receipts and safe retry after reload (mocked writes).");
await page.GotoAsync(baseUrl + "/Scan");
await page.RouteAsync("**/api/scan?*", async route => await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"productId\":1,\"productName\":\"Scanner test\",\"matchType\":\"SERIAL_NUMBER\",\"unitStatus\":\"IN_STOCK\",\"isAvailable\":true,\"isActive\":true,\"currentStock\":3}" }));
await page.Locator("#scanInput").FillAsync("BAT001");
await page.Locator("#scanInput").PressAsync("Enter");
await page.Locator("#resultCard:not([hidden])").WaitForAsync();
if (await page.Locator("#scanInput").InputValueAsync() != "" || !await page.Locator("#scanInput").EvaluateAsync<bool>("el => el === document.activeElement")) throw new Exception("Scanner clear/focus failed");
await page.RouteAsync("**/api/suppliers?*", async route => await route.FulfillAsync(new() { ContentType = "application/json", Body = "[{\"supplierId\":1,\"supplierName\":\"Test supplier\"}]" }));
await page.RouteAsync("**/api/products/variants?*", async route => await route.FulfillAsync(new() { ContentType = "application/json", Body = "[{\"productId\":1,\"variantId\":1,\"sku\":\"SERIAL-TEST\",\"productName\":\"Serialized test\",\"isSerialTracked\":true,\"purchasePrice\":10,\"taxPercentage\":0}]" }));
await page.GotoAsync(baseUrl + "/Purchases/Create");
await page.Locator("#purchase-items select").SelectOptionAsync("1");
await page.Locator("#purchase-items input[type=number]").First.FillAsync("3");
foreach (var serial in new[] { "BAT101", "BAT102", "BAT101", "BAT103" })
{
    await page.GetByLabel("Scan serial and press Enter", new() { Exact = true }).FillAsync(serial);
    await page.GetByLabel("Scan serial and press Enter", new() { Exact = true }).PressAsync("Enter");
}
await page.WaitForTimeoutAsync(200);
if ((await page.Locator("#purchase-items textarea").InputValueAsync()).Split('\n').Length != 3) throw new Exception("Duplicate scanner serial accepted");
if (!(await page.Locator("#purchase-items").InnerTextAsync()).Contains("Serials entered: 3 / 3")) throw new Exception("Scan count mismatch");
await page.Locator("#purchase-items textarea").FillAsync("BAT102\nBAT103");
if (!(await page.Locator("#purchase-items").InnerTextAsync()).Contains("Serials entered: 2 / 3")) throw new Exception("Serial removal failed");
Console.WriteLine("PASS: scanner Enter, clear/refocus, purchase duplicate prevention, counts and removal (mock data; no writes)");
if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
Console.WriteLine("PASS: login, password visibility, live dashboard, mobile navigation and overflow at all six viewport sizes.");

async Task CheckLayout(string name, int width)
{
    await page.WaitForTimeoutAsync(300);
    if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth")) throw new Exception($"Horizontal overflow: {name} {width}");
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"{name}-{width}.png"), FullPage = true });
    Console.WriteLine($"PASS: {name} {width}px");
}
