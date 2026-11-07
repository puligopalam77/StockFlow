using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public partial class BranchRepository : IBranchRepository
{
    private readonly DbConnectionFactory factory;
    private readonly BusinessScope scope;
    // ASP.NET supplies these services when it creates this class.
    public BranchRepository(DbConnectionFactory factory, BusinessScope scope)
    {
        this.factory = factory;
        this.scope = scope;
    }

    public async Task<BranchUserState?> UserStateAsync()
    {
        using var c = factory.CreateConnection();
        // Reading the optional key through to_jsonb keeps existing databases
        // working until the owner manually installs migration 046. This does
        // not catch database failures or silently bypass tenant validation.
        return await c.QuerySingleOrDefaultAsync<BranchUserState>("""
            SELECT u.is_tenant_owner AS IsOwner,
                lower(btrim(r.role_name))='admin' AS IsAdmin,
                u.is_active AND t.is_active AND r.is_active AS IsActive,
                COALESCE(NULLIF(btrim(to_jsonb(t)->>'business_profile_code'),''),'ELECTRICAL') AS BusinessProfileCode,
                NULLIF(btrim(to_jsonb(t)->>'business_profile_code'),'') IS NOT NULL AS BusinessProfilePersisted,
                COALESCE((to_jsonb(t)->>'business_profile_version')::bigint,0) AS BusinessProfileVersion,
                to_jsonb(t) ? 'business_profile_version' AS BusinessProfileConfigurationAvailable,
                COALESCE((to_jsonb(t)->>'profile_catalog_switching_enabled')::boolean,false) AS ProfileCatalogSwitchingAvailable,
                COALESCE((to_jsonb(t)->>'profile_data_visibility_enabled')::boolean,false) AS ProfileDataVisibilityAvailable,
                COALESCE((to_jsonb(t)->>'catalog_registration_enhancements_enabled')::boolean,false) AS CatalogRegistrationEnhancementsAvailable,
                COALESCE((to_jsonb(t)->>'product_image_gallery_enabled')::boolean,false) AS ProductImageGalleryAvailable
            FROM users u JOIN tenants t USING(tenant_id) JOIN roles r ON r.role_id=u.role_id
            WHERE u.user_id=@UserId AND u.tenant_id=@TenantId
            """,
scope);
    }

    public async Task<IEnumerable<BranchLocation>> LocationsAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<BranchLocation>("""
            SELECT l.location_id AS LocationId,l.store_id AS StoreId,COALESCE(s.store_name,'Central warehouse') AS StoreName,
            l.location_name AS LocationName,l.location_type AS LocationType,l.is_active AS IsActive,
            EXISTS(SELECT 1 FROM user_store_access a WHERE a.user_id=@UserId AND a.store_id=l.store_id AND a.is_default AND a.is_active) AS IsDefault
            FROM store_locations l LEFT JOIN stores s ON s.store_id=l.store_id
            WHERE l.is_active AND l.location_type<>'TRANSIT' AND (l.store_id IS NULL OR s.is_active) ORDER BY IsDefault DESC,l.store_id NULLS LAST,l.location_id
            """,
scope);
    }

    public async Task<object> StoresAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync("SELECT store_id AS \"storeId\",store_code AS \"storeCode\",store_name AS \"storeName\",address,city,state,mobile_number AS \"mobileNumber\",gst_number AS \"gstNumber\" FROM stores WHERE is_active ORDER BY store_name");
    }

    private void Owner()
    {
        if (!scope.IsOwner)
        {
            throw new UnauthorizedAccessException("Only the business owner can manage stores and assign store access.");
        }
    }

    public async Task<long> CreateStoreAsync(StoreRequest r)
    {
        Owner();Normalize(r);await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await Manage(c,t,"CREATE");
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('store-management:'||sf_tenant()::text,0))",transaction:t);
        var metadata=await ManagementAvailable(c,t);var details=Metadata(r);
        if(!metadata&&(r.AddressLine1 is not null||r.Email is not null||r.AlternatePhone is not null))throw new ValidationException("Apply manual migration 089 before saving structured store metadata.");
        var existing=await c.ExecuteScalarAsync<long?>("SELECT store_id FROM stores WHERE lower(store_code)=lower(@StoreCode)",r,t);
        if(existing is {} id){
            var same=await c.ExecuteScalarAsync<bool>("""
            SELECT store_name=@StoreName AND address IS NOT DISTINCT FROM @Address AND city IS NOT DISTINCT FROM @City AND state IS NOT DISTINCT FROM @State AND mobile_number IS NOT DISTINCT FROM @MobileNumber AND is_active
            AND jsonb_strip_nulls(coalesce(to_jsonb(s)->'management_details','{}'::jsonb))=jsonb_strip_nulls(CAST(@details AS jsonb))
            AND (SELECT count(*) FROM store_locations l WHERE l.store_id=s.store_id AND l.location_type='SALES_FLOOR' AND l.is_active)=1
            FROM stores s WHERE store_id=@id
            """,new{r.StoreName,r.Address,r.City,r.State,r.MobileNumber,details,id},t);
            if(!same)throw new ValidationException("This store code is already used. Review the existing store instead of creating a duplicate.");
            await t.CommitAsync();return id;
        }
        var created=await c.ExecuteScalarAsync<long>(metadata?"INSERT INTO stores(store_code,store_name,address,city,state,mobile_number,management_details) VALUES(@StoreCode,@StoreName,@Address,@City,@State,@MobileNumber,CAST(@details AS jsonb)) RETURNING store_id":"INSERT INTO stores(store_code,store_name,address,city,state,mobile_number) VALUES(@StoreCode,@StoreName,@Address,@City,@State,@MobileNumber) RETURNING store_id",new{r.StoreCode,r.StoreName,r.Address,r.City,r.State,r.MobileNumber,details},t);
        await c.ExecuteAsync("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@created,'Main stock','SALES_FLOOR'); INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(sf_actor(),'Stores','CREATE','stores',@created)",new{created},t);
        await t.CommitAsync();return created;
    }
    public async Task<long> CreateLocationAsync(LocationRequest r)
    {
        Owner();r.LocationName=r.LocationName?.Trim()??"";Validator.ValidateObject(r,new(r),true);
        if(r.LocationType is not("SALES_FLOOR" or "BACKROOM" or "STOCKROOM" or "WAREHOUSE" or "SERVICE_CENTER")||r.LocationName.Length==0||r.StoreId is null&&r.LocationType!="WAREHOUSE")throw new ValidationException("Choose a valid location name and type. A location without a store must be a central warehouse.");
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await Manage(c,t,"CREATE");
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('store-management:'||sf_tenant()::text,0))",transaction:t);
        if(r.StoreId.HasValue&&!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM stores WHERE store_id=@StoreId AND is_active)",r,t))throw new ValidationException("Choose an active store in this business.");
        var existing=(await c.QueryAsync<long>("SELECT location_id FROM store_locations WHERE store_id IS NOT DISTINCT FROM @StoreId AND lower(btrim(location_name))=lower(@LocationName) AND location_type=@LocationType AND is_active",r,t)).ToArray();
        if(existing.Length>1)throw new ValidationException("Several existing locations match. Review them before retrying creation.");
        if(existing.Length==1){await t.CommitAsync();return existing[0];}
        // The location read policy resolves the persisted row through a STABLE
        // helper. Read its generated ID in the next statement, after insertion.
        await c.ExecuteAsync("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@StoreId,@LocationName,@LocationType)",r,t);
        var id=await c.ExecuteScalarAsync<long>("SELECT currval(pg_get_serial_sequence('public.store_locations','location_id'))",transaction:t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(sf_actor(),'Stores','CREATE_LOCATION','store_locations',@id)",new{id},t);await t.CommitAsync();return id;
    }

    public async Task<StoreAccessRequest> AccessAsync(int userId)
    {
        Owner();
        using var c = factory.CreateConnection();
        await Manage(c,action:"MANAGE_ACCESS");
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@userId)", new
        {
            userId
        }))
        {
            throw new KeyNotFoundException("User not found.");
        }

        return new()
        {
            StoreIds = (await c.QueryAsync<long>("SELECT store_id FROM user_store_access WHERE user_id=@userId AND is_active", new
            {
                userId
            })).ToList(),
            DefaultStoreId = await c.QuerySingleOrDefaultAsync<long?>("SELECT store_id FROM user_store_access WHERE user_id=@userId AND is_active AND is_default", new
            {
                userId
            }),
            WarehouseLocationIds = (await c.QueryAsync<long>("SELECT location_id FROM user_location_access WHERE user_id=@userId", new
            {
                userId
            })).ToList()
        };
    }

    public async Task SetAccessAsync(int userId, StoreAccessRequest r)
    {
        Owner();
        if (r.StoreIds is null || r.WarehouseLocationIds is null || r.StoreIds.Count > 500 || r.WarehouseLocationIds.Count > 500 || r.StoreIds.Distinct().Count() != r.StoreIds.Count || r.WarehouseLocationIds.Distinct().Count() != r.WarehouseLocationIds.Count || r.DefaultStoreId.HasValue && !r.StoreIds.Contains(r.DefaultStoreId.Value))
        {
            throw new ValidationException("Choose distinct stores and a default from the assigned stores.");
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        using var t = c.BeginTransaction();
        await Manage(c,t,"MANAGE_ACCESS");
        var target = await c.QuerySingleOrDefaultAsync<BranchUserState>("SELECT is_tenant_owner AS IsOwner FROM users WHERE user_id=@userId FOR UPDATE", new
        {
            userId
        }, t) ?? throw new KeyNotFoundException("User not found.");
        if (!target.IsOwner && r.StoreIds.Count + r.WarehouseLocationIds.Count == 0)
        {
            throw new ValidationException("Assign at least one store or central warehouse.");
        }

        if (await c.ExecuteScalarAsync<long>("SELECT count(*) FROM stores WHERE store_id=ANY(@ids) AND is_active", new
        {
            ids = r.StoreIds.ToArray()
        }, t) != r.StoreIds.Count)
        {
            throw new ValidationException("A store is outside this business or inactive.");
        }

        if (await c.ExecuteScalarAsync<long>("SELECT count(*) FROM store_locations WHERE location_id=ANY(@ids) AND store_id IS NULL AND location_type='WAREHOUSE' AND is_active",
new
{
    ids = r.WarehouseLocationIds.ToArray()
},
t) != r.WarehouseLocationIds.Count)
        {
            throw new ValidationException("Choose central warehouses in this business.");
        }

        await c.ExecuteAsync("DELETE FROM user_store_access WHERE user_id=@userId;DELETE FROM user_location_access WHERE user_id=@userId",
new
{
    userId
},
t);
        foreach (var id in r.StoreIds)
        {
            await c.ExecuteAsync("INSERT INTO user_store_access(user_id,store_id,is_default) VALUES(@userId,@id,@isDefault)",
new
{
    userId,
    id,
    isDefault = id == (r.DefaultStoreId ?? r.StoreIds.First())
},
t);
        }

        foreach (var id in r.WarehouseLocationIds)
        {
            await c.ExecuteAsync("INSERT INTO user_location_access(user_id,location_id) VALUES(@userId,@id)", new
            {
                userId,
                id
            }, t);
        }

        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@actor,'Stores','ASSIGN','users',@userId,CAST(@payload AS jsonb))",
new
{
    actor = scope.UserId,
    userId,
    payload = JsonSerializer.Serialize(r)
},
t);
        t.Commit();
    }

    public async Task<IEnumerable<StockComparison>> ComparisonAsync(string? term)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<StockComparison>("""
            SELECT v.variant_id AS VariantId,p.product_name AS ProductName,v.variant_name AS VariantName,
            COALESCE(s.store_name,CASE WHEN l.location_type='TRANSIT' THEN 'In transit' ELSE 'Central warehouse' END) AS StoreName,
            l.location_name AS LocationName,l.location_type AS LocationType,b.current_stock AS CurrentStock
            FROM vw_location_current_stock b JOIN product_variants v ON v.variant_id=b.variant_id JOIN products p ON p.product_id=v.product_id
            JOIN store_locations l ON l.location_id=b.location_id LEFT JOIN stores s ON s.store_id=l.store_id
            WHERE sf_visible_location(l.location_id) AND (@term IS NULL OR p.product_name ILIKE @term OR v.variant_name ILIKE @term)
            ORDER BY p.product_name,v.variant_id,s.store_name,l.location_id
            """,
new
{
    term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%"
});
    }

    public async Task<object> SummaryAsync(bool includeSales)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync("""
            SELECT COALESCE(s.store_name,CASE WHEN l.location_type='TRANSIT' THEN 'In transit' ELSE 'Central warehouse' END) AS "storeName",
            l.location_name AS "locationName",l.location_type AS "locationType",
            COALESCE((SELECT sum(current_stock) FROM vw_location_current_stock b WHERE b.location_id=l.location_id),0) AS "currentStock",
            COALESCE((SELECT sum(quantity) FROM inventory_transactions i WHERE i.location_id=l.location_id AND i.transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN')),0) AS "externalInflow",
            COALESCE((SELECT sum(quantity) FROM inventory_transactions i WHERE i.location_id=l.location_id AND i.transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_LOSS')),0) AS "externalOutflow",
            COALESCE((SELECT sum(quantity) FROM inventory_transactions i WHERE i.location_id=l.location_id AND i.transaction_type='TRANSFER_IN'),0) AS "transferIn",
            COALESCE((SELECT sum(quantity) FROM inventory_transactions i WHERE i.location_id=l.location_id AND i.transaction_type='TRANSFER_OUT'),0) AS "transferOut",
            CASE WHEN @includeSales THEN COALESCE((SELECT sum(total_amount) FROM sales x WHERE x.location_id=l.location_id AND x.status='COMPLETED'),0) END AS "salesTotal"
            FROM store_locations l LEFT JOIN stores s ON s.store_id=l.store_id
            WHERE sf_visible_location(l.location_id) ORDER BY s.store_name,l.location_id
            """,
new
{
    includeSales
});
    }
}
