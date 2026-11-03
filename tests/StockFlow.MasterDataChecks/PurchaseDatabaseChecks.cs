using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class PurchaseDatabaseChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("STOCKFLOW_TEST_CONNECTION") ?? throw new Exception("An isolated test database connection is required.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Host != "127.0.0.1" || settings.Port != 55439 || settings.Database != "phase6_test") throw new Exception("Only the isolated Phase 6 test database is allowed.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in new[] { "scripts/002_create_master_tables.sql", "scripts/003_create_purchase_tables.sql", "scripts/004_create_sales_tables.sql", "scripts/005_create_warranty_service_tables.sql", "migrations/007_phase5_master_data.sql", "migrations/008_phase6_purchases.sql", "migrations/009_phase7_inventory.sql" })
            await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database", script)));
        // Check repeatability of the new migrations against the isolated database only.
        foreach (var script in new[] { "008_phase6_purchases.sql", "009_phase7_inventory.sql" })
            await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations", script)));
        await connection.ExecuteAsync("""
            INSERT INTO roles (role_name) VALUES ('Admin');
            INSERT INTO users (role_id, full_name, username, password_hash) VALUES (1, 'Test', 'test', 'not-used');
            INSERT INTO suppliers (supplier_name) VALUES ('Test Supplier');
            INSERT INTO categories (category_name) VALUES ('Test Category');
            INSERT INTO products (category_id, product_name, is_serial_tracked) VALUES (1, 'Battery', TRUE), (1, 'Filter', FALSE);
            """);
        var factory = new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
        var repository = new PurchaseRepository(factory);
        var first = Request(true, Item(1, 2, "BAT001", "BAT002"), Item(2, 10));
        first.DiscountAmount = 1m;
        var id = await repository.CreatePurchaseAsync(first, 1);
        var saved = (await repository.GetByIdAsync(id))!;
        Check(saved.Status == "COMPLETED" && saved.Items.Count == 2 && saved.TotalAmount == 12 * 11.80m - 1, "mixed purchase totals and detail");
        Check(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM product_units") == 2, "two serialized units");
        Check(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory_transactions") == 3, "one movement per unit plus quantity movement");
        Check(await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=2") == 10, "stock view quantity");
        Check(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_logs") == 1, "completion audit");
        var before = await Counts(connection);
        await Reject(() => repository.CreatePurchaseAsync(Request(true, Item(2, 4), Item(1, 2, "NEW001", "bat001")), 1), "existing serial rejects entire mixed purchase");
        Check(await Counts(connection) == before, "rollback restores purchases, items, units, movements and audit rows");
        await Reject(() => repository.CreatePurchaseAsync(Request(true, Item(1, 3, "A", "B")), 1), "serial count mismatch");
        await Reject(() => repository.CreatePurchaseAsync(Request(true, Item(1, 1, "Same"), Item(1, 1, "same")), 1), "cross-item duplicate serials");
        await Reject(() => repository.CreatePurchaseAsync(Request(true, Item(2, 1, "unexpected")), 1), "serial on quantity product rejected");
        var draftId = await repository.CreatePurchaseAsync(Request(false, Item(1, 2, "DRAFT1")), 1);
        Check((await repository.GetByIdAsync(draftId))!.Status == "DRAFT", "draft persisted");
        Check(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory_transactions") == 3, "draft creates no stock");
        await Reject(() => repository.CompleteAsync(draftId, 1), "incomplete draft cannot complete");
        await repository.UpdateDraftAsync(draftId, Request(false, Item(1, 2, "DRAFT1", "DRAFT2")), 1);
        await Task.WhenAll(repository.CompleteAsync(draftId, 1), repository.CompleteAsync(draftId, 1));
        Check(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory_transactions WHERE reference_id=@Id", new { Id = draftId }) == 2, "concurrent completion posts stock once");
        await Reject(() => repository.UpdateDraftAsync(draftId, Request(false, Item(2, 2)), 1), "completed purchase immutable");
        var concurrent = await Task.WhenAll(repository.CreatePurchaseAsync(Request(true, Item(2, 1)), 1), repository.CreatePurchaseAsync(Request(true, Item(2, 1)), 1));
        Check(concurrent.Distinct().Count() == 2, "concurrent purchase numbers unique");
        var rounded = Request(true, Item(2, 3)); rounded.Items[0].PurchasePrice = 0.335m; rounded.Items[0].TaxPercentage = 18m;
        var roundedId = await repository.CreatePurchaseAsync(rounded, 1);
        Check((await repository.GetByIdAsync(roundedId))!.TotalAmount == 1.20m, "currency rounding consistent");
        var invalid = Request(true, Item(2, 1)); invalid.DiscountAmount = 1000;
        await Reject(() => repository.CreatePurchaseAsync(invalid, 1), "excess discount rejected");
        await connection.ExecuteAsync("UPDATE suppliers SET is_active=FALSE WHERE supplier_id=1;");
        await Reject(() => repository.CreatePurchaseAsync(Request(true, Item(2, 1)), 1), "inactive supplier rejected");
        Console.WriteLine("All purchase transaction checks passed in the isolated test database.");
        await InventoryDatabaseChecks.Run(factory, connection);
    }
    private static CreatePurchaseRequest Request(bool complete, params CreatePurchaseItemRequest[] items) => new()
    { SupplierId = 1, PurchaseDate = new DateTime(2026, 9, 28), CompleteImmediately = complete, Items = items.ToList() };
    private static CreatePurchaseItemRequest Item(int product, int quantity, params string[] serials) => new()
    { ProductId = product, Quantity = quantity, PurchasePrice = 10, TaxPercentage = 18, SerialNumbers = serials.ToList() };
    private static async Task<string?> Counts(NpgsqlConnection connection) => await connection.ExecuteScalarAsync<string>("""
        SELECT (SELECT COUNT(*) FROM purchases)::TEXT || ',' || (SELECT COUNT(*) FROM purchase_items)::TEXT || ',' ||
               (SELECT COUNT(*) FROM product_units)::TEXT || ',' || (SELECT COUNT(*) FROM inventory_transactions)::TEXT || ',' ||
               (SELECT COUNT(*) FROM audit_logs)::TEXT;
        """);
    private static async Task Reject(Func<Task> action, string name)
    {
        try { await action(); } catch (ValidationException) { Console.WriteLine("PASS: " + name); return; }
        throw new Exception("FAIL: " + name);
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}
