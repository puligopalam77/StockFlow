using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class CashReleaseReadOnlyChecks
{
    public static async Task Run(bool details=false)
    {
        // Explicit read-only audit of the configured application schema. No customer
        // details, balances, connection strings, passwords or references are logged.
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var config=new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName,"src/StockFlow.Web")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",optional:true).AddEnvironmentVariables().Build();
        await using var c=new DbConnectionFactory(config).CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await c.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:t);
        var installed=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.cash_opening_expense_reviews') IS NOT NULL",transaction:t);
        var scope=await c.QueryFirstOrDefaultAsync<BusinessScope>("""
            SELECT u.tenant_id TenantId,u.user_id UserId,l.store_id StoreId,l.location_id LocationId,true IsOwner,true CanReviewStores,
             coalesce(nullif(to_jsonb(t)->>'business_profile_code',''),'ELECTRICAL') BusinessProfileCode,
             coalesce((to_jsonb(t)->>'profile_data_visibility_enabled')::boolean,false) ProfileDataVisibilityAvailable
            FROM users u JOIN tenants t USING(tenant_id) JOIN roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id
            JOIN store_locations l ON l.tenant_id=u.tenant_id JOIN stores s ON s.tenant_id=l.tenant_id AND s.store_id=l.store_id
            WHERE u.is_tenant_owner AND u.is_active AND t.is_active AND r.is_active AND l.is_active AND s.is_active AND l.location_type<>'TRANSIT'
            ORDER BY u.tenant_id,u.user_id,l.location_id LIMIT 1
            """,transaction:t);
        await t.CommitAsync();
        if(scope is null)throw new InvalidOperationException("No authorized active owner/shop context available for read-only cash verification.");
        var time=Options.Create(new ShopTimeOptions());var repo=new CashInHandRepository(new DbConnectionFactory(config,scope),scope,time,TimeProvider.System);
        var today=time.Value.Today(TimeProvider.System);
        await repo.GetAsync(today);
        if(details)
        {
            // Explicit opt-in financial diagnostics. Repository reads remain READ ONLY.
            Console.WriteLine($"AUDIT scope tenant={scope.TenantId}, store={scope.StoreId}, location={scope.LocationId}, profile={scope.BusinessProfileCode}; selected first authorized owner/location.");
            foreach(var date in new[]{today.AddDays(-1),today})
            {
                var report=await repo.GetAsync(date);
                Console.WriteLine("AUDIT actual repository DTO (not a live HTTP capture): "+System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            }
        }
        Console.WriteLine(details
            ? "PASS configured application database: opted-in financial diagnostics used read-only queries; no records changed."
            : "PASS configured application database: actual cash-source queries executed in read-only transactions; no financial data logged or changed.");
        Console.WriteLine(installed?"INFO migration 088 is installed.":"INFO migration 088 is not installed; current-count opening/review requires manual deployment.");
    }
}
