using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class ExpenseRecordingChecks
{
    // Pre-transition fixtures only. Production code rejects standalone SAVE.
    public static Task<long> SeedLegacyAsync(NpgsqlConnection setup,Guid id,ExpenseOperation op)=>setup.ExecuteScalarAsync<long>("SELECT sf_expense_write(@id,@operation,@expected,'SAVE',@body::jsonb)",new{id,operation=op.OperationId,expected=op.ExpectedVersion,body=op.Body.GetRawText()});
    public static async Task<long> SeedLegacyAsync(DbConnectionFactory factory,Guid id,ExpenseOperation op){await using var setup=factory.CreateConnection();return await SeedLegacyAsync(setup,id,op);}
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55444;Username=postgres;Database=";
        var database="expense_recording_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database);
        try
        {
            await using var setup=new NpgsqlConnection(host+database);await setup.OpenAsync();
            // Use actual migration tables and financial write function, with synthetic
            // principals/permission fixtures. No live database records are copied.
            await setup.ExecuteAsync("""
                CREATE TABLE stores(tenant_id bigint,store_id bigint,PRIMARY KEY(tenant_id,store_id));
                CREATE TABLE store_locations(tenant_id bigint,location_id bigint,PRIMARY KEY(tenant_id,location_id));
                CREATE TABLE users(tenant_id bigint,user_id integer,PRIMARY KEY(tenant_id,user_id));
                INSERT INTO stores VALUES(1,1),(1,2); INSERT INTO store_locations VALUES(1,1),(1,2); INSERT INTO users VALUES(1,1);
                CREATE FUNCTION sf_expense_allowed(text) RETURNS boolean LANGUAGE sql AS $$SELECT $1<>coalesce(current_setting('app.denied_action',true),'')$$;
                CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.tenant_id')::bigint$$;
                CREATE FUNCTION sf_actor() RETURNS integer LANGUAGE sql AS $$SELECT current_setting('app.user_id')::integer$$;
                CREATE FUNCTION sf_store() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.store_id')::bigint$$;
                CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.location_id')::bigint$$;
                CREATE FUNCTION sf_visible_business_profile() RETURNS text LANGUAGE sql AS $$SELECT current_setting('app.business_profile_code')$$;
                CREATE FUNCTION sf_quotation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'Immutable history';END$$;
                """);
            var migration=await File.ReadAllTextAsync("database/migrations/086_operating_expenses.sql");
            var tables=migration.IndexOf("CREATE TABLE public.expense_categories",StringComparison.Ordinal);
            var masters=migration.IndexOf("CREATE FUNCTION public.sf_expense_master",StringComparison.Ordinal);
            await setup.ExecuteAsync(migration[tables..masters]);
            var write=migration.IndexOf("CREATE FUNCTION public.sf_expense_write",StringComparison.Ordinal);
            var cash=migration.IndexOf("CREATE FUNCTION public.sf_expense_cash_movements",StringComparison.Ordinal);
            await setup.ExecuteAsync(migration[write..cash]);
            await setup.ExecuteAsync("GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO stockflow_runtime; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
            var cat=Guid.NewGuid();var payee=Guid.NewGuid();
            await setup.ExecuteAsync("INSERT INTO expense_categories VALUES(1,1,'ELECTRICAL',@cat,'Test rent'); INSERT INTO expense_payees VALUES(1,1,'ELECTRICAL',@payee,'Test payee')",new{cat,payee});
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",IsOwner=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var repo=new ExpenseRepository(new DbConnectionFactory(config,scope));
            await setup.ExecuteAsync("CREATE FUNCTION sf_expense_tax_options() RETURNS jsonb LANGUAGE sql AS $$SELECT '[]'::jsonb$$; GRANT EXECUTE ON FUNCTION sf_expense_tax_options() TO stockflow_runtime;");
            var databaseDay=await setup.ExecuteScalarAsync<DateTime>("SELECT (now() AT TIME ZONE 'Asia/Kolkata')::date");
            Check((await repo.OptionsAsync()).GetProperty("businessDate").GetString()==databaseDay.ToString("yyyy-MM-dd"),"expense options expose the same business date used by database validation");
            try{await repo.TaxPreviewAsync(new(5400m,databaseDay.AddDays(1),JsonSerializer.SerializeToElement(new{ruleId=1})));throw new Exception("Future GST expense date accepted");}
            catch(ValidationException error){Check(error.Message.Contains("database business date")&&error.Message.Contains(databaseDay.ToString("dd-MM-yyyy")),"future GST dates explain the database clock boundary instead of blaming a valid amount");}
            var day=DateTime.UtcNow.Date.AddDays(-2).ToString("yyyy-MM-dd");
            JsonElement Element(object body)=>JsonSerializer.SerializeToElement(body);
            var details=Element(new{categoryId=cat,payeeId=payee,incurredDate=day,amount=2700m,description="Recording transition fixture",gst=(object?)null});
            ExpenseOperation Record(decimal? paid,string method="CASH")=>new(Guid.NewGuid(),0,Element(new{expense=details,payment=paid is {} value?(object)new{amount=value,paidDate=day,paymentMode=method,confirmed=true}:null}));
            var rejected=Guid.NewGuid();
            try{await repo.WriteAsync(rejected,"SAVE",new(Guid.NewGuid(),0,details));throw new Exception("New draft accepted");}catch(ValidationException e){Check(e.Message.Contains("Record Expense"),"standalone draft creation rejected with clear validation");}
            Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM expenses")==0,"rejected draft creates no records or financial history");
            foreach(var amount in new decimal?[]{null,1000m,2700m})
            {
                var id=Guid.NewGuid();var op=Record(amount,amount==2700m?"BANK_TRANSFER":"CASH");
                await Task.WhenAll(repo.RecordAsync(id,op),repo.RecordAsync(id,op));var saved=await repo.GetAsync(id);
                Check(saved.GetProperty("status").GetString()=="POSTED"&&saved.GetProperty("paid").GetDecimal()==(amount??0)&&saved.GetProperty("payments").GetArrayLength()==(amount is null?0:1),"record and identical retry preserve one expense/payment: "+(amount?.ToString()??"Unpaid"));
                Check(saved.GetProperty("amount").GetDecimal()-saved.GetProperty("paid").GetDecimal()==2700-(amount??0),"remaining balance is unchanged");
            }
            var invalid=Guid.NewGuid();try{await repo.RecordAsync(invalid,Record(2701));throw new Exception("Overpayment accepted");}catch(PostgresException){}
            Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM expenses WHERE expense_id=@invalid",new{invalid})==0,"invalid payment rolls back internal draft, posting, ledger and events");
            var legacy=Guid.NewGuid();await setup.ExecuteScalarAsync<long>("SELECT sf_expense_write(@legacy,@op,0,'SAVE',@body::jsonb)",new{legacy,op=Guid.NewGuid(),body=details.GetRawText()});
            var before=(await repo.GetAsync(legacy)).GetRawText();
            try{await repo.WriteAsync(legacy,"SAVE",new(Guid.NewGuid(),1,details));throw new Exception("Draft update accepted");}catch(ValidationException){}
            Check((await repo.GetAsync(legacy)).GetRawText()==before,"blocked draft updates preserve existing record and audit history");
            var review=await repo.ListAsync(null,null,null,1,10,status:"DRAFT");Check(review.GetProperty("rows").GetArrayLength()==1,"legacy draft remains available through scoped review");
            var recorded=Record(null) with {ExpectedVersion=1};await repo.RecordAsync(legacy,recorded);Check((await repo.GetAsync(legacy)).GetProperty("status").GetString()=="POSTED","legacy draft can be explicitly recorded using atomic workflow");
            var discard=Guid.NewGuid();await setup.ExecuteScalarAsync<long>("SELECT sf_expense_write(@discard,@op,0,'SAVE',@body::jsonb)",new{discard,op=Guid.NewGuid(),body=details.GetRawText()});
            await repo.WriteAsync(discard,"CANCEL",new(Guid.NewGuid(),1,Element(new{reason="User discarded legacy expense"})));var cancelled=await repo.GetAsync(discard);
            Check(cancelled.GetProperty("status").GetString()=="CANCELLED"&&cancelled.GetProperty("events").GetArrayLength()==2&&cancelled.GetProperty("payments").GetArrayLength()==0,"explicit legacy cancellation preserves history without creating payment");
            var receiptExpense=legacy;var version=(await repo.GetAsync(legacy)).GetProperty("version").GetInt64();var attachment=new ExpenseOperation(Guid.NewGuid(),version,Element(new{name="receipt.pdf",contentType="application/pdf",data=Convert.ToBase64String("%PDF-test receipt"u8.ToArray())}));
            await repo.WriteAsync(receiptExpense,"ATTACH",attachment);await repo.WriteAsync(receiptExpense,"ATTACH",attachment);
            var withReceipt=await repo.GetAsync(receiptExpense);Check(withReceipt.GetProperty("attachments").GetArrayLength()==1&&withReceipt.GetProperty("paid").GetDecimal()==0,"receipt-only retries attach once without replaying financial record");
            using(var original=new Factory())
            using(var web=original.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();services.RemoveAll<IExpenseRepository>();services.AddSingleton<IExpenseRepository>(repo);})))
            using(var client=web.CreateClient(new(){AllowAutoRedirect=false}))
            {
                client.DefaultRequestHeaders.Add("Test-Role","Admin");
                var token=await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("requestToken").GetString());
                var deniedSave=await client.PostAsJsonAsync($"/api/expenses/{Guid.NewGuid()}/save",new ExpenseOperation(Guid.NewGuid(),0,details));
                Check(deniedSave.StatusCode==HttpStatusCode.BadRequest&&(await deniedSave.Content.ReadAsStringAsync()).Contains("Record Expense"),"actual draft HTTP endpoint returns clear 400 validation");
                var httpId=Guid.NewGuid();var response=await client.PostAsJsonAsync($"/api/expenses/{httpId}/record",Record(1000,"BANK_TRANSFER"));
                Check(response.IsSuccessStatusCode&&(await repo.GetAsync(httpId)).GetProperty("paid").GetDecimal()==1000,"actual Record Expense HTTP endpoint retains partial bank settlement");
            }
            scope.StoreId=2;scope.LocationId=2;try{await repo.GetAsync(legacy);throw new Exception("Cross-store read allowed");}catch(KeyNotFoundException){Check(true,"legacy and recorded expense access respects store/location isolation");}
            Console.WriteLine("Recording checks use migration 086 financial writes and synthetic permission fixtures; GST calculations are unchanged.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH(FORCE)");}
    }
}
