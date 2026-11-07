using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public InventoryRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private string BalanceSql => """
            SELECT p.product_id AS ProductId, v.variant_id AS VariantId, p.product_name AS ProductLabel,p.product_name || ' / ' || v.variant_name AS ProductName, v.variant_name AS VariantName,
                p.product_image_url AS ProductImageUrl,(p.product_image_name IS NOT NULL) AS HasProductImage,c.category_name AS CategoryName,
                v.attributes::text AS AttributesJson,p.unit_of_measure AS UnitOfMeasure,
                b.brand_name AS BrandName, v.sku AS Sku, (SELECT barcode FROM variant_barcodes WHERE variant_id=v.variant_id AND is_active ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) AS Barcode, (p.is_active AND v.is_active) AS IsActive,
                v.is_serial_tracked AS IsSerialTracked, sf_variant_minimum(v.variant_id,v.minimum_stock_level) AS MinimumStockLevel,
                s.current_stock AS CurrentStock,
                (SELECT COUNT(*) FROM product_units pu WHERE pu.variant_id = v.variant_id AND pu.unit_status = 'IN_STOCK') AS AvailableUnits
            FROM products p JOIN product_variants v ON v.product_id=p.product_id JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id
            JOIN categories c ON c.category_id = p.category_id LEFT JOIN brands b ON b.brand_id = p.brand_id
            WHERE (@IncludeInactive OR (p.is_active AND v.is_active))
                AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')
                AND (@Term IS NULL OR p.product_name ILIKE @Term OR v.sku ILIKE @Term OR v.variant_name ILIKE @Term OR 
            """ + (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "concat_ws(' ',sf_searchable_attributes(p.category_id,v.attributes),(SELECT string_agg(i.supplier_sku,' ') FROM variant_supplier_identifiers i WHERE i.variant_id=v.variant_id))" : "v.attributes::text") + """
             ILIKE @Term
                    OR c.category_name ILIKE @Term OR b.brand_name ILIKE @Term OR EXISTS(SELECT 1 FROM variant_barcodes vb WHERE vb.variant_id=v.variant_id AND vb.is_active AND vb.barcode ILIKE @Term)
                    OR EXISTS(SELECT 1 FROM product_units pu WHERE pu.variant_id=v.variant_id AND (pu.serial_number ILIKE @Term OR pu.barcode ILIKE @Term OR pu.internal_barcode ILIKE @Term OR pu.manufacturer_barcode ILIKE @Term)))
                AND (NOT @LowStockOnly OR s.current_stock <= sf_variant_minimum(v.variant_id,v.minimum_stock_level))

            """;

    public async Task<IEnumerable<InventoryBalance>> GetBalancesAsync(string? term, bool lowStockOnly, bool includeInactive)
    {
        await using var connection = factory.CreateConnection();
        return await connection.QueryAsync<InventoryBalance>(BalanceSql+" ORDER BY p.product_name,p.product_id,v.variant_id;",new{
            Term=string.IsNullOrWhiteSpace(term)?null:"%"+term.Trim()+"%",LowStockOnly=lowStockOnly,IncludeInactive=includeInactive});
    }
    public async Task<LowStockPage> GetLowStockPageAsync(string? term,string? stockStatus,int page,int pageSize)
    {
        if(page<1||page>100000||pageSize is not (10 or 25 or 50 or 100)||term?.Length>150||stockStatus is not (null or "" or "OUT" or "LOW"))
            throw new ValidationException("Choose valid shortage filters and pagination.");
        factory.RequireLocation();
        await using var connection=factory.CreateConnection();await connection.OpenAsync();
        if(!await connection.ExecuteScalarAsync<bool>("SELECT sf_store()>0 AND sf_can_location(sf_location()) AND EXISTS(SELECT 1 FROM store_locations WHERE tenant_id=sf_tenant() AND store_id=sf_store() AND location_id=sf_location() AND is_active) AND sf_transfer_permission('INVENTORY','VIEW')"))
            throw new UnauthorizedAccessException("Choose an authorized inventory shop and location.");
        await using var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:tx);
        var cte="WITH balances AS ("+BalanceSql+"), filtered AS (SELECT *,GREATEST(minimumstocklevel::bigint-currentstock,0) AS QuantityNeeded,CASE WHEN currentstock=0 THEN 'OUT' WHEN currentstock<0 THEN 'REVIEW' ELSE 'LOW' END AS StockStatus FROM balances WHERE (@Status::text IS NULL OR (@Status='OUT' AND currentstock=0) OR (@Status='LOW' AND currentstock>0))) ";
        var args=new{Term=string.IsNullOrWhiteSpace(term)?null:"%"+term.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%",Status=string.IsNullOrEmpty(stockStatus)?null:stockStatus,LowStockOnly=true,IncludeInactive=false,Offset=(page-1)*pageSize,PageSize=pageSize};
        using var results=await connection.QueryMultipleAsync(cte+"SELECT count(*) AS LowStockVariants,count(*) FILTER(WHERE currentstock=0) AS OutOfStock,coalesce(sum(quantityneeded),0)::bigint AS UnitsToReplenish FROM filtered;"+cte+"SELECT * FROM filtered ORDER BY productname,productid,variantid LIMIT @PageSize OFFSET @Offset;",args,tx);
        var summary=await results.ReadSingleAsync<LowStockSummary>();var rows=(await results.ReadAsync<LowStockItem>()).ToArray();
        await tx.CommitAsync();return new(rows,summary.LowStockVariants,page,pageSize,summary);
    }

    public async Task<IEnumerable<InventoryMovement>> GetHistoryAsync(int? productId, DateTime? from, DateTime? to, int offset, long? variantId = null)
    {
        await using var connection = factory.CreateConnection();
        return await connection.QueryAsync<InventoryMovement>("""
            SELECT COUNT(*) OVER() AS TotalCount,it.inventory_transaction_id AS InventoryTransactionId, it.product_id AS ProductId, it.variant_id AS VariantId,
                p.product_name || ' / ' || v.variant_name AS ProductName, COALESCE(it.serial_number_snapshot,pu.serial_number) AS SerialNumber, it.transaction_type AS TransactionType,
                it.quantity AS Quantity, it.reference_type AS ReferenceType, it.reference_id AS ReferenceId,
                it.remarks AS Remarks, u.full_name AS CreatedByName, it.created_at AS CreatedAt
            FROM inventory_transactions it JOIN products p ON p.product_id = it.product_id JOIN product_variants v ON v.variant_id=it.variant_id
            LEFT JOIN product_units pu ON pu.product_unit_id = it.product_unit_id JOIN users u ON u.user_id = it.created_by
            WHERE (@ProductId IS NULL OR it.product_id = @ProductId) AND (CAST(@VariantId AS bigint) IS NULL OR it.variant_id=@VariantId)
                AND (CAST(@FromDate AS date) IS NULL OR it.created_at >= CAST(@FromDate AS date))
                AND (CAST(@ToDate AS date) IS NULL OR it.created_at < CAST(@ToDate AS date) + INTERVAL '1 day')
            ORDER BY it.created_at DESC, it.inventory_transaction_id DESC LIMIT 100 OFFSET @Offset;
            """,
new
{
    ProductId = productId,
    VariantId = variantId,
    FromDate = from?.Date,
    ToDate = to?.Date,
    Offset = offset
});
    }

    public async Task<IEnumerable<InventoryUnit>> GetUnitsAsync(int? productId, string? term, bool availableOnly, int offset, long? variantId = null)
    {
        await using var connection = factory.CreateConnection();
        return await connection.QueryAsync<InventoryUnit>("""
            SELECT COUNT(*) OVER() AS TotalCount,pu.product_unit_id AS ProductUnitId, pu.product_id AS ProductId, pu.variant_id AS VariantId, p.product_name || ' / ' || v.variant_name AS ProductName,
                COALESCE(to_jsonb(pu)->>'serial_source','LEGACY_UNKNOWN') AS SerialSource, pu.serial_number AS SerialNumber, COALESCE(pu.internal_barcode,pu.manufacturer_barcode,pu.barcode) AS Barcode, pu.unit_status AS UnitStatus, pu.purchase_id AS PurchaseId,pu.purchase_item_id AS PurchaseItemId,
                ARRAY[pu.serial_number::text,pu.barcode::text,pu.internal_barcode::text,pu.manufacturer_barcode::text] AS Barcodes
            FROM product_units pu JOIN products p ON p.product_id = pu.product_id JOIN product_variants v ON v.variant_id=pu.variant_id
            WHERE (@ProductId IS NULL OR pu.product_id = @ProductId) AND (CAST(@VariantId AS bigint) IS NULL OR pu.variant_id=@VariantId)
                AND EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=p.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))
                AND (NOT @AvailableOnly OR (pu.unit_status = 'IN_STOCK' AND p.is_active AND v.is_active))
                AND (@Term IS NULL OR pu.serial_number ILIKE @Term OR pu.barcode ILIKE @Term OR pu.manufacturer_barcode ILIKE @Term OR pu.internal_barcode ILIKE @Term OR p.product_name ILIKE @Term OR v.variant_name ILIKE @Term)
            ORDER BY pu.product_unit_id DESC LIMIT 100 OFFSET @Offset;
            """,
new
{
    ProductId = productId,
    VariantId = variantId,
    Term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%",
    AvailableOnly = availableOnly,
    Offset = offset
});
    }

    public async Task<long> AdjustAsync(StockAdjustmentRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        InventoryRules.Validate(request);
        var payload = JsonSerializer.Serialize(request);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        // Serialize retries, including concurrent retries, before consulting the audit receipt.
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@Key, 0));",
new
{
    Key = "StockFlow.Inventory:" + request.RequestId
},
transaction);
        var prior = await connection.QuerySingleOrDefaultAsync<AdjustmentReceipt>("""
            SELECT audit_log_id AS Id, new_values::text AS Payload, user_id AS UserId FROM audit_logs
            WHERE module_name = 'Inventory' AND action_name = 'ADJUST' AND new_values->>'RequestId' = @RequestId;
            """,
new
{
    RequestId = request.RequestId.ToString()
},
transaction);
        if (prior is not null)
        {
            var same = await connection.ExecuteScalarAsync<bool>("SELECT CAST(@Previous AS jsonb) = CAST(@Payload AS jsonb);",
new
{
    Previous = prior.Payload,
    Payload = payload
},
transaction);
            if (!same || prior.UserId != userId)
            {
                throw new ValidationException("This request ID has already been used for a different adjustment.");
            }

            await transaction.CommitAsync();
            return prior.Id;
        }

        string? purchaseBefore = null;
        if (request.PurchaseId is { } purchaseId)
        {
            var purchase = await connection.QuerySingleOrDefaultAsync<StockFlow.Application.DTOs.Purchases.PurchaseResponse>("SELECT purchase_id PurchaseId,status Status,edit_version EditVersion FROM purchases WHERE purchase_id=@purchaseId FOR UPDATE", new { purchaseId }, transaction);
            if (purchase is null || purchase.Status is not ("COMPLETED" or "PARTIALLY_RECEIVED")) throw new ValidationException("Stock corrections require a received purchase at this location.");
            PurchaseRepository.Version(purchase.EditVersion, request.ExpectedVersion);
            if (request.Reason.Length < 3 || request.PurchaseItemId is null) throw new ValidationException("Select the purchase item and give a reason for correction.");
            var received = await connection.QuerySingleOrDefaultAsync<int?>("SELECT received_quantity FROM purchase_items WHERE purchase_id=@PurchaseId AND purchase_item_id=@PurchaseItemId AND product_id=@ProductId AND variant_id=@VariantId", request, transaction);
            if (received is null) throw new ValidationException("The product does not belong to this purchase item.");
            var net = await connection.ExecuteScalarAsync<int>("SELECT coalesce(sum(CASE WHEN request_payload->>'TransactionType'='ADJUSTMENT_IN' THEN (request_payload->>'Quantity')::int ELSE -(request_payload->>'Quantity')::int END),0)::int FROM purchase_changes WHERE purchase_id=@PurchaseId AND change_type='STOCK' AND (request_payload->>'PurchaseItemId')::bigint=@PurchaseItemId", request, transaction);
            var returned = await connection.ExecuteScalarAsync<int>("SELECT coalesce(sum(quantity),0)::int FROM purchase_return_items WHERE purchase_item_id=@PurchaseItemId", request, transaction);
            if (request.TransactionType != "ADJUSTMENT_IN" && request.Quantity > received+net-returned) throw new ValidationException("Correction exceeds this purchase item's remaining received quantity.");
            if (request.ProductUnitIds.Count > 0 && await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM product_units WHERE product_unit_id=ANY(@ProductUnitIds) AND purchase_item_id=@PurchaseItemId", request, transaction) != request.ProductUnitIds.Count) throw new ValidationException("Select serials received under this purchase item.");
            purchaseBefore = await PurchaseRepository.ChangeSnapshot(connection, transaction, purchaseId);
        }
        else if (request.PurchaseItemId.HasValue) throw new ValidationException("Select the purchase for this correction.");
        // Conflicts with other adjustments and the purchase repository's product FOR SHARE lock.
        var tracked = await connection.QuerySingleOrDefaultAsync<bool?>("SELECT is_serial_tracked FROM products WHERE product_id = @ProductId AND is_active FOR UPDATE;",
new
{
    request.ProductId
},
transaction);
        if (tracked is null)
        {
            throw new ValidationException("Choose an active product.");
        }

        var variant = await VariantRepository.ResolveAsync(connection, transaction, request.ProductId, request.VariantId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
        tracked = variant.IsSerialTracked;
        if (await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE product_id = @ProductId AND
                (quantity <= 0 OR transaction_type NOT IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS')));
            """,
new
{
    request.ProductId
},
transaction))
        {
            throw new ValidationException("Review legacy or invalid movements before adjusting this product.");
        }

        var stock = await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id = @VariantId;",
new
{
    variant.VariantId
},
transaction);
        var incoming = request.TransactionType == "ADJUSTMENT_IN";
        if (!incoming && stock < request.Quantity)
        {
            throw new ValidationException("Insufficient stock for this adjustment.");
        }

        var units = new List<long?>();
        if (tracked.Value)
        {
            var available = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_units WHERE variant_id = @VariantId AND unit_status = 'IN_STOCK';",
new
{
    variant.VariantId
},
transaction);
            if (available != stock)
            {
                throw new ValidationException("Serialized units and the stock ledger disagree. Reconcile existing records before adjusting.");
            }

            if (incoming)
            {
                if (request.SerialNumbers.Count != request.Quantity || request.ProductUnitIds.Count != 0)
                {
                    throw new ValidationException("Enter one new serial number per incoming unit.");
                }

                foreach (var serial in request.SerialNumbers)
                {
                    units.Add(await connection.ExecuteScalarAsync<long>("""
                        INSERT INTO product_units (product_id, variant_id, serial_number, unit_status,purchase_id,purchase_item_id) VALUES (@ProductId, @VariantId, @Serial, 'IN_STOCK',@PurchaseId,@PurchaseItemId) RETURNING product_unit_id;
                        """,
new
{
    request.ProductId,
    variant.VariantId,
    Serial = serial,
    request.PurchaseId,
    request.PurchaseItemId
},
transaction));
                }
            }
            else
            {
                if (request.ProductUnitIds.Count != request.Quantity || request.SerialNumbers.Count != 0)
                {
                    throw new ValidationException("Select one available physical unit per outgoing quantity.");
                }

                foreach (var unitId in request.ProductUnitIds.Order())
                {
                    var status = await connection.QuerySingleOrDefaultAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id = @Id AND product_id = @ProductId AND variant_id=@VariantId FOR UPDATE;",
new
{
    Id = unitId,
    request.ProductId,
    variant.VariantId
},
transaction);
                    if (status != "IN_STOCK")
                    {
                        throw new ValidationException("A selected unit is unavailable or belongs to another product.");
                    }

                    await connection.ExecuteAsync("UPDATE product_units SET unit_status = @Status, updated_at = CURRENT_TIMESTAMP WHERE product_unit_id = @Id;",
new
{
    Id = unitId,
    Status = request.TransactionType == "DAMAGED" ? "DAMAGED" : "ADJUSTED_OUT"
},
transaction);
                    units.Add(unitId);
                }
            }
        }
        else
        {
            if (request.SerialNumbers.Count != 0 || request.ProductUnitIds.Count != 0)
            {
                throw new ValidationException("Quantity-only products cannot include serial numbers or physical units.");
            }

            units.Add(null);
        }

        var receipt = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO audit_logs (user_id, module_name, action_name, table_name, record_id, new_values, reason)
            VALUES (@UserId, 'Inventory', 'ADJUST', 'products', @ProductId, CAST(@Payload AS jsonb), @Reason) RETURNING audit_log_id;
            """,
new
{
    UserId = userId,
    request.ProductId,
    Payload = payload,
    request.Reason
},
transaction);
        foreach (var unitId in units)
        {
            await connection.ExecuteAsync("""
                INSERT INTO inventory_transactions (product_id, variant_id, product_unit_id, transaction_type, quantity, reference_type, reference_id, remarks, created_by)
                VALUES (@ProductId, @VariantId, @UnitId, @TransactionType, @Quantity, 'ADJUSTMENT', @Receipt, @Reason, @UserId);
                """,
new
{
    request.ProductId,
    variant.VariantId,
    UnitId = unitId,
    request.TransactionType,
    Quantity = tracked.Value ? 1 : request.Quantity,
    Receipt = receipt,
    request.Reason,
    UserId = userId
},
transaction);
        }

        if (request.PurchaseId is { } changedPurchase)
        {
            await connection.ExecuteAsync("UPDATE purchases SET notes=notes WHERE purchase_id=@changedPurchase", new { changedPurchase }, transaction);
            var after = await PurchaseRepository.ChangeSnapshot(connection, transaction, changedPurchase);
            var oldSnapshot = System.Text.Json.Nodes.JsonNode.Parse(purchaseBefore!)!;
            var newSnapshot = System.Text.Json.Nodes.JsonNode.Parse(after)!;
            oldSnapshot["stock"] = JsonSerializer.SerializeToNode(new { variant_id = variant.VariantId, available_quantity = stock });
            newSnapshot["stock"] = JsonSerializer.SerializeToNode(new { variant_id = variant.VariantId, available_quantity = stock + (incoming ? request.Quantity : -request.Quantity) });
            await PurchaseRepository.WriteChange(connection, transaction, changedPurchase, request.RequestId, "STOCK", request.Reason,
                oldSnapshot.ToJsonString(), newSnapshot.ToJsonString(), payload, userId, adjustment: receipt);
        }
        await transaction.CommitAsync();
        return receipt;
    }

    private sealed class AdjustmentReceipt
    {
        public long Id
        {
            get; set;
        }
        public string Payload { get; set; } = "";
        public int UserId
        {
            get; set;
        }
    }
}
