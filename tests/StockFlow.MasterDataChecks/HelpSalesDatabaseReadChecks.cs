using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using Dapper;

namespace StockFlow.MasterDataChecks;

internal static class HelpSalesDatabaseReadChecks
{
    public static async Task Run()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"StockFlow.sln"))) directory=directory.Parent;
        var config=new ConfigurationBuilder().SetBasePath(Path.Combine(directory!.FullName,"src/StockFlow.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",optional:true).AddEnvironmentVariables().Build();
        // No authorized business: the runtime role must return no sales/payment rows.
        var variants=await new VariantRepository(new DbConnectionFactory(config,new BusinessScope())).GetAsync(null,null,true);
        if(variants.Any()) throw new Exception("Empty business scope exposed product variants or image metadata.");
        Console.WriteLine("PASS: Scoped variant/image metadata SQL uses existing image columns and returns no unauthorized records.");
        var repository=new SalesEnquiryRepository(new DbConnectionFactory(config,new BusinessScope()));
        var periods=new[] {"today","yesterday","day-before-yesterday","this-week","last-week","this-month","last-month","range"};
        foreach(var name in periods)
        {
            var period=name=="range" ? new SalesPeriod(name,new DateTime(2026,10,1),new DateTime(2026,10,7)) : new(name);
            var result=await repository.GetSummaryAsync(period,CancellationToken.None);
            if(result.From>result.To || result.Quantity!=0 || result.InvoiceCount!=0 || result.Amount!=0 || result.Collected!=0)
                throw new Exception("An empty business scope exposed totals or produced invalid date bounds.");
            Console.WriteLine("PASS: PostgreSQL scoped read-only enquiry SQL and result mapping: "+name);
        }
        var extended=new HelpBusinessRepository(new DbConnectionFactory(config,new BusinessScope()),
            Microsoft.Extensions.Options.Options.Create(new ShopTimeOptions()),TimeProvider.System);
        foreach(var metric in new[]{"customer-dues","supplier-balance","return-count","return-quantity","return-amount","refunded","payment-breakdown","cash-collected"})
        {
            var value=await extended.GetAsync(new(metric,new(metric is "customer-dues" or "supplier-balance" ? "current" : "today")),CancellationToken.None);
            if(value.Status!="answered" || value.Value is not ("0" or "₹0.00")) throw new Exception("Empty-scope extended query leaked a value: "+metric);
            Console.WriteLine("PASS: PostgreSQL scoped extended read/mapping: "+metric);
        }
        foreach(var metric in new[]{"product-quantity","customer-dues","supplier-balance"})
        {
            var value=await extended.GetAsync(new(metric,new(metric=="product-quantity"?"today":"current"),"__NO_MATCH__"),CancellationToken.None);
            if(value.Status!="clarify")throw new Exception("Missing account/product was reported as a zero.");
            Console.WriteLine("PASS: PostgreSQL scoped exact-name resolution: "+metric);
        }
        foreach(var metric in new[]{"pending-purchases","reorder","top-products","slow-products","overdue-customers","overdue-repairs","ready-repair-list","repair-deliveries","pending-transfers","warranty-detail","sales-comparison"})
        {
            var period=metric is "top-products" or "slow-products" or "sales-comparison" ? "this-month" : "current";
            var value=await extended.GetAsync(new(metric,new(period),metric=="warranty-detail" ? "__NO_MATCH__" : null),CancellationToken.None);
            if(value.Status!="answered" && !(metric=="overdue-customers" && value.Status=="unavailable" && value.Value.Contains("migration 037"))) throw new Exception("Operational SQL/mapping failed: "+metric);
            Console.WriteLine("PASS: PostgreSQL empty-scope operational SQL/mapping: "+metric);
        }
        await using var connection=new DbConnectionFactory(config,new BusinessScope()).CreateConnection();
        await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:transaction);
        // CTEs contain synthetic records only. No tables, invoices or due dates are created.
        const string records="""
            WITH sales(sale_id,customer_id,total_amount,status,payment_due_date) AS (VALUES
             (101,1,100::numeric,'COMPLETED','2026-10-07'::date),
             (102,2,200::numeric,'COMPLETED','2026-10-09'::date),
             (103,3,300::numeric,'COMPLETED',NULL::date),
             (104,4,100::numeric,'COMPLETED','2026-10-06'::date),
             (105,5,100::numeric,'COMPLETED','2026-10-05'::date),
             (106,1,1000::numeric,'CANCELLED','2026-10-04'::date),
             (107,1,500::numeric,'COMPLETED','2026-10-08'::date)),
            customers(customer_id,customer_name) AS (VALUES (1,'Overdue'),(2,'Future'),(3,'Unknown'),(4,'Paid'),(5,'Returned')),
            payments(sale_id,amount,payment_mode) AS (VALUES (101,20::numeric,'CASH'),(101,80::numeric,'CREDIT'),(104,100::numeric,'UPI'),(105,20::numeric,'CASH')),
            sale_returns(sale_id,return_total,refund_amount) AS (VALUES (101,10::numeric,5::numeric),(105,50::numeric,10::numeric))
            """;
        var balances=(await connection.QueryAsync<OverdueRow>(records+", "+HelpBusinessRepository.OverdueCustomerSql[4..],new {Today=new DateTime(2026,10,8)},transaction)).ToArray();
        if(balances.Length!=2 || balances.Single(b=>b.Id==1).Amount!=75 || balances.Single(b=>b.Id==5).Amount!=40)
            throw new Exception("Overdue balances must reconcile returns/refunds/payments and exclude future, unknown, paid, cancelled and due-today invoices.");
        var unknown=await connection.ExecuteScalarAsync<long>(records+" "+HelpBusinessRepository.UnknownDueDatesSql,transaction:transaction);
        if(unknown!=1)throw new Exception("Unknown payment dates must be counted separately from overdue invoices.");
        Console.WriteLine("PASS: Synthetic PostgreSQL overdue balances reconcile payments, returns and refunds; unknown dates are separate. No schema changes.");
        Console.WriteLine("No database writes. Empty-scope and synthetic checks validate SQL/mapping; authorized business figures need separate review.");
    }
    private sealed record OverdueRow(int Id,string Name,decimal Amount,DateTime Due);
}
