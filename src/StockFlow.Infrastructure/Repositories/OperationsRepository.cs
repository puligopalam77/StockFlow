using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class OperationsRepository : IOperationsRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public OperationsRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private static void Base(OperationRequest r)
    {
        if (r.RequestId == Guid.Empty)
        {
            throw new ValidationException("A request ID is required.");
        }
    }

    private static string Required(string? value, string name, int max)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
        {
            throw new ValidationException($"Enter {name} (maximum {max} characters).");
        }

        return value;
    }

    private static string Optional(string? value, string name, int max)
    {
        value = value?.Trim();
        if (value?.Length > max)
        {
            throw new ValidationException($"{name} cannot exceed {max} characters.");
        }

        return value ?? "";
    }

    private static async Task<long?> Replay(NpgsqlConnection c,
NpgsqlTransaction t,
string table,
string idColumn,
Guid requestId,
string payload,
int userId)
    {
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", new
        {
            key = "StockFlow.Operations:" + requestId
        }, t);
        var row = await c.QuerySingleOrDefaultAsync<ReplayRow>($"SELECT {idColumn} Id,(request_payload=@payload::jsonb AND created_by=@userId) Matches FROM {table} WHERE request_id=@requestId",
new
{
    requestId,
    payload,
    userId
},
t);
        if (row is null)
        {
            return null;
        }

        if (!row.Matches)
        {
            throw new ValidationException("This request ID was already used for different details.");
        }

        return row.Id;
    }

    public async Task<DocumentOperations> SaleAsync(long saleId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var snapshot = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var result = await c.QuerySingleOrDefaultAsync<DocumentOperations>("""
            SELECT s.total_amount DocumentTotal,
              coalesce((SELECT sum(return_total) FROM sale_returns WHERE sale_id=s.sale_id),0) ReturnedTotal,
              coalesce((SELECT sum(amount) FROM payments WHERE sale_id=s.sale_id AND payment_mode<>'CREDIT'),0) PaidTotal,
              coalesce((SELECT sum(refund_amount) FROM sale_returns WHERE sale_id=s.sale_id),0) RefundedTotal
            FROM sales s WHERE s.sale_id=@saleId
            """,
new
{
    saleId
},snapshot) ?? throw new KeyNotFoundException("Sale not found.");
        result.Payments = (await c.QueryAsync<OperationRow>("SELECT payment_id Id,payment_date AT TIME ZONE current_setting('TimeZone') Date,'PAYMENT' Type,payment_mode Status,CASE WHEN payment_mode='CARD' THEN coalesce(request_payload->>'cardType',request_payload->>'CardType') END CardType,amount,reference_number Reference,NULL Notes FROM payments WHERE sale_id=@saleId ORDER BY payment_id",
new
{
    saleId
},snapshot)).ToList();
        result.Returns = (await c.QueryAsync<OperationRow>("SELECT sale_return_id Id,return_date Date,'RETURN' Type,refund_mode Status,return_total Amount,refund_amount RefundedAmount,return_number Reference,notes FROM sale_returns WHERE sale_id=@saleId ORDER BY sale_return_id",
new
{
    saleId
},snapshot)).ToList();
        result.Fulfilments = (await c.QueryAsync<OperationRow>("SELECT fulfilment_id Id,scheduled_at Date,fulfilment_type Type,status Status,0 Amount,technician_name Reference,address||coalesce(E'\n'||notes,'') Notes FROM sale_fulfilments WHERE sale_id=@saleId ORDER BY fulfilment_id",
new
{
    saleId
},snapshot)).ToList();
        result.TradeIns = (await c.QueryAsync<OperationRow>("SELECT trade_in_id Id,created_at Date,item_type Type,status Status,allowance_amount Amount,coalesce(serial_number,brand_model) Reference,notes FROM trade_ins WHERE sale_id=@saleId ORDER BY trade_in_id",
new
{
    saleId
},snapshot)).ToList();
        result.ReturnedLines = (await c.QueryAsync<SaleReturnHistoryLine>("""
            SELECT i.sale_return_id ReturnId,coalesce(s.product_name_snapshot,p.product_name) ProductName,
              to_jsonb(s)->>'sku_snapshot' Sku,i.quantity Quantity,i.disposition Disposition
            FROM sale_return_items i JOIN sale_returns r ON r.sale_return_id=i.sale_return_id
            JOIN sale_items s ON s.sale_item_id=i.sale_item_id LEFT JOIN products p ON p.product_id=s.product_id
            WHERE r.sale_id=@saleId ORDER BY i.sale_return_id,i.sale_item_id
            """,new {saleId},snapshot)).ToList();
        await snapshot.CommitAsync();
        return result;
    }

    public async Task<long> AddCustomerPaymentAsync(long saleId, MoneyReceiptRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.PaymentMode = Required(request.PaymentMode, "payment mode", 30).ToUpperInvariant();
        request.CardType = string.IsNullOrWhiteSpace(request.CardType) ? null : request.CardType.Trim().ToUpperInvariant();
        if (request.CardType is not null && (request.PaymentMode != "CARD" || request.CardType is not ("DEBIT" or "CREDIT")))
            throw new ValidationException("Choose a valid debit or credit card subtype for a Card collection.");
        if (request.PaymentMode is not ("CASH" or "CARD" or "UPI" or "BANK" or "CHEQUE"))
        {
            throw new ValidationException("Choose cash, card, UPI, bank or cheque.");
        }

        if (request.Amount <= 0 || request.Amount > 9999999999.99m || decimal.Round(request.Amount,2) != request.Amount)
        {
            throw new ValidationException("Enter a positive payment amount within the supported limit and with at most two decimal places.");
        }

        request.ReferenceNumber = Optional(request.ReferenceNumber, "Reference number", 100);
        request.Notes = Optional(request.Notes, "Notes", 1000);
        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "payments", "payment_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var s = await c.QuerySingleOrDefaultAsync<MoneyHeader>("SELECT total_amount Total,status Status FROM sales WHERE sale_id=@saleId FOR UPDATE", new
        {
            saleId
        }, t) ?? throw new KeyNotFoundException("Sale not found.");
        if (s.Status != "COMPLETED")
        {
            throw new ValidationException("Payments can be added only to a completed invoice.");
        }

        var due = await c.ExecuteScalarAsync<decimal>("""
          SELECT greatest(s.total_amount-coalesce((SELECT sum(return_total) FROM sale_returns WHERE sale_id=s.sale_id),0)
           -coalesce((SELECT sum(amount) FROM payments WHERE sale_id=s.sale_id AND payment_mode<>'CREDIT'),0)
           +coalesce((SELECT sum(refund_amount) FROM sale_returns WHERE sale_id=s.sale_id),0),0)
          FROM sales s WHERE sale_id=@saleId
          """,
new
{
    saleId
},
t);
        if (request.Amount > due)
        {
            throw new ValidationException($"Payment exceeds the outstanding amount {due:0.00}.");
        }

        var id = await c.ExecuteScalarAsync<long>("""
          INSERT INTO payments(sale_id,payment_mode,amount,reference_number,payment_date,created_by,request_id,request_payload)
          VALUES(@saleId,@PaymentMode,@Amount,NULLIF(@ReferenceNumber,''),now(),@userId,@RequestId,@payload::jsonb) RETURNING payment_id
          """,
new
{
    saleId,
    request.PaymentMode,
    request.Amount,
    request.ReferenceNumber,
    userId,
    request.RequestId,
    payload
},
t);
        await Audit(c, t, "Sales", "COLLECT", "payments", id, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<long> ReturnSaleAsync(long saleId, SaleReturnRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.Notes = Required(request.Notes, "return reason", 2000);
        request.RefundMode = Required(request.RefundMode, "refund method", 30).ToUpperInvariant();
        if (request.RefundMode is not ("CASH" or "CARD" or "UPI" or "BANK" or "CREDIT_ADJUSTMENT" or "EXCHANGE" or "NONE"))
        {
            throw new ValidationException("Choose a supported return settlement.");
        }

        if (request.RefundMode is "CREDIT_ADJUSTMENT" or "EXCHANGE" or "NONE" && request.RefundAmount != 0)
        {
            throw new ValidationException("Only a cash, card, UPI or bank settlement can record money refunded now.");
        }

        if (request.Items.Count is < 1 or > 100 || request.Items.GroupBy(x => x.SaleItemId).Any(g => g.Count() > 1))
        {
            throw new ValidationException("Choose one or more distinct invoice lines.");
        }

        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "sale_returns", "sale_return_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var sale = await c.QuerySingleOrDefaultAsync<MoneyHeader>("SELECT total_amount Total,status Status FROM sales WHERE sale_id=@saleId FOR UPDATE", new
        {
            saleId
        }, t) ?? throw new KeyNotFoundException("Sale not found.");
        if (sale.Status != "COMPLETED")
        {
            throw new ValidationException("Only a completed invoice can be returned.");
        }

        decimal total = 0;
        var rows = new List<SaleLine>();
        foreach (var input in request.Items.OrderBy(x => x.SaleItemId))
        {
            input.Disposition = Required(input.Disposition, "stock disposition", 30).ToUpperInvariant();
            if (input.Disposition is not ("RESTOCK" or "DAMAGED" or "SUPPLIER_RETURN" or "SERVICE_HOLD" or "NON_STOCK"))
            {
                throw new ValidationException("Choose a valid stock disposition.");
            }

            var row = await c.QuerySingleOrDefaultAsync<SaleLine>("SELECT sale_item_id Id,product_id ProductId,variant_id VariantId,product_unit_id UnitId,quantity Quantity,line_total Total FROM sale_items WHERE sale_id=@saleId AND sale_item_id=@SaleItemId FOR UPDATE",
new
{
    saleId,
    input.SaleItemId
},
t) ?? throw new ValidationException("An invoice line was not found.");
            if(row.ProductId is not null && input.Disposition=="NON_STOCK") throw new ValidationException("Choose a stock disposition for the registered invoice item.");
            var already = await c.ExecuteScalarAsync<int>("SELECT coalesce(sum(quantity),0) FROM sale_return_items WHERE sale_item_id=@SaleItemId", input, t);
            if (input.Quantity <= 0 || input.Quantity > row.Quantity - already)
            {
                throw new ValidationException("Returned quantity exceeds the remaining invoice quantity.");
            }

            if (row.UnitId.HasValue && input.Quantity != 1)
            {
                throw new ValidationException("A serialized invoice line must be returned as one unit.");
            }

            row.ReturnQuantity = input.Quantity;
            row.Disposition = row.ProductId is null ? "NON_STOCK" : input.Disposition;
            row.ReturnAmount = Math.Round(row.Total * input.Quantity / row.Quantity, 2, MidpointRounding.AwayFromZero);
            total += row.ReturnAmount;
            rows.Add(row);
        }

        if (request.RefundAmount < 0 || request.RefundAmount > total)
        {
            throw new ValidationException("Refund cannot exceed the returned value.");
        }

        var paid = await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(amount),0) FROM payments WHERE sale_id=@saleId AND payment_mode<>'CREDIT'", new
        {
            saleId
        }, t);
        var priorReturns = await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(return_total),0) FROM sale_returns WHERE sale_id=@saleId", new
        {
            saleId
        }, t);
        var priorRefunds = await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(refund_amount),0) FROM sale_returns WHERE sale_id=@saleId", new
        {
            saleId
        }, t);
        var refundable = Math.Max(0, paid - priorRefunds - (sale.Total - priorReturns - total));
        if (request.RefundAmount > refundable)
        {
            throw new ValidationException($"Refund exceeds the paid amount available after this return ({refundable:0.00}). Use credit adjustment or exchange for the balance.");
        }

        var next = await c.ExecuteScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('sale_returns','sale_return_id'))", transaction: t);
        var number = $"SRT-{DateTime.UtcNow.Year}-{next:D6}";
        await c.ExecuteAsync("INSERT INTO sale_returns(sale_return_id,sale_id,return_number,return_total,refund_mode,refund_amount,notes,request_id,request_payload,created_by) VALUES(@next,@saleId,@number,@total,@RefundMode,@RefundAmount,@Notes,@RequestId,@payload::jsonb,@userId)",
new
{
    next,
    saleId,
    number,
    total,
    request.RefundMode,
    request.RefundAmount,
    request.Notes,
    request.RequestId,
    payload,
    userId
},
t);
        foreach (var row in rows)
        {
            if (row.UnitId.HasValue)
            {
                var status = await c.QuerySingleOrDefaultAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=@UnitId AND sale_id=@saleId FOR UPDATE",
new
{
    row.UnitId,
    saleId
},
t);
                if (status is not ("SOLD" or "REPLACEMENT_ISSUED"))
                {
                    throw new ValidationException("The serialized unit is no longer eligible for return.");
                }
            }

            await c.ExecuteAsync("""
              INSERT INTO sale_return_items(sale_return_id,sale_item_id,product_id,variant_id,product_unit_id,quantity,amount,disposition)
              VALUES(@next,@Id,@ProductId,@VariantId,@UnitId,@ReturnQuantity,@ReturnAmount,@Disposition);
              INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by)
              SELECT @ProductId,@VariantId,@UnitId,'CUSTOMER_RETURN',@ReturnQuantity,'SALE_RETURN',@next,@Notes,@userId WHERE @ProductId::integer IS NOT NULL
              """,
new
{
    next,
    row.Id,
    row.ProductId,
    row.VariantId,
    row.UnitId,
    row.ReturnQuantity,
    row.ReturnAmount,
    row.Disposition,
    request.Notes,
    userId
},
t);
            if (row.ProductId.HasValue && row.Disposition != "RESTOCK")
            {
                await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@VariantId,@UnitId,@type,@ReturnQuantity,'SALE_RETURN',@next,@Notes,@userId)",
