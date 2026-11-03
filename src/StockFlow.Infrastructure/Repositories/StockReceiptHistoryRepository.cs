using Dapper;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class StockReceiptHistoryRepository : IStockReceiptHistoryRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public StockReceiptHistoryRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string ReceiptCte = """
        WITH receipts AS (
          SELECT 'PURCHASE'::text Source,r.receipt_id SourceId,r.created_at OccurredAt,r.receipt_number ReceiptNumber,
            'PURCHASE'::text MovementType,'Purchase Receipt'::text TypeLabel,
            COALESCE(p.supplier_name_snapshot,s.supplier_name) PartyName,count(ri.receipt_item_id)::int ItemCount,
            COALESCE(sum(ri.quantity),0)::bigint Quantity,
            COALESCE(sum(round((((ri.quantity+ri.damaged_quantity)*ri.purchase_price)-ri.discount_amount)*(1+ri.tax_percentage/100),2)),0)::numeric Value,
            r.status Status,p.supplier_id SupplierId,
            concat_ws(' ',r.receipt_number,p.purchase_number,r.supplier_invoice_number,COALESCE(p.supplier_name_snapshot,s.supplier_name),
              string_agg(DISTINCT product.product_name||' / '||variant.variant_name,' ')) SearchText
          FROM purchase_receipts r JOIN purchases p ON p.purchase_id=r.purchase_id
          JOIN suppliers s ON s.supplier_id=p.supplier_id
          JOIN purchase_receipt_items ri ON ri.receipt_id=r.receipt_id
          JOIN purchase_items pi ON pi.purchase_item_id=ri.purchase_item_id
          JOIN products product ON product.product_id=pi.product_id JOIN product_variants variant ON variant.variant_id=pi.variant_id
          GROUP BY r.receipt_id,p.purchase_id,s.supplier_name
          UNION ALL
          SELECT 'OPENING_STOCK',h.intake_id,h.created_at,h.intake_number,'OPENING_STOCK','Opening Stock',NULL,
            count(i.intake_item_id)::int,COALESCE(sum(i.quantity),0)::bigint,
            COALESCE(sum(round(i.quantity*i.purchase_price*(1+i.tax_percentage/100),2)),0)::numeric,h.status,NULL,
            concat_ws(' ',h.intake_number,string_agg(i.product_name_snapshot||' / '||i.variant_name_snapshot,' '))
          FROM stock_intakes h JOIN stock_intake_items i ON i.intake_id=h.intake_id
          WHERE h.kind='OPENING_STOCK' GROUP BY h.intake_id
          UNION ALL
          SELECT 'CUSTOMER_RETURN',sr.sale_return_id,sr.return_date,sr.return_number,'CUSTOMER_RETURN','Customer Return',
            COALESCE(sale.customer_name_snapshot,c.customer_name,'Walk-in'),count(sri.sale_return_item_id)::int,
            COALESCE(sum(sri.quantity),0)::bigint,sr.return_total,'COMPLETED',NULL,
            concat_ws(' ',sr.return_number,sale.invoice_number,COALESCE(sale.customer_name_snapshot,c.customer_name),
              string_agg(DISTINCT product.product_name||' / '||variant.variant_name,' '))
          FROM sale_returns sr JOIN sale_return_items sri ON sri.sale_return_id=sr.sale_return_id
          JOIN sales sale ON sale.sale_id=sr.sale_id LEFT JOIN customers c ON c.customer_id=sale.customer_id
          JOIN products product ON product.product_id=sri.product_id JOIN product_variants variant ON variant.variant_id=sri.variant_id
          GROUP BY sr.sale_return_id,sale.sale_id,c.customer_name
          UNION ALL
          SELECT 'STOCK_COUNT',sc.stock_count_id,sc.counted_at,sc.count_number,'ADJUSTMENT_IN','Adjustment In',NULL,
            count(sci.stock_count_item_id)::int,COALESCE(sum(sci.variance),0)::bigint,NULL,'COMPLETED',NULL,
            concat_ws(' ',sc.count_number,string_agg(DISTINCT product.product_name||' / '||variant.variant_name,' '))
          FROM stock_counts sc JOIN stock_count_items sci ON sci.stock_count_id=sc.stock_count_id AND sci.variance>0
          JOIN products product ON product.product_id=sci.product_id JOIN product_variants variant ON variant.variant_id=sci.variant_id
          GROUP BY sc.stock_count_id
          UNION ALL
          SELECT 'ADJUSTMENT',m.reference_id,max(m.created_at),
            'ADJ-'||to_char(max(m.created_at),'YYYY')||'-'||lpad(m.reference_id::text,6,'0'),'ADJUSTMENT_IN','Adjustment In',NULL,
            count(DISTINCT m.variant_id)::int,sum(m.quantity)::bigint,NULL,'COMPLETED',NULL,
            concat_ws(' ','ADJ-'||lpad(m.reference_id::text,6,'0'),string_agg(DISTINCT product.product_name||' / '||variant.variant_name,' '))
          FROM inventory_transactions m JOIN products product ON product.product_id=m.product_id
          JOIN product_variants variant ON variant.variant_id=m.variant_id
          WHERE m.transaction_type='ADJUSTMENT_IN' AND m.reference_type='ADJUSTMENT' AND m.reference_id IS NOT NULL
          GROUP BY m.reference_id
        ), filtered AS (
          SELECT * FROM receipts WHERE (@Type='' OR MovementType=@Type)
            AND (@SupplierId::int IS NULL OR SupplierId=@SupplierId)
            AND (@Term='' OR (@ExactReceipt AND lower(ReceiptNumber)=lower(@Term))
              OR (NOT @ExactReceipt AND SearchText ILIKE @Pattern))
            AND (@From::date IS NULL OR OccurredAt>=@From::date)
            AND (@To::date IS NULL OR OccurredAt<@To::date+INTERVAL '1 day')
        )
        """;
    public async Task<StockReceiptHistoryResult> SearchAsync(StockReceiptSearch search)
    {
        await using var connection = factory.CreateConnection();
        var args = Arguments(search);
        using var rows = await connection.QueryMultipleAsync(ReceiptCte + """
            SELECT count(*) Receipts,COALESCE(sum(Quantity),0)::bigint UnitsReceived,
              COALESCE(sum(Value) FILTER(WHERE MovementType='PURCHASE'),0)::numeric PurchaseValue,
              count(*) FILTER(WHERE MovementType='ADJUSTMENT_IN') Adjustments FROM filtered;
            """ + ReceiptCte + """
            SELECT Source,SourceId,OccurredAt,ReceiptNumber,MovementType,TypeLabel,PartyName,ItemCount,Quantity,Value,Status
            FROM filtered ORDER BY OccurredAt DESC,SourceId DESC LIMIT @PageSize OFFSET @Offset;
            """ + ReceiptCte + """
            SELECT count(*)::int FROM filtered;
            """,
args);
        return new StockReceiptHistoryResult
        {
            Summary = await rows.ReadSingleAsync<StockReceiptSummary>(),
            Rows = (await rows.ReadAsync<StockReceiptRow>()).ToList(),
            TotalCount = await rows.ReadSingleAsync<int>(),
            Page = search.Page,
            PageSize = search.PageSize
        };
    }

    public async Task<StockReceiptItemHistoryResult> SearchItemsAsync(StockReceiptSearch search)
    {
        await using var connection = factory.CreateConnection();
        var args = Arguments(search);
        const string movementCte = """
            WITH movements AS (
              SELECT m.created_at OccurredAt,product.product_name||' / '||variant.variant_name ProductName,m.quantity::bigint Quantity,
                CASE WHEN m.transaction_type='ADJUSTMENT_IN' AND m.reference_type='OPENING_STOCK' THEN 'OPENING_STOCK' ELSE m.transaction_type END MovementType,
                CASE WHEN m.transaction_type='PURCHASE' THEN 'Purchase Receipt'
                     WHEN m.transaction_type='CUSTOMER_RETURN' THEN 'Customer Return'
                     WHEN m.reference_type='OPENING_STOCK' THEN 'Opening Stock' ELSE 'Adjustment In' END TypeLabel,
                COALESCE(receipt.receipt_number,sr.return_number,intake.intake_number,sc.count_number,
                  CASE WHEN m.reference_type='ADJUSTMENT' THEN 'ADJ-'||to_char(m.created_at,'YYYY')||'-'||lpad(m.reference_id::text,6,'0') END) ReceiptNumber,
                CASE WHEN m.transaction_type='PURCHASE' THEN 'PURCHASE' WHEN m.transaction_type='CUSTOMER_RETURN' THEN 'CUSTOMER_RETURN'
                     WHEN m.reference_type='OPENING_STOCK' THEN 'OPENING_STOCK' WHEN m.reference_type='STOCK_COUNT' THEN 'STOCK_COUNT' ELSE 'ADJUSTMENT' END Source,
                COALESCE(receipt.receipt_id,sr.sale_return_id,intake.intake_id,sc.stock_count_id,m.reference_id) SourceId,
                CASE WHEN m.transaction_type='PURCHASE' THEN COALESCE(purchase.supplier_name_snapshot,supplier.supplier_name)
                     WHEN m.transaction_type='CUSTOMER_RETURN' THEN COALESCE(sale.customer_name_snapshot,customer.customer_name,'Walk-in') END PartyName,
                CASE WHEN m.transaction_type='PURCHASE' THEN purchase.supplier_id END SupplierId,
                concat_ws(' ',product.product_name,variant.variant_name,receipt.receipt_number,purchase.purchase_number,receipt.supplier_invoice_number,
                  COALESCE(purchase.supplier_name_snapshot,supplier.supplier_name),sr.return_number,sale.invoice_number,
                  COALESCE(sale.customer_name_snapshot,customer.customer_name),intake.intake_number,sc.count_number) SearchText
              FROM inventory_transactions m JOIN products product ON product.product_id=m.product_id
              JOIN product_variants variant ON variant.variant_id=m.variant_id
              LEFT JOIN purchase_receipts receipt ON receipt.receipt_id=m.receipt_id
              LEFT JOIN purchases purchase ON purchase.purchase_id=m.reference_id AND m.reference_type='PURCHASE'
              LEFT JOIN suppliers supplier ON supplier.supplier_id=purchase.supplier_id
              LEFT JOIN sale_returns sr ON sr.sale_return_id=m.reference_id AND m.reference_type='SALE_RETURN'
              LEFT JOIN sales sale ON sale.sale_id=sr.sale_id LEFT JOIN customers customer ON customer.customer_id=sale.customer_id
              LEFT JOIN stock_intakes intake ON intake.intake_id=m.reference_id AND m.reference_type='OPENING_STOCK'
              LEFT JOIN stock_counts sc ON sc.stock_count_id=m.reference_id AND m.reference_type='STOCK_COUNT'
              WHERE m.transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN')
                AND (m.transaction_type<>'ADJUSTMENT_IN' OR m.reference_type IN('OPENING_STOCK','ADJUSTMENT','STOCK_COUNT'))
            ), filtered AS (
              SELECT * FROM movements WHERE (@Type='' OR MovementType=@Type)
                AND (@SupplierId::int IS NULL OR SupplierId=@SupplierId)
                AND (@Term='' OR (@ExactReceipt AND lower(ReceiptNumber)=lower(@Term))
                  OR (NOT @ExactReceipt AND SearchText ILIKE @Pattern))
                AND (@From::date IS NULL OR OccurredAt>=@From::date)
                AND (@To::date IS NULL OR OccurredAt<@To::date+INTERVAL '1 day')
            )
            """;
        var sql = movementCte + """
            SELECT OccurredAt,ProductName,Quantity,MovementType,TypeLabel,ReceiptNumber,Source,SourceId,PartyName
            FROM filtered ORDER BY OccurredAt DESC,ProductName LIMIT @PageSize OFFSET @Offset;
            """ + movementCte + """
            SELECT count(*)::int FROM filtered;
            """;
        using var rows = await connection.QueryMultipleAsync(sql, args);
        return new StockReceiptItemHistoryResult
        {
            Rows = (await rows.ReadAsync<StockReceiptItemMovement>()).ToList(),
            TotalCount = await rows.ReadSingleAsync<int>(),
            Page = search.Page,
            PageSize = search.PageSize
        };
    }

    public async Task<IReadOnlyList<StockReceiptSupplier>> GetSuppliersAsync()
    {
        await using var connection = factory.CreateConnection();
        return (await connection.QueryAsync<StockReceiptSupplier>("""
            SELECT DISTINCT s.supplier_id SupplierId,COALESCE(p.supplier_name_snapshot,s.supplier_name) SupplierName
            FROM purchase_receipts r JOIN purchases p ON p.purchase_id=r.purchase_id JOIN suppliers s ON s.supplier_id=p.supplier_id
            ORDER BY SupplierName,SupplierId
            """)).ToList();
    }

    public async Task<StockReceiptDetail?> GetDetailAsync(string source, long id)
    {
        return source switch
        {
            "PURCHASE" => await PurchaseAsync(id),
            "OPENING_STOCK" => await OpeningAsync(id),
            "CUSTOMER_RETURN" => await CustomerReturnAsync(id),
            "STOCK_COUNT" => await StockCountAsync(id),
            "ADJUSTMENT" => await AdjustmentAsync(id),
            _ => null
        };
    }

    private async Task<StockReceiptDetail?> PurchaseAsync(long id)
    {
        await using var c = factory.CreateConnection();
        var detail = await c.QuerySingleOrDefaultAsync<StockReceiptDetail>("""
            SELECT 'PURCHASE' Source,r.receipt_id SourceId,r.receipt_number ReceiptNumber,'Purchase Receipt' TypeLabel,
              COALESCE(p.supplier_name_snapshot,s.supplier_name) PartyName,r.supplier_invoice_number SupplierInvoiceNumber,
              r.supplier_invoice_date SupplierInvoiceDate,r.received_date ReceivedDate,p.purchase_number ReferenceNumber,
              u.full_name ReceivedBy,r.status Status,r.notes Notes,
              COALESCE(sum((ri.quantity+ri.damaged_quantity)*ri.purchase_price),0) Subtotal,
              COALESCE(sum(ri.discount_amount),0) Discount,
              COALESCE(sum(round((((ri.quantity+ri.damaged_quantity)*ri.purchase_price)-ri.discount_amount)*ri.tax_percentage/100,2)),0) Tax,
              COALESCE(sum(round((((ri.quantity+ri.damaged_quantity)*ri.purchase_price)-ri.discount_amount)*(1+ri.tax_percentage/100),2)),0) Total
            FROM purchase_receipts r JOIN purchases p ON p.purchase_id=r.purchase_id JOIN suppliers s ON s.supplier_id=p.supplier_id
            JOIN users u ON u.user_id=r.created_by JOIN purchase_receipt_items ri ON ri.receipt_id=r.receipt_id
            WHERE r.receipt_id=@id GROUP BY r.receipt_id,p.purchase_id,s.supplier_name,u.full_name
            """,
new
{
    id
});
        if (detail is null)
        {
            return null;
        }

        detail.Items = (await c.QueryAsync<StockReceiptDetailItem>("""
            SELECT COALESCE(to_jsonb(pi)->>'product_name_snapshot',product.product_name)||' / '||COALESCE(pi.variant_name_snapshot,variant.variant_name) ProductName,ri.quantity::bigint Quantity,
              COALESCE(to_jsonb(pi)->>'variant_attributes_snapshot','{}') AS AttributesJson,to_jsonb(pi)->>'sku_snapshot' AS Sku,
              ri.damaged_quantity::bigint DamagedQuantity,ri.purchase_price PurchasePrice,ri.discount_amount Discount,
              ri.tax_percentage TaxPercentage,round((((ri.quantity+ri.damaged_quantity)*ri.purchase_price)-ri.discount_amount)*(1+ri.tax_percentage/100),2) Total,
              ri.batch_number BatchNumber,ri.serial_numbers||ri.damaged_serial_numbers SerialNumbers
            FROM purchase_receipt_items ri JOIN purchase_items pi ON pi.purchase_item_id=ri.purchase_item_id
            JOIN products product ON product.product_id=pi.product_id JOIN product_variants variant ON variant.variant_id=pi.variant_id
            WHERE ri.receipt_id=@id ORDER BY ri.receipt_item_id
            """,
new
{
    id
})).ToList();
        return detail;
    }

    private async Task<StockReceiptDetail?> OpeningAsync(long id)
    {
        await using var c = factory.CreateConnection();
        var detail = await c.QuerySingleOrDefaultAsync<StockReceiptDetail>("""
            SELECT 'OPENING_STOCK' Source,h.intake_id SourceId,h.intake_number ReceiptNumber,'Opening Stock' TypeLabel,
              h.received_date ReceivedDate,u.full_name ReceivedBy,h.status Status,h.request_payload->'request'->>'Notes' Notes,
              COALESCE(sum(i.quantity*i.purchase_price),0) Subtotal,0 Discount,
              COALESCE(sum(round(i.quantity*i.purchase_price*i.tax_percentage/100,2)),0) Tax,
              COALESCE(sum(round(i.quantity*i.purchase_price*(1+i.tax_percentage/100),2)),0) Total
            FROM stock_intakes h JOIN users u ON u.user_id=h.created_by JOIN stock_intake_items i ON i.intake_id=h.intake_id
            WHERE h.intake_id=@id AND h.kind='OPENING_STOCK' GROUP BY h.intake_id,u.full_name
            """,
new
{
    id
});
        if (detail is null)
        {
            return null;
        }

        detail.Items = (await c.QueryAsync<StockReceiptDetailItem>("""
            SELECT i.product_name_snapshot||' / '||i.variant_name_snapshot ProductName,i.quantity::bigint Quantity,0::bigint DamagedQuantity,
              i.purchase_price PurchasePrice,0 Discount,i.tax_percentage TaxPercentage,
              round(i.quantity*i.purchase_price*(1+i.tax_percentage/100),2) Total,i.serial_numbers SerialNumbers
            FROM stock_intake_items i WHERE i.intake_id=@id ORDER BY i.intake_item_id
            """,
new
{
    id
})).ToList();
        return detail;
    }

    private async Task<StockReceiptDetail?> CustomerReturnAsync(long id)
    {
        await using var c = factory.CreateConnection();
        var detail = await c.QuerySingleOrDefaultAsync<StockReceiptDetail>("""
            SELECT 'CUSTOMER_RETURN' Source,sr.sale_return_id SourceId,sr.return_number ReceiptNumber,'Customer Return' TypeLabel,
              COALESCE(sale.customer_name_snapshot,customer.customer_name,'Walk-in') PartyName,sr.return_date ReceivedDate,
              sale.invoice_number ReferenceNumber,u.full_name ReceivedBy,'COMPLETED' Status,sr.notes Notes,
              sr.return_total Subtotal,0 Discount,0 Tax,sr.return_total Total
            FROM sale_returns sr JOIN sales sale ON sale.sale_id=sr.sale_id LEFT JOIN customers customer ON customer.customer_id=sale.customer_id
            JOIN users u ON u.user_id=sr.created_by WHERE sr.sale_return_id=@id
            """,
new
{
    id
});
        if (detail is null)
        {
            return null;
        }

        detail.Items = (await c.QueryAsync<StockReceiptDetailItem>("""
            SELECT product.product_name||' / '||variant.variant_name ProductName,sri.quantity::bigint Quantity,0::bigint DamagedQuantity,
              sri.amount Total,sri.disposition Disposition,
              CASE WHEN unit.serial_number IS NULL THEN ARRAY[]::text[] ELSE ARRAY[unit.serial_number] END SerialNumbers
            FROM sale_return_items sri JOIN products product ON product.product_id=sri.product_id
            JOIN product_variants variant ON variant.variant_id=sri.variant_id LEFT JOIN product_units unit ON unit.product_unit_id=sri.product_unit_id
            WHERE sri.sale_return_id=@id ORDER BY sri.sale_return_item_id
            """,
new
{
    id
})).ToList();
        return detail;
    }

    private async Task<StockReceiptDetail?> StockCountAsync(long id)
    {
        await using var c = factory.CreateConnection();
        var detail = await c.QuerySingleOrDefaultAsync<StockReceiptDetail>("""
            SELECT 'STOCK_COUNT' Source,sc.stock_count_id SourceId,sc.count_number ReceiptNumber,'Adjustment In' TypeLabel,
              sc.counted_at ReceivedDate,u.full_name ReceivedBy,'COMPLETED' Status,sc.notes Notes
            FROM stock_counts sc JOIN users u ON u.user_id=sc.created_by
            WHERE sc.stock_count_id=@id AND EXISTS(SELECT 1 FROM stock_count_items WHERE stock_count_id=sc.stock_count_id AND variance>0)
            """,
new
{
    id
});
        if (detail is null)
        {
            return null;
        }

        detail.Items = (await c.QueryAsync<StockReceiptDetailItem>("""
            SELECT product.product_name||' / '||variant.variant_name ProductName,sci.variance::bigint Quantity,0::bigint DamagedQuantity
            FROM stock_count_items sci JOIN products product ON product.product_id=sci.product_id
            JOIN product_variants variant ON variant.variant_id=sci.variant_id WHERE sci.stock_count_id=@id AND sci.variance>0
            ORDER BY sci.stock_count_item_id
            """,
new
{
    id
})).ToList();
        return detail;
    }

    private async Task<StockReceiptDetail?> AdjustmentAsync(long id)
    {
        await using var c = factory.CreateConnection();
        var detail = await c.QuerySingleOrDefaultAsync<StockReceiptDetail>("""
            SELECT 'ADJUSTMENT' Source,@id SourceId,'ADJ-'||to_char(max(m.created_at),'YYYY')||'-'||lpad(@id::text,6,'0') ReceiptNumber,
              'Adjustment In' TypeLabel,max(m.created_at) ReceivedDate,max(u.full_name) ReceivedBy,'COMPLETED' Status,max(m.remarks) Notes
            FROM inventory_transactions m JOIN users u ON u.user_id=m.created_by
            WHERE m.transaction_type='ADJUSTMENT_IN' AND m.reference_type='ADJUSTMENT' AND m.reference_id=@id
            HAVING count(*)>0
            """,
new
{
    id
});
        if (detail is null)
        {
            return null;
        }

        detail.Items = (await c.QueryAsync<StockReceiptDetailItem>("""
            SELECT product.product_name||' / '||variant.variant_name ProductName,sum(m.quantity)::bigint Quantity,0::bigint DamagedQuantity,
              CASE WHEN count(unit.serial_number)=0 THEN ARRAY[]::text[] ELSE array_agg(unit.serial_number ORDER BY unit.serial_number) END SerialNumbers
            FROM inventory_transactions m JOIN products product ON product.product_id=m.product_id
            JOIN product_variants variant ON variant.variant_id=m.variant_id LEFT JOIN product_units unit ON unit.product_unit_id=m.product_unit_id
            WHERE m.transaction_type='ADJUSTMENT_IN' AND m.reference_type='ADJUSTMENT' AND m.reference_id=@id
            GROUP BY product.product_name,variant.variant_name ORDER BY product.product_name,variant.variant_name
            """,
new
{
    id
})).ToList();
        return detail;
    }

    private static object Arguments(StockReceiptSearch search)
    {
        return new
        {
            search.Term,
            Pattern = "%" + search.Term + "%",
            ExactReceipt = new[]
            {
                "GRN-",
                "OPEN-",
                "INT-",
                "SRT-",
                "CNT-",
                "ADJ-"
            }.Any(prefix => search.Term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
            search.Type,
            search.SupplierId,
            From = search.From?.Date,
            To = search.To?.Date,
            search.PageSize,
            search.Offset
        };
    }
}
