using Dapper;
using Npgsql;

namespace StockFlow.MasterDataChecks;

// Runs unchanged 079 function bodies against the disposable visibility test's
// synthetic schema. Not a full-schema inventory/payment checkout certification.
static class SavedBillLifecycleFixture
{
    public static async Task Run(NpgsqlConnection c,string connection)
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        const string context="SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false);";
        await c.ExecuteAsync(context+"""
            CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.tenant_id')::bigint$$;
            CREATE FUNCTION sf_actor() RETURNS int LANGUAGE sql STABLE AS $$SELECT current_setting('app.user_id')::int$$;
            CREATE FUNCTION sf_store() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.store_id')::bigint$$;
            CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.location_id')::bigint$$;
            CREATE FUNCTION sf_visible_business_profile() RETURNS text LANGUAGE sql STABLE AS $$SELECT current_setting('app.business_profile_code')$$;
            CREATE FUNCTION sf_gst_allowed(text,text) RETURNS boolean LANGUAGE sql STABLE AS $$SELECT sf_tenant()=1 AND sf_actor()=1$$;
            CREATE FUNCTION sf_can_location(bigint) RETURNS boolean LANGUAGE sql STABLE AS $$SELECT $1=sf_location() AND $1=1$$;
            CREATE TABLE billing_draft_numbers(tenant_id bigint,user_id int,location_id bigint,draft_id uuid);
            CREATE SEQUENCE synthetic_draft_sequence START 100;
            ALTER TABLE billing_drafts ALTER tenant_id SET DEFAULT sf_tenant(),ALTER user_id SET DEFAULT sf_actor(),
              ALTER store_id SET DEFAULT sf_store(),ALTER location_id SET DEFAULT sf_location(),
              ALTER business_profile_code SET DEFAULT sf_visible_business_profile(),
              ALTER draft_sequence SET DEFAULT nextval('synthetic_draft_sequence'),
              ADD held_at timestamptz,ADD discarded_at timestamptz,ADD completed_at timestamptz,
              ADD completed_sale_id bigint,ADD last_operation_id uuid,ADD last_operation_action text;
            """);
        var sql=await File.ReadAllTextAsync("database/migrations/079_bill_draft_lifecycle.sql");
        var start=sql.IndexOf("CREATE OR REPLACE FUNCTION public.sf_draft_reply",StringComparison.Ordinal);
        var end=sql.IndexOf("CREATE OR REPLACE FUNCTION public.sf_draft_complete",StringComparison.Ordinal);
        if(start<0||end<=start)throw new Exception("079 source sections changed");
        await c.ExecuteAsync(sql[start..end]);
        await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/094_bill_minimum_items.sql"));
        var id=Guid.NewGuid();var editor=Guid.NewGuid();var other=Guid.NewGuid();var operation=Guid.NewGuid();
        var payload=System.Text.Json.JsonSerializer.Serialize(new{draftId=id,customerId=10,customerLabel="Synthetic customer",cart=new[]{new{productId=1,variantId=2,quantity=2,sellingPrice=100,discountAmount=3,taxPercentage=18}},pricesIncludeTax=true,gstPlaceOfSupplyStateCode="33",notes="Preserve exact draft fields"});
        async Task<long> Action(NpgsqlConnection db,string action,Guid target,Guid owner,long revision,Guid? retry=null,string? body=null)=>
            await db.ExecuteScalarAsync<long>("SELECT (sf_draft_action(@action,@target,@owner,@revision,@retry,'Synthetic saved bill',@body::jsonb)->>'revision')::bigint",new{action,target,owner,revision,retry=retry??Guid.NewGuid(),body});
        async Task Deny(Func<Task> action,string label){try{await action();throw new Exception("Accepted: "+label);}catch(PostgresException e)when(e.SqlState is "P0001" or "42501"){Check(true,label);}}
        var salesBefore=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM sales");
        Check(await Action(c,"SAVE",id,editor,0,operation,payload)==1&&await Action(c,"SAVE",id,editor,0,operation,payload)==1,"actual 079 save retry preserves one draft and revision");
        await Action(c,"HOLD",id,editor,1);
        async Task<bool> Resume(Guid owner){await using var db=new NpgsqlConnection(connection);await db.OpenAsync();await db.ExecuteAsync(context);try{await Action(db,"RESUME",id,owner,2);return true;}catch(PostgresException e)when(e.SqlState=="P0001"){return false;}}
        var contenders=await Task.WhenAll(Resume(editor),Resume(other));Check(contenders.Count(x=>x)==1,"actual 079 concurrent resumes allow only one cashier lease");
        var winner=contenders[0]?editor:other;var loser=contenders[0]?other:editor;
        await Deny(()=>Action(c,"SAVE",id,loser,3,body:payload),"actual 079 stale cashier cannot overwrite selected bill");
        await Action(c,"RELEASE",id,winner,3);await Action(c,"RECOVER",id,editor,3);
        Check(await c.ExecuteScalarAsync<bool>("SELECT payload=@payload::jsonb FROM billing_drafts WHERE draft_id=@id",new{payload,id}),"interrupted recovery preserves exact customer/items/quantity/discount/tax/notes");
        var empty=Guid.NewGuid();var emptyEditor=Guid.NewGuid();var emptyBody=System.Text.Json.JsonSerializer.Serialize(new{draftId=empty,cart=Array.Empty<object>(),customerId=10});
        await Deny(()=>Action(c,"SAVE",empty,emptyEditor,0,body:emptyBody),"094 rejects new empty drafts, including customer-only payloads");
        Check(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM billing_drafts WHERE draft_id=@empty)",new{empty}),"rejected empty save creates no row");
        // Seed a pre-policy historical row without invoking SAVE. Never delete historical rows.
        await c.ExecuteAsync("INSERT INTO billing_drafts(draft_id,draft_status,revision,payload,label) VALUES(@empty,'LEGACY',1,@body::jsonb,'Historical empty draft')",new{empty,body=emptyBody});
        await Action(c,"RECOVER",empty,emptyEditor,1);
        await Deny(()=>Action(c,"SAVE",empty,emptyEditor,2,body:emptyBody),"resumed historical empty draft cannot be saved again");
        await Deny(()=>Action(c,"HOLD",empty,emptyEditor,2),"094 Hold rejects historical zero-item draft");
        foreach(var quantity in new[]{0,-1}){
            var invalid=Guid.NewGuid();var invalidBody=System.Text.Json.JsonSerializer.Serialize(new{draftId=invalid,cart=new[]{new{productId=1,variantId=2,quantity,sellingPrice=0}}});
            await Deny(()=>Action(c,"SAVE",invalid,editor,0,body:invalidBody),"094 quantity "+quantity+" cannot count as an item");
        }
        var free=Guid.NewGuid();var freeBody=System.Text.Json.JsonSerializer.Serialize(new{draftId=free,cart=new[]{new{productId=1,variantId=2,quantity=1,sellingPrice=0}}});
        await Action(c,"SAVE",free,editor,0,body:freeBody);await Action(c,"HOLD",free,editor,1);Check(true,"094 permits valid zero-price items for Save and Hold");
        await Deny(()=>Action(c,"SAVE",id,editor,4,body:emptyBody.Replace(empty.ToString(),id.ToString())),"removing the last item cannot overwrite a valid saved draft");
        await Action(c,"DISCARD",empty,emptyEditor,2);await Deny(()=>Action(c,"RECOVER",empty,emptyEditor,3),"historical empty drafts can still be discarded safely");
        await Action(c,"HOLD",id,editor,4);
        var current=Guid.NewGuid();await Action(c,"SAVE",current,editor,0,body:payload.Replace(id.ToString(),current.ToString()));
        await c.ExecuteScalarAsync<string>("SELECT sf_draft_switch(@id,@editor,5,@retry,false,@current,1,'HOLD')::text",new{id,editor,retry=Guid.NewGuid(),current});
        Check(await c.ExecuteScalarAsync<bool>("SELECT draft_status='HELD' FROM billing_drafts WHERE draft_id=@current",new{current}),"actual 079 switching atomically holds the current bill before resuming exact selection");
        Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM sales")==salesBefore,"actual draft save/hold/resume/recovery/discard never create an invoice");
        await c.ExecuteAsync("INSERT INTO sales VALUES(1,1,1,@id)",new{id});
        await Deny(()=>Action(c,"SAVE",id,editor,6,body:payload),"completed request cannot be saved or resumed as an active draft");
    }
}