new
{
    row.ProductId,
    row.VariantId,
    row.UnitId,
    row.ReturnQuantity,
    type = row.Disposition switch
    {
        "DAMAGED" => "DAMAGED",
        "SUPPLIER_RETURN" => "SUPPLIER_RETURN",
        _ => "SERVICE_OUT"
    },
    next,
    request.Notes,
    userId
},
t);
            }

            if (row.UnitId.HasValue)
            {
                await c.ExecuteAsync("UPDATE product_units SET unit_status=@status,sale_id=CASE WHEN @restock THEN NULL ELSE sale_id END,updated_at=now() WHERE product_unit_id=@UnitId",
new
{
    row.UnitId,
    status = row.Disposition switch
    {
        "RESTOCK" => "IN_STOCK",
        "DAMAGED" => "DAMAGED",
        "SUPPLIER_RETURN" => "RETURNED_TO_SUPPLIER",
        _ => "IN_SERVICE"
    },
    restock = row.Disposition == "RESTOCK"
},
t);
            }
        }

        await Audit(c, t, "Sales", "RETURN", "sale_returns", next, payload, userId);
        await t.CommitAsync();
        return next;
    }

    public async Task<DocumentOperations> PurchaseAsync(long purchaseId)
    {
        await using var c = factory.CreateConnection();
        var r = await c.QuerySingleOrDefaultAsync<DocumentOperations>("SELECT p.total_amount DocumentTotal,coalesce((SELECT sum(return_total) FROM purchase_returns WHERE purchase_id=p.purchase_id),0) ReturnedTotal,coalesce((SELECT sum(amount) FROM supplier_payments WHERE purchase_id=p.purchase_id),0) PaidTotal,0 RefundedTotal FROM purchases p WHERE p.purchase_id=@purchaseId",
new
{
    purchaseId
}) ?? throw new KeyNotFoundException("Purchase not found.");
        r.Payments = (await c.QueryAsync<OperationRow>("SELECT supplier_payment_id Id,payment_date Date,'PAYMENT' Type,payment_mode Status,amount,reference_number Reference,notes FROM supplier_payments WHERE purchase_id=@purchaseId ORDER BY supplier_payment_id",
new
{
    purchaseId
})).ToList();
        r.Returns = (await c.QueryAsync<OperationRow>("SELECT purchase_return_id Id,return_date Date,'RETURN' Type,'RETURNED' Status,return_total Amount,supplier_reference Reference,notes FROM purchase_returns WHERE purchase_id=@purchaseId ORDER BY purchase_return_id",
new
{
    purchaseId
})).ToList();
        return r;
    }

    public Task<long> AddSupplierPaymentAsync(long purchaseId, MoneyReceiptRequest request, int userId)
    {
        return AddPayablePayment(purchaseId, request, userId);
    }

    private async Task<long> AddPayablePayment(long purchaseId, MoneyReceiptRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.PaymentMode = Required(request.PaymentMode, "payment mode", 30).ToUpperInvariant();
        if (request.PaymentMode is not ("CASH" or "CARD" or "UPI" or "BANK" or "CHEQUE") || request.Amount <= 0)
        {
            throw new ValidationException("Choose a valid payment method and positive amount.");
        }

        request.ReferenceNumber = Optional(request.ReferenceNumber, "Reference number", 100);
        request.Notes = Optional(request.Notes, "Notes", 1000);
        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "supplier_payments", "supplier_payment_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var p = await c.QuerySingleOrDefaultAsync<MoneyHeader>("SELECT total_amount Total,status Status FROM purchases WHERE purchase_id=@purchaseId FOR UPDATE",
new
{
    purchaseId
},
t) ?? throw new KeyNotFoundException("Purchase not found.");
        if (p.Status != "COMPLETED")
        {
            throw new ValidationException("Supplier payment requires a completed purchase.");
        }

        var due = await c.ExecuteScalarAsync<decimal>("SELECT greatest(@Total-coalesce((SELECT sum(return_total) FROM purchase_returns WHERE purchase_id=@purchaseId),0)-coalesce((SELECT sum(amount) FROM supplier_payments WHERE purchase_id=@purchaseId),0),0)",
new
{
    p.Total,
    purchaseId
},
t);
        if (request.Amount > due)
        {
            throw new ValidationException($"Payment exceeds the supplier balance {due:0.00}.");
        }

        var id = await c.ExecuteScalarAsync<long>("INSERT INTO supplier_payments(purchase_id,payment_mode,amount,reference_number,notes,request_id,request_payload,created_by) VALUES(@purchaseId,@PaymentMode,@Amount,NULLIF(@ReferenceNumber,''),NULLIF(@Notes,''),@RequestId,@payload::jsonb,@userId) RETURNING supplier_payment_id",
new
{
    purchaseId,
    request.PaymentMode,
    request.Amount,
    request.ReferenceNumber,
    request.Notes,
    request.RequestId,
    payload,
    userId
},
t);
        await Audit(c, t, "Purchases", "PAY_SUPPLIER", "supplier_payments", id, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<long> ReturnPurchaseAsync(long purchaseId, PurchaseReturnRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.Notes = Required(request.Notes, "return reason", 2000);
        request.SupplierReference = Optional(request.SupplierReference, "Supplier reference", 100);
        if (request.Items.Count is < 1 or > 100 || request.Items.GroupBy(x => x.PurchaseItemId).Any(g => g.Count() > 1))
        {
            throw new ValidationException("Choose distinct purchase lines.");
        }

        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "purchase_returns", "purchase_return_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var status = await c.QuerySingleOrDefaultAsync<string>("SELECT status FROM purchases WHERE purchase_id=@purchaseId FOR UPDATE", new
        {
            purchaseId
        }, t) ?? throw new KeyNotFoundException("Purchase not found.");
        if (status != "COMPLETED")
        {
            throw new ValidationException("Only received purchases can be returned.");
        }

        decimal total = 0;
        var lines = new List<PurchaseLine>();
        foreach (var input in request.Items.OrderBy(x => x.PurchaseItemId))
        {
            input.Reason = Required(input.Reason, "item return reason", 1000);
            var row = await c.QuerySingleOrDefaultAsync<PurchaseLine>("SELECT purchase_item_id Id,product_id ProductId,variant_id VariantId,received_quantity Quantity,purchase_price Price,tax_percentage Tax,line_total Total,is_serial_tracked Tracked FROM purchase_items WHERE purchase_id=@purchaseId AND purchase_item_id=@PurchaseItemId FOR UPDATE",
new
{
    purchaseId,
    input.PurchaseItemId
},
t) ?? throw new ValidationException("A purchase line was not found.");
            var returned = await c.ExecuteScalarAsync<int>("SELECT coalesce(sum(quantity),0) FROM purchase_return_items WHERE purchase_item_id=@PurchaseItemId", input, t);
            if (input.Quantity <= 0 || input.Quantity > row.Quantity - returned)
            {
                throw new ValidationException("Return quantity exceeds received stock.");
            }

            if (row.Tracked && (input.Quantity != 1 || !input.ProductUnitId.HasValue))
            {
                throw new ValidationException("Select one serial for each serialized return.");
            }

            if (!row.Tracked && input.ProductUnitId.HasValue)
            {
                throw new ValidationException("Quantity products do not use a serial.");
            }

            var stock = await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@VariantId", new
            {
                row.VariantId
            }, t);
            if (stock < input.Quantity)
            {
                throw new ValidationException("Insufficient current stock for the supplier return.");
            }

            if (row.Tracked)
            {
                var ok = await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE product_unit_id=@ProductUnitId AND purchase_item_id=@PurchaseItemId AND unit_status='IN_STOCK' FOR UPDATE)",
input,
t);
                if (!ok)
                {
                    throw new ValidationException("Selected serial is unavailable or came from another purchase line.");
                }
            }

            row.ReturnQuantity = input.Quantity;
            row.UnitId = input.ProductUnitId;
            row.Reason = input.Reason;
            row.ReturnAmount = Math.Round(row.Price * input.Quantity * (1 + row.Tax / 100), 2, MidpointRounding.AwayFromZero);
            total += row.ReturnAmount;
            lines.Add(row);
        }

        var next = await c.ExecuteScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('purchase_returns','purchase_return_id'))", transaction: t);
        var number = $"PRT-{DateTime.UtcNow.Year}-{next:D6}";
        await c.ExecuteAsync("INSERT INTO purchase_returns(purchase_return_id,purchase_id,return_number,return_total,supplier_reference,notes,request_id,request_payload,created_by) VALUES(@next,@purchaseId,@number,@total,NULLIF(@SupplierReference,''),@Notes,@RequestId,@payload::jsonb,@userId)",
new
{
    next,
    purchaseId,
    number,
    total,
    request.SupplierReference,
    request.Notes,
    request.RequestId,
    payload,
    userId
},
t);
        foreach (var row in lines)
        {
            await c.ExecuteAsync("INSERT INTO purchase_return_items(purchase_return_id,purchase_item_id,product_id,variant_id,product_unit_id,quantity,amount,reason) VALUES(@next,@Id,@ProductId,@VariantId,@UnitId,@ReturnQuantity,@ReturnAmount,@Reason); INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@VariantId,@UnitId,'SUPPLIER_RETURN',@ReturnQuantity,'PURCHASE_RETURN',@next,@Reason,@userId)",
new
{
    next,
    row.Id,
    row.ProductId,
    row.VariantId,
    row.UnitId,
    row.ReturnQuantity,
    row.ReturnAmount,
    row.Reason,
    userId
},
t);
            if (row.UnitId.HasValue)
            {
                await c.ExecuteAsync("UPDATE product_units SET unit_status='RETURNED_TO_SUPPLIER',updated_at=now() WHERE product_unit_id=@UnitId", row, t);
            }
        }

        await Audit(c, t, "Purchases", "RETURN", "purchase_returns", next, payload, userId);
        await t.CommitAsync();
        return next;
    }

    public async Task<long> PostStockCountAsync(StockCountRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.Notes = Required(request.Notes, "count notes", 2000);
        if (request.Items.Count is < 1 or > 500 || request.Items.GroupBy(x => x.VariantId).Any(g => g.Count() > 1))
        {
            throw new ValidationException("Supply distinct count lines.");
        }

        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "stock_counts", "stock_count_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        foreach (var product in request.Items.Select(x => x.ProductId).Distinct().Order())
        {
            await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=@product FOR UPDATE", new
            {
                product
            }, t);
        }

        var next = await c.ExecuteScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('stock_counts','stock_count_id'))", transaction: t);
        var number = $"CNT-{DateTime.UtcNow.Year}-{next:D6}";
        await c.ExecuteAsync("INSERT INTO stock_counts(stock_count_id,count_number,notes,request_id,request_payload,created_by) VALUES(@next,@number,@Notes,@RequestId,@payload::jsonb,@userId)",
new
{
    next,
    number,
    request.Notes,
    request.RequestId,
    payload,
    userId
},
t);
        foreach (var line in request.Items.OrderBy(x => x.VariantId))
        {
            if (line.CountedQuantity < 0)
            {
                throw new ValidationException("Counted quantity cannot be negative.");
            }

            var v = await c.QuerySingleOrDefaultAsync<CountVariant>("SELECT product_id ProductId,is_serial_tracked Tracked FROM product_variants WHERE variant_id=@VariantId AND product_id=@ProductId AND is_active",
line,
t) ?? throw new ValidationException("Choose an active product configuration.");
            var expected = await c.ExecuteScalarAsync<int>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@VariantId", line, t);
            var variance = line.CountedQuantity - expected;
            if (v.Tracked && variance != 0)
            {
                throw new ValidationException("A serialized count differs from the unit register. Reconcile the missing or extra serial through Stock adjustment first.");
            }

            await c.ExecuteAsync("INSERT INTO stock_count_items(stock_count_id,product_id,variant_id,expected_quantity,counted_quantity,variance,remarks) VALUES(@next,@ProductId,@VariantId,@expected,@CountedQuantity,@variance,@Remarks)",
new
{
    next,
    line.ProductId,
    line.VariantId,
    expected,
    line.CountedQuantity,
    variance,
    line.Remarks
},
t);
            if (variance != 0)
            {
                await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@VariantId,@type,@qty,'STOCK_COUNT',@next,@remarks,@userId)",
new
{
    line.ProductId,
    line.VariantId,
    type = variance > 0 ? "ADJUSTMENT_IN" : "ADJUSTMENT_OUT",
    qty = Math.Abs(variance),
    next,
    remarks = $"{request.Notes}; {line.Remarks}",
    userId
},
t);
            }
        }

        await Audit(c, t, "Inventory", "COUNT", "stock_counts", next, payload, userId);
        await t.CommitAsync();
        return next;
    }

    public async Task<DocumentOperations> ServiceAsync(long serviceId)
    {
        await using var c = factory.CreateConnection();
        var r = await c.QuerySingleOrDefaultAsync<DocumentOperations>("SELECT sr.service_charge+coalesce((SELECT sum(total_amount) FROM service_parts WHERE service_request_id=sr.service_request_id),0) DocumentTotal,0 ReturnedTotal,coalesce((SELECT sum(amount) FROM service_payments WHERE service_request_id=sr.service_request_id AND payment_mode<>'CREDIT'),0) PaidTotal,0 RefundedTotal FROM service_requests sr WHERE sr.service_request_id=@serviceId",
new
{
    serviceId
}) ?? throw new KeyNotFoundException("Service request not found.");
        r.Payments = (await c.QueryAsync<OperationRow>("SELECT service_payment_id Id,payment_date Date,'SERVICE_PAYMENT' Type,payment_mode Status,amount,reference_number Reference,NULL Notes FROM service_payments WHERE service_request_id=@serviceId ORDER BY service_payment_id",
new
{
    serviceId
})).ToList();
        return r;
    }

    public async Task<long> AddServicePaymentAsync(long serviceId, MoneyReceiptRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.PaymentMode = Required(request.PaymentMode, "payment mode", 30).ToUpperInvariant();
        if (request.PaymentMode is not ("CASH" or "CARD" or "UPI" or "BANK" or "CREDIT") || request.Amount <= 0)
        {
            throw new ValidationException("Choose a valid payment and amount.");
        }

        request.ReferenceNumber = Optional(request.ReferenceNumber, "Reference number", 100);
        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "service_payments", "service_payment_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var total = await c.QuerySingleOrDefaultAsync<decimal?>("SELECT service_charge+coalesce((SELECT sum(total_amount) FROM service_parts WHERE service_request_id=@serviceId),0) FROM service_requests WHERE service_request_id=@serviceId FOR UPDATE",
new
{
    serviceId
},
t) ?? throw new KeyNotFoundException("Service request not found.");
        var paid = await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(amount),0) FROM service_payments WHERE service_request_id=@serviceId AND payment_mode<>'CREDIT'",
