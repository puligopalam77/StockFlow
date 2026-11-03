using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class InvoiceDatabaseChecks
{
    public static async Task Run(DbConnectionFactory factory,NpgsqlConnection connection)
    {
        var settings = new CompanySettingsRepository(factory);
        await settings.SaveAsync(new() { CompanyName="Original Test Store",Address="Original store address",MobileNumber="0123456789",InvoiceFooter="Original footer" },1);
        await connection.ExecuteAsync("""
            INSERT INTO brands(brand_name) VALUES('Original Brand');
            UPDATE products SET brand_id=(SELECT brand_id FROM brands WHERE brand_name='Original Brand'),model='Original Model',warranty_months=12 WHERE product_id=1;
            UPDATE customers SET customer_name='Invoice Customer',mobile_number='0098765432',address='Original customer address',gst_number=NULL WHERE customer_id=1;
            """);
        var sales = new SalesRepository(factory); var invoices = new InvoiceRepository(factory);
        var request = new CreateSaleRequest { RequestId=Guid.NewGuid(),CustomerId=1,Items=[new() { ProductId=1,ProductUnitId=2,Quantity=1,SellingPrice=100 },new() { ProductId=2,Quantity=2,SellingPrice=10 }] };
        var quote = await sales.QuoteAsync(request); request.Payments=[new() { PaymentMode="CASH",Amount=100 },new() { PaymentMode="UPI",Amount=quote.TotalAmount-100,ReferenceNumber="invoice-test" }];
        var id = await sales.CreateSaleAsync(request,1);
        var original = (await invoices.GetInvoiceAsync(id))!;
        Check(original.Company.CompanyName=="Original Test Store" && original.CustomerMobile=="0098765432" && original.Items.Count==2 && original.Payments.Count==2,"complete invoice data");
        await settings.SaveAsync(new() { CompanyName="Changed Store",GstNumber="LATER-GST",InvoiceFooter="Changed footer" },1);
        await connection.ExecuteAsync("""
            UPDATE customers SET customer_name='Changed Customer',mobile_number='CHANGED',address='Changed address',gst_number='LATER-CUSTOMER-GST' WHERE customer_id=1;
            UPDATE brands SET brand_name='Changed Brand' WHERE brand_name='Original Brand';
            UPDATE products SET model='Changed model',selling_price=555,tax_percentage=5,warranty_months=60 WHERE product_id=1;
            """);
        var history = (await invoices.GetInvoiceAsync(id))!;
        Check(history.HasHistoricalDetails && history.CustomerName=="Invoice Customer" && history.CustomerAddress=="Original customer address" && history.CustomerGstNumber is null,"customer snapshot preserves blanks and original details");
        Check(history.Company.CompanyName=="Original Test Store" && history.Company.GstNumber is null && history.Company.InvoiceFooter=="Original footer","company snapshot survives settings edits");
        Check(history.Items[0].BrandName=="Original Brand" && history.Items[0].Model=="Original Model" && history.Items[0].WarrantyMonths==12 && history.Items[0].SellingPrice==100 && history.Items[0].TaxPercentage==18,"product descriptions, warranty term and transaction prices remain historical");
        Check(history.Items[0].WarrantyEndDate.HasValue && history.Items[1].SerialNumber is null,"serialized warranty and quantity-only item");
        Check((await sales.GetAllAsync("0098765432",null,0,DateTime.Today,DateTime.Today)).Any(s=>s.SaleId==id),"invoice search uses historical mobile and inclusive dates");
        Check(!(await sales.GetAllAsync(null,null,0,DateTime.Today.AddDays(1),null)).Any(),"date filter excludes earlier invoices");
        var before = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_logs");
        await invoices.GetInvoiceAsync(id); await invoices.GetInvoiceAsync(id);
        Check(await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_logs")==before,"invoice reads create no audit/writes");
        await connection.ExecuteAsync("UPDATE sales SET status='CANCELLED' WHERE sale_id=@Id",new { Id=id });
        Check((await invoices.GetInvoiceAsync(id))!.Status=="CANCELLED","cancelled invoice remains available");
        await connection.ExecuteAsync("UPDATE sales SET status='COMPLETED' WHERE sale_id=@Id",new { Id=id });
        var legacy = await connection.ExecuteScalarAsync<long>("INSERT INTO sales(customer_id,invoice_number,created_by,status) VALUES(1,'LEGACY-TEST',1,'COMPLETED') RETURNING sale_id;");
        Check(!(await invoices.GetInvoiceAsync(legacy))!.HasHistoricalDetails,"legacy invoice fallback explicitly identified");
        await connection.ExecuteAsync("UPDATE sales SET status='DRAFT' WHERE sale_id=@Id",new { Id=legacy });
        Check(await invoices.GetInvoiceAsync(legacy) is null && await invoices.GetInvoiceAsync(-1) is null,"draft and missing invoices are not issued");
        await connection.ExecuteAsync("UPDATE products SET selling_price=100,tax_percentage=18,warranty_months=12 WHERE product_id=1;");
        // A realistic long invoice for multi-page print testing, using newly seeded disposable stock.
        var product = await connection.ExecuteScalarAsync<int>("INSERT INTO products(category_id,product_name,selling_price,tax_percentage) VALUES(1,'Long invoice product with a detailed model description',12,18) RETURNING product_id;");
        await connection.ExecuteAsync("INSERT INTO inventory_transactions(product_id,transaction_type,quantity,created_by) VALUES(@Id,'PURCHASE',100,1);",new { Id=product });
        var longRequest = new CreateSaleRequest { RequestId=Guid.NewGuid(),Items=Enumerable.Range(0,100).Select(_=>new CreateSaleItemRequest { ProductId=product,Quantity=1,SellingPrice=12 }).ToList() };
        longRequest.Payments=[new() { PaymentMode="CASH",Amount=(await sales.QuoteAsync(longRequest)).TotalAmount }];
        var longId = await sales.CreateSaleAsync(longRequest,1);
        Directory.CreateDirectory("tests/bin");
        await File.WriteAllTextAsync("tests/bin/phase10-invoice-ids.txt",$"{id}\n{longId}");
        Console.WriteLine("PASS: invoice PostgreSQL mappings, preserved customer/company/product snapshots, search, legacy/draft/cancelled behavior and 100-line invoice fixture.");
    }
    private static void Check(bool ok,string label) { if(!ok) throw new Exception("FAIL: "+label); }
}
