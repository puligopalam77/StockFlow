using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StockFlow.MasterDataChecks;

internal static class ShopReadinessChecks
{
    public static async Task Run(Factory factory)
    {
        void Check(bool passed, string message)
        {
            if (!passed) throw new Exception(message);
            Console.WriteLine("PASS: " + message);
        }
        var date = DateTime.Today.AddDays(-1);
        var request = new CashClosingRequest { Date = date, RequestId = Guid.NewGuid(), Fingerprint = new string('A', 64), OpeningCash = 100, CashAdded = 20, CashRemoved = 10, CountedCash = 1130, Reason = "Drawer float and owner deposit" };
        var totals = new CashTotals { SalesCash = 1000, ServiceCash = 100, CashRefunds = 30, SupplierCash = 50 };
        CashClosingRules.Validate(request, DateTime.Today);
        Check(CashClosingRules.Expected(request, totals) == 1130, "Cash closing includes receipts, refunds, supplier payments and manual movements exactly once");
        CashClosingRules.CheckReason(request, 1130);
        foreach (var invalid in new CashClosingRequest[] {
            new() { Date = date },
            new() { Date = DateTime.Today.AddDays(2), RequestId = Guid.NewGuid(), Fingerprint = new string('A',64) },
            new() { Date = date, RequestId = Guid.NewGuid(), Fingerprint = new string('A',64), CountedCash = -1 },
            new() { Date = date, RequestId = Guid.NewGuid(), Fingerprint = new string('A',64), CountedCash = 1.001m },
            new() { Date = date, RequestId = Guid.NewGuid(), Fingerprint = new string('A',64), OpeningCash = 10000000000m }
        }) {
            var rejected = false;
            try { CashClosingRules.Validate(invalid, DateTime.Today); } catch (ValidationException) { rejected = true; }
            Check(rejected, "Invalid cash closing rejected");
        }
        request.Reason = "";
        var reasonRejected = false;
        try { CashClosingRules.CheckReason(request, 1129); } catch (ValidationException) { reasonRejected = true; }
        Check(reasonRejected, "Cash differences require a reason");
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Check((int)(await anonymous.GetAsync("/api/cashclosing")).StatusCode == 401, "Anonymous cash closing rejected");
        using var staffApp = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IBranchRepository>();
            services.AddSingleton<IBranchRepository>(new FakeBranchRepository { State = new() { IsActive = true } });
        }));
        foreach (var role in new[] { "Sales", "Service" }) {
            using var staff = staffApp.CreateClient(new() { AllowAutoRedirect = false });
            staff.DefaultRequestHeaders.Add("Test-Role", role);
            Check((int)(await staff.GetAsync("/api/cashclosing")).StatusCode == 403, role + " cannot review owner cash totals");
            Check((int)(await staff.GetAsync("/CashClosing")).StatusCode is 302 or 403, role + " cannot open cash closing page");
            Check((int)(await staff.GetAsync("/Admin/Pilot")).StatusCode is 302 or 403, role + " cannot sign off shop pilot");
        }
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        Check((int)(await admin.PostAsJsonAsync("/api/cashclosing", request)).StatusCode == 400, "Cash closing requires CSRF token");
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        var report = await admin.GetFromJsonAsync<CashDayReport>("/api/cashclosing?date=" + date.ToString("yyyy-MM-dd"));
        Check(report!.Totals.NetCash == 1020, "Owner reads authoritative cash report");
        request.Fingerprint = report.Totals.Fingerprint;
        request.Reason = "Drawer float and owner deposit";
        var saved = await admin.PostAsJsonAsync("/api/cashclosing", request);
        Check(saved.IsSuccessStatusCode, "Owner saves closing with a reason");
        admin.DefaultRequestHeaders.Remove("Test-Location"); admin.DefaultRequestHeaders.Add("Test-Location", "0");
        Check((int)(await admin.GetAsync("/api/cashclosing")).StatusCode == 400, "Consolidated location cannot close cash");
        admin.DefaultRequestHeaders.Remove("Test-Location");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName,"tests/bin/shop-readiness-fixtures"); Directory.CreateDirectory(output);
        foreach (var page in new[] { "/CashClosing", "/Admin/Pilot", "/Customers", "/Sales/Create", "/Account/Login" })
            await File.WriteAllTextAsync(Path.Combine(output,page.Trim('/').Replace('/','-')+".html"),await (page == "/Account/Login" ? anonymous : admin).GetStringAsync(page));
        Console.WriteLine("Shop readiness checks passed using mocked repositories. Physical shop checks remain pending.");
    }
}

internal class FakeCashClosingRepository : ICashClosingRepository
{
    public Task<CashDayReport> GetAsync(DateTime date) => Task.FromResult(new CashDayReport {
        Date = date, Location = "Main shop", TimeZone = "Asia/Kolkata",
        Totals = new() { SalesCash = 1000, ServiceCash = 100, CashRefunds = 30, SupplierCash = 50, Fingerprint = new string('A',64) }
    });
    public Task<CashClosingRecord> SaveAsync(CashClosingRequest request)
    {
        CashClosingRules.Validate(request, DateTime.Today);
        var totals = GetAsync(request.Date).Result.Totals;
        CashClosingRules.CheckReason(request, CashClosingRules.Expected(request, totals));
        return Task.FromResult(new CashClosingRecord { ClosingId = 1, Version = 1, Date = request.Date, RequestId = request.RequestId, Fingerprint = request.Fingerprint, OpeningCash = request.OpeningCash, CashAdded = request.CashAdded, CashRemoved = request.CashRemoved, CountedCash = request.CountedCash, Reason = request.Reason, Totals = totals, ExpectedCash = CashClosingRules.Expected(request, totals), ClosedAt = DateTimeOffset.UtcNow });
    }
}
