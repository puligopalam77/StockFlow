using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Warranty;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;

internal static class WarrantyChecks
{
    public static async Task Run(Factory factory)
    {
        using var anonymous = factory.CreateClient();
        Check((int)(await anonymous.GetAsync("/api/warranty/1")).StatusCode == 401, "anonymous warranty denied");
        foreach (var role in new[] { "Admin", "Sales", "Service" })
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("Test-Role", role); client.DefaultRequestHeaders.Add("Test-UserId", "1");
            var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
            foreach (var path in new[] { "/Warranty/Lookup", "/Warranty/Claims", "/Warranty/Details/1", "/Warranty/Expiring",
                "/api/warranty/1", "/api/warranty/serial/BAT001", "/api/warranty/invoice/INV-1", "/api/warranty/expiring", "/api/warranty/claims", "/api/warranty/1/history" })
                Check((await client.GetAsync(path)).IsSuccessStatusCode, role + " reads " + path);
            Check((int)(await client.GetAsync("/api/warranty/999")).StatusCode == 404, "missing warranty");
            Check((int)(await client.GetAsync("/api/warranty/expiring?days=0")).StatusCode == 400, "invalid expiry window");
            Check((int)(await client.GetAsync("/api/warranty/claims?status=UNKNOWN")).StatusCode == 400, "invalid claim filter");
            var response = await client.PostAsJsonAsync("/api/warranty/claims", new CreateWarrantyClaimRequest { RequestId = Guid.NewGuid(), WarrantyId = 1, Complaint = "Not charging" });
            Check((int)response.StatusCode == (role == "Sales" ? 403 : 200), role + " create permission");
            var request = new ResolveWarrantyClaimRequest { RequestId = Guid.NewGuid(), ExpectedStatus = "OPEN", ClaimStatus = "APPROVED", ResolutionNotes = "Checked" };
            Check((int)(await client.PutAsJsonAsync("/api/warranty/claims/1", request)).StatusCode == (role == "Sales" ? 403 : 200), role + " update permission");
            if (role != "Sales")
            {
                request.ClaimStatus = "REPLACED"; request.ResolutionType = "REPLACEMENT"; request.ReplacementProductUnitId = 2;
                Check((int)(await client.PutAsJsonAsync("/api/warranty/claims/1", request)).StatusCode == 400, "unapproved replacement denied");
                client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
                Check((int)(await client.PostAsJsonAsync("/api/warranty/claims", new { })).StatusCode == 400, "claim CSRF required");
            }
        }
        Reject(() => WarrantyRules.Validate(new CreateWarrantyClaimRequest { RequestId = Guid.NewGuid(), WarrantyId = 1, Complaint = " " }), "empty complaint");
        Reject(() => WarrantyRules.Validate(new ResolveWarrantyClaimRequest { RequestId = Guid.NewGuid(), ExpectedStatus = "APPROVED", ClaimStatus = "REPLACED", ResolutionType = "REPLACEMENT", ResolutionNotes = "Replace", ReplacementProductUnitId = 2 }), "explicit replacement rule required");
        Reject(() => WarrantyRules.Validate(new ResolveWarrantyClaimRequest { RequestId = Guid.NewGuid(), ExpectedStatus = "CLOSED", ClaimStatus = "APPROVED", ResolutionNotes = "Retry" }), "terminal claim cannot reopen");
        WarrantyRules.Validate(new ResolveWarrantyClaimRequest { RequestId = Guid.NewGuid(), ExpectedStatus = "APPROVED", ClaimStatus = "REPLACED", ResolutionType = "REPLACEMENT", ResolutionNotes = "Replace", ReplacementProductUnitId = 2, ReplacementWarrantyRule = "CONTINUE_ORIGINAL" });
        Console.WriteLine("PASS: warranty page/API access, role permissions, CSRF, filters and claim transition validation (fake repository; no database writes).");
    }
    static void Reject(Action action, string label) { try { action(); } catch (ValidationException) { return; } throw new Exception("FAIL: " + label); }
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); }
}
internal sealed class FakeWarrantyRepository : IWarrantyRepository
{
    static WarrantyLookupResponse Item() => new() { WarrantyId=1, ProductId=1, ProductUnitId=1, SerialNumber="BAT001", InvoiceNumber="INV-1", WarrantyStatus="ACTIVE", IsUnderWarranty=true, CanCreateClaim=true, WarrantyStartDate=DateTime.Today, WarrantyEndDate=DateTime.Today.AddMonths(12) };
    public Task<WarrantyLookupResponse?> GetByIdAsync(long id) => Task.FromResult<WarrantyLookupResponse?>(id == 1 ? Item() : null);
    public Task<WarrantyLookupResponse?> GetBySerialAsync(string serial) => Task.FromResult<WarrantyLookupResponse?>(Item());
    public Task<WarrantyLookupResponse?> GetByUnitAsync(long id) => GetByIdAsync(id);
    public Task<IEnumerable<WarrantyLookupResponse>> GetByInvoiceAsync(string invoice) => Task.FromResult<IEnumerable<WarrantyLookupResponse>>([Item()]);
    public Task<IEnumerable<WarrantyLookupResponse>> GetExpiringAsync(int days,int offset) => Task.FromResult<IEnumerable<WarrantyLookupResponse>>([Item()]);
    public Task<IEnumerable<WarrantyClaimResponse>> GetClaimsAsync(long? warrantyId,string? status,int offset) => Task.FromResult<IEnumerable<WarrantyClaimResponse>>([]);
    public Task<IEnumerable<WarrantyEventResponse>> GetHistoryAsync(long id,int offset) => Task.FromResult<IEnumerable<WarrantyEventResponse>>([]);
    public Task<long> RegisterAsync(long id,int userId) => Task.FromResult(1L);
    public Task<long> CreateClaimAsync(CreateWarrantyClaimRequest request,int userId) => Task.FromResult(1L);
    public Task<bool> ResolveClaimAsync(long id,ResolveWarrantyClaimRequest request,int userId) => Task.FromResult(true);
    public Task<bool> SetCoverageAsync(long id,WarrantyCoverageRequest request,int userId) => Task.FromResult(true);
}
