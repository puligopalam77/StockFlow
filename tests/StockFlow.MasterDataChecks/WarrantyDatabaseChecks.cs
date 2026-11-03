using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Warranty;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

// Called only by the existing guarded disposable-database test harness.
internal static class WarrantyDatabaseChecks
{
    public static async Task Run(DbConnectionFactory factory, NpgsqlConnection c)
    {
        var service = new WarrantyService(new WarrantyRepository(factory));
        var original = (await service.GetBySerialAsync(" bat001 "))!;
        Check(original.IsUnderWarranty && original.CanCreateClaim, "active serial lookup");
        Check((await service.GetByInvoiceAsync(original.InvoiceNumber)).Any(w => w.WarrantyId == original.WarrantyId), "invoice lookup");
        var request = new CreateWarrantyClaimRequest { RequestId = Guid.NewGuid(), WarrantyId = original.WarrantyId, Complaint = "Battery not charging" };
        var ids = await Task.WhenAll(service.CreateClaimAsync(request,1), service.CreateClaimAsync(request,1));
        Check(ids[0] == ids[1] && (await service.GetByIdAsync(original.WarrantyId))!.ClaimCount == 1, "concurrent retry creates one claim");
        await Reject(() => service.CreateClaimAsync(new() { RequestId=Guid.NewGuid(), WarrantyId=original.WarrantyId, Complaint="Duplicate" },1));
        await service.ResolveClaimAsync(ids[0],new() { RequestId=Guid.NewGuid(), ExpectedStatus="OPEN", ClaimStatus="APPROVED", ResolutionNotes="Confirmed failure" },1);
        var replacement = new ResolveWarrantyClaimRequest { RequestId=Guid.NewGuid(), ExpectedStatus="APPROVED", ClaimStatus="REPLACED",
            ResolutionType="REPLACEMENT", ResolutionNotes="Issued replacement", ReplacementProductUnitId=3, ReplacementWarrantyRule="CONTINUE_ORIGINAL" };
        var before = await Snapshot(c);
        await Reject(() => service.ResolveClaimAsync(ids[0],replacement,1)); // Unit 3 was sold in the sales tests.
        Check(await Snapshot(c) == before, "sold replacement rolls back all changes");
        replacement.RequestId=Guid.NewGuid(); replacement.ReplacementProductUnitId=5;
        var stock = await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=1");
        await service.ResolveClaimAsync(ids[0],replacement,1);
        await service.ResolveClaimAsync(ids[0],replacement,1);
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=1") == stock-1, "replacement retry decrements stock once");
        Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory_transactions WHERE transaction_type='WARRANTY_REPLACEMENT' AND reference_id=@id",new{id=ids[0]}) == 1, "one replacement movement");
        Check(await c.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=1") == "REPLACED", "old unit retained as replaced");
        Check(await c.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=5") == "REPLACEMENT_ISSUED", "new unit issued");
        var child = (await service.GetBySerialAsync("BAT005"))!;
        Check(child.ParentWarrantyId == original.WarrantyId && child.WarrantyEndDate == original.WarrantyEndDate, "linked continued coverage");
        Check(!(await service.GetBySerialAsync("BAT001"))!.IsUnderWarranty, "old serial no longer claimable");
        Check((await service.GetHistoryAsync(child.WarrantyId,0)).Any(), "replacement registration history");
        await c.ExecuteAsync("UPDATE warranties SET warranty_start_date=CURRENT_DATE-10,warranty_end_date=CURRENT_DATE-1 WHERE warranty_id=@id",new{id=child.WarrantyId});
        Check((await service.GetByIdAsync(child.WarrantyId))!.WarrantyStatus == "EXPIRED", "computed expiry overrides stored ACTIVE");
        await Reject(() => service.CreateClaimAsync(new() { RequestId=Guid.NewGuid(), WarrantyId=child.WarrantyId, Complaint="Expired" },1));
        await c.ExecuteAsync("UPDATE warranties SET warranty_start_date=CURRENT_DATE,warranty_end_date=CURRENT_DATE+7 WHERE warranty_id=@id",new{id=child.WarrantyId});
        Check((await service.GetExpiringAsync(7,0)).Any(w => w.WarrantyId==child.WarrantyId && w.IsUnderWarranty && w.RemainingDays==7), "expiry boundary and calculated fields");
        Console.WriteLine("PASS: warranty PostgreSQL retries, expiry, claims, replacement, history and rollback.");
    }
    static Task<string> Snapshot(NpgsqlConnection c) => c.QuerySingleAsync<string>("""
        SELECT jsonb_build_object(
            'claims',(SELECT jsonb_agg(to_jsonb(x) ORDER BY warranty_claim_id) FROM warranty_claims x),
            'warranties',(SELECT jsonb_agg(to_jsonb(x) ORDER BY warranty_id) FROM warranties x),
            'units',(SELECT jsonb_agg(to_jsonb(x) ORDER BY product_unit_id) FROM product_units x),
            'movements',(SELECT jsonb_agg(to_jsonb(x) ORDER BY inventory_transaction_id) FROM inventory_transactions x),
            'events',(SELECT jsonb_agg(to_jsonb(x) ORDER BY warranty_event_id) FROM warranty_events x))::text
        """);
    static async Task Reject(Func<Task> action) { try { await action(); } catch (ValidationException) { return; } throw new Exception("Expected warranty validation rejection."); }
    static void Check(bool ok,string message) { if(!ok) throw new Exception("FAIL: " + message); }
}
