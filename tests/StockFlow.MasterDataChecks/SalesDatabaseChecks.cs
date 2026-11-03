using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class SalesDatabaseChecks
{
    public static async Task Run()
    {
        var cs = Environment.GetEnvironmentVariable("STOCKFLOW_TEST_CONNECTION") ?? throw new Exception("Disposable database required.");
        var settings = new NpgsqlConnectionStringBuilder(cs);
        if (settings.Host != "127.0.0.1" || settings.Port != 55439 || settings.Database != "phase9_test") throw new Exception("Only the disposable phase9_test database at 127.0.0.1:55439 is allowed.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
        foreach (var script in new[] { "scripts/002_create_master_tables.sql", "scripts/003_create_purchase_tables.sql", "scripts/004_create_sales_tables.sql", "scripts/005_create_warranty_service_tables.sql", "migrations/007_phase5_master_data.sql", "migrations/008_phase6_purchases.sql", "migrations/009_phase7_inventory.sql", "migrations/010_phase8_scanning.sql", "migrations/011_phase9_invoice_sequence.sql", "migrations/012_phase9_sales.sql", "migrations/012_phase9_sales.sql" })
            await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database", script)));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/013_phase10_invoices.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/013_phase10_invoices.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/014_phase11_warranty.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/014_phase11_warranty.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/015_phase12_service.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/015_phase12_service.sql")));
        await connection.ExecuteAsync("""
            INSERT INTO roles (role_name) VALUES ('Admin'),('Sales'),('Service');
            INSERT INTO users (role_id, full_name, username, password_hash) VALUES (1,'Test Admin','test',@Hash),(2,'Test Sales','sales',@Hash),(3,'Test Service','service',@Hash);
            INSERT INTO categories (category_name) VALUES ('Test Category');
            INSERT INTO customers (customer_name) VALUES ('Test Customer');
            INSERT INTO products (category_id,product_name,barcode,is_serial_tracked,selling_price,tax_percentage,warranty_months)
                VALUES (1,'Battery','MODEL-001',true,100,18,12),(1,'Filter','FILTER-001',false,10,18,0),(1,'Last item','LAST-001',false,10,0,0);
            INSERT INTO product_units (product_id,serial_number) VALUES (1,'BAT001'),(1,'BAT002'),(1,'BAT003'),(1,'BAT004'),(1,'BAT005');
            INSERT INTO inventory_transactions (product_id,product_unit_id,transaction_type,quantity,created_by)
                SELECT product_id,product_unit_id,'PURCHASE',1,1 FROM product_units;
            INSERT INTO inventory_transactions (product_id,transaction_type,quantity,created_by) VALUES (2,'PURCHASE',50,1),(3,'PURCHASE',1,1);
            """, new { Hash = new PasswordHasher<User>().HashPassword(new(), "Inventory-test-only-42") });
        var factory = new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = cs }).Build());
        var repository = new SalesRepository(factory);
        var mixed = Request(Item(1, 1, 100, 1), Item(2, 3, 10)); mixed.Items[0].DiscountAmount = 10; mixed.DiscountAmount = 1;
        var quote = await repository.QuoteAsync(mixed);
        Check(quote.Subtotal == 120 && quote.TaxAmount == 21.60m && quote.TotalAmount == 140.60m, "authoritative tax and discount totals");
        mixed.Payments = [new() { PaymentMode = "CASH", Amount = 100 }, new() { PaymentMode = "UPI", Amount = 40.60m, ReferenceNumber = "test-reference" }];
        var ids = await Task.WhenAll(repository.CreateSaleAsync(mixed, 1), repository.CreateSaleAsync(mixed, 1));
        Check(ids[0] == ids[1], "concurrent sale retry posts once");
        var sale = (await repository.GetByIdAsync(ids[0]))!;
        Check(sale.Lines.Count == 2 && sale.Payments.Count == 2 && sale.TotalAmount == 140.60m, "mixed invoice details and split payments");
        Check(await connection.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=1") == "SOLD", "unit marked sold");
        Check(sale.Lines[0].WarrantyStartDate == DateTime.Today && sale.Lines[0].WarrantyEndDate == DateTime.Today.AddMonths(12), "serialized warranty registered");
        Check(await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=2") == 47, "quantity stock reduced");
        await connection.ExecuteAsync("UPDATE products SET product_name='Battery renamed' WHERE product_id=1; UPDATE customers SET customer_name='Customer renamed' WHERE customer_id=1;");
        var historical = (await repository.GetByIdAsync(ids[0]))!;
        Check(historical.Lines[0].ProductName == "Battery" && historical.CustomerName == "Test Customer", "invoice names remain historical snapshots");
        mixed.Notes = "different";
        await Reject(() => repository.CreateSaleAsync(mixed, 1), "changed retry rejected");
        await Reject(() => repository.CreateSaleAsync(Request(Item(1, 1, 100, 1)), 1), "sold serial rejected");
        await Reject(() => repository.CreateSaleAsync(Request(Item(1, 1, 100, 2), Item(1, 1, 100, 2)), 1), "duplicate serial rejected");
        await Reject(() => repository.CreateSaleAsync(Request(Item(2, 25, 10), Item(2, 25, 10)), 1), "aggregate quantity prevents split-line overselling");
        await Reject(() => repository.CreateSaleAsync(Request(Item(1, 1, 100)), 1), "serialized product requires unit");
        await Reject(() => repository.CreateSaleAsync(Request(Item(2, 1, 10, 2)), 1), "unit on quantity-only product rejected");
        var invalidPayment = Request(Item(2, 1, 10)); invalidPayment.Payments = [new() { PaymentMode = "CASH", Amount = 1 }];
        var before = await Counts(connection);
        await Reject(() => repository.CreateSaleAsync(invalidPayment, 1), "payment mismatch rejected");
        Check(await Counts(connection) == before, "invalid payment leaves no partial sale");
        var rounded = Request(Item(2, 3, 0.335m)); var rounding = await repository.QuoteAsync(rounded);
        Check(rounding.TotalAmount == 1.20m, "decimal rounding consistent");
        // Force failure after stock, unit and warranty writes to prove full transaction rollback.
        await connection.ExecuteAsync("""
            CREATE FUNCTION reject_test_payment() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test payment failure'; END $$;
            CREATE TRIGGER reject_test_payment BEFORE INSERT ON payments FOR EACH ROW EXECUTE FUNCTION reject_test_payment();
            """);
        var rollback = Request(Item(1, 1, 100, 2)); await Pay(rollback); before = await Counts(connection);
        try { await repository.CreateSaleAsync(rollback, 1); throw new Exception("Expected payment failure"); } catch (PostgresException ex) when (ex.SqlState == "P0001") { }
        Check(await Counts(connection) == before && await connection.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=2") == "IN_STOCK", "payment failure rolls back invoice, stock, unit, warranty and audit");
        await connection.ExecuteAsync("DROP TRIGGER reject_test_payment ON payments; DROP FUNCTION reject_test_payment();");
        var first = Request(Item(3, 1, 10)); var second = Request(Item(3, 1, 10)); await Pay(first); await Pay(second);
        var outcomes = await Task.WhenAll(TrySale(first), TrySale(second));
        Check(outcomes.Count(ok => ok) == 1, "concurrent sales cannot oversell last quantity");
        first = Request(Item(1, 1, 100, 3)); second = Request(Item(1, 1, 100, 3)); await Pay(first); await Pay(second);
        outcomes = await Task.WhenAll(TrySale(first), TrySale(second));
        Check(outcomes.Count(ok => ok) == 1, "concurrent sales cannot sell same serial twice");
        var inventory = new InventoryRepository(factory);
        await inventory.AdjustAsync(new() { RequestId = Guid.NewGuid(), ProductId = 3, Quantity = 1, TransactionType = "ADJUSTMENT_IN", Reason = "Concurrent writer test" }, 1);
        var competingSale = Request(Item(3, 1, 10)); await Pay(competingSale);
        async Task<bool> TryAdjustment()
        {
            try { await inventory.AdjustAsync(new() { RequestId = Guid.NewGuid(), ProductId = 3, Quantity = 1, TransactionType = "ADJUSTMENT_OUT", Reason = "Competing withdrawal" }, 1); return true; }
            catch (ValidationException) { return false; }
        }
        outcomes = await Task.WhenAll(TrySale(competingSale), TryAdjustment());
        Check(outcomes.Count(ok => ok) == 1, "sales and inventory adjustments share stock locks");
        await inventory.AdjustAsync(new() { RequestId = Guid.NewGuid(), ProductId = 1, Quantity = 1, TransactionType = "DAMAGED", ProductUnitIds = [4], Reason = "Test damage" }, 1);
        await Reject(() => repository.CreateSaleAsync(Request(Item(1, 1, 100, 4)), 1), "damaged unit rejected");
        var credit = Request(Item(2, 1, 10)); credit.Payments = [new() { PaymentMode = "CREDIT", Amount = 11.80m }];
        await repository.CreateSaleAsync(credit, 2);
        credit.RequestId = Guid.NewGuid(); credit.CustomerId = null;
        await Reject(() => repository.CreateSaleAsync(credit, 2), "walk-in credit rejected");
        Check((await repository.GetAllAsync(null, 1, 0)).Any() && !(await repository.GetAllAsync(null, null, 1000)).Any(), "customer history and pagination");
        await connection.ExecuteAsync("UPDATE customers SET is_active=false WHERE customer_id=1;");
        var inactive = Request(Item(2, 1, 10)); await Pay(inactive);
        await Reject(() => repository.CreateSaleAsync(inactive, 1), "inactive customer rejected");
        await connection.ExecuteAsync("UPDATE customers SET is_active=true WHERE customer_id=1;");
        Console.WriteLine("All sales transaction checks passed. Disposable database only.");
        await InvoiceDatabaseChecks.Run(factory, connection);
        await WarrantyDatabaseChecks.Run(factory, connection);
        await ServiceDatabaseChecks.Run(factory, connection);
        async Task Pay(CreateSaleRequest request) => request.Payments = [new() { PaymentMode = "CASH", Amount = (await repository.QuoteAsync(request)).TotalAmount }];
        async Task<bool> TrySale(CreateSaleRequest request) { try { await repository.CreateSaleAsync(request, 1); return true; } catch (ValidationException) { return false; } }
    }
    private static CreateSaleRequest Request(params CreateSaleItemRequest[] items) => new() { RequestId = Guid.NewGuid(), CustomerId = 1, Items = items.ToList() };
    private static CreateSaleItemRequest Item(int product, int quantity, decimal price, long? unit = null) => new() { ProductId = product, Quantity = quantity, SellingPrice = price, ProductUnitId = unit };
    private static Task<string?> Counts(NpgsqlConnection connection) => connection.ExecuteScalarAsync<string>("""
        SELECT (SELECT COUNT(*) FROM sales)::text || ',' || (SELECT COUNT(*) FROM sale_items)::text || ',' || (SELECT COUNT(*) FROM payments)::text || ',' || (SELECT COUNT(*) FROM inventory_transactions)::text || ',' || (SELECT COUNT(*) FROM warranties)::text || ',' || (SELECT COUNT(*) FROM audit_logs)::text;
        """);
    private static async Task Reject(Func<Task> action, string name) { try { await action(); } catch (ValidationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
    private static void Check(bool passed, string name) { if (!passed) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}
