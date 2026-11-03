using Dapper;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class StockReceiptDatabaseReadChecks
{
    public static async Task Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName, "src", "StockFlow.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables().Build();

        var adminFactory = new DbConnectionFactory(config);
        await using var admin = adminFactory.CreateConnection();
        var context = await admin.QuerySingleAsync<ContextRow>("""
            SELECT p.tenant_id TenantId,p.created_by UserId,l.store_id StoreId,p.location_id LocationId,p.purchase_id PurchaseId
            FROM purchases p JOIN store_locations l ON l.location_id=p.location_id AND l.tenant_id=p.tenant_id
            ORDER BY p.purchase_id LIMIT 1
            """);
        var privileges = await admin.QuerySingleAsync<PrivilegeRow>("""
            SELECT has_table_privilege('stockflow_runtime','purchase_receipts','SELECT') PurchaseReceipts,
              has_table_privilege('stockflow_runtime','purchase_receipt_items','SELECT') PurchaseReceiptItems,
              has_table_privilege('stockflow_runtime','stock_intakes','SELECT') StockIntakes,
              has_table_privilege('stockflow_runtime','sale_returns','SELECT') SaleReturns,
              has_table_privilege('stockflow_runtime','stock_counts','SELECT') StockCounts
            """);
        Console.WriteLine($"Privileges: purchase_receipts={privileges.PurchaseReceipts}, purchase_receipt_items={privileges.PurchaseReceiptItems}, stock_intakes={privileges.StockIntakes}, sale_returns={privileges.SaleReturns}, stock_counts={privileges.StockCounts}");

        var scope = new BusinessScope
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            StoreId = context.StoreId,
            LocationId = context.LocationId,
            IsOwner = true,
            CanReviewStores = true
        };
        var runtimeFactory = new DbConnectionFactory(config, scope);
        var history = new StockReceiptHistoryRepository(runtimeFactory);
        await Check("receipt list", () => history.SearchAsync(new StockReceiptSearch()));
        await CheckEmpty("incorrect receipt number", () => history.SearchAsync(new StockReceiptSearch { Term = "GRN-2026-00" }));
        await Check("item movements", () => history.SearchItemsAsync(new StockReceiptSearch()));
        await Check("supplier filter", () => history.GetSuppliersAsync());
        await Check("purchase receipt list", () => new PurchaseReceiptRepository(runtimeFactory).GetAsync(context.PurchaseId));
        await Check("purchase account summary", () => new OperationsRepository(runtimeFactory).PurchaseAsync(context.PurchaseId));
    }

    private static async Task Check<T>(string name, Func<Task<T>> query)
    {
        try
        {
            await query();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {name}: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null) Console.WriteLine($"INNER: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }

    private static async Task CheckEmpty(string name, Func<Task<StockReceiptHistoryResult>> query)
    {
        var result = await query();
        if (result.TotalCount != 0) throw new InvalidOperationException($"{name} returned {result.TotalCount} rows.");
        Console.WriteLine($"PASS: {name} returns no rows");
    }

    private sealed class ContextRow
    {
        public long TenantId { get; init; }
        public int UserId { get; init; }
        public long StoreId { get; init; }
        public long LocationId { get; init; }
        public long PurchaseId { get; init; }
    }

    private sealed class PrivilegeRow
    {
        public bool PurchaseReceipts { get; init; }
        public bool PurchaseReceiptItems { get; init; }
        public bool StockIntakes { get; init; }
        public bool SaleReturns { get; init; }
        public bool StockCounts { get; init; }
    }
}