new
{
    serviceId
},
t);
        if (request.PaymentMode != "CREDIT" && request.Amount > total - paid)
        {
            throw new ValidationException("Payment exceeds the service balance.");
        }

        var id = await c.ExecuteScalarAsync<long>("INSERT INTO service_payments(service_request_id,payment_mode,amount,reference_number,request_id,request_payload,created_by) VALUES(@serviceId,@PaymentMode,@Amount,NULLIF(@ReferenceNumber,''),@RequestId,@payload::jsonb,@userId) RETURNING service_payment_id",
new
{
    serviceId,
    request.PaymentMode,
    request.Amount,
    request.ReferenceNumber,
    request.RequestId,
    payload,
    userId
},
t);
        await Audit(c, t, "Service", "COLLECT", "service_payments", id, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<long> AddFulfilmentAsync(long saleId, FulfilmentRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.FulfilmentType = Required(request.FulfilmentType, "fulfilment type", 30).ToUpperInvariant();
        request.Status = Required(request.Status, "status", 20).ToUpperInvariant();
        request.Address = Required(request.Address, "delivery or installation address", 1000);
        if (request.FulfilmentType is not ("DELIVERY" or "INSTALLATION" or "DELIVERY_INSTALLATION") || request.Status is not ("SCHEDULED" or "IN_PROGRESS" or "COMPLETED" or "CANCELLED") || request.ScheduledAt == default)
        {
            throw new ValidationException("Enter valid fulfilment details.");
        }

        var payload = JsonSerializer.Serialize(request);
        return await SimpleSaleInsert(saleId,
request,
userId,
payload,
"sale_fulfilments",
"fulfilment_id",
"INSERT INTO sale_fulfilments(sale_id,fulfilment_type,status,scheduled_at,address,contact_number,technician_name,notes,completed_at,request_id,request_payload,created_by) VALUES(@saleId,@FulfilmentType,@Status,@ScheduledAt,@Address,@ContactNumber,@TechnicianName,@Notes,CASE WHEN @Status='COMPLETED' THEN now() END,@RequestId,@payload::jsonb,@userId) RETURNING fulfilment_id");
    }

    public async Task<long> AddTradeInAsync(long saleId, TradeInRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.ItemType = Required(request.ItemType, "trade-in item type", 50).ToUpperInvariant();
        request.Status = Required(request.Status, "trade-in status", 20).ToUpperInvariant();
        if (request.Status is not ("RECEIVED" or "VALUED" or "RESOLD" or "RECYCLED" or "DISPOSED") || request.AllowanceAmount < 0)
        {
            throw new ValidationException("Enter valid trade-in details.");
        }

        var payload = JsonSerializer.Serialize(request);
        return await SimpleSaleInsert(saleId,
request,
userId,
payload,
"trade_ins",
"trade_in_id",
"INSERT INTO trade_ins(sale_id,item_type,brand_model,serial_number,allowance_amount,status,notes,request_id,request_payload,created_by) VALUES(@saleId,@ItemType,@BrandModel,@SerialNumber,@AllowanceAmount,@Status,@Notes,@RequestId,@payload::jsonb,@userId) RETURNING trade_in_id");
    }

    private async Task<long> SimpleSaleInsert(long saleId,
OperationRequest request,
int userId,
string payload,
string table,
string idColumn,
string sql)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, table, idColumn, request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        var status = await c.QuerySingleOrDefaultAsync<string>("SELECT status FROM sales WHERE sale_id=@saleId FOR SHARE", new
        {
            saleId
        }, t) ?? throw new KeyNotFoundException("Sale not found.");
        if (status != "COMPLETED")
        {
            throw new ValidationException("A completed sale is required.");
        }

        var args = new DynamicParameters(request);
        args.Add("saleId", saleId);
        args.Add("userId", userId);
        args.Add("payload", payload);
        var id = await c.ExecuteScalarAsync<long>(sql, args, t);
        await Audit(c, t, "Sales", "FOLLOW_UP", table, id, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<IEnumerable<OperationRow>> WarrantyRmasAsync(long claimId)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<OperationRow>("SELECT warranty_rma_id Id,created_at Date,'MANUFACTURER_RMA' Type,status Status,charge_amount Amount,provider_reference Reference,notes FROM warranty_rmas WHERE warranty_claim_id=@claimId ORDER BY warranty_rma_id",
new
{
    claimId
});
    }

    public async Task<long> AddWarrantyRmaAsync(long claimId, WarrantyRmaRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        Base(request);
        request.Status = Required(request.Status, "RMA status", 30).ToUpperInvariant();
        if (request.Status is not ("READY_TO_SEND" or "SENT_TO_PROVIDER" or "PROVIDER_RECEIVED" or "REPAIRED" or "REPLACED" or "REJECTED" or "RETURNED_TO_CUSTOMER") || request.ChargeAmount < 0)
        {
            throw new ValidationException("Enter valid manufacturer RMA details.");
        }

        var payload = JsonSerializer.Serialize(request);
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await Replay(c, t, "warranty_rmas", "warranty_rma_id", request.RequestId, payload, userId) is { } replay)
        {
            return replay;
        }

        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM warranty_claims WHERE warranty_claim_id=@claimId)", new
        {
            claimId
        }, t))
        {
            throw new KeyNotFoundException("Warranty claim not found.");
        }

        var id = await c.ExecuteScalarAsync<long>("INSERT INTO warranty_rmas(warranty_claim_id,provider_reference,status,dispatched_at,returned_at,charge_amount,notes,request_id,request_payload,created_by) VALUES(@claimId,@ProviderReference,@Status,@DispatchedAt,@ReturnedAt,@ChargeAmount,@Notes,@RequestId,@payload::jsonb,@userId) RETURNING warranty_rma_id",
new
{
    claimId,
    request.ProviderReference,
    request.Status,
    request.DispatchedAt,
    request.ReturnedAt,
    request.ChargeAmount,
    request.Notes,
    request.RequestId,
    payload,
    userId
},
t);
        await Audit(c, t, "Warranty claims", "RMA", "warranty_rmas", id, payload, userId);
        await t.CommitAsync();
        return id;
    }

    public async Task<IEnumerable<ProductSalesRow>> ProductSalesAsync(DateTime from, DateTime to)
    {
        if (from.Date > to.Date || (to.Date - from.Date).TotalDays > 3660)
        {
            throw new ValidationException("Choose a valid period up to ten years.");
        }

        await using var c = factory.CreateConnection();
        return await c.QueryAsync<ProductSalesRow>("""
          WITH original_lines AS (
            SELECT i.*,s.sale_date,s.discount_amount header_allowance,s.location_id sale_location,
              i.quantity*i.selling_price-i.discount_amount base_value
            FROM sale_items i JOIN sales s ON s.sale_id=i.sale_id
            WHERE s.status='COMPLETED' AND (s.sale_date>=@from AND s.sale_date<@end OR EXISTS(
              SELECT 1 FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date>=@from AND r.return_date<@end))
          ), allocated AS (
            SELECT l.*,
              base_value-coalesce(round(header_allowance*sum(base_value) OVER(PARTITION BY sale_id ORDER BY sale_item_id)/
                  nullif(sum(base_value) OVER(PARTITION BY sale_id),0),2)
                -round(header_allowance*(sum(base_value) OVER(PARTITION BY sale_id ORDER BY sale_item_id)-base_value)/
                  nullif(sum(base_value) OVER(PARTITION BY sale_id),0),2),0) net_value
            FROM original_lines l
          ), evidence AS (
            SELECT l.*,cost.unit_cost FROM allocated l
            LEFT JOIN LATERAL (
              -- Preserve the existing receipt-price management estimate. This is not
              -- an inventory costing policy, cost layer or authoritative COGS.
              SELECT CASE WHEN bool_and(ri.purchase_price>0 AND
                    ri.purchase_price-ri.discount_amount::numeric/(ri.quantity+ri.damaged_quantity)>=0)
                THEN sum((ri.purchase_price-ri.discount_amount::numeric/(ri.quantity+ri.damaged_quantity))*ri.quantity)/nullif(sum(ri.quantity),0) END unit_cost
              FROM purchase_receipt_items ri JOIN purchase_receipts r ON r.receipt_id=ri.receipt_id
              JOIN purchase_items pi ON pi.purchase_item_id=ri.purchase_item_id
              WHERE pi.variant_id=l.variant_id AND r.location_id=l.sale_location AND r.status='COMPLETED'
                AND ri.quantity>0 AND r.received_date<=l.sale_date::date AND r.created_at<=l.sale_date
            ) cost ON NOT coalesce((to_jsonb(l)->>'is_manual')::boolean,false)
          ), events AS (
            SELECT l.product_id,l.variant_id,l.quantity sold,0::integer returned,l.net_value revenue,
              round(l.unit_cost*l.quantity,2) cost
            FROM evidence l WHERE l.sale_date>=@from AND l.sale_date<@end
            UNION ALL
            SELECT l.product_id,l.variant_id,0,ri.quantity,
              -CASE WHEN l.line_total=0 THEN 0 ELSE round(ri.amount*l.base_value/l.line_total,2) END,
              -round(l.unit_cost*ri.quantity,2)
            FROM evidence l JOIN sale_return_items ri ON ri.sale_item_id=l.sale_item_id
            JOIN sale_returns r ON r.sale_return_id=ri.sale_return_id
            WHERE r.return_date>=@from AND r.return_date<@end
          )
          SELECT e.product_id ProductId,e.variant_id VariantId,
            CASE WHEN e.product_id IS NULL THEN 'Manual items (cost unknown / unallocated)'
              ELSE p.product_name||' / '||v.variant_name END ProductName,v.sku Sku,
            sum(e.sold)::bigint QuantitySold,sum(e.returned)::bigint QuantityReturned,
            sum(e.revenue) NetSales,
            CASE WHEN count(*) FILTER(WHERE e.cost IS NULL)=0 THEN sum(e.cost) END EstimatedCost
          FROM events e LEFT JOIN products p ON p.product_id=e.product_id
          LEFT JOIN product_variants v ON v.variant_id=e.variant_id
          GROUP BY e.product_id,e.variant_id,p.product_name,v.variant_name,v.sku
          ORDER BY NetSales DESC,ProductName
          """,
new
{
    from = from.Date,
    end = to.Date.AddDays(1)
});
    }

    private static Task Audit(NpgsqlConnection c,
NpgsqlTransaction t,
string module,
string action,
string table,
long id,
string payload,
int userId)
    {
        return c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,@module,@action,@table,@id,@payload::jsonb)",
