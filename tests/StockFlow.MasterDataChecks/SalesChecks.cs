using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class SalesChecks
{
    public static async Task Run(Factory factory)
    {
        using var anonymous = factory.CreateClient();
        await Expect(anonymous.GetAsync("/api/sales"), 401);
        foreach (var role in new[] { "Admin", "Sales", "Service" })
        {
            using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", role); client.DefaultRequestHeaders.Add("Test-UserId", "1");
            var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
            await Expect(client.GetAsync("/api/sales"), role == "Service" ? 403 : 200);
            await Expect(client.GetAsync("/api/sales/list"), role == "Service" ? 403 : 200);
            if(role!="Service"){
                await Expect(client.GetAsync("/api/sales/list?limit=101"),400);
                await Expect(client.GetAsync("/api/sales/list?status=INVALID"),400);
                await Expect(client.GetAsync("/api/sales/list?sort=INVALID"),400);
                await Expect(client.GetAsync("/api/sales/list?paymentMethod=INVALID"),400);
                await Expect(client.GetAsync("/api/sales/list?paymentStatus=INVALID"),400);
                await Expect(client.GetAsync("/api/sales/list?term="+new string('a',201)),400);
                await Expect(client.GetAsync("/api/sales/list?paymentMethod=CREDIT_CARD&paymentStatus=PAID&status=PARTIALLY_RETURNED&sort=due"),200);
                await Expect(client.GetAsync("/api/sales/list?from=2026-10-10&to=2026-10-01"),400);
            }
            var request = new CreateSaleRequest { RequestId = Guid.NewGuid(), CustomerId=1, Items = [new() { ProductId = 1, Quantity = 1, SellingPrice = 10 }], Payments = [new() { PaymentMode = "CASH", Amount = 11.80m }] };
            if (role == "Service") { await Expect(client.PostAsJsonAsync("/api/sales", request), 403); continue; }
            foreach (var page in new[] { "/Sales", "/Sales/Create", "/Sales/Details/1" }) await Expect(client.GetAsync(page), 200);
            await Expect(client.PostAsJsonAsync("/api/sales/quote", request), 200);
            var response = await client.PostAsJsonAsync("/api/sales", request);
            Check((int)response.StatusCode == 201 && response.Headers.Location?.ToString().Contains("/api/sales/1") == true, role + " creates sale with detail location");
            await Expect(client.GetAsync("/api/sales/1"), 200); await Expect(client.GetAsync("/api/sales/999"), 404);
            await Expect(client.DeleteAsync("/api/sales/1"), 403);
            await Expect(client.GetAsync("/api/sales?offset=-1"), 400);
            request.Items = []; await Expect(client.PostAsJsonAsync("/api/sales", request), 400);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); await Expect(client.PostAsJsonAsync("/api/sales", request), 400);
        }
        Console.WriteLine("PASS: sales roles, pages, quote, creation, details, validation, CSRF and no hard-delete endpoint.");
    }
    private static async Task Expect(Task<HttpResponseMessage> action, int status) { var response = await action; Check((int)response.StatusCode == status, $"Expected HTTP {status}, got {(int)response.StatusCode}"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
internal class FakeSalesRepository : ISalesRepository
{
    public Task<SalesListPage> GetListAsync(string? term,int? customerId,int offset,int limit,DateTime? from,DateTime? to,string? status,string sort="date",bool ascending = false, string? paymentStatus = null, string? paymentMethod = null)=>Task.FromResult(new SalesListPage());
    public Task<long?> RecoverCompletedAsync(CreateSaleRequest request,int userId)=>Task.FromResult<long?>(userId==1 ? 1L : null);
    public Task<IEnumerable<SaleResponse>> GetAllAsync(string? term, int? customerId, int offset, DateTime? from = null, DateTime? to = null) => Task.FromResult<IEnumerable<SaleResponse>>([]);
    public Task<SaleResponse?> GetByIdAsync(long id) => Task.FromResult<SaleResponse?>(id == 1 ? new() { SaleId = 1, InvoiceNumber = "INV-TEST-1" } : null);
    public Task<SaleQuote> QuoteAsync(CreateSaleRequest request) => Task.FromResult(SaleRules.Calculate(request, new Dictionary<int, decimal> { [1] = 18 }, false));
    public Task<long> CreateSaleAsync(CreateSaleRequest request, int userId) { SaleRules.Calculate(request, new Dictionary<int, decimal> { [1] = 18 }, true); SaleRules.RequireCollectedPayments(request); return Task.FromResult(1L); }
}


