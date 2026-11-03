using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class InventoryDatabaseChecks
{
    public static async Task Run(DbConnectionFactory factory, NpgsqlConnection connection)
    {
        var repository = new InventoryRepository(factory);
        var initial = (await repository.GetBalancesAsync(null, false, false)).Single(p => p.ProductId == 2).CurrentStock;
        var incoming = Request(2, "ADJUSTMENT_IN", 5);
        var receipts = await Task.WhenAll(repository.AdjustAsync(incoming, 1), repository.AdjustAsync(incoming, 1));
        Check(receipts[0] == receipts[1], "concurrent adjustment retries return the same receipt");
        Check((await repository.GetBalancesAsync(null, false, false)).Single(p => p.ProductId == 2).CurrentStock == initial + 5, "retry posts stock only once");
        incoming.Quantity = 6;
        await Reject(() => repository.AdjustAsync(incoming, 1), "request ID cannot be reused with changed values");
        var before = await Counts(connection);
        await Reject(() => repository.AdjustAsync(Request(2, "ADJUSTMENT_OUT", 10000), 1), "insufficient stock rejected");
        Check(await Counts(connection) == before, "failed adjustment leaves stock and audit unchanged");
        var serialIn = Request(1, "ADJUSTMENT_IN", 2); serialIn.SerialNumbers = ["ADJ-001", "ADJ-002"];
        await repository.AdjustAsync(serialIn, 1);
        var units = (await repository.GetUnitsAsync(1, "ADJ-", true, 0)).ToArray();
        Check(units.Length == 2, "serialized adjustment creates physical units");
        var damaged = Request(1, "DAMAGED", 1); damaged.ProductUnitIds = [units[0].ProductUnitId];
        await repository.AdjustAsync(damaged, 1);
        Check((await repository.GetUnitsAsync(1, "ADJ-", true, 0)).Count() == 1, "damaged unit excluded from availability");
        await Reject(() => repository.AdjustAsync(WithNewId(), 1), "same unit cannot leave stock twice");
        Check(!(await repository.GetBalancesAsync(null, false, false)).Single(p => p.ProductId == 1).HasDiscrepancy, "serial ledger remains reconciled");
        var duplicate = Request(1, "ADJUSTMENT_IN", 2); duplicate.SerialNumbers = ["ADJ-ROLLBACK", "adj-002"];
        before = await Counts(connection);
        try { await repository.AdjustAsync(duplicate, 1); throw new Exception("Duplicate serial accepted"); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { }
        Check(await Counts(connection) == before, "duplicate serial rolls back newly inserted units and audit");
        var stock = (await repository.GetBalancesAsync(null, false, false)).Single(p => p.ProductId == 2).CurrentStock;
        var outcomes = await Task.WhenAll(TryAdjust(Request(2, "ADJUSTMENT_OUT", (int)stock)), TryAdjust(Request(2, "ADJUSTMENT_OUT", (int)stock)));
        Check(outcomes.Count(ok => ok) == 1, "concurrent outgoing adjustments cannot overspend stock");
        Check((await repository.GetBalancesAsync("Filter", true, false)).Single().CurrentStock == 0, "low stock and search include zero balance");
        Check((await repository.GetHistoryAsync(1, DateTime.Today, DateTime.Today, 0)).Any(x => x.TransactionType == "DAMAGED"), "history includes damage, dates and product filters");
        Check(!(await repository.GetHistoryAsync(null, null, null, 10000)).Any(), "history pagination works");
        Check((await repository.GetUnitsAsync(null, null, false, 0)).Any(x => x.UnitStatus == "DAMAGED"), "all-unit history includes unavailable units");
        await connection.ExecuteAsync("UPDATE products SET is_active=FALSE WHERE product_id=2");
        await Reject(() => repository.AdjustAsync(Request(2, "ADJUSTMENT_IN", 1), 1), "inactive product adjustment rejected");
        Check((await repository.GetBalancesAsync(null, false, true)).Count() == 2, "inactive inventory remains available for review");
        Console.WriteLine("All inventory transaction checks passed in the isolated database.");

        async Task<bool> TryAdjust(StockAdjustmentRequest request)
        {
            try { await repository.AdjustAsync(request, 1); return true; }
            catch (ValidationException) { return false; }
        }
        StockAdjustmentRequest WithNewId()
        {
            var request = Request(1, "DAMAGED", 1); request.ProductUnitIds = damaged.ProductUnitIds; return request;
        }
    }
    private static StockAdjustmentRequest Request(int productId, string type, int quantity) => new()
    { RequestId = Guid.NewGuid(), ProductId = productId, TransactionType = type, Quantity = quantity, Reason = "Inventory test" };
    private static Task<string?> Counts(NpgsqlConnection connection) => connection.ExecuteScalarAsync<string>("""
        SELECT (SELECT COUNT(*) FROM product_units)::text || ',' || (SELECT COUNT(*) FROM inventory_transactions)::text || ',' || (SELECT COUNT(*) FROM audit_logs)::text;
        """);
    private static async Task Reject(Func<Task> action, string name)
    {
        try { await action(); } catch (ValidationException) { Check(true, name); return; }
        throw new Exception("FAIL: " + name);
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}