new
{
    userId,
    module,
    action,
    table,
    id,
    payload
},
t);
    }

    private sealed class ReplayRow
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

    private sealed class MoneyHeader
    {
        public decimal Total
        {
            get; set;
        }
        public string Status { get; set; } = "";
    }

    private sealed class SaleLine
    {
        public long Id
        {
            get; set;
        }
        public int? ProductId
        {
            get; set;
        }
        public long? VariantId
        {
            get; set;
        }
        public long? UnitId
        {
            get; set;
        }
        public int Quantity
        {
            get; set;
        }
        public decimal Total
        {
            get; set;
        }
        public int ReturnQuantity
        {
            get; set;
        }
        public decimal ReturnAmount
        {
            get; set;
        }
        public string Disposition { get; set; } = "";
    }

    private sealed class PurchaseLine
    {
        public long Id
        {
            get; set;
        }
        public int ProductId
        {
            get; set;
        }
        public long VariantId
        {
            get; set;
        }
        public long? UnitId
        {
            get; set;
        }
        public int Quantity
        {
            get; set;
        }
        public decimal Price
        {
            get; set;
        }
        public decimal Tax
        {
            get; set;
        }
        public bool Tracked
        {
            get; set;
        }
        public int ReturnQuantity
        {
            get; set;
        }
        public decimal ReturnAmount
        {
            get; set;
        }
        public string Reason { get; set; } = "";
    }

    private sealed class CountVariant
    {
        public int ProductId
        {
            get; set;
        }
        public bool Tracked
        {
            get; set;
        }
    }
}
