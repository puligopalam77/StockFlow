using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace StockFlow.MasterDataChecks;

internal static class TaxBreakdownChecks
{
    public static async Task RenderInvoice()
    {
        var invoice = new InvoiceResponse { SaleId = 901, InvoiceNumber = "TAX-TEST-901", SaleDate = DateTime.Today,
            HasHistoricalDetails = true, CustomerName = "Tax breakdown fixture", Subtotal = 300, ItemSubtotal = 300, TaxAmount = 41, TotalAmount = 341,
            Gst = new() { RegistrationType = "REGULAR", PlaceOfSupply = "27", Cgst = 20.5m, Sgst = 20.5m,
                Lines = [new() { SourceLineId = 1, Description = "Goods at five percent", TaxRate = 5, TaxableValue = 100, Cgst = 2.5m, Sgst = 2.5m }, new() { SourceLineId = 2, Description = "Goods at eighteen percent", TaxRate = 18, TaxableValue = 200, Cgst = 18, Sgst = 18 }] },
            Items = [new() { SaleItemId = 1, ProductName = "Goods at five percent", Quantity = 1, SellingPrice = 100, TaxPercentage = 5, LineTotal = 105 }, new() { SaleItemId = 2, ProductName = "Goods at eighteen percent", Quantity = 1, SellingPrice = 200, TaxPercentage = 18, LineTotal = 236 }],
            Payments = [new() { PaymentMode = "CASH", Amount = 341 }] };
        using var web = new Factory();
        using var rendered = web.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IInvoiceRepository>(new InvoiceFixture(invoice))));
        using var client = rendered.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var html = await client.GetStringAsync("/Sales/Invoice/901");
        Check(html.Contains("GST breakdown by rate") && html.Contains("GST 5%") && html.Contains("GST 18%") && html.Contains("CGST 9%") && html.Contains("SGST 9%"), "Actual Razor invoice renders recorded rates/components");
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/tax-breakdown-fixtures"); Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "Invoice.html"), html);
        Console.WriteLine("PASS tax breakdown Razor invoice: grouped rates and recorded components rendered without accessing the application database.");
    }

    private sealed class InvoiceFixture(InvoiceResponse invoice) : IInvoiceRepository
    {
        public Task<InvoiceResponse?> GetInvoiceAsync(long id) => Task.FromResult<InvoiceResponse?>(invoice);
    }

    public static void Run()
    {
        var request = new CreateSaleRequest { AllocateInvoiceDiscount = true, DiscountAmount = 30,
            Items = [new() { ProductId = 1, Quantity = 1, SellingPrice = 100 }, new() { ProductId = 2, Quantity = 1, SellingPrice = 100 }, new() { ProductId = 3, Quantity = 1, SellingPrice = 100 }] };
        var quote = SaleRules.CalculateLines(request, [5m, 18m, 0m], false);
        var intra = GstBreakdown.ForQuote(quote, true, "REGULAR", "27", "27");
        Check(quote.TaxableAmount == 180 && quote.AmountWithoutGst == 90 && quote.TaxAmount == 20.7m && quote.TotalAmount == 290.7m, "Mixed rates and allocated discount reconcile");
        Check(intra.Status == "CGST_SGST" && intra.Rates.Count == 3 && intra.Rates.Sum(r => r.SupplyValue) == quote.Subtotal && intra.Rates.Sum(r => r.TaxAmount) == quote.TaxAmount, "Rate groups reconcile with the quote");
        Check(intra.Rates.Sum(r => r.Cgst) == 10.35m && intra.Rates.Sum(r => r.Sgst) == 10.35m && intra.Rates.All(r => r.Igst == 0), "Intra-state components");
        var inter = GstBreakdown.ForQuote(quote, true, "REGULAR", "27", "29");
        Check(inter.Status == "IGST" && inter.Rates.Sum(r => r.Igst) == quote.TaxAmount && inter.Rates.All(r => r.Cgst + r.Sgst + r.Utgst == 0), "Inter-state components");
        var ut = GstBreakdown.ForQuote(quote, true, "REGULAR", "04", "04");
        Check(ut.Status == "CGST_UTGST" && ut.Rates.Sum(r => r.Utgst) == 10.35m && ut.Rates.All(r => r.Sgst == 0), "UT components");
        foreach (var missing in new[] { GstBreakdown.ForQuote(quote, false, null, null, "27"), GstBreakdown.ForQuote(quote, true, "REGULAR", "27", null) })
            Check(missing.Rates.All(r => r.Cgst + r.Sgst + r.Utgst + r.Igst == 0) && missing.Rates.Sum(r => r.TaxAmount) == quote.TaxAmount, "Unknown context never invents a component split");
        var tiny = new SaleQuote { Subtotal = .20m, TaxAmount = .02m, TotalAmount = .22m, Items = [new() { TaxPercentage = 5, TaxAmount = .01m, LineTotal = .11m }, new() { TaxPercentage = 5, TaxAmount = .01m, LineTotal = .11m }] };
        var rounding = GstBreakdown.ForQuote(tiny, true, "REGULAR", "27", "27").Rates.Single();
        Check(rounding.Cgst == .02m && rounding.Sgst == 0 && rounding.TaxAmount == .02m, "Group components sum per-line rounding, matching persisted SQL");
        request = new() { AllocateInvoiceDiscount = true, PricesIncludeTax = true, DiscountAmount = 11.80m, Items = [new() { ProductId = 1, Quantity = 1, SellingPrice = 118 }] };
        quote = SaleRules.CalculateLines(request, [18m], false);
        var inclusive = GstBreakdown.ForQuote(quote, true, "REGULAR", "27", "27").Rates.Single();
        Check(inclusive.SupplyValue == 90 && inclusive.TaxAmount == 16.2m && quote.TotalAmount == 106.2m, "Inclusive prices extract GST once after discounts");
        var noTax = SaleRules.CalculateLines(new() { Items = [new() { ProductId = 1, Quantity = 1, SellingPrice = 100 }] }, [0m], false);
        Check(GstBreakdown.ForQuote(noTax, true, "UNREGISTERED", "27", null).Status == "NO_GST" && noTax.AmountWithoutGst == 100 && noTax.TotalAmount == 100, "No-GST values remain in the bill total");
        var recorded = GstBreakdown.ForInvoice([new() { TaxRate = 18, TaxableValue = 100, Igst = 18 }, new() { TaxRate = 18, TaxableValue = 200, Igst = 36 }, new() { TaxRate = 5, TaxableValue = 100, Cgst = 2.5m, Sgst = 2.5m }]);
        Check(recorded.Count == 2 && recorded.Single(r => r.Rate == 18).Igst == 54 && recorded.Sum(r => r.TaxAmount) == 59, "Invoice groups preserve recorded historical components");
        var fixtures = new List<object>();
        foreach (var (name, inclusivePrices, rates) in new[] { ("mixed", false, new[] { 5m, 18m, 0m }), ("inclusive", true, new[] { 18m }) })
        foreach (var pos in new string?[] { null, "27", "29" })
        for (var count = 1; count <= rates.Length; count++)
        foreach (var discount in new[] { 0m, 30m })
        {
            var req = new CreateSaleRequest { AllocateInvoiceDiscount = true, PricesIncludeTax = inclusivePrices, DiscountAmount = discount,
                Items = rates.Take(count).Select((rate, index) => new CreateSaleItemRequest { ProductId = index + 1, Quantity = 1, SellingPrice = inclusivePrices ? 118 : 100 }).ToList() };
            var result = SaleRules.CalculateLines(req, rates.Take(count).ToArray(), false);
            result.TaxBreakdown = GstBreakdown.ForQuote(result, true, "REGULAR", "27", pos);
            foreach (var line in result.Items) { line.CurrentStock = 50; line.CatalogPrice = inclusivePrices ? 118 : 100; }
            fixtures.Add(new { name, inclusivePrices, pos, count, discount, quote = result });
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/tax-breakdown-fixtures"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "quotes.json"), System.Text.Json.JsonSerializer.Serialize(fixtures, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Console.WriteLine("PASS tax breakdown: mixed rates, discount allocation, inclusive/exclusive prices, intra/inter-state/UT, no GST, missing context, per-line rounding and historical invoice groups.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
