using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Infrastructure.Repositories;

public partial class BranchRepository
{
    private static Task<bool> ShopSetupAvailable(NpgsqlConnection c,NpgsqlTransaction? t=null)=>
        c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_shop_setup_status(bigint,text,date)') IS NOT NULL",transaction:t);

    private static async Task ValidateShopGst(NpgsqlConnection c,NpgsqlTransaction t,StoreRequest r)
    {
        if(r.GstSetupMode is not null && !await ShopSetupAvailable(c,t))
            throw new ValidationException("Apply manual migration 096 before using shop registration setup.");
        if(r.GstSetupMode is not(null or "LATER" or "EXISTING" or "UNREGISTERED"))
            throw new ValidationException("Choose an existing GST registration, an existing unregistered setup, or configure later.");
        if(r.GstBusinessId.HasValue && !await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM gst_legal_businesses WHERE business_id=@GstBusinessId AND tenant_id=sf_tenant())",r,t))
            throw new ValidationException("Choose a legal business available in GST Settings for this tenant.");
        if(r.GstSetupMode is null or "LATER")
        {
            if(r.GstRegistrationId.HasValue)throw new ValidationException("Choose a GST setup option before assigning a registration.");
            return;
        }
        if(!r.IsActive)throw new ValidationException("Keep GST configuration as Configure Later for an inactive shop.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT')" ,transaction:t))
            throw new UnauthorizedAccessException("GST edit permission is required to assign a registration. You can configure the shop later.");
        if(r.AddressLine1 is null||string.IsNullOrWhiteSpace(r.City)||r.PinCode is null||!GstRules.States.Any(s=>s.Key!="28"&&s.Value==r.State))
            throw new ValidationException("Complete the structured address, city, state and PIN before assigning GST setup.");
        var state=GstRules.States.Single(s=>s.Value==r.State).Key;
        if(!r.GstRegistrationId.HasValue||!r.GstBusinessId.HasValue||!await c.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM gst_registrations r WHERE r.tenant_id=sf_tenant()
             AND registration_id=@GstRegistrationId AND business_id=@GstBusinessId AND state_code=@state
             AND status='ACTIVE' AND CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity')
             AND CASE WHEN @mode='UNREGISTERED' THEN registration_type='UNREGISTERED' ELSE registration_type IN('REGULAR','COMPOSITION') END)
            """,new{r.GstRegistrationId,r.GstBusinessId,state,mode=r.GstSetupMode},t))
            throw new ValidationException("Choose an active, effective registration matching this shop's legal business and state.");
    }

    private static async Task MapNewShop(NpgsqlConnection c,NpgsqlTransaction t,long id,StoreRequest r)
    {
        if(r.GstRegistrationId is null)return;
        // Reuse the existing table and its GST permission/date/overlap/audit triggers.
        await c.ExecuteAsync("""
            INSERT INTO gst_shop_mappings(registration_id,business_profile_code,store_id,effective_from,effective_to,status)
            SELECT registration_id,sf_visible_business_profile(),@id,CURRENT_DATE,effective_to,'ACTIVE'
            FROM gst_registrations WHERE tenant_id=sf_tenant() AND registration_id=@GstRegistrationId
            AND NOT EXISTS(SELECT 1 FROM gst_shop_mappings WHERE tenant_id=sf_tenant() AND store_id=@id
             AND business_profile_code=sf_visible_business_profile() AND registration_id=@GstRegistrationId AND status='ACTIVE'
             AND CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity'));
            """,new{id,r.GstRegistrationId},t);
    }

    private static async Task AddShopSetup(NpgsqlConnection c,StoreDirectoryRow[] rows)
    {
        if(rows.Length==0||!await ShopSetupAvailable(c))return;
        var values=await c.QueryAsync<(long Id,string Status)>("""
            SELECT store_id,public.sf_shop_setup_status(store_id,sf_visible_business_profile(),CURRENT_DATE)
            FROM stores WHERE store_id=ANY(@ids)
            """,new{ids=rows.Select(r=>r.StoreId).ToArray()});
        foreach(var value in values)rows.Single(r=>r.StoreId==value.Id).SetupReadiness=value.Status;
    }
}
