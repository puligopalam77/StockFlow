using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Infrastructure.Repositories;

public partial class BranchRepository
{
    private static Task<bool> ManagementAvailable(NpgsqlConnection c,NpgsqlTransaction? t=null)=>c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='stores' AND column_name='management_details')",transaction:t);
    private async Task Manage(NpgsqlConnection c,NpgsqlTransaction? t=null,string action="EDIT")
    {
        Owner();
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_owner() AND sf_transfer_permission('STORES',@action)",new{action},t))throw new UnauthorizedAccessException("Store owner and store-management permission required.");
    }
    private static void Normalize(StoreRequest r)
    {
        r.StoreCode=r.StoreCode?.Trim()??"";r.StoreName=r.StoreName?.Trim()??"";
        if(!string.IsNullOrWhiteSpace(r.GstNumber))throw new ValidationException("Manage GST registrations and effective store mappings in GST Settings; do not enter a separate store GSTIN.");
        r.AddressLine1=GstRules.Clean(r.AddressLine1);r.AddressLine2=GstRules.Clean(r.AddressLine2);r.District=GstRules.Clean(r.District);r.PinCode=GstRules.Clean(r.PinCode);r.Email=GstRules.Clean(r.Email);r.AlternatePhone=GstRules.Clean(r.AlternatePhone);
        r.State=GstRules.Clean(r.State);r.City=GstRules.Clean(r.City);
        if(r.AddressLine1 is not null||r.AddressLine2 is not null||r.District is not null||r.PinCode is not null)
        {
            if(r.State is not null&&!GstRules.States.Any(s=>s.Key!="28"&&s.Value==r.State))throw new ValidationException("Select an Indian State or Union Territory from the list.");
            if(r.District is not null&&r.State is null)throw new ValidationException("Select the State before choosing a District.");
            if(r.AddressLine1 is null && r.GstSetupMode!="LATER")throw new ValidationException("Address Line 1 is required for a structured store address.");
            r.Address=PostalAddress.Format(r.AddressLine1,r.AddressLine2,r.City,r.District,r.State,r.PinCode);
        }
        Validator.ValidateObject(r,new(r),true);
        if(r.StoreCode.Length==0||r.StoreName.Length==0)throw new ValidationException("Store name and code are required.");
    }
    private static string Metadata(StoreRequest r)
    {
        var doc=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new{r.AddressLine1,r.AddressLine2,r.District,r.PinCode,r.Email,r.AlternatePhone}))!.AsObject();
        if(r.GstBusinessId.HasValue)doc["LegalBusinessId"]=r.GstBusinessId.Value;
        return doc.ToJsonString();
    }
    private const string BaseRows="""
        SELECT s.store_id StoreId,s.store_code StoreCode,s.store_name StoreName,s.is_active IsActive,s.is_head_office IsHeadOffice,s.address,s.city,s.state,s.mobile_number MobileNumber,
        coalesce((to_jsonb(s)->>'management_version')::bigint,0) Version,
        (to_jsonb(s)->'management_details'->>'LegalBusinessId')::bigint GstBusinessId,
        to_jsonb(s)->'management_details'->>'AddressLine1' AddressLine1,to_jsonb(s)->'management_details'->>'AddressLine2' AddressLine2,
        to_jsonb(s)->'management_details'->>'District' District,to_jsonb(s)->'management_details'->>'PinCode' PinCode,
        to_jsonb(s)->'management_details'->>'Email' Email,to_jsonb(s)->'management_details'->>'AlternatePhone' AlternatePhone,
        (SELECT count(*) FROM store_locations l WHERE l.tenant_id=s.tenant_id AND l.store_id=s.store_id AND l.is_active AND l.location_type<>'TRANSIT') LocationCount,
        (SELECT count(*) FROM store_locations l WHERE l.tenant_id=s.tenant_id AND l.store_id=s.store_id AND l.is_active AND l.location_type='SALES_FLOOR') SalesFloorCount,
        coalesce(to_jsonb(t)->>'business_profile_code','ELECTRICAL') BusinessProfileCode
        FROM stores s JOIN tenants t USING(tenant_id)
        """;
    public async Task<StoreDirectoryPage> DirectoryAsync(StoreDirectoryQuery q)
    {
        Validator.ValidateObject(q,new(q),true);
        if(q.Page<1||q.PageSize is not(10 or 25 or 50 or 100)||q.Status is not("ALL" or "ACTIVE" or "INACTIVE"))throw new ValidationException("Choose a valid directory page, page size (10/25/50/100) and status.");
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('STORES','VIEW')"))throw new UnauthorizedAccessException("Store viewing permission required.");
        var filter=" WHERE (@search IS NULL OR StoreName ILIKE @search OR StoreCode ILIKE @search) AND (@status='ALL' OR IsActive=(@status='ACTIVE'))";
        var args=new{search=string.IsNullOrWhiteSpace(q.Search)?null:"%"+q.Search.Trim()+"%",status=q.Status};
        var total=await c.ExecuteScalarAsync<long>("WITH directory AS ("+BaseRows+") SELECT count(*) FROM directory"+filter,args);
        var page=(int)Math.Min(q.Page,Math.Max(1,(total+q.PageSize-1)/q.PageSize));
        var rows=(await c.QueryAsync<StoreDirectoryRow>("WITH directory AS ("+BaseRows+") SELECT * FROM directory"+filter+" ORDER BY StoreName,StoreId OFFSET @offset LIMIT @size",new{args.search,args.status,offset=(long)(page-1)*q.PageSize,size=q.PageSize})).ToArray();
        await AddGst(c,rows);await AddShopSetup(c,rows);
        var counts=await c.QuerySingleAsync<(long Stores,long Missing,long Active,long Inactive)>("WITH directory AS ("+BaseRows+") SELECT count(*),count(*) FILTER(WHERE IsActive AND SalesFloorCount=0),count(*) FILTER(WHERE IsActive),count(*) FILTER(WHERE NOT IsActive) FROM directory");
        var locations=await c.QuerySingleAsync<(long Total,long Warehouses)>("SELECT count(*),count(*) FILTER(WHERE l.location_type='WAREHOUSE') FROM store_locations l LEFT JOIN stores s USING(tenant_id,store_id) WHERE l.is_active AND l.location_type<>'TRANSIT' AND (l.store_id IS NULL OR s.is_active)");
        return new(rows,total,page,q.PageSize,counts.Stores,locations.Total,locations.Warehouses,counts.Missing,await ManagementAvailable(c),scope.IsOwner&&await c.ExecuteScalarAsync<bool>("SELECT sf_owner() AND sf_transfer_permission('STORES','EDIT')"),scope.IsOwner&&await c.ExecuteScalarAsync<bool>("SELECT sf_owner() AND sf_transfer_permission('STORES','CREATE')"),scope.IsOwner&&await c.ExecuteScalarAsync<bool>("SELECT sf_owner() AND sf_transfer_permission('STORES','MANAGE_ACCESS')"),scope.BusinessProfileCode,counts.Active,counts.Inactive,await ShopSetupAvailable(c));
    }
    private static async Task AddGst(NpgsqlConnection c,StoreDirectoryRow[] rows)
    {
        if(rows.Length==0)return;
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_shop_mappings') IS NOT NULL"))return;
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('VIEW')")){foreach(var row in rows)row.GstStatus="GST view permission required";return;}
        var mappings=await c.QueryAsync<(long Store,string Type,bool Effective)>("""
        SELECT m.store_id,r.registration_type,
        m.status='ACTIVE' AND r.status='ACTIVE' AND CURRENT_DATE BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND CURRENT_DATE BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity')
        FROM gst_shop_mappings m JOIN gst_registrations r USING(tenant_id,registration_id)
        WHERE m.store_id=ANY(@ids) AND m.business_profile_code=sf_visible_business_profile()
        """,new{ids=rows.Select(r=>r.StoreId).ToArray()});
        foreach(var row in rows){var matches=mappings.Where(m=>m.Store==row.StoreId).ToArray();var effective=matches.Where(m=>m.Effective).ToArray();row.GstStatus=effective.Length==1?(effective[0].Type=="UNREGISTERED"?"Unregistered mapping":"Mapped"):effective.Length>1?"Review overlapping mappings":matches.Length>0?"Review dates / status":"Not assigned";row.GstRegistrationType=effective.Length==1?effective[0].Type:null;}
    }
    public async Task<StoreDetails> StoreDetailsAsync(long id)
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('STORES','VIEW')"))throw new UnauthorizedAccessException("Store viewing permission required.");
        var row=await c.QuerySingleOrDefaultAsync<StoreDirectoryRow>(BaseRows+" WHERE s.store_id=@id",new{id})??throw new KeyNotFoundException("Store unavailable in the authorized business.");
        await AddGst(c,[row]);await AddShopSetup(c,[row]);
        var locations=await c.QueryAsync<BranchLocation>("SELECT l.location_id LocationId,l.store_id StoreId,s.store_name StoreName,l.location_name LocationName,l.location_type LocationType,l.is_active IsActive FROM store_locations l JOIN stores s USING(tenant_id,store_id) WHERE l.store_id=@id AND l.location_type<>'TRANSIT' ORDER BY l.location_id",new{id});
        return new(row,locations);
    }
    public async Task UpdateStoreAsync(long id,StoreUpdateRequest r)
    {
        Normalize(r);await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await c.ExecuteAsync("SET LOCAL lock_timeout='5s'",transaction:t);await Manage(c,t);
        if(!await ManagementAvailable(c,t))throw new ValidationException("Apply manual migration 089 before editing store metadata.");
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('store-management:'||sf_tenant()::text,0))",transaction:t);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:t);
        if(r.GstRegistrationId.HasValue||r.GstSetupMode is "EXISTING" or "UNREGISTERED")throw new ValidationException("Use existing GST Settings for effective-dated changes to a saved shop mapping.");
        await ValidateShopGst(c,t,r);
        var old=await c.QuerySingleOrDefaultAsync<string>("SELECT to_jsonb(s)::text FROM stores s WHERE store_id=@id FOR UPDATE",new{id},t)??throw new KeyNotFoundException("Store unavailable in this business.");
        using var doc=JsonDocument.Parse(old);var value=doc.RootElement;
        if(r.ExpectedVersion!=value.GetProperty("management_version").GetInt64())throw new System.Data.DBConcurrencyException("Store changed; reload its details before saving.");
        if(r.StoreCode!=value.GetProperty("store_code").GetString())throw new ValidationException("Existing store codes cannot be changed.");
        if(!r.IsActive&&value.GetProperty("is_active").GetBoolean())await RequireUnusedStore(c,t,id);
        await c.ExecuteAsync("UPDATE stores SET store_name=@StoreName,address=@Address,city=@City,state=@State,mobile_number=@MobileNumber,is_active=@IsActive,is_head_office=coalesce(@IsHeadOffice,is_head_office),management_details=management_details||CAST(@details AS jsonb),management_version=management_version+1 WHERE store_id=@id",new{r.StoreName,r.Address,r.City,r.State,r.MobileNumber,r.IsActive,r.IsHeadOffice,details=Metadata(r),id},t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values) SELECT sf_actor(),'Stores','UPDATE','stores',store_id,CAST(@old AS jsonb),to_jsonb(s) FROM stores s WHERE store_id=@id",new{id,old},t);await t.CommitAsync();
    }
    private static async Task RequireUnusedStore(NpgsqlConnection c,NpgsqlTransaction t,long id)
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_store_unused(@id)",new{id},t))throw new ValidationException("Store has operational history, masters, staff access or GST/policy configuration; a reviewed closure workflow is required.");
    }
}
