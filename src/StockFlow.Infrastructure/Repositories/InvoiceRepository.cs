using System.Data;
using System.Text.Json;
using Dapper;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public InvoiceRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<InvoiceResponse?> GetInvoiceAsync(long saleId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var invoice = await connection.QuerySingleOrDefaultAsync<InvoiceResponse>("""
            SELECT s.sale_id AS SaleId,s.invoice_number AS InvoiceNumber,s.sale_date AS SaleDate,
                s.customer_name_snapshot AS CustomerName,s.customer_mobile_snapshot AS CustomerMobile,
                s.customer_address_snapshot AS CustomerAddress,s.customer_gst_snapshot AS CustomerGstNumber,
                coalesce((s.request_payload->>'PricesIncludeTax')::boolean,false) AS PricesIncludeTax,

            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (SELECT sum((i->>'Quantity')::numeric*(i->>'SellingPrice')::numeric) FROM jsonb_array_elements(s.request_payload->'Items') i) END AS ItemSubtotal,
            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (SELECT sum((i->>'DiscountAmount')::numeric) FROM jsonb_array_elements(s.request_payload->'Items') i) END AS ItemDiscountAmount,
            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (s.request_payload->>'DiscountAmount')::numeric-coalesce((s.request_payload->'TradeIn'->>'AllowanceAmount')::numeric,0) END AS InvoiceDiscountAmount,
                s.subtotal AS Subtotal,s.discount_amount AS DiscountAmount,s.tax_amount AS TaxAmount,s.total_amount AS TotalAmount,
                coalesce((s.request_payload->'TradeIn'->>'AllowanceAmount')::numeric,0) AS TradeInAllowance,
                (s.request_payload->>'CashTendered')::numeric AS CashTendered,
                (s.request_payload->>'CashTendered')::numeric-coalesce((SELECT sum((entry->>'Amount')::numeric) FROM jsonb_array_elements(s.request_payload->'Payments') entry WHERE entry->>'PaymentMode'='CASH'),0) AS CashChange,
                s.status AS Status,s.notes AS Notes,s.invoice_snapshot_version > 0 AS HasHistoricalDetails
            FROM sales s
            WHERE s.sale_id=@Id AND s.status IN ('COMPLETED','CANCELLED');
            """,
new
{
    Id = saleId
},
transaction);
        if (invoice is null)
        {
            return null;
        }

        var company = await connection.QuerySingleAsync<string?>("SELECT company_snapshot::text FROM sales WHERE sale_id=@Id;", new
        {
            Id = saleId
        }, transaction);
        invoice.Company = company is null ? new CompanySettings { CompanyName="Business details not captured" } : JsonSerializer.Deserialize<CompanySettings>(company) ?? throw new InvalidOperationException("Invalid company snapshot.");
        invoice.Items = (await connection.QueryAsync<InvoiceItemResponse>("""
            SELECT coalesce((to_jsonb(i)->>'is_manual')::boolean,false) IsManual,(to_jsonb(i)->>'mrp_snapshot')::numeric Mrp,
                coalesce(to_jsonb(i)->>'discount_type','FIXED') DiscountType,(to_jsonb(i)->>'discount_percent')::numeric DiscountPercent,
                CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                  (s.request_payload->'Items'->((row_number() OVER(ORDER BY i.sale_item_id)-1)::integer)->>'SellingPrice')::numeric END AS EnteredRate,
                CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                  (s.request_payload->'Items'->((row_number() OVER(ORDER BY i.sale_item_id)-1)::integer)->>'DiscountAmount')::numeric END AS EnteredDiscount,
                to_jsonb(i)->>'description_snapshot' Description,to_jsonb(i)->'manual_tax_snapshot'->>'hsnSac' HsnSac,
                to_jsonb(i)->'manual_tax_snapshot'->>'treatment' GstTreatment,(to_jsonb(i)->'manual_tax_snapshot'->>'ruleId')::bigint TaxRuleId,
                i.sale_item_id AS SaleItemId,i.product_id AS ProductId,i.product_unit_id AS ProductUnitId,
                COALESCE(i.product_name_snapshot,'Item details not captured') || CASE WHEN i.variant_name_snapshot IS NULL OR i.variant_name_snapshot='DEFAULT' THEN '' ELSE ' / ' || i.variant_name_snapshot END AS ProductName,
                i.brand_name_snapshot AS BrandName,i.model_snapshot AS Model,i.serial_number_snapshot AS SerialNumber,
                'LEGACY_UNKNOWN' AS SerialSource, i.quantity AS Quantity,i.selling_price AS SellingPrice,i.discount_amount AS DiscountAmount,
                COALESCE(to_jsonb(i)->>'variant_attributes_snapshot','{}') AS AttributesJson,to_jsonb(i)->>'sku_snapshot' AS Sku,
                to_jsonb(i)->>'unit_of_measure_snapshot' AS UnitOfMeasure,
                i.tax_percentage AS TaxPercentage,i.line_total AS LineTotal,
                coalesce(i.warranty_months_snapshot,0) AS WarrantyMonths,
                w.warranty_start_date AS WarrantyStartDate,w.warranty_end_date AS WarrantyEndDate,w.warranty_status AS WarrantyStatus
            FROM sale_items i JOIN sales s ON s.sale_id=i.sale_id
            LEFT JOIN LATERAL (SELECT * FROM warranties WHERE sale_item_id=i.sale_item_id ORDER BY warranty_id LIMIT 1) w ON TRUE
            WHERE i.sale_id=@Id ORDER BY i.sale_item_id;
            """,
new
{
    Id = saleId
},
transaction)).ToList();
        invoice.Payments = (await connection.QueryAsync<InvoicePaymentResponse>("""
            SELECT payment_mode AS PaymentMode,amount AS Amount,reference_number AS ReferenceNumber,payment_date AS PaymentDate,
              CASE WHEN payment_mode='CARD' THEN coalesce(request_payload->>'cardType',request_payload->>'CardType') END AS CardType FROM payments
            WHERE sale_id=@Id ORDER BY payment_id;
            """,
new
{
    Id = saleId
},
transaction)).ToList();
        // Reuse the operational settlement definition; the issued invoice totals stay immutable.
        invoice.Settlement = await connection.QuerySingleAsync<StockFlow.Application.DTOs.Operations.DocumentOperations>("""
            SELECT s.total_amount AS DocumentTotal,
              coalesce((SELECT sum(return_total) FROM sale_returns WHERE sale_id=s.sale_id),0) AS ReturnedTotal,
              coalesce((SELECT sum(amount) FROM payments WHERE sale_id=s.sale_id AND payment_mode<>'CREDIT'),0) AS PaidTotal,
              coalesce((SELECT sum(refund_amount) FROM sale_returns WHERE sale_id=s.sale_id),0) AS RefundedTotal
            FROM sales s WHERE s.sale_id=@saleId;
            """,new { saleId },transaction);
        if(await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_gst_invoice(bigint)') IS NOT NULL",transaction:transaction))
        {
            var snapshot=await connection.ExecuteScalarAsync<string?>("SELECT public.sf_gst_invoice(@saleId)::text",new{saleId},transaction);
            if(snapshot is not null)
            {
                using var json=JsonDocument.Parse(snapshot);var doc=json.RootElement;var reg=doc.GetProperty("registration_snapshot");var business=doc.GetProperty("business_snapshot");
                invoice.Company.CompanyName=business.GetProperty("trade_name").GetString()??business.GetProperty("legal_name").GetString()!;
                invoice.Company.LegalBusinessName=business.GetProperty("legal_name").GetString();
                invoice.Company.Address=reg.GetProperty("address").GetString();invoice.Company.GstNumber=reg.GetProperty("gstin").GetString();
                invoice.Company.City="";invoice.Company.State=StockFlow.Application.Common.GstRules.States.GetValueOrDefault(reg.GetProperty("state_code").GetString()!,"");
                invoice.Company.PinCode=business.TryGetProperty("pin_code",out var pin) ? pin.GetString()??"" : "";
                // The issued GST source is also an original snapshot, never a current master fallback.
                if(!invoice.HasHistoricalDetails)
                {
                    invoice.CustomerName=doc.GetProperty("party_name").GetString();invoice.CustomerAddress=doc.GetProperty("party_address").GetString();
                    invoice.CustomerGstNumber=doc.GetProperty("party_gstin").GetString();
                }
                invoice.Company.PanNumber=business.GetProperty("pan").GetString();invoice.Company.MobileNumber=business.GetProperty("mobile").GetString();invoice.Company.Email=business.GetProperty("email").GetString();
                invoice.Gst=new(){RegistrationType=reg.GetProperty("registration_type").GetString()!,PlaceOfSupply=doc.GetProperty("place_of_supply").GetString()!,Cgst=doc.GetProperty("cgst").GetDecimal(),Sgst=doc.GetProperty("sgst").GetDecimal(),Utgst=doc.GetProperty("utgst").GetDecimal(),Igst=doc.GetProperty("igst").GetDecimal(),Cess=doc.GetProperty("cess").GetDecimal(),Lines=JsonSerializer.Deserialize<List<StockFlow.Application.DTOs.Gst.GstLine>>(doc.GetProperty("lines").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!,Warnings=JsonSerializer.Deserialize<string[]>(doc.GetProperty("warnings").GetRawText())!};
                invoice.Gst.UnregisteredRecipient=doc.GetProperty("party_gstin").ValueKind==JsonValueKind.Null;
                invoice.Gst.DeliveryAddress=doc.GetProperty("delivery_address").GetString();
            }
        }
        await transaction.CommitAsync();
        return invoice;
    }
}
