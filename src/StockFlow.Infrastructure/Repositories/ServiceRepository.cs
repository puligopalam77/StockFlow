using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Service;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly DbConnectionFactory factory;
    private readonly IWarrantyRepository warranties;
    // ASP.NET supplies these services when it creates this class.
    public ServiceRepository(DbConnectionFactory factory, IWarrantyRepository warranties)
    {
        this.factory = factory;
        this.warranties = warranties;
    }

    private const string Read = """
        SELECT sr.service_request_id AS ServiceRequestId,sr.service_number AS ServiceNumber,
            sr.customer_id AS CustomerId,coalesce(c.customer_name,'Customer #'||sr.customer_id||' (ownership review pending)') AS CustomerName,c.mobile_number AS CustomerMobile,
            sr.product_id AS ProductId,sr.variant_id AS VariantId,coalesce(p.product_name || ' / ' || v.variant_name,'Product #'||sr.product_id||' (ownership review pending)') AS ProductName,sr.product_unit_id AS ProductUnitId,
            pu.serial_number AS SerialNumber,sr.warranty_id AS WarrantyId,sr.complaint AS Complaint,
            sr.diagnosis AS Diagnosis,sr.repair_details AS RepairDetails,sr.service_charge AS ServiceCharge,
            sr.status AS Status,sr.received_date AS ReceivedDate,sr.expected_delivery_date AS ExpectedDeliveryDate,
            sr.delivered_date AS DeliveredDate,sr.assigned_to AS AssignedTo,u.full_name AS AssignedToName,
            sr.notes AS Notes,sr.version AS Version,sr.created_by AS CreatedBy,sr.created_at AS CreatedAt,
            sr.updated_at AS UpdatedAt,sr.under_warranty_at_receipt AS UnderWarrantyAtReceipt,
            COALESCE((SELECT SUM(total_amount) FROM service_parts WHERE service_request_id=sr.service_request_id),0) AS PartsTotal,
            COALESCE(ws.status='COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED')
                AND CURRENT_DATE BETWEEN w.warranty_start_date AND w.warranty_end_date,FALSE) AS IsUnderWarranty,
            CASE WHEN ws.status='CANCELLED' THEN 'CANCELLED'
                 WHEN w.warranty_status IN ('ACTIVE','CLAIMED') AND w.warranty_end_date<CURRENT_DATE THEN 'EXPIRED'
                 ELSE w.warranty_status END AS WarrantyStatus
        FROM service_requests sr LEFT JOIN customers c ON c.customer_id=sr.customer_id
        LEFT JOIN products p ON p.product_id=sr.product_id LEFT JOIN product_variants v ON v.variant_id=sr.variant_id
        LEFT JOIN product_units pu ON pu.product_unit_id=sr.product_unit_id
        LEFT JOIN users u ON u.user_id=sr.assigned_to
        LEFT JOIN warranties w ON w.warranty_id=sr.warranty_id
        LEFT JOIN sale_items wi ON wi.sale_item_id=w.sale_item_id
        LEFT JOIN sales ws ON ws.sale_id=wi.sale_id
        """;
    public async Task<ServiceRequestResponse?> GetByIdAsync(long id)
    {
        await using var c = factory.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<ServiceRequestResponse>(Read + " WHERE sr.service_request_id=@id", new
        {
            id
        });
    }

    public async Task<IEnumerable<ServiceRequestResponse>> GetAllAsync(ServiceSearch search)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<ServiceRequestResponse>(Read.Insert(7,"COUNT(*) OVER() AS TotalCount,") + """

            WHERE (@Status::text IS NULL OR sr.status=@Status)
              AND (NOT @PendingOnly OR sr.status NOT IN ('DELIVERED','CANCELLED'))
              AND (@Term='' OR sr.service_number ILIKE @Pattern OR c.customer_name ILIKE @Pattern
                   OR c.mobile_number ILIKE @Pattern OR pu.serial_number ILIKE @Pattern OR p.product_name ILIKE @Pattern)
              AND (@From::date IS NULL OR sr.received_date>=@From::date)
              AND (@To::date IS NULL OR sr.received_date<@To::date+INTERVAL '1 day')
            ORDER BY sr.received_date DESC,sr.service_request_id DESC LIMIT 100 OFFSET @Offset
            """,
new
{
    search.Status,
    search.PendingOnly,
    Term = search.Term ?? "",
    Pattern = "%" + search.Term + "%",
    search.From,
    search.To,
    search.Offset
});
    }

    public async Task<IEnumerable<ServiceTechnician>> GetTechniciansAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<ServiceTechnician>("""
            SELECT u.user_id AS UserId,u.full_name AS FullName FROM users u JOIN roles r ON r.role_id=u.role_id
            WHERE u.is_active AND r.is_active AND r.role_name IN ('Admin','Service') ORDER BY u.full_name,u.user_id
            """);
    }

    public async Task<ServiceUnitContext?> GetUnitContextAsync(long unitId)
    {
        await using var c = factory.CreateConnection();
        var result = await c.QuerySingleOrDefaultAsync<ServiceUnitContext>("""
            SELECT pu.product_unit_id AS ProductUnitId,pu.product_id AS ProductId,pu.variant_id AS VariantId,p.product_name || ' / ' || v.variant_name AS ProductName,
                pu.serial_number AS SerialNumber,pu.unit_status AS UnitStatus,pu.sale_id AS SaleId,
                s.status AS SaleStatus,s.invoice_number AS InvoiceNumber,s.customer_id AS CustomerId,
                c.customer_name AS CustomerName,c.mobile_number AS CustomerMobile
            FROM product_units pu JOIN products p ON p.product_id=pu.product_id JOIN product_variants v ON v.variant_id=pu.variant_id
            LEFT JOIN sales s ON s.sale_id=pu.sale_id LEFT JOIN customers c ON c.customer_id=s.customer_id
            WHERE pu.product_unit_id=@unitId
            """,
new
{
    unitId
});
        if (result is not null)
        {
            result.Warranty = await warranties.GetByUnitAsync(unitId);
        }

        return result;
    }

    public async Task<IEnumerable<ServiceHistory>> GetHistoryAsync(long id, int offset)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<ServiceHistory>("""
            SELECT sh.service_history_id AS ServiceHistoryId,sh.service_request_id AS ServiceRequestId,
                sh.old_status AS OldStatus,sh.new_status AS NewStatus,sh.action_name AS ActionName,
                sh.remarks AS Remarks,sh.details::text AS Details,sh.changed_by AS ChangedBy,
                u.full_name AS ChangedByName,sh.changed_at AS ChangedAt
            FROM service_history sh JOIN users u ON u.user_id=sh.changed_by
            WHERE sh.service_request_id=@id ORDER BY sh.service_history_id DESC LIMIT 100 OFFSET @offset
            """,
new
{
    id,
    offset
});
    }

    public async Task<IEnumerable<ServicePart>> GetPartsAsync(long id, int offset)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<ServicePart>("""
            SELECT sp.service_part_id AS ServicePartId,sp.service_request_id AS ServiceRequestId,
                sp.product_id AS ProductId,sp.variant_id AS VariantId,sp.product_name_snapshot AS ProductName,sp.product_unit_id AS ProductUnitId,
                pu.serial_number AS SerialNumber,sp.quantity AS Quantity,sp.unit_price AS UnitPrice,
                sp.total_amount AS TotalAmount,sp.created_by AS CreatedBy,sp.created_at AS CreatedAt
            FROM service_parts sp LEFT JOIN product_units pu ON pu.product_unit_id=sp.product_unit_id
            WHERE sp.service_request_id=@id ORDER BY sp.service_part_id DESC LIMIT 100 OFFSET @offset
            """,
new
{
    id,
    offset
});
    }

    // Lock a retry ID before checking the receipt, so concurrent retries cannot issue stock twice.
    private static async Task<long?> Replay(NpgsqlConnection c, NpgsqlTransaction t, Guid requestId, string payload, int userId)
    {
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", new
        {
            key = "StockFlow.Service:" + requestId
        }, t);
        var receipt = await c.QuerySingleOrDefaultAsync<Receipt>("""
            SELECT result_id AS Id,(payload=@payload::jsonb AND created_by=@userId) AS Matches
            FROM service_operations WHERE request_id=@requestId
            """,
new
{
    requestId,
    payload,
    userId
},
t);
        if (receipt is null)
        {
            return null;
        }

        if (!receipt.Matches)
        {
            throw new ValidationException("This request ID was already used for different service details.");
        }

        return receipt.Id;
    }

    private static Task ReceiptAsync(NpgsqlConnection c,
NpgsqlTransaction t,
Guid requestId,
long id,
string operation,
string payload,
long result,
int userId)
    {
        return c.ExecuteAsync("""
            INSERT INTO service_operations(request_id,service_request_id,operation,payload,result_id,created_by)
            VALUES(@requestId,@id,@operation,@payload::jsonb,@result,@userId)
            """,
new
{
    requestId,
    id,
    operation,
    payload,
    result,
    userId
},
t);
    }

    private static Task History(NpgsqlConnection c,
NpgsqlTransaction t,
long id,
string? before,
string after,
string action,
string? remarks,
int userId,
string? details = null)
    {
        return c.ExecuteAsync("""
            INSERT INTO service_history(service_request_id,old_status,new_status,action_name,remarks,changed_by,details)
            VALUES(@id,@before,@after,@action,@remarks,@userId,@details::jsonb)
            """,
new
{
    id,
    before,
    after,
    action,
    remarks,
    userId,
    details
},
t);
    }

    private static async Task Assign(NpgsqlConnection c, NpgsqlTransaction t, int? id)
    {
        if (id is null)
        {
            return;
        }

        var technician = await c.QuerySingleOrDefaultAsync<int?>("""
            SELECT u.user_id FROM users u JOIN roles r ON r.role_id=u.role_id
            WHERE u.user_id=@id AND u.is_active AND r.is_active AND r.role_name IN ('Admin','Service')
            FOR SHARE OF u,r
            """,
new
{
    id
},
t);
        if (technician is null)
        {
            throw new ValidationException("Choose an active Admin or Service technician.");
        }
    }

    private static async Task<Ticket> Lock(NpgsqlConnection c, NpgsqlTransaction t, long id, long version)
    {
        var ticket = await c.QuerySingleOrDefaultAsync<Ticket>("""
            SELECT service_request_id AS Id,status AS Status,product_unit_id AS UnitId,version AS Version,
                received_date::date AS ReceivedDate,unit_status_before_service AS PreviousUnitStatus
            FROM service_requests WHERE service_request_id=@id FOR UPDATE
            """,
new
{
    id
},
t) ?? throw new KeyNotFoundException("Service request not found.");
        if (ticket.Version != version)
        {
            throw new ValidationException("This ticket changed. Reload it before saving.");
        }

        if (ServiceRules.IsTerminal(ticket.Status))
        {
            throw new ValidationException("Delivered or cancelled tickets cannot be changed.");
        }

        return ticket;
    }

    private static void ExpectedDate(DateTime? expected, DateTime received)
    {
        if (expected?.Date < received.Date)
        {
            throw new ValidationException("Expected delivery cannot precede the received date.");
        }
    }

    public async Task<long> CreateAsync(CreateServiceRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        ServiceRules.Validate(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "CREATE",
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        // Follow the sale -> product -> unit lock order used by warranty replacements.
        long? saleId = null;
        if (request.ProductUnitId.HasValue)
        {
            saleId = await c.QuerySingleOrDefaultAsync<long?>("SELECT sale_id FROM product_units WHERE product_unit_id=@ProductUnitId", request, t);
        }
        else if (request.WarrantyId.HasValue)
        {
            saleId = await c.QuerySingleOrDefaultAsync<long?>("SELECT si.sale_id FROM warranties w JOIN sale_items si ON si.sale_item_id=w.sale_item_id WHERE w.warranty_id=@WarrantyId",
request,
t);
        }

        Sale? sale = null;
        if (saleId.HasValue)
        {
            sale = await c.QuerySingleOrDefaultAsync<Sale>("SELECT sale_id AS Id,customer_id AS CustomerId,status AS Status FROM sales WHERE sale_id=@saleId FOR SHARE",
new
{
    saleId
},
t);
            if (sale is null || sale.Status != "COMPLETED")
            {
                throw new ValidationException("The unit must belong to a completed sale.");
            }

            if (sale.CustomerId.HasValue && sale.CustomerId != request.CustomerId)
            {
                throw new ValidationException("The selected customer does not match the original sale.");
            }
        }

        var product = await c.QuerySingleOrDefaultAsync<Product>("SELECT product_id AS Id,product_name AS Name,is_serial_tracked AS Tracked FROM products WHERE product_id=@ProductId FOR UPDATE",
request,
t) ?? throw new ValidationException("Product not found.");
        var variant = await VariantRepository.ResolveAsync(c, t, request.ProductId, request.VariantId, request.ProductUnitId, activeOnly: false);
        product.Tracked = variant.IsSerialTracked;
        if (product.Tracked != request.ProductUnitId.HasValue)
        {
            throw new ValidationException("Scan a sold unit for a serialized product; quantity-only products must not include a unit.");
        }

        string previousUnitStatus = "SOLD";
        if (request.ProductUnitId.HasValue)
        {
            var unit = await c.QuerySingleOrDefaultAsync<Unit>("SELECT product_id AS ProductId,unit_status AS Status,sale_id AS SaleId FROM product_units WHERE product_unit_id=@ProductUnitId FOR UPDATE",
request,
t);
            if (unit is null || unit.ProductId != request.ProductId || sale is null || unit.SaleId != sale.Id || unit.Status is not ("SOLD" or "REPLACEMENT_ISSUED"))
            {
                throw new ValidationException("Choose this customer's sold unit. Units already in service, in stock or replaced cannot be received.");
            }

            previousUnitStatus = unit.Status;
        }

        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM customers WHERE customer_id=@CustomerId AND is_active)", request, t))
        {
            throw new ValidationException("Choose an active customer.");
        }

        await Assign(c, t, request.AssignedTo);
        var today = await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE", transaction: t);
        ExpectedDate(request.ExpectedDeliveryDate, today);
        var warrantyId = request.WarrantyId;
        if (request.ProductUnitId.HasValue)
        {
            var actual = await c.QuerySingleOrDefaultAsync<long?>("SELECT warranty_id FROM warranties WHERE product_unit_id=@ProductUnitId ORDER BY warranty_id DESC LIMIT 1",
request,
t);
            if (warrantyId.HasValue && warrantyId != actual)
            {
                throw new ValidationException("The warranty does not match the selected unit.");
            }

            warrantyId = actual;
        }

        bool underWarranty = false;
        if (warrantyId.HasValue)
        {
            var warranty = await c.QuerySingleOrDefaultAsync<Coverage>("""
                SELECT si.product_id AS ProductId,w.product_unit_id AS UnitId,si.sale_id AS SaleId,
                    (w.warranty_status IN ('ACTIVE','CLAIMED') AND CURRENT_DATE BETWEEN w.warranty_start_date AND w.warranty_end_date) AS Covered
                FROM warranties w JOIN sale_items si ON si.sale_item_id=w.sale_item_id WHERE w.warranty_id=@warrantyId FOR SHARE OF w
                """,
new
{
    warrantyId
},
t);
            if (warranty is null || warranty.ProductId != request.ProductId || warranty.UnitId != request.ProductUnitId || sale?.Id != warranty.SaleId)
            {
                throw new ValidationException("The warranty does not belong to this sale and product.");
            }

            underWarranty = warranty.Covered;
        }

        var next = await c.ExecuteScalarAsync<long>("SELECT nextval('service_request_number_seq')", transaction: t);
        var number = $"SRV-{today.Year}-{next:D6}";
        var id = await c.QuerySingleAsync<long>("""
            INSERT INTO service_requests(service_number,customer_id,product_id,variant_id,product_unit_id,warranty_id,complaint,
                assigned_to,expected_delivery_date,notes,created_by,unit_status_before_service,under_warranty_at_receipt)
            VALUES(@number,@CustomerId,@ProductId,@VariantId,@ProductUnitId,@warrantyId,@Complaint,
                @AssignedTo,@ExpectedDeliveryDate,@Notes,@userId,@previousUnitStatus,@underWarranty) RETURNING service_request_id
            """,
new
{
    number,
    request.CustomerId,
    request.ProductId,
    variant.VariantId,
    request.ProductUnitId,
    warrantyId,
    request.Complaint,
    request.AssignedTo,
    request.ExpectedDeliveryDate,
    request.Notes,
    userId,
    previousUnitStatus,
    underWarranty
},
t);
        if (request.ProductUnitId.HasValue)
        {
            await c.ExecuteAsync("UPDATE product_units SET unit_status='IN_SERVICE',updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@ProductUnitId",
request,
t);
        }

        await History(c, t, id, null, "RECEIVED", "CREATE", "Service request received.", userId, payload);
        await ReceiptAsync(c, t, request.RequestId, id, "CREATE", payload, id, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task UpdateAsync(long id, UpdateServiceRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        ServiceRules.Validate(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "UPDATE",
            id,
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is not null)
        {
            return;
        }

        var ticket = await Lock(c, t, id, request.ExpectedVersion);
        ExpectedDate(request.ExpectedDeliveryDate, ticket.ReceivedDate);
        await Assign(c, t, request.AssignedTo);
        await c.ExecuteAsync("""
            UPDATE service_requests SET diagnosis=@Diagnosis,repair_details=@RepairDetails,service_charge=@ServiceCharge,
                assigned_to=@AssignedTo,expected_delivery_date=@ExpectedDeliveryDate,notes=@Notes,
                version=version+1,updated_at=CURRENT_TIMESTAMP WHERE service_request_id=@id
            """,
new
{
    id,
    request.Diagnosis,
    request.RepairDetails,
    request.ServiceCharge,
    request.AssignedTo,
    request.ExpectedDeliveryDate,
    request.Notes
},
t);
        await History(c,
t,
id,
ticket.Status,
ticket.Status,
"UPDATE",
"Diagnosis, repair, charge, assignment or delivery details saved.",
userId,
JsonSerializer.Serialize(request));
        await ReceiptAsync(c, t, request.RequestId, id, "UPDATE", payload, id, userId);
        await t.CommitAsync();
    }

    public async Task UpdateStatusAsync(long id, UpdateServiceStatusRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        ServiceRules.Validate(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "STATUS",
            id,
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is not null)
        {
            return;
        }

        var ticket = await Lock(c, t, id, request.ExpectedVersion);
        ServiceRules.Transition(ticket.Status, request.Status);
        if (request.Status == "CANCELLED" && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM service_parts WHERE service_request_id=@id)", new
        {
            id
        }, t))
        {
            throw new ValidationException("Parts have already been used. Complete and deliver this repair instead of cancelling it.");
        }

        if (ServiceRules.IsTerminal(request.Status) && ticket.UnitId.HasValue)
        {
            var status = await c.QuerySingleAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=@UnitId FOR UPDATE", ticket, t);
            if (status != "IN_SERVICE")
            {
                throw new ValidationException("The unit's status changed outside this ticket; reconcile it before returning the unit.");
            }

            await c.ExecuteAsync("UPDATE product_units SET unit_status=@PreviousUnitStatus,updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@UnitId",
ticket,
t);
        }

        await c.ExecuteAsync("""
            UPDATE service_requests SET status=@Status,
                delivered_date=CASE WHEN @Status='DELIVERED' THEN CURRENT_TIMESTAMP ELSE delivered_date END,
                version=version+1,updated_at=CURRENT_TIMESTAMP WHERE service_request_id=@id
            """,
new
{
    id,
    request.Status
},
t);
        await History(c, t, id, ticket.Status, request.Status, "STATUS", request.Remarks, userId);
        await ReceiptAsync(c, t, request.RequestId, id, "STATUS", payload, id, userId);
        await t.CommitAsync();
    }

    public async Task<long> AddPartAsync(long id, AddServicePartRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        ServiceRules.Validate(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var payload = JsonSerializer.Serialize(new
        {
            Operation = "PART",
            id,
            request
        });
        if (await Replay(c, t, request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var ticket = await Lock(c, t, id, request.ExpectedVersion);
        if (ticket.Status is not ("INSPECTION" or "REPAIRING" or "WAITING_FOR_PART"))
        {
            throw new ValidationException("Parts can be used during inspection, repairing or waiting for parts.");
        }

        // The same product lock is used by sales, adjustments and warranty replacements.
        var product = await c.QuerySingleOrDefaultAsync<Product>("""
            SELECT product_id AS Id,product_name AS Name,is_serial_tracked AS Tracked
            FROM products WHERE product_id=@ProductId AND is_active FOR UPDATE
            """,
request,
t) ?? throw new ValidationException("Choose an active inventory part.");
        var variant = await VariantRepository.ResolveAsync(c, t, request.ProductId, request.VariantId, request.ProductUnitId);
        product.Tracked = variant.IsSerialTracked;
        if (product.Tracked != request.ProductUnitId.HasValue)
        {
            throw new ValidationException("Select an available serial for a tracked part; quantity-only parts do not take a serial.");
        }

        if (await c.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE product_id=@ProductId AND
                (quantity<=0 OR transaction_type NOT IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN',
                    'SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS')))
            """,
request,
t))
        {
            throw new ValidationException("Reconcile invalid inventory movements before issuing this part.");
        }

        var stock = await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@VariantId", new
        {
            variant.VariantId
        }, t);
        if (stock < request.Quantity)
        {
            throw new ValidationException("Insufficient spare-part stock.");
        }

        if (product.Tracked)
        {
            var count = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_units WHERE variant_id=@VariantId AND unit_status='IN_STOCK'",
new
{
    variant.VariantId
},
t);
            if (count != stock)
            {
                throw new ValidationException("Serialized stock needs reconciliation.");
            }

            var unit = await c.QuerySingleOrDefaultAsync<Unit>("SELECT product_id AS ProductId,unit_status AS Status,sale_id AS SaleId FROM product_units WHERE product_unit_id=@ProductUnitId FOR UPDATE",
request,
t);
            if (unit is null || unit.ProductId != request.ProductId || unit.Status != "IN_STOCK" || unit.SaleId.HasValue)
            {
                throw new ValidationException("Choose an available unit of the selected part.");
            }

            await c.ExecuteAsync("UPDATE product_units SET unit_status='SERVICE_USED',updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@ProductUnitId",
request,
t);
        }

        var total = request.Quantity * request.UnitPrice;
        var partId = await c.QuerySingleAsync<long>("""
            INSERT INTO service_parts(service_request_id,product_id,variant_id,product_unit_id,product_name_snapshot,quantity,unit_price,total_amount,created_by)
            VALUES(@id,@ProductId,@VariantId,@ProductUnitId,@name,@Quantity,@UnitPrice,@total,@userId) RETURNING service_part_id
            """,
new
{
    id,
    request.ProductId,
    variant.VariantId,
    request.ProductUnitId,
    name = product.Name + " / " + variant.VariantName,
    request.Quantity,
    request.UnitPrice,
    total,
    userId
},
t);
        await c.ExecuteAsync("""
            INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by)
            VALUES(@ProductId,@VariantId,@ProductUnitId,'SERVICE_OUT',@Quantity,'SERVICE_PART',@partId,@remarks,@userId);
            UPDATE service_requests SET version=version+1,updated_at=CURRENT_TIMESTAMP WHERE service_request_id=@id;
            """,
new
{
    id,
    request.ProductId,
    variant.VariantId,
    request.ProductUnitId,
    request.Quantity,
    partId,
    remarks = $"Part for service request {id}",
    userId
},
t);
        await History(c,
t,
id,
ticket.Status,
ticket.Status,
"PART",
$"{product.Name}: {request.Quantity} used; total {total:0.00}.",
userId,
JsonSerializer.Serialize(request));
        await ReceiptAsync(c, t, request.RequestId, id, "PART", payload, partId, userId);
        await t.CommitAsync();
        return partId;
    }

    private sealed class Receipt
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

    private sealed class Product
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = "";
        public bool Tracked
        {
            get; set;
        }
    }

    private sealed class Unit
    {
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

    private sealed class Sale
    {
        public long Id
        {
            get; set;
        }
        public int? CustomerId
        {
            get; set;
        }
        public string Status { get; set; } = "";
    }

    private sealed class Coverage
    {
        public int ProductId
        {
            get; set;
        }
        public long? UnitId
        {
            get; set;
        }
        public long SaleId
        {
            get; set;
        }
        public bool Covered
        {
            get; set;
        }
    }

    private sealed class Ticket
    {
        public long Id
        {
            get; set;
        }
        public string Status { get; set; } = "";
        public long? UnitId
        {
            get; set;
        }
        public long Version
        {
            get; set;
        }
        public DateTime ReceivedDate
        {
            get; set;
        }
        public string PreviousUnitStatus { get; set; } = "SOLD";
    }
}
