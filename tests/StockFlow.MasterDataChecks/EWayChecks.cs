using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class EWayChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception ex) when (ex is ValidationException or UnauthorizedAccessException or KeyNotFoundException || ex is PostgresException p && p.SqlState is "23514" or "42501" or "40001" or "23505") { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }
    public static async Task Run()
    {
        const string host = "Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var database = "eway_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(host + "postgres"); await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE " + database + " TEMPLATE product_edit_069_test");
        try
        {
            await using var setup = new NpgsqlConnection(host + database); await setup.OpenAsync();
            foreach (var file in Directory.GetFiles("database/migrations", "*.sql").Where(f => int.Parse(Path.GetFileName(f)[..3]) >= 70).Order()) await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
            await setup.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false)");
            var profile = (await setup.ExecuteScalarAsync<string>("SELECT business_profile_code FROM tenants WHERE tenant_id=1"))!;
            await setup.ExecuteAsync("SELECT set_config('app.business_profile_code',@profile,false)", new { profile });
            var scope = new BusinessScope { TenantId = 1, UserId = 1, StoreId = 1, LocationId = 1, BusinessProfileCode = profile, ProfileDataVisibilityAvailable = true, CatalogRegistrationEnhancementsAvailable = true };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = host + database }).Build();
            var factory = new DbConnectionFactory(config, scope); var gst = new GstRepository(factory); var repo = new EWayRepository(factory);
            var today = DateTime.Today;
            var business = await gst.SaveBusinessAsync(new() { LegalName = "Synthetic e-way issuer", Address = "Synthetic business address", StateCode = "29", PinCode = "560001" });
            var reg = await gst.SaveRegistrationAsync(new() { BusinessId = business, Gstin = "29ABCDE1234F1ZW", StateCode = "29", Address = "Synthetic issuer address", EffectiveFrom = today.AddDays(-1) });
            await gst.SaveMappingAsync(new() { RegistrationId = reg, StoreId = 1, BusinessProfileCode = profile, EffectiveFrom = today.AddDays(-1) });
            var sku = await setup.QuerySingleAsync<(int Product, long Variant)>("SELECT p.product_id,v.variant_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.identifier_profile=@profile AND p.is_active AND v.is_active AND NOT v.is_serial_tracked ORDER BY p.product_id,v.variant_id LIMIT 1", new { profile });
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=100,mrp=1000 WHERE variant_id=@Variant", new { sku.Variant });
            await gst.SaveTaxAsync(new() { ProductId = sku.Product, VariantId = sku.Variant, HsnSac = "610910", Uqc = "NOS", TaxRate = 5, EffectiveFrom = today.AddDays(-1), ReviewReference = "Synthetic test source classification" });
            await new StockIntakeRepository(factory).PostAsync(new() { LocationId = 1, RequestId = Guid.NewGuid(), ReceivedDate = today, Items = [new() { ProductId = sku.Product, VariantId = sku.Variant, Quantity = 5 }] }, true, 1);
            var customer = await new CustomerRepository(factory).CreateAsync(new() { CustomerName = "=Eway fixture recipient", MobileNumber = "9861200992" });
            var saleRequest = new CreateSaleRequest { RequestId = Guid.NewGuid(), LocationId = 1, CustomerId = customer, GstPlaceOfSupplyStateCode = "29",
                PricesIncludeTax = (await new CompanySettingsRepository(factory).GetAsync()).DefaultTaxMode == "INCLUSIVE", Items = [new() { ProductId = sku.Product, VariantId = sku.Variant, Quantity = 1, SellingPrice = 100 }] };
            var sales = new SalesRepository(factory); saleRequest.Payments = [new() { Amount = (await sales.QuoteAsync(saleRequest)).TotalAmount }];
            var sale = await sales.CreateSaleAsync(saleRequest, 1);
            var sourceId = await setup.ExecuteScalarAsync<long>("SELECT document_id FROM gst_documents WHERE sale_id=@sale", new { sale });
            Task<string?> Financial() => setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT sum(quantity) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM gst_documents)::text");
            var before = await Financial();
            var originalSource = await repo.SourceAsync("GST", sourceId, reg);
            EWayParty Party() => new() { Name = "Synthetic actual party", Address = "Actual reviewed test address", State = "29", Pin = "560001", Gstin = "URP" };
            EWaySave New() => new() { DocumentId = Guid.NewGuid(), OperationId = Guid.NewGuid(), SourceType = "GST", SourceId = sourceId, RegistrationId = reg,
                Preparation = new() { DocumentType = "INV", MovementDocumentNumber = originalSource.GetProperty("number").GetString()!, MovementDocumentDate = today.ToString("yyyy-MM-dd"), MovementReason = "Supply; reviewed official subtype 1", TransactionType = 4,
                    BillFrom = new() { Name = "Synthetic issuer", Address = "Actual issuer test address", State = "29", Pin = "560001", Gstin = "29ABCDE1234F1ZW" }, DispatchFrom = Party(), BillTo = Party(), ShipTo = Party(), DistanceKm = 20, Mode = "ROAD", Vehicle = "KA01AB1234", ApplicabilityReview = "Synthetic authorized state/movement review, not legal advice" } };
            var save = New(); save.Preparation.BillFrom.Gstin = "29ABCDE1234F1ZW";
            Check((await Task.WhenAll(repo.SaveAsync(save), repo.SaveAsync(save))).All(v => v == 1), "Concurrent identical save creates one version/event");
            Check(await Financial() == before, "Preparation posts no additional stock, payment, sale or GST document");
            var record = await repo.GetAsync(save.DocumentId);
            Check(record.ExternalEbn is null && record.Status == "DRAFT", "New preparation has no invented official EBN or validity");
            Check(EWayPreparationRules.Validate(save.Preparation, originalSource).Issues.Length == 0, "Complete domestic worksheet passes basic data checks while applicability remains Review Required");
            var unchanged = record.SourceJson;
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=150 WHERE variant_id=@Variant", new { sku.Variant });
            Check((await repo.SourceAsync("GST", sourceId, reg)).GetProperty("total").GetDecimal() == originalSource.GetProperty("total").GetDecimal(), "Historical source values never reprice from current catalog");
            await repo.ActAsync(save.DocumentId, new(Guid.NewGuid(), 1), "VALIDATE");
            await repo.ActAsync(save.DocumentId, new(Guid.NewGuid(), 1), "EXPORT");
            var revised = JsonSerializer.Deserialize<EWaySave>(JsonSerializer.Serialize(save))!; revised.OperationId = Guid.NewGuid(); revised.ExpectedVersion = 1; revised.Preparation.TransactionType = 2;
            Check(await repo.SaveAsync(revised) == 2, "Revision preserves prior saved source/body history");
            var priorEvent = await setup.ExecuteScalarAsync<string>("SELECT snapshot->'body'->>'transactionType' FROM eway_events WHERE operation_id=@OperationId", new { save.OperationId });
            Check(priorEvent == "4", "Previous bill/ship/dispatch arrangement remains immutable");
            save.OperationId = Guid.NewGuid(); await Reject(() => repo.SaveAsync(save), "Stale version cannot overwrite a revision");
            var reference = new EWayAction(Guid.NewGuid(), 2, "123456789012", "Portal reference inspected by synthetic reviewer on test date");
            await Task.WhenAll(repo.ActAsync(save.DocumentId, reference, "RECORD_EXTERNAL"), repo.ActAsync(save.DocumentId, reference, "RECORD_EXTERNAL"));
            var external = await repo.GetAsync(save.DocumentId);
            Check(external.Status == "EXTERNAL_UNVERIFIED" && external.ExternalEbn == reference.Ebn, "External reference is idempotent and remains explicitly unverified");
            revised.ExpectedVersion = 3; revised.OperationId = Guid.NewGuid(); await Reject(() => repo.SaveAsync(revised), "External reference freezes its preparation instead of editing official Part A locally");
            var invalid = New(); invalid.Preparation.ShipTo.Pin = "BAD"; await repo.SaveAsync(invalid);
            Check(EWayPreparationRules.Validate(invalid.Preparation, originalSource).Issues.Any(i => i.Contains("PIN")), "Bad PIN produces a missing-information issue");
            await Reject(() => repo.ActAsync(invalid.DocumentId, new(Guid.NewGuid(), 1, "111111111111", "Synthetic actual portal evidence"), "RECORD_EXTERNAL"), "Incomplete data cannot be marked externally referenced through application service");
            await Reject(() => repo.SourceAsync("GST", long.MaxValue, reg), "Nonexistent source is denied");
            await Reject(() => repo.SourceAsync("GST", sourceId, reg + 1000), "Forged registration cannot reuse a source invoice");
            var unknown = New(); unknown.Preparation.TransferGoods = [new()]; await Reject(() => repo.SaveAsync(unknown), "Client cannot replace immutable invoice goods/value snapshots");
            var other = await setup.QuerySingleAsync<(long Store, long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'EWAY-OTHER','Other e-way fixture shop') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'Other counter','SALES_FLOOR' FROM s RETURNING store_id,location_id");
            var supplier = await new SupplierRepository(factory).CreateAsync(new() { SupplierName = "Synthetic inward e-way supplier", Address = "Actual supplier address", MobileNumber = "9861200993", IsActive = true });
            var purchase = await new PurchaseRepository(factory).CreatePurchaseAsync(new() { SupplierId = supplier, LocationId = 1, RequestId = Guid.NewGuid(), PurchaseDate = today,
                Items = [new() { ProductId = sku.Product, VariantId = sku.Variant, Quantity = 1, PurchasePrice = 100, TaxPercentage = 5 }] }, 1);
            var inwardSource = await gst.RecordAsync(new() { RequestId = Guid.NewGuid(), RegistrationId = reg, SupplierId = supplier, PurchaseIds = [purchase], Direction = "PURCHASE", DocumentNumber = "SUPPLIER-EWAY-1", DocumentDate = today,
                SupplierStateCode = "29", PlaceOfSupply = "29", EvidenceReference = "Synthetic actual supplier invoice evidence", Lines = [new() { ProductId = sku.Product, Description = "Supplier invoice goods", HsnSac = "610910", Uqc = "NOS", Quantity = 1, TaxRate = 5, TaxableValue = 100, Cgst = 2.5m, Sgst = 2.5m, Total = 105 }] });
            var inward = New(); inward.SourceId = inwardSource; inward.Preparation.MovementDocumentNumber = "SUPPLIER-EWAY-1";
            inward.Preparation.BillTo.Gstin = "29ABCDE1234F1ZW";
            var beforeInward = await Financial(); await repo.SaveAsync(inward);
            Check((await repo.SourceAsync("GST", inwardSource, reg)).GetProperty("direction").GetString() == "PURCHASE" && await Financial() == beforeInward,
                "Inward preparation uses actual separately recorded supplier tax invoice without treating purchase order as e-way invoice or posting stock");
            var transferRepo = new TransferRepository(factory);
            var transferId = await transferRepo.CreateAsync(new() { RequestId = Guid.NewGuid(), FromLocationId = 1, ToLocationId = other.Location, Items = [new() { VariantId = sku.Variant, Quantity = 1 }] });
            await transferRepo.ActAsync(transferId, "request", new() { RequestId = Guid.NewGuid() });
            await transferRepo.ActAsync(transferId, "approve", new() { RequestId = Guid.NewGuid() });
            var transfer = New(); transfer.SourceType = "TRANSFER"; transfer.SourceId = transferId; transfer.Preparation.DocumentType = "CHL"; transfer.Preparation.MovementDocumentNumber = "CHL-SYNTHETIC-1";
            transfer.Preparation.GoodsEvidence = "Synthetic actual delivery challan, not invoice";
            transfer.Preparation.TransferGoods = [new() { Description = "Actual challan goods", HsnSac = "610910", Quantity = 1, TaxableValue = 100, Total = 100 }];
            var beforeTransfer = await Financial(); await repo.SaveAsync(transfer);
            Check(await Financial() == beforeTransfer && !(await repo.SourceAsync("TRANSFER", transferId, reg)).TryGetProperty("total", out _), "Approved transfer links without a fake sale or inferred consignment value");
            scope.StoreId = other.Store; scope.LocationId = other.Location;
            await Reject(() => repo.GetAsync(save.DocumentId), "Same-profile other shop cannot read stored preparation");
            await Reject(() => repo.SourceAsync("GST", sourceId, reg), "Shared GSTIN does not expose another shop source");
            scope.StoreId = 1; scope.LocationId = 1;
            scope.BusinessProfileCode = profile == BusinessProfiles.Clothing ? BusinessProfiles.Footwear : BusinessProfiles.Clothing;
            await Reject(() => repo.GetAsync(save.DocumentId), "Different profile cannot read historical source or preparation"); scope.BusinessProfileCode = profile;
            scope.TenantId = 999; await Reject(() => repo.GetAsync(save.DocumentId), "Forged tenant cannot read preparation"); scope.TenantId = 1;
            scope.UserId = 2; await Reject(() => repo.GetAsync(save.DocumentId), "Ungrant​ed staff cannot access preparation via runtime SQL authority"); scope.UserId = 1;
            await using (var runtime = factory.CreateConnection()) await Reject(() => runtime.ExecuteAsync("UPDATE eway_documents SET status='DRAFT'"), "Runtime has no direct mutation privilege");
            var salesGrants = (await setup.QueryAsync<long>("SELECT p.module_action_id FROM user_action_permissions p JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id) WHERE p.tenant_id=1 AND p.user_id=1 AND m.module_code='SALES' AND p.is_allowed")).ToArray();
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.tenant_id=1 AND p.user_id=1 AND m.module_code='SALES' AND a.action_code<>'VIEW'");
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.tenant_id=1 AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='VIEW'");
            await Reject(() => repo.GetAsync(save.DocumentId), "Revoked original sales authority also hides stored e-way snapshots");
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.tenant_id=1 AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='VIEW'");
            await setup.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE tenant_id=1 AND user_id=1 AND module_action_id=ANY(@salesGrants)", new { salesGrants });
            Check(await Financial() == beforeTransfer, "All e-way actions leave transaction/source totals unchanged");
            await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repo.SaveAsync(New())));
            var paged = await repo.ListAsync(null, null, null, null, 1, 2);
            Check(paged.Total > 10 && paged.Rows.Count == 2, "More than ten persisted preparations work with server pagination and no monthly quota");
            Check(await Financial() == beforeTransfer, "Additional preparations do not duplicate transaction postings");
            var usage = JsonSerializer.SerializeToElement(await repo.UsageAsync(today, today), EWayPreparationRules.Json);
            Check(usage.GetProperty("providerRequests").GetInt32() == 0 && usage.GetProperty("monthlyStockFlowCap").ValueKind == JsonValueKind.Null && usage.GetProperty("providerCharges").ValueKind == JsonValueKind.Null, "Metering separates local operations from real provider generations; no numeric cap or invented fees");
            var blocked = new UnavailableEWayProvider(); Check(blocked.SupportedOperations.Count == 0, "Production adapter exposes no fake supported operations");
            var fixtureProvider = new FixtureProvider(); var uncertain = await fixtureProvider.ExecuteAsync(new(Guid.NewGuid(), "GENERATE", reg.ToString(), default), default);
            Check(uncertain.State == EWayProviderState.UnknownAfterTransmission && uncertain.OfficialEbn is null && fixtureProvider.Calls == 1, "Test-only adapter contract represents unknown transmission separately from confirmed generation");
            try { await blocked.ExecuteAsync(new(Guid.NewGuid(), "GENERATE", reg.ToString(), default), default); throw new Exception("Fake provider unexpectedly available"); } catch (InvalidOperationException) { Check(true, "Unavailable provider cannot transmit or invent an official response"); }
            using var original = new Factory(); using var web = original.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<IEWayRepository>(); s.AddSingleton<IEWayRepository>(repo); }));
            using var client = web.CreateClient(new() { AllowAutoRedirect = false }); client.DefaultRequestHeaders.Add("Test-Role", "Admin");
            Check((await client.PostAsJsonAsync("/api/ewaybills", New())).StatusCode == HttpStatusCode.BadRequest, "E-way mutations require antiforgery token");
            var csrf = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!["requestToken"]!.GetValue<string>());
            var apiDraft = New(); var response = await client.PostAsJsonAsync("/api/ewaybills", apiDraft); Check(response.IsSuccessStatusCode, "Actual API persists scoped source-linked draft: " + await response.Content.ReadAsStringAsync());
            var export = await client.PostAsJsonAsync("/api/ewaybills/" + apiDraft.DocumentId + "/export", new EWayAction(Guid.NewGuid(), 1)); var csv = await export.Content.ReadAsStringAsync();
            Check(export.IsSuccessStatusCode && csv.Contains("NOT A PORTAL UPLOAD") && csv.Contains("'=Eway fixture"), "Worksheet is clearly non-submission and neutralizes CSV formula injection");
            Directory.CreateDirectory("tests/bin/eway-fixtures");
            foreach (var page in new[] { "Index", "Prepare", "Provider" }) await File.WriteAllTextAsync("tests/bin/eway-fixtures/" + page + ".html", await client.GetStringAsync(page == "Index" ? "/EWayBills" : "/EWayBills/" + page));
            await File.WriteAllTextAsync("tests/bin/eway-fixtures/source.json", originalSource.GetRawText());
            await File.WriteAllTextAsync("tests/bin/eway-fixtures/transfer.json", (await repo.SourceAsync("TRANSFER", transferId, reg)).GetRawText());
            await File.WriteAllTextAsync("tests/bin/eway-fixtures/detail.json", await client.GetStringAsync("/api/ewaybills/" + apiDraft.DocumentId));
            await File.WriteAllTextAsync("tests/bin/eway-fixtures/options.json", await client.GetStringAsync("/api/ewaybills/options"));
            await File.WriteAllTextAsync("tests/bin/eway-fixtures/worksheet.csv", csv);
            Check((await client.GetAsync("/api/ewaybills/sources?page=1")).IsSuccessStatusCode, "Source selector uses real server-side scope and pagination");
            foreach (var code in BusinessProfiles.Codes)
            {
                scope.BusinessProfileCode = code;
                if (code != profile) await Reject(() => repo.GetAsync(save.DocumentId), code + " cannot inherit another profile's preparations");
            }
            scope.BusinessProfileCode = profile;
            client.DefaultRequestHeaders.Add("Test-Deny", "EWAY_BILLS"); Check((await client.GetAsync("/api/ewaybills")).StatusCode == HttpStatusCode.Forbidden, "API permission middleware denies module access before repository");
            using var anon = web.CreateClient(new() { AllowAutoRedirect = false }); Check((await anon.GetAsync("/api/ewaybills")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous cannot access e-way data");
            client.DefaultRequestHeaders.Remove("Test-Deny");
            var limited = false;
            for (var attempt = 0; attempt < 125; attempt++) if ((await client.GetAsync("/api/ewaybills/provider")).StatusCode == HttpStatusCode.TooManyRequests) { limited = true; break; }
            Check(limited, "Temporary API abuse throttle returns 429 independently of monthly usage");
            Console.WriteLine("E-way preparation checks passed in disposable database. No live provider or application database used.");
        }
        finally { NpgsqlConnection.ClearAllPools(); await admin.ExecuteAsync("DROP DATABASE " + database + " WITH (FORCE)"); }
    }
    private sealed class FixtureProvider : IEWayProvider
    {
        public int Calls { get; private set; }
        public string Name => "Test only; never registered in production";
        public IReadOnlySet<string> SupportedOperations { get; } = new HashSet<string> { "GENERATE" };
        public Task<EWayProviderOutcome> ExecuteAsync(EWayProviderCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new EWayProviderOutcome(EWayProviderState.UnknownAfterTransmission, null, null, null, null, command.AttemptId.ToString(), JsonSerializer.SerializeToElement(new { synthetic = true }), null));
        }
    }
}
