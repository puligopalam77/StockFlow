using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Warranty;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class WarrantyRepository : IWarrantyRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public WarrantyRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    const string Read = """
        SELECT w.warranty_id AS WarrantyId,i.sale_id AS SaleId,w.sale_item_id AS SaleItemId,i.product_id AS ProductId,
        w.product_unit_id AS ProductUnitId,COALESCE(i.product_name_snapshot,p.product_name) AS ProductName,
        COALESCE(i.brand_name_snapshot,b.brand_name) AS BrandName,COALESCE(i.model_snapshot,p.model) AS Model,
        COALESCE(to_jsonb(u)->>'serial_source','LEGACY_UNKNOWN') AS SerialSource,u.serial_number AS SerialNumber,s.invoice_number AS InvoiceNumber,s.sale_date AS SaleDate,
        COALESCE(s.customer_name_snapshot,c.customer_name) AS CustomerName,COALESCE(s.customer_mobile_snapshot,c.mobile_number) AS CustomerMobile,
        w.warranty_start_date AS WarrantyStartDate,w.warranty_end_date AS WarrantyEndDate,
        CASE WHEN s.status='CANCELLED' THEN 'CANCELLED' WHEN w.warranty_status IN ('ACTIVE','CLAIMED') AND w.warranty_end_date<CURRENT_DATE THEN 'EXPIRED' ELSE w.warranty_status END AS WarrantyStatus,
        w.warranty_type AS WarrantyType,w.warranty_provider AS WarrantyProvider,w.claim_count AS ClaimCount,
        (s.status='COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED') AND CURRENT_DATE BETWEEN w.warranty_start_date AND w.warranty_end_date) AS IsUnderWarranty,
        (s.status='COMPLETED' AND w.warranty_status='ACTIVE' AND CURRENT_DATE BETWEEN w.warranty_start_date AND w.warranty_end_date) AS CanCreateClaim,
        GREATEST(w.warranty_end_date-CURRENT_DATE,0) AS RemainingDays,w.parent_warranty_id AS ParentWarrantyId,
        (SELECT MAX(warranty_id) FROM warranties child WHERE child.parent_warranty_id=w.warranty_id) AS ReplacementWarrantyId,
        ru.serial_number AS ReplacementSerialNumber,w.replacement_warranty_rule AS ReplacementWarrantyRule,w.notes AS Notes
        FROM warranties w JOIN sale_items i ON i.sale_item_id=w.sale_item_id JOIN sales s ON s.sale_id=i.sale_id
        LEFT JOIN products p ON p.product_id=i.product_id LEFT JOIN brands b ON b.brand_id=p.brand_id
        LEFT JOIN customers c ON c.customer_id=s.customer_id LEFT JOIN product_units u ON u.product_unit_id=w.product_unit_id
        LEFT JOIN product_units ru ON ru.product_unit_id=w.replacement_product_unit_id
        """;
    async Task<WarrantyLookupResponse?> One(string where, object args)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryFirstOrDefaultAsync<WarrantyLookupResponse>(Read + where, args);
    }

    public Task<WarrantyLookupResponse?> GetByIdAsync(long id)
    {
        return One(" WHERE w.warranty_id=@id", new
        {
            id
        });
    }

    public Task<WarrantyLookupResponse?> GetBySerialAsync(string serial)
    {
        return One(" WHERE LOWER(BTRIM(u.serial_number))=LOWER(@serial) OR LOWER(BTRIM(u.internal_barcode))=LOWER(@serial) OR LOWER(BTRIM(u.manufacturer_barcode))=LOWER(@serial) OR LOWER(BTRIM(u.barcode))=LOWER(@serial) ORDER BY w.warranty_id DESC", new
        {
            serial
        });
    }

    public Task<WarrantyLookupResponse?> GetByUnitAsync(long unitId)
    {
        return One(" WHERE w.product_unit_id=@unitId ORDER BY w.warranty_id DESC", new
        {
            unitId
        });
    }

    public async Task<IEnumerable<WarrantyLookupResponse>> GetByInvoiceAsync(string invoice)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<WarrantyLookupResponse>(Read + " WHERE LOWER(s.invoice_number)=LOWER(@invoice) ORDER BY w.warranty_id", new
        {
            invoice
        });
    }

    public async Task<IEnumerable<WarrantyLookupResponse>> GetExpiringAsync(int days, int offset)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<WarrantyLookupResponse>(Read.Insert(7,"COUNT(*) OVER() AS TotalCount,") + " WHERE s.status='COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED') AND w.warranty_end_date BETWEEN CURRENT_DATE AND CURRENT_DATE+@days ORDER BY w.warranty_end_date,w.warranty_id LIMIT 100 OFFSET @offset",
new
{
    days,
    offset
});
    }

    public async Task<IEnumerable<WarrantyClaimResponse>> GetClaimsAsync(long? warrantyId, string? status, int offset)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<WarrantyClaimResponse>("""
        SELECT COUNT(*) OVER() AS TotalCount,wc.warranty_claim_id AS WarrantyClaimId,wc.warranty_id AS WarrantyId,wc.claim_number AS ClaimNumber,wc.claim_date AS ClaimDate,
        COALESCE(i.product_name_snapshot,p.product_name) AS ProductName,u.serial_number AS SerialNumber,COALESCE(s.customer_name_snapshot,c.customer_name) AS CustomerName,
        wc.complaint AS Complaint,wc.claim_status AS ClaimStatus,wc.resolution_type AS ResolutionType,wc.resolution_notes AS ResolutionNotes,
        wc.replacement_product_unit_id AS ReplacementProductUnitId,wc.replacement_warranty_id AS ReplacementWarrantyId
        FROM warranty_claims wc JOIN warranties w ON w.warranty_id=wc.warranty_id JOIN sale_items i ON i.sale_item_id=w.sale_item_id
        JOIN sales s ON s.sale_id=i.sale_id LEFT JOIN products p ON p.product_id=i.product_id LEFT JOIN customers c ON c.customer_id=s.customer_id
        LEFT JOIN product_units u ON u.product_unit_id=w.product_unit_id
        WHERE (@warrantyId::bigint IS NULL OR wc.warranty_id=@warrantyId) AND (@status::text IS NULL OR wc.claim_status=@status)
        ORDER BY wc.warranty_claim_id DESC LIMIT 100 OFFSET @offset
        """,
new
{
    warrantyId,
    status,
    offset
});
    }

    public async Task<IEnumerable<WarrantyEventResponse>> GetHistoryAsync(long warrantyId, int offset)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<WarrantyEventResponse>("""
        SELECT e.warranty_event_id AS WarrantyEventId,e.warranty_claim_id AS WarrantyClaimId,e.action_name AS ActionName,e.old_status AS OldStatus,
        e.new_status AS NewStatus,e.remarks AS Remarks,u.username AS CreatedByName,e.created_at AS CreatedAt
        FROM warranty_events e JOIN users u ON u.user_id=e.created_by WHERE e.warranty_id=@warrantyId ORDER BY e.warranty_event_id DESC LIMIT 100 OFFSET @offset
        """,
new
{
    warrantyId,
    offset
});
    }

    static async Task<long?> Replay(NpgsqlConnection c, NpgsqlTransaction t, Guid id, string payload, int user)
    {
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", new
        {
            key = "StockFlow.Warranty:" + id
        }, t);
        var e = await c.QuerySingleOrDefaultAsync<Retry>("SELECT COALESCE(warranty_claim_id,warranty_id) AS Id,(request_payload=CAST(@payload AS jsonb) AND created_by=@user) AS Matches FROM warranty_events WHERE request_id=@id",
new
{
    id,
    payload,
    user
},
t);
        if (e is null)
        {
            return null;
        }

        if (!e.Matches)
        {
            throw new ValidationException("This request ID was already used with different details.");
        }

        return e.Id;
    }

    static Task Event(NpgsqlConnection c,
NpgsqlTransaction t,
long wid,
long? claim,
string action,
string? before,
string after,
string? notes,
Guid? id,
string? payload,
int user)
    {
        return c.ExecuteAsync("""
        INSERT INTO warranty_events(warranty_id,warranty_claim_id,action_name,old_status,new_status,remarks,request_id,request_payload,created_by)
        VALUES(@wid,@claim,@action,@before,@after,@notes,@id,CAST(@payload AS jsonb),@user)
        """,
new
{
    wid,
    claim,
    action,
    before,
    after,
    notes,
    id,
    payload,
    user
},
t);
    }

    static async Task<Locked> Lock(NpgsqlConnection c, NpgsqlTransaction t, long wid)
    {
        var key = await c.QuerySingleOrDefaultAsync<Locked>("SELECT i.sale_id AS SaleId,i.product_id AS ProductId FROM warranties w JOIN sale_items i ON i.sale_item_id=w.sale_item_id WHERE w.warranty_id=@wid",
new
{
    wid
},
t) ?? throw new ValidationException("Warranty not found.");
        var sale = await c.QuerySingleAsync<string>("SELECT status FROM sales WHERE sale_id=@SaleId FOR SHARE", key, t);
        await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=@ProductId FOR UPDATE", key, t);
        var w = await c.QuerySingleAsync<Locked>("SELECT warranty_id AS Id,sale_item_id AS ItemId,product_unit_id AS UnitId,warranty_status AS Status,warranty_start_date AS Start,warranty_end_date AS End,CURRENT_DATE AS Today FROM warranties WHERE warranty_id=@wid FOR UPDATE",
new
{
    wid
},
t);
        if (sale != "COMPLETED")
        {
            throw new ValidationException("The sale is not completed.");
        }

        w.SaleId = key.SaleId;
        w.ProductId = key.ProductId;
        return w;
    }

    public async Task<long> CreateClaimAsync(CreateWarrantyClaimRequest request, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "CREATE",
            request
        });
        var replay = await Replay(c, t, request.RequestId, payload, userId);
        if (replay.HasValue)
        {
            return replay.Value;
        }

        var w = await Lock(c, t, request.WarrantyId);
        if (w.Status != "ACTIVE" || w.Today < w.Start || w.Today > w.End)
        {
            throw new ValidationException("This warranty is not eligible for a new claim.");
        }

        var id = await c.QuerySingleAsync<long>("""
            INSERT INTO warranty_claims(warranty_id,claim_number,complaint,created_by)
            SELECT @WarrantyId,'WCL-'||EXTRACT(YEAR FROM CURRENT_DATE)::text||'-'||LPAD(n::text,GREATEST(6,LENGTH(n::text)),'0'),@Complaint,@userId
            FROM (SELECT nextval('warranty_claim_number_seq') AS n) sequence_value RETURNING warranty_claim_id
            """,
new
{
    request.WarrantyId,
    request.Complaint,
    userId
},
t);
        await c.ExecuteAsync("UPDATE warranties SET warranty_status='CLAIMED',claim_count=claim_count+1,updated_at=CURRENT_TIMESTAMP WHERE warranty_id=@WarrantyId",
request,
t);
        await Event(c, t, w.Id, id, "CREATE_CLAIM", w.Status, "CLAIMED", request.Complaint, request.RequestId, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<bool> SetCoverageAsync(long warrantyId, WarrantyCoverageRequest request, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "COVERAGE",
            warrantyId,
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is not null)
        {
            return true;
        }

        var w = await Lock(c, t, warrantyId);
        if (w.Status is "REPLACED" or "CANCELLED")
        {
            throw new ValidationException("Historical warranty coverage cannot be changed.");
        }

        await c.ExecuteAsync("UPDATE warranties SET warranty_type=@WarrantyType,warranty_provider=@WarrantyProvider,updated_at=CURRENT_TIMESTAMP WHERE warranty_id=@warrantyId",
new
{
    request.WarrantyType,
    request.WarrantyProvider,
    warrantyId
},
t);
        await Event(c,
t,
warrantyId,
null,
"COVERAGE",
w.Status,
w.Status,
$"{request.WarrantyType ?? "Unspecified"} / {request.WarrantyProvider ?? "Unspecified"}: {request.Reason}",
request.RequestId,
payload,
userId);
        await t.CommitAsync();
        return true;
    }

    public async Task<bool> ResolveClaimAsync(long claimId, ResolveWarrantyClaimRequest request, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "RESOLVE",
            claimId,
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is not null)
        {
            return true;
        }

        var wid = await c.QuerySingleOrDefaultAsync<long?>("SELECT warranty_id FROM warranty_claims WHERE warranty_claim_id=@claimId", new
        {
            claimId
        }, t);
        if (wid is null)
        {
            return false;
        }

        var w = await Lock(c, t, wid.Value);
        var status = await c.QuerySingleAsync<string>("SELECT claim_status FROM warranty_claims WHERE warranty_claim_id=@claimId FOR UPDATE", new
        {
            claimId
        }, t);
        if (status != request.ExpectedStatus || w.Status != "CLAIMED")
        {
            throw new ValidationException("The claim changed. Reload before updating it.");
        }

        long? child = null;
        if (request.ClaimStatus == "REPLACED")
        {
            if (w.UnitId is null)
            {
                throw new ValidationException("Physical replacement requires a serial-tracked warranty.");
            }

            var units = (await c.QueryAsync<Unit>("SELECT product_unit_id AS Id,product_id AS ProductId,variant_id AS VariantId,unit_status AS Status,sale_id AS SaleId FROM product_units WHERE product_unit_id IN (@old,@next) ORDER BY product_unit_id FOR UPDATE",
new
{
    old = w.UnitId,
    next = request.ReplacementProductUnitId
},
t)).ToList();
            var old = units.SingleOrDefault(x => x.Id == w.UnitId);
            var next = units.SingleOrDefault(x => x.Id == request.ReplacementProductUnitId);
            if (old is null || old.SaleId != w.SaleId || old.Status is not ("SOLD" or "REPLACEMENT_ISSUED") || next is null || next.ProductId != w.ProductId || next.VariantId != old.VariantId || next.Status != "IN_STOCK" || next.SaleId.HasValue)
            {
                throw new ValidationException("Select an available unit of the same product; the original unit must still belong to this sale.");
            }

            var stock = await c.QuerySingleAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@VariantId", next, t);
            var count = await c.QuerySingleAsync<long>("SELECT COUNT(*) FROM product_units WHERE variant_id=@VariantId AND unit_status='IN_STOCK'", next, t);
            if (stock < 1 || stock != count)
            {
                throw new ValidationException("Stock is unavailable or needs reconciliation.");
            }

            var months = await c.QuerySingleOrDefaultAsync<int?>("SELECT v.warranty_months FROM product_variants v JOIN products p USING(product_id) WHERE v.variant_id=@VariantId AND v.is_active AND p.is_active",
next,
t) ?? throw new ValidationException("The product is inactive or unavailable.");
            var start = request.ReplacementWarrantyRule == "CONTINUE_ORIGINAL" ? w.Start : w.Today;
            var end = request.ReplacementWarrantyRule switch
            {
                "CONTINUE_ORIGINAL" => w.End,
                "NEW_FULL_WARRANTY" when months > 0 => w.Today.AddMonths(months),
                "CUSTOM" => request.CustomWarrantyEndDate!.Value.Date,
                _ => throw new ValidationException("The product has no full warranty term configured.")
            };
            if (end < start)
            {
                throw new ValidationException("The end date cannot precede the replacement date.");
            }

            await c.ExecuteAsync("UPDATE product_units SET unit_status='REPLACED',updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@UnitId; UPDATE product_units SET unit_status='REPLACEMENT_ISSUED',sale_id=@SaleId,updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@next; UPDATE warranties SET warranty_status='REPLACED',replacement_product_unit_id=@next,updated_at=CURRENT_TIMESTAMP WHERE warranty_id=@Id",
new
{
    w.UnitId,
    w.SaleId,
    w.Id,
    next = request.ReplacementProductUnitId
},
t);
            child = await c.QuerySingleAsync<long>("""
                INSERT INTO warranties(sale_item_id,product_unit_id,warranty_start_date,warranty_end_date,warranty_status,warranty_type,warranty_provider,parent_warranty_id,replacement_warranty_rule)
                SELECT sale_item_id,@next,@start,@end,CASE WHEN @end<CURRENT_DATE THEN 'EXPIRED' ELSE 'ACTIVE' END,warranty_type,warranty_provider,warranty_id,@rule FROM warranties WHERE warranty_id=@Id RETURNING warranty_id
                """,
new
{
    next = request.ReplacementProductUnitId,
    start,
    end,
    w.Id,
    rule = request.ReplacementWarrantyRule
},
t);
            await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@next,'WARRANTY_REPLACEMENT',1,'WARRANTY_CLAIM',@claimId,@notes,@userId)",
