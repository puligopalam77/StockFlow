using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class ExpenseCategoryChecks
{
    private static readonly string[] Defaults = ["Shop Rent","Electricity & Water","Staff Salary","Internet & Phone","Transport & Delivery","Repairs & Maintenance","Cleaning & Supplies","Packaging Expenses","Marketing & Advertising","Software & Subscriptions","Bank & Payment Charges","Other Expenses"];
    public static async Task Run(NpgsqlConnection c,IConfiguration configuration,ExpenseRepository original,Guid existing,long emptyStore)
    {
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        Check(await c.ExecuteScalarAsync<bool>("SELECT name='  SHOP RENT  ' AND NOT is_system_default FROM expense_categories WHERE category_id=@existing",new{existing}),"Default seeding preserves existing category identity, spacing and name");
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_categories WHERE store_id=1 AND business_profile_code='ELECTRICAL' AND is_system_default")==0,"Customized store/profile list is not backfilled or renamed");
        Check(!await c.ExecuteScalarAsync<bool>("SELECT has_function_privilege('stockflow_runtime','sf_seed_store_expense_categories(bigint,bigint)','EXECUTE')"),"Runtime clients cannot invoke the internal cross-store seeding helper");
        foreach(var profile in BusinessProfiles.Codes){
            var names=(await c.QueryAsync<string>("SELECT name FROM expense_categories WHERE tenant_id=1 AND store_id=@emptyStore AND business_profile_code=@profile",new{emptyStore,profile})).ToArray();
            Check(names.Length==12&&names.Order().SequenceEqual(Defaults.Order()),"Empty pre-existing store receives twelve exact defaults for "+profile);
        }
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};
        var branches=new BranchRepository(new DbConnectionFactory(configuration,scope),scope);
        StoreRequest Request()=>new(){StoreCode="EXPENSE-CATEGORY-DEFAULTS",StoreName="Expense default test store"};
        var stores=await Task.WhenAll(branches.CreateStoreAsync(Request()),branches.CreateStoreAsync(Request()));var store=stores[0];
        Check(stores.Distinct().Count()==1&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_categories WHERE tenant_id=1 AND store_id=@store AND is_system_default",new{store})==48,"Actual new-store workflow and concurrent retries seed twelve defaults per supported profile once");
        await c.ExecuteAsync("SELECT sf_seed_store_expense_categories(1,@store)",new{store});
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_categories WHERE store_id=@store",new{store})==48,"Repeated default initialization creates no duplicates");
        Check(await c.ExecuteScalarAsync<bool>("SELECT sf_store_unused(@store)",new{store}),"System defaults alone do not mark an otherwise unused store as used");
        var location=await c.ExecuteScalarAsync<long>("SELECT location_id FROM store_locations WHERE store_id=@store AND location_type='SALES_FLOOR'",new{store});
        var storeScope=new BusinessScope{TenantId=1,UserId=1,StoreId=store,LocationId=location,IsOwner=true,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};
        var repository=new ExpenseRepository(new DbConnectionFactory(configuration,storeScope));
        var options=await repository.OptionsAsync();var categories=options.GetProperty("categories").EnumerateArray().ToArray();
        Check(categories.Length==12&&categories.Select(x=>x.GetProperty("name").GetString()!).Order().SequenceEqual(Defaults.Order()),"Runtime options expose only the current store and profile's default categories");
        var rent=categories.Single(x=>x.GetProperty("name").GetString()=="Shop Rent").GetProperty("id").GetGuid();
        var duplicate=await repository.MasterAsync("CATEGORY",Guid.NewGuid(),"  SHOP RENT  ");
        Check(duplicate==rent&&await c.ExecuteScalarAsync<string>("SELECT name FROM expense_categories WHERE category_id=@rent",new{rent})=="Shop Rent","Category duplicate lookup ignores case/spaces and preserves the seeded name");
        var ids=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>repository.MasterAsync("CATEGORY",Guid.NewGuid(),i%2==0?"Local delivery":" LOCAL DELIVERY ")));
        Check(ids.Distinct().Count()==1&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_categories WHERE store_id=@store AND lower(btrim(name))='local delivery'",new{store})==1,"Concurrent custom category submissions create one category");
        var other=await original.MasterAsync("CATEGORY",Guid.NewGuid(),"Local delivery");
        Check(other!=ids[0]&&!(await repository.OptionsAsync()).GetProperty("categories").EnumerateArray().Any(x=>x.GetProperty("id").GetGuid()==other),"Equal category names in different stores keep separate IDs and remain isolated");
        Check(!await c.ExecuteScalarAsync<bool>("SELECT sf_store_unused(@store)",new{store}),"User-created category still prevents unused-store deactivation");
        var payee=await repository.MasterAsync("PAYEE",Guid.NewGuid(),"Default category payee");
        var day=await c.ExecuteScalarAsync<string>("SELECT to_char((now() AT TIME ZONE 'Asia/Kolkata')::date,'YYYY-MM-DD')");
        await repository.RecordAsync(Guid.NewGuid(),new(Guid.NewGuid(),0,System.Text.Json.JsonSerializer.SerializeToElement(new{expense=new{categoryId=rent,payeeId=payee,incurredDate=day,amount=10,description="Recorded using default category"},payment=(object?)null})));
        Check(!await c.ExecuteScalarAsync<bool>("SELECT sf_store_unused(@store)",new{store}),"Expense using a default category retains the existing store history protection");
    }
}
