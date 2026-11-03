using Microsoft.Playwright;
internal static class SalesUiChecks
{
    public static async Task Run(IPage page, string baseUrl)
    {
        if (baseUrl != "http://localhost:5232" || Environment.GetEnvironmentVariable("STOCKFLOW_UI_DISPOSABLE_DATABASE") != "phase9_test")
            throw new Exception("Sales browser writes require the explicitly configured disposable phase9_test server on localhost:5232.");
        if (Environment.GetEnvironmentVariable("STOCKFLOW_UI_INVOICE_ONLY") is { Length: > 0 } invoiceId)
        {
            await page.GotoAsync(baseUrl + "/Sales/Details/" + invoiceId);
            await page.WaitForFunctionAsync("document.querySelector('#sale-title').textContent.startsWith('INV-')");
            foreach (var width in new[] { 1440, 390, 320 })
            {
                await page.SetViewportSizeAsync(width, 900);
                await page.ScreenshotAsync(new() { Path = $"tests/bin/ui-screens/sales-invoice-{width}.png", FullPage = true });
                if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"))
                    throw new Exception("Invoice overflow: " + await page.EvaluateAsync<string>("JSON.stringify([...document.querySelectorAll('main *')].filter(e=>e.getBoundingClientRect().right>innerWidth).map(e=>({tag:e.tagName,id:e.id,cls:e.className,right:e.getBoundingClientRect().right})))"));
            }
            Console.WriteLine("PASS: invoice responsive layout"); return;
        }
        await page.GotoAsync(baseUrl + "/Settings/Company");
        await page.Locator("#Input_CompanyName").FillAsync("Browser Test Store");
        await page.Locator("#Input_Email").FillAsync("billing@example.test");
        await page.GetByRole(AriaRole.Button,new() { Name="Save company details" }).ClickAsync();
        await page.GetByText("Company details saved. Newly completed sales will use these details.",new() { Exact=true }).WaitForAsync();
        await page.GotoAsync(baseUrl + "/Sales/Create");
        foreach (var width in new[] { 1440, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.WaitForTimeoutAsync(300);
            if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth")) throw new Exception("Sales page overflow: " + width);
        }
        await page.SetViewportSizeAsync(1440, 900);
        async Task Scan(string code) { await page.Locator("#sale-scan").FillAsync(code); await page.Locator("#sale-scan").PressAsync("Enter"); }
        await Scan("FILTER-001"); await page.Locator("#sale-cart tr").WaitForAsync();
        await Scan("FILTER-001"); await page.WaitForFunctionAsync("document.querySelector('#sale-cart input').value === '2'");
        await Scan("MODEL-001"); await page.Locator("#unit-picker:not([hidden])").WaitForAsync();
        await page.Locator("#sale-unit option[value='5']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.Locator("#sale-unit").SelectOptionAsync("5"); await page.Locator("#add-unit").ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('#sale-cart tr').length === 2");
        await Scan("BAT005"); await page.GetByText("This serial number is already in the cart.", new() { Exact = true }).WaitForAsync();
        await Scan("BAT004"); await page.GetByText("This product or physical unit is unavailable.", new() { Exact = true }).WaitForAsync();
        await page.Locator("#sale-cart tr").Nth(1).GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Scan("BAT005"); await page.WaitForFunctionAsync("document.querySelectorAll('#sale-cart tr').length === 2");
        await page.Locator("#billing-new-customer summary").ClickAsync();
        await page.Locator("#new-customer-name").FillAsync("Browser sales customer");
        await page.Locator("#new-customer-mobile").FillAsync("9876543210"); await page.Locator("#create-customer").ClickAsync();
        await page.WaitForFunctionAsync("document.querySelector('#sale-customer').value !== ''");
        await page.Locator("#billing-options summary").ClickAsync();
        await page.Locator("#sale-cart tr").First.Locator("input").Nth(2).FillAsync("2");
        await page.Locator("#sale-discount").FillAsync("1");
        await page.Locator("#refresh-quote").ClickAsync();
        await page.WaitForFunctionAsync("document.querySelector('#sale-totals').textContent.includes('Total: 138.24')");
        await page.Locator("#use-split-payment").CheckAsync();
        await page.Locator("#sale-payment-inputs input[type=number]").First.FillAsync("100");
        await page.Locator("#add-payment").ClickAsync();
        await page.Locator("#sale-payment-inputs select").Nth(1).SelectOptionAsync("UPI");
        await page.Locator("#sale-payment-inputs input[type=text]").Nth(1).FillAsync("UI-reference");
        var before = await page.EvaluateAsync<int>("fetch('/api/sales').then(r=>r.json()).then(rows=>rows.length)");
        var loseFirstResponse = true;
        await page.RouteAsync("**/api/sales", async route =>
        {
            if (route.Request.Method != "POST" || !loseFirstResponse) { await route.ContinueAsync(); return; }
            loseFirstResponse = false;
            var result = await route.FetchAsync();
            if (result.Status != 201) throw new Exception("Browser sale failed: " + await result.TextAsync());
            await route.AbortAsync(); // The server committed, but the browser never receives confirmation.
        });
        await page.Locator("#complete-sale").ClickAsync();
        await page.Locator("#confirm-payment").ClickAsync();
        await page.Locator("#pending-sale:not([hidden])").WaitForAsync();
        await page.WaitForFunctionAsync("!document.querySelector('#retry-sale').disabled");
        await page.ReloadAsync(); await page.Locator("#retry-sale").ClickAsync();
        await page.WaitForURLAsync("**/Sales/Invoice/*");
        if (!(await page.Locator(".invoice-paper").InnerTextAsync()).Contains("Browser Test Store")) throw new Exception("New invoice did not use saved company settings");
        var completedSaleId = page.Url.Split('/').Last();
        await page.GotoAsync(baseUrl + "/Sales/Details/" + completedSaleId);
        await page.WaitForFunctionAsync("document.querySelector('#sale-title').textContent.startsWith('INV-')");
        if (!(await page.Locator("#sale-lines").InnerTextAsync()).Contains("BAT005") || !(await page.Locator("#sale-lines").InnerTextAsync()).Contains("ACTIVE")) throw new Exception("Invoice serial/warranty missing");
        if (await page.Locator("#sale-payments tr").CountAsync() != 2) throw new Exception("Split payment missing");
        var after = await page.EvaluateAsync<int>("fetch('/api/sales').then(r=>r.json()).then(rows=>rows.length)");
        if (after != before + 1) throw new Exception("Retry posted more than one sale");
        if (await page.EvaluateAsync<bool>("fetch('/api/scan?code=BAT005').then(r=>r.json()).then(r=>r.isAvailable)")) throw new Exception("Sold serial still available");
        foreach (var width in new[] { 1440, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.WaitForTimeoutAsync(300);
            if (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth")) throw new Exception("Invoice overflow: " + width);
            await page.ScreenshotAsync(new() { Path = $"tests/bin/ui-screens/sales-invoice-{width}.png", FullPage = true });
        }
        await page.GotoAsync(baseUrl + "/Sales"); await page.Locator("#sales-rows a").First.WaitForAsync();
        await page.GotoAsync(baseUrl + "/Customers"); await page.GetByText("Sales history", new() { Exact = true }).First.WaitForAsync();
        Console.WriteLine("PASS: real sales browser workflow, barcode quantity, serial picker, duplicate/damage checks, customer creation, split payments, invoice/warranty, mobile layout and retry after lost response. Disposable database only.");
    }
}
