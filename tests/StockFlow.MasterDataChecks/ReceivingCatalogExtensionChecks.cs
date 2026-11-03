using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.MasterDataChecks;

internal static class ReceivingCatalogExtensionChecks
{
    public static Task Run()
    {
        var request=new CreatePurchaseRequest
        {
            SupplierId=1,PurchaseDate=new DateTime(2026,10,4),SupplierInvoiceDate=new DateTime(2026,10,3),ReceivedDate=new DateTime(2026,10,4),
            FreightAmount=50,OtherLandedCost=10,CompleteImmediately=true,
            InvoiceAttachmentName="invoice.pdf",InvoiceAttachmentContentType="application/pdf",InvoiceAttachmentData=[1,2,3],
            Items=[new(){ProductId=1,Quantity=10,ReceivedQuantity=8,DamagedQuantity=2,PurchasePrice=100,TaxPercentage=18,UnitOfMeasure="BOX",PackQuantity=5,Mrp=1500,BatchNumber="LOT-1",ManufacturingDate=new DateTime(2026,9,1),ExpiryDate=new DateTime(2028,9,1)}]
        };
        var totals=PurchaseRules.ValidateAndCalculate(request);
        Check(totals.Subtotal==1000&&totals.Tax==180&&totals.Total==1240,"landed costs are included in supplier payable total");
        PurchaseRules.ValidateSerials(request.Items[0],false,true);
        var invalid=Clone(request);invalid.Items[0].DamagedQuantity=3;
        Reject(()=>PurchaseRules.ValidateAndCalculate(invalid),"received plus damaged cannot exceed expected quantity");
        invalid=Clone(request);invalid.Items[0].ExpiryDate=new DateTime(2026,8,1);
        Reject(()=>PurchaseRules.ValidateAndCalculate(invalid),"expiry cannot precede manufacture");
        invalid=Clone(request);invalid.InvoiceAttachmentContentType="application/x-msdownload";
        Reject(()=>PurchaseRules.ValidateAndCalculate(invalid),"invoice attachment type is restricted");

        var root=Root();
        var migration=File.ReadAllText(Path.Combine(root,"database/migrations/030_receiving_catalog_extensions.sql"));
        Check(migration.Contains("supplier_product_catalog")&&migration.Contains("FORCE ROW LEVEL SECURITY")&&migration.Contains("pending_damaged_quantity")&&migration.Contains("product_image_data"),"migration contains tenant catalogue, product images and resolved receiving quantities");
        var page=File.ReadAllText(Path.Combine(root,"src/StockFlow.Web/Pages/Purchases/Create.cshtml"));
        Check(page.Contains("supplierInvoiceDate")&&page.Contains("invoiceAttachment")&&page.Contains("freightAmount"),"receipt screen captures commercial dates, invoice and landed costs");
        var script=File.ReadAllText(Path.Combine(root,"src/StockFlow.Web/wwwroot/js/purchase-create.js"));
        Check(script.Contains("location.assign('/Products')")&&script.Contains("damagedSerialNumbers")&&script.Contains("supplierProductCode"),"scan flow navigates to existing registration and posts detailed receipt lines");
        Console.WriteLine("Receiving/catalog extension checks passed. No database was accessed.");
        return Task.CompletedTask;
    }
    static CreatePurchaseRequest Clone(CreatePurchaseRequest value)=>System.Text.Json.JsonSerializer.Deserialize<CreatePurchaseRequest>(System.Text.Json.JsonSerializer.Serialize(value))!;
    static void Reject(Action action,string message){try{action();}catch(ValidationException){Console.WriteLine("PASS: "+message);return;}throw new Exception("FAIL: "+message);}
    static string Root(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"StockFlow.sln")))directory=directory.Parent;return directory?.FullName??throw new Exception("Solution root not found.");}
    static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
}