new
{
    w.ProductId,
    next = request.ReplacementProductUnitId,
    claimId,
    notes = request.ResolutionNotes,
    userId
},
t);
        }
        else
        {
            await c.ExecuteAsync("UPDATE warranties SET warranty_status=CASE WHEN @closed THEN CASE WHEN warranty_end_date<CURRENT_DATE THEN 'EXPIRED' ELSE 'ACTIVE' END ELSE 'CLAIMED' END,updated_at=CURRENT_TIMESTAMP WHERE warranty_id=@Id",
new
{
    w.Id,
    closed = request.ClaimStatus is "CLOSED" or "REJECTED"
},
t);
        }

        await c.ExecuteAsync("UPDATE warranty_claims SET claim_status=@ClaimStatus,resolution_type=@ResolutionType,resolution_notes=@ResolutionNotes,replacement_product_unit_id=@ReplacementProductUnitId,replacement_warranty_id=@child,updated_at=CURRENT_TIMESTAMP WHERE warranty_claim_id=@claimId",
new
{
    request.ClaimStatus,
    request.ResolutionType,
    request.ResolutionNotes,
    request.ReplacementProductUnitId,
    child,
    claimId
},
t);
        await Event(c,
t,
w.Id,
claimId,
"UPDATE_CLAIM",
status,
request.ClaimStatus,
request.ResolutionNotes,
request.RequestId,
payload,
userId);
        if (child.HasValue)
        {
            await Event(c,
t,
child.Value,
null,
"REGISTER",
null,
"REGISTERED",
$"Replacement for warranty {w.Id}; rule {request.ReplacementWarrantyRule}.",
null,
null,
userId);
        }

        await t.CommitAsync();
        return true;
    }

    public async Task<long> RegisterAsync(long saleItemId, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var sale = await c.QuerySingleOrDefaultAsync<long?>("SELECT sale_id FROM sale_items WHERE sale_item_id=@saleItemId", new
        {
            saleItemId
        }, t) ?? throw new ValidationException("Sale item not found.");
        if (await c.QuerySingleAsync<string>("SELECT status FROM sales WHERE sale_id=@sale FOR SHARE", new
        {
            sale
        }, t) != "COMPLETED")
        {
            throw new ValidationException("Only completed sales qualify.");
        }

        await c.ExecuteAsync("SELECT sale_item_id FROM sale_items WHERE sale_item_id=@saleItemId FOR UPDATE", new
        {
            saleItemId
        }, t);
        var existing = await c.QuerySingleOrDefaultAsync<long?>("SELECT warranty_id FROM warranties WHERE sale_item_id=@saleItemId AND parent_warranty_id IS NULL",
new
{
    saleItemId
},
t);
        if (existing.HasValue)
        {
            return existing.Value;
        }

        var id = await c.QuerySingleOrDefaultAsync<long?>("""
            INSERT INTO warranties(sale_item_id,product_unit_id,warranty_start_date,warranty_end_date,warranty_status)
            SELECT i.sale_item_id,i.product_unit_id,s.sale_date::date,(s.sale_date+make_interval(months=>COALESCE(i.warranty_months_snapshot,p.warranty_months)))::date,
            CASE WHEN (s.sale_date+make_interval(months=>COALESCE(i.warranty_months_snapshot,p.warranty_months)))::date<CURRENT_DATE THEN 'EXPIRED' ELSE 'ACTIVE' END
            FROM sale_items i JOIN sales s ON s.sale_id=i.sale_id JOIN products p ON p.product_id=i.product_id
            WHERE i.sale_item_id=@saleItemId AND COALESCE(i.warranty_months_snapshot,p.warranty_months)>0 RETURNING warranty_id
            """,
new
{
    saleItemId
},
t) ?? throw new ValidationException("This sold item has no warranty term.");
        await Event(c,
t,
id,
null,
"REGISTER",
null,
"REGISTERED",
"Registered from the original sale date and term.",
null,
null,
userId);
        await t.CommitAsync();
        return id;
    }

    sealed class Retry
    {
        public long Id
        {
            get; set;
        }
        public bool Matches
        {
            get; set;
        }
    }

    sealed class Locked
    {
        public long Id
        {
            get; set;
        }
        public long ItemId
        {
            get; set;
        }
        public long SaleId
        {
            get; set;
        }
        public int ProductId
        {
            get; set;
        }
        public long? UnitId
        {
            get; set;
        }
        public string Status { get; set; } = "";
        public DateTime Start
        {
            get; set;
        }
        public DateTime End
        {
            get; set;
        }
        public DateTime Today
        {
            get; set;
        }
    }

    sealed class Unit
    {
        public long VariantId
        {
            get; set;
        }
        public long Id
        {
            get; set;
        }
        public int ProductId
        {
            get; set;
        }
        public string Status { get; set; } = "";
        public long? SaleId
        {
            get; set;
        }
    }
}
