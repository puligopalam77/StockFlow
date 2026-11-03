using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

static class ShopOperationsDatabaseChecks
{
    public static async Task Run(NpgsqlConnection c,DbConnectionFactory factory)
    {
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        var product=await c.ExecuteScalarAsync<int>("INSERT INTO products(tenant_id,category_id,product_name,sku,selling_price) VALUES(1,1,'Operations fixture','OPS-FIXTURE',20) RETURNING product_id");
        var variant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@product",new{product});
        await c.ExecuteAsync("INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,transaction_type,quantity,reference_type,created_by) VALUES(1,1,@product,@variant,'PURCHASE',10,'FIXTURE',1)",new{product,variant});
        var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(tenant_id,location_id,created_by,invoice_number,subtotal,total_amount,status) VALUES(1,1,1,'OPS-SALE',100,100,'COMPLETED') RETURNING sale_id");
        var item=await c.ExecuteScalarAsync<long>("INSERT INTO sale_items(tenant_id,location_id,sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES(1,1,@sale,@product,@variant,5,20,100) RETURNING sale_item_id",new{sale,product,variant});
        await c.ExecuteAsync("INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,transaction_type,quantity,reference_type,reference_id,created_by) VALUES(1,1,@product,@variant,'SALE',5,'SALE',@sale,1)",new{product,variant,sale});
        var repo=new OperationsRepository(factory);var payment=new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),Amount=40,PaymentMode="CASH"};
        var paymentId=await repo.AddCustomerPaymentAsync(sale,payment,1);Check(await repo.AddCustomerPaymentAsync(sale,payment,1)==paymentId,"customer collection retry records one payment");
        var returned=new SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),RefundMode="CREDIT_ADJUSTMENT",Notes="Customer returned sealed units",Items=[new(){SaleItemId=item,Quantity=2,Disposition="RESTOCK"}]};
        var returnId=await repo.ReturnSaleAsync(sale,returned,1);Check(await repo.ReturnSaleAsync(sale,returned,1)==returnId,"sale return retry posts stock once");
        var account=await repo.SaleAsync(sale);Check(account.ReturnedTotal==40&&account.PaidTotal==40&&account.Outstanding==20,"returns and later collections reconcile invoice balance");
        try{await repo.ReturnSaleAsync(sale,new(){LocationId=1,RequestId=Guid.NewGuid(),RefundMode="NONE",Notes="Invalid over return",Items=[new(){SaleItemId=item,Quantity=4}]},1);throw new Exception("over return accepted");}catch(ValidationException){Console.WriteLine("PASS cumulative returned quantity is blocked");}
        await repo.AddCustomerPaymentAsync(sale,new(){LocationId=1,RequestId=Guid.NewGuid(),Amount=20,PaymentMode="UPI"},1);

        var supplier=await c.ExecuteScalarAsync<int?>("SELECT supplier_id FROM suppliers WHERE tenant_id=1 ORDER BY supplier_id LIMIT 1")
            ??await c.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,supplier_name,mobile_number) VALUES(1,'Operations supplier','9000000000') RETURNING supplier_id");
        var purchase=await c.ExecuteScalarAsync<long>("INSERT INTO purchases(tenant_id,location_id,supplier_id,purchase_number,purchase_date,received_date,subtotal,total_amount,status,created_by) VALUES(1,1,@supplier,'OPS-PURCHASE',CURRENT_DATE,CURRENT_DATE,100,100,'COMPLETED',1) RETURNING purchase_id",new{supplier});
        var purchaseItem=await c.ExecuteScalarAsync<long>("INSERT INTO purchase_items(tenant_id,location_id,purchase_id,product_id,variant_id,quantity,received_quantity,purchase_price,line_total,is_serial_tracked) VALUES(1,1,@purchase,@product,@variant,5,5,20,100,false) RETURNING purchase_item_id",new{purchase,product,variant});
        await repo.AddSupplierPaymentAsync(purchase,new(){LocationId=1,RequestId=Guid.NewGuid(),Amount=30,PaymentMode="BANK"},1);
        await repo.ReturnPurchaseAsync(purchase,new(){LocationId=1,RequestId=Guid.NewGuid(),Notes="Supplier accepted unopened unit",Items=[new(){PurchaseItemId=purchaseItem,Quantity=1,Reason="Wrong specification"}]},1);
        var payable=await repo.PurchaseAsync(purchase);Check(payable.ReturnedTotal==20&&payable.PaidTotal==30&&payable.Outstanding==50,"supplier payment and purchase return reconcile payable");
        var before=await c.ExecuteScalarAsync<int>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@variant",new{variant});
        var count=await repo.PostStockCountAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),Notes="End of day shelf count",Items=[new(){ProductId=product,VariantId=variant,CountedQuantity=before-1,Remarks="One unit missing"}]},1);
        Check(await c.ExecuteScalarAsync<int>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@variant",new{variant})==before-1&&count>0,"physical stock count posts exactly the approved variance");
    }
}
