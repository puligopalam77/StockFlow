using Microsoft.Extensions.Configuration;
using Npgsql;
using Dapper;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class ProductSalesReadChecks
{
    public static async Task Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName, "src", "StockFlow.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true).AddEnvironmentVariables().Build();
        // Reproduce the failing request's tenant/location under RLS. PostgreSQL itself forbids writes.
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"))
        {
            Options = "-c default_transaction_read_only=on -c role=stockflow_runtime -c app.tenant_id=1 -c app.user_id=1 -c app.store_id=1 -c app.location_id=1"
        };
        var safeConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString
        }).Build();
        var factory = new DbConnectionFactory(safeConfig);
        var cashScope = new StockFlow.Application.Common.BusinessScope { TenantId=1, UserId=1, StoreId=1, LocationId=1 };
        var cash = await new CashClosingRepository(factory,cashScope,Microsoft.Extensions.Options.Options.Create(new StockFlow.Application.Common.ShopTimeOptions()),TimeProvider.System).GetAsync(new DateTime(2026,10,10));
        if(cash.Totals.SalesCount<0 || cash.Totals.ServiceCount<0 || cash.Totals.RefundCount<0 || cash.Totals.SupplierCount<0 || cash.Totals.SalesCount+cash.Totals.ServiceCount+cash.Totals.RefundCount+cash.Totals.SupplierCount>cash.Totals.MovementCount) throw new Exception("Cash movement counts do not reconcile.");
        Console.WriteLine("PASS: daily cash totals and transaction counts execute under database-enforced read-only RLS.");
        await using (var database = factory.CreateConnection())
        {
            await database.OpenAsync();
            if (await database.QuerySingleAsync<string>("SHOW default_transaction_read_only") != "on") throw new Exception("Read-only guard missing.");
        }
        var report = new OperationsRepository(factory);
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        var balances=(await new InventoryRepository(factory).GetBalancesAsync(null,false,false)).ToArray();
        if(balances.Any(r=>string.IsNullOrWhiteSpace(r.VariantName)))throw new Exception("Inventory variant details missing.");
        Console.WriteLine($"PASS: inventory product image metadata and variant details execute under read-only RLS ({elapsed.Elapsed.TotalMilliseconds:F0} ms; {balances.Length} rows).");
        var dashboardRepository=new DashboardRepository(factory);
        foreach(var seeSales in new[] {true,false})
        {
            elapsed.Restart();
            var dashboard=await dashboardRepository.GetAsync(true,seeSales);
            if(dashboard.Months.Count!=6 || dashboard.RecentInflow.Count>5 || dashboard.RecentOutflow.Count>5 || dashboard.LowStock.Count>5 || (!seeSales && (dashboard.RecentSales.Count!=0 || dashboard.Business.TodaySalesAmount is not null)))
                throw new Exception("Batched dashboard results or denied sales mapping changed.");
            Console.WriteLine($"PASS: batched dashboard read, sales access {seeSales}: {elapsed.Elapsed.TotalMilliseconds:F0} ms; read-only RLS and panel limits enforced.");
        }
        var sales = await new SalesRepository(factory).GetListAsync(null,null,0,25,new DateTime(2026,10,1),new DateTime(2026,10,10),"COMPLETED");
        if(sales.Summary.Count<sales.Rows.Count||sales.Rows.Any(r=>r.Status!="COMPLETED")||sales.Previous is null)throw new Exception("Sales list filters or summary failed.");
        if(sales.Summary.Count==sales.Rows.Count&&sales.Summary.TotalSales!=sales.Rows.Sum(r=>r.TotalAmount))throw new Exception("Sales summary does not reconcile.");
        var sorted=await new SalesRepository(factory).GetListAsync(null,null,0,25,null,null,null,"total",true);
        if(!sorted.Rows.Select(r=>r.TotalAmount).SequenceEqual(sorted.Rows.Select(r=>r.TotalAmount).Order()))throw new Exception("Sales sort is incorrect.");
        Console.WriteLine("PASS: sales list customer details, quantities, payment modes, full filtered totals and previous period execute read-only under RLS.");
        foreach (var (from,to) in new[] { (new DateTime(2026,10,10),new DateTime(2026,10,10)), (new DateTime(2026,10,1),new DateTime(2026,10,10)) })
        {
            var rows = (await report.ProductSalesAsync(from,to)).ToArray();
            if (rows.Any(row => row.EstimatedMargin != row.NetSales-row.EstimatedCost)) throw new Exception("Report totals do not reconcile.");
            Console.WriteLine($"PASS: product sales query for {from:yyyy-MM-dd} to {to:yyyy-MM-dd}: {rows.Length} rows; RLS and database read-only mode enabled.");
            var scope = new StockFlow.Application.Common.BusinessScope {TenantId=1,UserId=1,StoreId=1,LocationId=1,LocationName="Main stock"};
            var reporting=new ReportingRepository(factory,scope);
            elapsed.Restart();
            var summary = await reporting.ActivityAsync(from,to,true,true);
            var fullMilliseconds=elapsed.Elapsed.TotalMilliseconds;
            elapsed.Restart();
            var financial=await reporting.FinancialAsync(from,to);
            if(System.Text.Json.JsonSerializer.Serialize(financial)!=System.Text.Json.JsonSerializer.Serialize(summary.Financial)) throw new Exception("Financial-only report changed the comparison totals.");
            Console.WriteLine($"TIMING: full shop summary {fullMilliseconds:F0} ms; financial-only comparison {elapsed.Elapsed.TotalMilliseconds:F0} ms.");
            if(summary.Trend.Sum(r=>r.Sales)!=summary.Financial.Sum(r=>r.SalesValue) || summary.Trend.Sum(r=>r.Collections)!=summary.Financial.Sum(r=>r.Collections)) throw new Exception("Trend and financial totals differ.");
            if(summary.TopProducts.Count>5||summary.Recent.Count>5)throw new Exception("Summary must limit products and activity to five.");
            Console.WriteLine("PASS: shop summary stock, trend, top products, receipts and financial totals execute and reconcile in read-only mode.");
        }
    }
}
