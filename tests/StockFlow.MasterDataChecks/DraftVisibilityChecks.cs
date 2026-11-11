using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Web.Controllers;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

// Actual controller query/Dapper/PostgreSQL with minimal synthetic read fixtures.
// This does not stand in for the full migration/lifecycle/checkout suite.
internal static class DraftVisibilityChecks
{
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var name="draft_visibility_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+name);
        try
        {
            await using var seed=new NpgsqlConnection(host+name);await seed.OpenAsync();
            await seed.ExecuteAsync("""
                CREATE TABLE billing_drafts(tenant_id bigint,user_id int,location_id bigint,store_id bigint,business_profile_code text,
                    draft_id uuid,draft_sequence bigint,creation_time_source text DEFAULT 'SERVER',draft_status text,revision bigint DEFAULT 1,
                    editor_id uuid,lease_until timestamptz,label text,updated_at timestamptz DEFAULT now(),payload jsonb);
                CREATE TABLE sales(tenant_id bigint,user_id int,location_id bigint,request_id uuid);
                CREATE FUNCTION sf_draft_number(n bigint) RETURNS text LANGUAGE sql IMMUTABLE AS $$ SELECT 'DRF-'||lpad(n::text,greatest(4,length(n::text)),'0') $$;
                ALTER TABLE billing_drafts ENABLE ROW LEVEL SECURITY;
                CREATE POLICY fixture_scope ON billing_drafts USING(
                    tenant_id=current_setting('app.tenant_id')::bigint AND user_id=current_setting('app.user_id')::int
                    AND location_id=current_setting('app.location_id')::bigint AND store_id=current_setting('app.store_id')::bigint
                    AND business_profile_code=current_setting('app.business_profile_code'));
                GRANT SELECT ON billing_drafts,sales TO stockflow_runtime;
                """
            );
            var editor=Guid.NewGuid();var elsewhere=Guid.NewGuid();var active=Guid.NewGuid();var held=Guid.NewGuid();var released=Guid.NewGuid();
            async Task Add(Guid id,string status,Guid owner,DateTime? lease,int tenant=1,int user=1,int location=1,int store=1,string profile="ELECTRICAL")=>
                await seed.ExecuteAsync("INSERT INTO billing_drafts VALUES(@tenant,@user,@location,@store,@profile,@id,(SELECT count(*)+1 FROM billing_drafts),'SERVER',@status,1,@owner,@lease,'Synthetic customer',now(),jsonb_build_object('draftId',@id::text,'customerId','10','customerLabel','Synthetic customer / 9876543210','cart','[]'::jsonb,'estimatedTotal',0))",new{id,status,owner,lease,tenant,user,location,store,profile});
            await Add(active,"ACTIVE",editor,DateTime.UtcNow.AddMinutes(2));await Add(held,"HELD",elsewhere,null);await Add(released,"ACTIVE",elsewhere,null);
            await Add(Guid.NewGuid(),"ACTIVE",elsewhere,DateTime.UtcNow.AddMinutes(-1));await Add(Guid.NewGuid(),"ACTIVE",elsewhere,DateTime.UtcNow.AddMinutes(2));
            foreach(var status in new[]{"LEGACY","COMPLETED","DISCARDED"})await Add(Guid.NewGuid(),status,elsewhere,null);
            await Add(Guid.NewGuid(),"HELD",elsewhere,null,tenant:2);await Add(Guid.NewGuid(),"HELD",elsewhere,null,user:2);await Add(Guid.NewGuid(),"HELD",elsewhere,null,location:2);await Add(Guid.NewGuid(),"HELD",elsewhere,null,store:2);await Add(Guid.NewGuid(),"HELD",elsewhere,null,profile:"BIKE_ACCESSORIES");
            var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+name}}).Build();
            var factory=new DbConnectionFactory(cfg,new BusinessScope{TenantId=1,UserId=1,LocationId=1,StoreId=1});
            var controller=new BillingController(factory,new PermissionContext());
            async Task<JsonObject> List(bool include=false,Guid? owner=null,bool recovery=false,string? search=null,int page=1)
            {
                var result=(OkObjectResult)await controller.Drafts(search,page,true,recovery,include,owner);
                return JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result.Value))!.AsObject();
            }
            void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
            Check((await List())["total"]!.GetValue<int>()==1,"Default list remains held-only and scoped");
            var all=await List(true,editor);Check(all["total"]!.GetValue<int>()==4,"Unified list includes own Active, Held, released and expired Active only; terminal, legacy, live other editor and foreign scopes excluded");
            var row=all["items"]!.AsArray().Single(r=>r!["DraftId"]!.GetValue<Guid>()==active)!;
            Check(row["itemCount"]!.GetValue<int>()==0&&row["estimatedTotal"]!.GetValue<decimal>()==0&&row["customerName"]!.GetValue<string>().Contains("Synthetic customer"),"Customer-only Active record retains customer, zero items and zero amount");
            Check((await List(true,null))["total"]!.GetValue<int>()==3,"Missing editor does not reveal another editor's live lease");
            Check((await List(recovery:true))["total"]!.GetValue<int>()==3,"Existing interrupted recovery retains legacy and expired/released Active records");
            Check((await List(true,editor,search:"DRF-0001"))["total"]!.GetValue<int>()==1,"Draft-reference search finds current Active draft");
            await seed.ExecuteAsync("INSERT INTO sales VALUES(1,1,1,@active)",new{active});
            Check((await List(true,editor))["total"]!.GetValue<int>()==3,"Completed request IDs are excluded even if a historical status is stale");
            for(var i=0;i<27;i++)await Add(Guid.NewGuid(),"HELD",elsewhere,null);
            var second=await List(true,editor,page:2);Check(second["total"]!.GetValue<int>()==30&&second["items"]!.AsArray().Count==5,"Unified list count and pagination share the same eligibility query");
            Console.WriteLine("Draft visibility controller/PostgreSQL checks passed; disposable synthetic read schema only.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+name+" WITH(FORCE)");}
    }
}
