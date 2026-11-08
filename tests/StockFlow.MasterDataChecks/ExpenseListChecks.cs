using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

// Query contract checks in a disposable database on the isolated test cluster.
// No application connection string, credentials or business records are accessed.
internal static class ExpenseListChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    public static async Task Fixtures(Factory factory)
    {
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        Directory.CreateDirectory("tests/bin/expense-list-fixtures");
        foreach(var page in new[]{"Index","Create","Details","Report"})
            await File.WriteAllTextAsync("tests/bin/expense-list-fixtures/"+page+".html",await client.GetStringAsync(page=="Index"?"/Expenses":"/Expenses/"+page));
        Console.WriteLine("Expense Razor fixtures rendered.");
    }
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55444;Username=postgres;Database=";
        var database="expense_list_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database);
        try
        {
            await using var setup=new NpgsqlConnection(host+database);await setup.OpenAsync();
            await setup.ExecuteAsync("""
                CREATE FUNCTION sf_expense_allowed(text) RETURNS boolean LANGUAGE sql AS 'SELECT true';
                CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.tenant_id')::bigint$$;
                CREATE FUNCTION sf_store() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.store_id')::bigint$$;
                CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql AS $$SELECT current_setting('app.location_id')::bigint$$;
                CREATE FUNCTION sf_visible_business_profile() RETURNS text LANGUAGE sql AS $$SELECT current_setting('app.business_profile_code')$$;
                CREATE TABLE expense_categories(tenant_id bigint,store_id bigint,category_id uuid,name text);
                CREATE TABLE expense_payees(tenant_id bigint,store_id bigint,payee_id uuid,name text);
                CREATE TABLE expenses(tenant_id bigint,store_id bigint,expense_id uuid,category_id uuid,payee_id uuid,incurred_date date,amount numeric(14,2),description text,status text);
                CREATE TABLE expense_payments(tenant_id bigint,store_id bigint,expense_id uuid,amount numeric(14,2),paid_date date,payment_id uuid DEFAULT gen_random_uuid());
                CREATE TABLE expense_attachments(tenant_id bigint,store_id bigint,expense_id uuid,attachment_id uuid,created_at timestamptz);
                GRANT USAGE ON SCHEMA public TO stockflow_runtime;
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO stockflow_runtime;
                DO $$DECLARE tab text;BEGIN FOREACH tab IN ARRAY ARRAY['expenses','expense_categories','expense_payees','expense_payments','expense_attachments'] LOOP
                 EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
                 EXECUTE format('CREATE POLICY shop_scope ON %I USING (tenant_id=sf_tenant() AND store_id=sf_store())',tab);
                END LOOP;END$$;
                """);
            var category=Guid.NewGuid();var payee=Guid.NewGuid();var receipt=Guid.NewGuid();
            await setup.ExecuteAsync("INSERT INTO expense_categories VALUES(1,1,@category,'Repairs'); INSERT INTO expense_payees VALUES(1,1,@payee,'Local contractor')",new{category,payee});
            var first=Guid.NewGuid();
            for(var i=1;i<=17;i++)
            {
                var id=i==1?first:Guid.NewGuid();
                await setup.ExecuteAsync("INSERT INTO expenses VALUES(1,1,@id,@category,@payee,@day,@amount,@description,@status)",
                    new{id,category,payee,day=new DateTime(2026,11,Math.Min(i,15)),amount=i==1?1180:i==16?700:i==17?90:100,description=i==1?"AC repair inclusive GST":"Shop overhead "+i,status=i==16?"DRAFT":i==17?"CANCELLED":"POSTED"});
            }
            await setup.ExecuteAsync("INSERT INTO expense_payments(tenant_id,store_id,expense_id,amount,paid_date) VALUES(1,1,@first,500,'2026-11-01'),(1,1,@first,200,'2026-12-01'),(1,1,@first,-20,'2026-12-02'); INSERT INTO expense_attachments VALUES(1,1,@first,@receipt,now());",new{first,receipt});
            // Same IDs in other tenant/store scopes must not multiply joins or totals.
            await setup.ExecuteAsync("INSERT INTO expense_categories VALUES(1,2,@category,'Foreign'),(2,1,@category,'Foreign'); INSERT INTO expense_payees VALUES(1,2,@payee,'Foreign'),(2,1,@payee,'Foreign'); INSERT INTO expenses VALUES(1,2,@id,@category,@payee,'2026-11-01',9999,'Foreign','POSTED'),(2,1,@id,@category,@payee,'2026-11-01',9999,'Foreign','POSTED')",new{category,payee,id=Guid.NewGuid()});
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",IsOwner=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var repo=new ExpenseRepository(new DbConnectionFactory(config,scope));
            var list=await repo.ListAsync(null,null,null,1,10);var summary=list.GetProperty("summary");
            Check(list.GetProperty("total").GetInt32()==16&&list.GetProperty("rows").GetArrayLength()==10&&list.GetProperty("legacyDraftCount").GetInt32()==1,"normal pagination excludes legacy drafts and exposes their scoped review count");
            Check(summary.GetProperty("totalExpenses").GetDecimal()==2580&&summary.GetProperty("amountPaid").GetDecimal()==680&&summary.GetProperty("outstanding").GetDecimal()==1900,"full-set summary counts gross GST once, signed subsequent payments and excludes drafts/cancellations");
            var page2=await repo.ListAsync(null,null,null,2,10);Check(page2.GetProperty("summary").GetRawText()==summary.GetRawText(),"summary is independent of pagination");
            foreach(var term in new[]{"AC repair","Repairs","Local contractor"})Check((await repo.ListAsync(term,null,null,1,10)).GetProperty("total").GetInt32()>0,"search supports "+term);
            var partial=await repo.ListAsync(null,new(2026,11,1),new(2026,11,1),1,10,category,payee,"POSTED","PARTIAL");
            Check(partial.GetProperty("total").GetInt32()==1&&partial.GetProperty("summary").GetProperty("amountPaid").GetDecimal()==680,"combined date/category/payee/status/payment filters include later payments against selected expenses");
            Check(partial.GetProperty("rows")[0].GetProperty("receiptId").GetGuid()==receipt,"list exposes a receipt identifier without loading attachment bytes");
            Check((await repo.ListAsync(null,null,null,1,10,category:Guid.NewGuid())).GetProperty("total").GetInt32()==0&&(await repo.ListAsync(null,null,null,1,10,payee:Guid.NewGuid())).GetProperty("total").GetInt32()==0,"nonmatching category and payee filters exclude records");
            var drafts=await repo.ListAsync(null,null,null,1,10,status:"DRAFT");Check(drafts.GetProperty("total").GetInt32()==1&&drafts.GetProperty("summary").GetProperty("totalExpenses").GetDecimal()==0,"drafts remain discoverable with zero financial totals");
            Check((await repo.ListAsync(null,null,null,1,10,paymentStatus:"UNPAID")).GetProperty("total").GetInt32()==14,"payment filters exclude drafts and cancellations");
            Check((await repo.ListAsync(null,null,null,1,10,sort:"amount-desc")).GetProperty("rows")[0].GetProperty("expense_id").GetGuid()==first,"amount sorting is server-side");
            Check((await repo.ListAsync(null,null,null,1,10,sort:"oldest")).GetProperty("rows")[0].GetProperty("incurred_date").GetString()=="2026-11-01","date sorting is server-side");
            var report=await repo.ReportAsync(new(2026,11,1),new(2026,11,30));
            Check(report.GetProperty("summary").GetRawText()==summary.GetRawText(),"report cards use the full selected expense set, excluding drafts/cancellations and including subsequent signed payments");
            var singleDay=await repo.ReportAsync(new(2026,11,1),new(2026,11,1));
            Check(singleDay.GetProperty("summary").GetProperty("totalExpenses").GetDecimal()==1180&&singleDay.GetProperty("summary").GetProperty("amountPaid").GetDecimal()==680&&singleDay.GetProperty("summary").GetProperty("outstanding").GetDecimal()==500,"report card date range counts GST-inclusive expense and partial payments once");
            Check(report.GetProperty("incurred").GetDecimal()==2580&&report.GetProperty("paid").GetDecimal()==500&&report.GetProperty("outstandingAtCutoff").GetDecimal()==2080,"existing report retains period/cutoff payment semantics separately from list totals");
            var missing=await repo.ListAsync("No matching record",null,null,1,10);Check(missing.GetProperty("total").GetInt32()==0&&missing.GetProperty("hasExpenses").GetBoolean(),"filtered empty result is distinguished from an empty shop");
            scope.StoreId=3;Check(!(await repo.ListAsync(null,null,null,1,10)).GetProperty("hasExpenses").GetBoolean(),"empty-state existence check respects shop scope");
            var emptyReport=await repo.ReportAsync(new(2026,11,1),new(2026,11,30));Check(emptyReport.GetProperty("summary").GetProperty("totalExpenses").GetDecimal()==0&&emptyReport.GetProperty("summary").GetProperty("amountPaid").GetDecimal()==0&&emptyReport.GetProperty("summary").GetProperty("outstanding").GetDecimal()==0,"report summaries respect store isolation");scope.StoreId=1;
            async Task Reject(Func<Task> action){try{await action();throw new Exception("Expected filter validation");}catch(ValidationException){}}
            await Reject(()=>repo.ListAsync(null,new(2026,11,2),new(2026,11,1),1,10));await Reject(()=>repo.ListAsync(null,null,null,1,10,sort:"amount; DROP TABLE expenses"));
            Check(true,"invalid date ranges and sort values are rejected");
            using var factory=new Factory();await Fixtures(factory);
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH(FORCE)");}
    }
}
