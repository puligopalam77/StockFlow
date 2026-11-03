using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class GstChecks
{
    public static async Task Run(bool useInstalledTestTemplate=false)
    {
        Check(GstRules.ValidGstin("27AAPFU0939F1ZV"),"Known valid checksum accepted");
        Check(!GstRules.ValidGstin("27AAPFU0939F1ZA"),"Invalid checksum rejected");
        var database="gst_test_"+Guid.NewGuid().ToString("N");
        var cs=$"Host=127.0.0.1;Port={(useInstalledTestTemplate?55443:55441)};Database={database};Username=postgres";
        await using var admin=new NpgsqlConnection(cs.Replace(database,"postgres"));await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+(useInstalledTestTemplate?" TEMPLATE product_edit_069_test":""));
        try
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
            foreach(var file in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(_=>!useInstalledTestTemplate).Order())
            {if(Path.GetFileName(file).StartsWith("001")||Path.GetFileName(file).StartsWith("006"))continue;await c.ExecuteAsync(await File.ReadAllTextAsync(file));}
            foreach(var file in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Order())
            {
                if(useInstalledTestTemplate && int.Parse(Path.GetFileName(file)[..3])<70)continue;
                if(Path.GetFileName(file).StartsWith("020"))await c.ExecuteAsync("INSERT INTO roles(role_name) VALUES('Admin'); INSERT INTO users(role_id,full_name,username,password_hash) VALUES(1,'GST owner','gstowner','unused'); INSERT INTO categories(category_name) VALUES('General'); INSERT INTO products(category_id,product_name,sku,selling_price,tax_percentage) VALUES(1,'Synthetic clothing','SHIRT',100,18);");
                await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            }
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var stockCount=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
            var repository=new GstRepository(new DbConnectionFactory(config,scope));
            Check((await repository.SettingsAsync()).Available,"GST foundation migration and runtime reads work");
            var business=await repository.SaveBusinessAsync(new(){LegalName="Synthetic retailer",Address="Test street",StateCode="27",PinCode="400001",Pan="AAPFU0939F"});
            var registration=await repository.SaveRegistrationAsync(new(){BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="27",Address="Test street",EffectiveFrom=new(2026,1,1)});
            await repository.SaveMappingAsync(new(){RegistrationId=registration,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=new(2026,1,1)});
            await Reject(()=>repository.SaveMappingAsync(new(){RegistrationId=registration,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=new(2026,2,1)}),"Overlapping mappings rejected");
            await repository.SaveMappingAsync(new(){RegistrationId=registration,BusinessProfileCode="BIKE_ACCESSORIES",StoreId=1,EffectiveFrom=new(2026,1,1)});
            Check((await repository.SettingsAsync()).Mappings.Count()==2,"Independent profiles can share a tax registration");
            await repository.SaveTaxAsync(new(){ProductId=1,HsnSac="610910",Uqc="NOS",TaxRate=18,EffectiveFrom=new(2026,1,1),ReviewReference="Synthetic reviewed classification"});
            await Reject(()=>repository.SaveTaxAsync(new(){ProductId=1,HsnSac="610910",Uqc="NOS",TaxRate=12,EffectiveFrom=new(2026,2,1),ReviewReference="Synthetic new version"}),"Tax version overlap rejected");
            Check((await repository.SettingsAsync()).History.Count()>=5,"Configuration audit history recorded");
            // Synthetic document facts in an isolated clone: no stock posting is performed by GST capture itself.
            async Task<long> Capture(string number,string pos,decimal discount=0,decimal rate=18,int productId=1,long variantId=1)
            {
                await using var tx=await c.BeginTransactionAsync();
                var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,status,created_by,request_id,request_payload,subtotal,tax_amount,discount_amount,total_amount,customer_name_snapshot,invoice_snapshot_version,company_snapshot) VALUES(@number,'COMPLETED',1,@request,'{}',100,@rate,@discount,100+@rate-@discount,'Synthetic GST customer',1,'{}') RETURNING sale_id",new{number,request=Guid.NewGuid(),discount,rate},tx);
                await c.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,discount_amount,tax_percentage,line_total,product_name_snapshot) VALUES(@sale,@productId,@variantId,1,100,0,@rate,100+@rate,'Synthetic goods')",new{sale,rate,productId,variantId},tx);
                var document=await c.ExecuteScalarAsync<long>("SELECT public.sf_gst_capture_sale(@sale,@pos)",new{sale,pos},tx);await tx.CommitAsync();return document;
            }
            var interstate=await Capture("GST-TEST-1","29");
            var intrastate=await Capture("GST-TEST-2","27",5);
            var inter=await repository.DocumentAsync(interstate);var intra=await repository.DocumentAsync(intrastate);
            Check(inter!.Igst==18 && inter.Cgst==0,"Interstate tax snapshot uses posted tax as IGST");
            Check(intra!.Cgst==9&&intra.Sgst==9&&intra.Total==113&&intra.Warnings.Contains("AFTER_TAX_INVOICE_DISCOUNT_REVIEW"),"Intrastate components and existing after-tax discount preserved with review warning");
            var date=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            var report=await repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date});
            Check(report.Count==2&&report.Summary.OutputGst==36,"Multi-document report totals do not duplicate line/header amounts");
            var hsn=await repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date,Report="hsn"});
            Check(hsn.Hsn.Sum(x=>x.Quantity)==2&&hsn.Hsn.Sum(x=>x.TaxableValue)==200,"HSN summary aggregates captured line facts");
            await Reject(()=>Capture("GST-TEST-BAD",""),"Missing actual place of supply rolls back the entire source transaction");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM sales WHERE invoice_number='GST-TEST-BAD'")==0,"Failed GST capture leaves no invoice");
            await Reject(()=>c.ExecuteAsync("UPDATE gst_documents SET cgst=0 WHERE document_id=@interstate",new{interstate}),"Captured tax facts are immutable");
            await c.ExecuteAsync("UPDATE gst_legal_businesses SET legal_name='Updated master name' WHERE business_id=@business",new{business});
            Check((await repository.DocumentAsync(interstate))!.BusinessSnapshot.Contains("Synthetic retailer"),"Current master changes cannot rewrite historical legal identity");
            await repository.ReviewAsync(new(interstate,"ACCOUNTANT_REVIEWED","Synthetic practitioner review"));
            Check((await repository.DocumentAsync(interstate))!.ReviewStatus=="ACCOUNTANT_REVIEWED","Append-only review status is visible");
            var exported=await repository.ExportAsync(new(){RegistrationId=registration,From=date,To=date});
            Check(System.Text.Encoding.UTF8.GetString(exported.Content).Contains("NOT FILED"),"CSV is explicitly an accountant working export");
            var supplierId=await c.ExecuteScalarAsync<int>("INSERT INTO suppliers(supplier_name,gst_number) VALUES('Synthetic GST supplier','27AAPFU0939F1ZV') RETURNING supplier_id");
            var purchaseId=await c.ExecuteScalarAsync<long>("INSERT INTO purchases(supplier_id,purchase_number,purchase_date,created_by) VALUES(@supplierId,'GST-PUR-TEST',CURRENT_DATE,1) RETURNING purchase_id",new{supplierId});
            var source=new GstRecordRequest{RequestId=Guid.NewGuid(),RegistrationId=registration,DocumentNumber="SUP-GST-01",DocumentDate=date,SupplierId=supplierId,PurchaseIds=[purchaseId],SupplierStateCode="27",PlaceOfSupply="29",EvidenceReference="Synthetic supplier document",Lines=[new(){ProductId=1,Description="Synthetic goods",HsnSac="610910",Quantity=1,TaxRate=18,TaxableValue=100,Igst=18,Total=118}]};
            var purchaseDoc=await repository.RecordAsync(source);
            Check(await repository.RecordAsync(source)==purchaseDoc,"Identical supplier-invoice retry returns the original tax document");
            source.RequestId=Guid.NewGuid();await Reject(()=>repository.RecordAsync(source),"Supplier invoice cannot be reported twice under another request ID");
            var credit=new GstRecordRequest{RequestId=Guid.NewGuid(),RegistrationId=registration,Kind="CREDIT_NOTE",Direction="SALES",DocumentNumber="GST-CN-01",DocumentDate=date,ParentDocumentId=interstate,PlaceOfSupply="29",EvidenceReference="Synthetic reviewed credit note",Reason="Synthetic partial return",Lines=[new(){ProductId=1,Description="Synthetic goods",HsnSac="610910",Quantity=0.5m,TaxRate=18,TaxableValue=50,Igst=9,Total=59}]};
            await repository.RecordAsync(credit);
            var adjusted=await repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date});
            Check(adjusted.Summary.OutputGst==27&&adjusted.Summary.RecordedPurchaseGst==18,"Linked credit note adjusts output separately from recorded input GST");
            Check((await repository.ExportHistoryAsync()).Any()&&(await repository.ReviewHistoryAsync(interstate)).Any(),"Export and review histories remain available");
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,a.action_code='VIEW' FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='GST_REPORTS' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=excluded.is_allowed");
            var limited=new GstRepository(new DbConnectionFactory(config,new BusinessScope{TenantId=1,UserId=2,StoreId=1,LocationId=1}));
            await Reject(()=>limited.ReportAsync(new(){RegistrationId=registration,From=date,To=date,RegistrationWide=true}),"Ordinary GST viewer cannot request registration-wide data");
            await Reject(()=>limited.RecordAsync(source),"Read-only GST permission cannot record supplier invoices");
            await Reject(()=>repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date,Size=11}),"Unsupported shared page size rejected");
            var foreignScope=new BusinessScope{TenantId=999,UserId=1,StoreId=1,LocationId=1};
            await Reject(()=>new GstRepository(new DbConnectionFactory(config,foreignScope)).SettingsAsync(),"Forged tenant cannot read GST settings");
            Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stockCount,"GST settings do not post stock");
            var sales=new SalesRepository(new DbConnectionFactory(config,scope));
            var testCustomer=await c.ExecuteScalarAsync<int>("INSERT INTO customers(customer_name,mobile_number) VALUES('Synthetic GST checkout customer','8000999901') RETURNING customer_id");
            var saleRequest=new CreateSaleRequest{RequestId=Guid.NewGuid(),LocationId=1,CustomerId=testCustomer,PricesIncludeTax=false,GstPlaceOfSupplyStateCode="29",AdjustmentReason="Synthetic authorized price adjustment",CanOverridePrice=true,CanDiscount=true,CanApproveDiscount=true,Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=100}],Payments=[new(){PaymentMode="CASH",Amount=118}]};
            var quote=await sales.QuoteAsync(saleRequest);
            Check(quote.TaxAmount==18&&quote.TotalAmount==118,"Existing SaleRules calculator consumes the effective GST rate");
            var completed=await sales.CreateSaleAsync(saleRequest,1);
            Check(await sales.CreateSaleAsync(saleRequest,1)==completed,"Completed POS retry preserves invoice idempotency with GST capture");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM gst_documents WHERE sale_id=@completed",new{completed})==1,"POS finalization creates exactly one tax snapshot");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@completed AND transaction_type='SALE'",new{completed})==1,"GST integration preserves one stock deduction per completed sale");
            var invoice=await new InvoiceRepository(new DbConnectionFactory(config,scope)).GetInvoiceAsync(completed);
            Check(invoice!.Gst?.Igst==18&&invoice.Gst.Lines.Single().HsnSac=="610910","Invoice reads immutable tax components and HSN snapshot");
            await c.ExecuteAsync("""
                INSERT INTO gst_documents SELECT (jsonb_populate_record(NULL::gst_documents,to_jsonb(d)||jsonb_build_object(
                'document_id',nextval('gst_documents_document_id_seq'),'business_profile_code','BIKE_ACCESSORIES','document_number','BIKE-GST-TEST',
                'sale_id',NULL,'request_id',gen_random_uuid(),'request_payload','{}'::jsonb))).*
                FROM gst_documents d WHERE document_id=@interstate
                """,new{interstate});
            var local=await repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date});
            var wide=await repository.ReportAsync(new(){RegistrationId=registration,From=date,To=date,RegistrationWide=true});
            Check(wide.Count==local.Count+1 && local.Documents.All(d=>d.BusinessProfileCode=="ELECTRICAL"),"Registration-wide tax permission aggregates mapped profiles without merging ordinary shop reports");
            await Reject(()=>repository.SaveRegistrationAsync(new(){RegistrationId=registration,BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="29",Address="Changed address",EffectiveFrom=new(2026,1,1)}),"Used registration cannot be reassigned to another state");
            var secondStore=await c.ExecuteScalarAsync<long>("INSERT INTO stores(store_code,store_name) VALUES('GST-TWO','Synthetic second shop') RETURNING store_id");
            var secondLocation=await c.ExecuteScalarAsync<long>("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@secondStore,'Synthetic stock','SALES_FLOOR') RETURNING location_id",new{secondStore});
            var secondGstin="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(ch=>"29AAPFU0939F1Z"+ch).Single(GstRules.ValidGstin);
            var secondReg=await repository.SaveRegistrationAsync(new(){BusinessId=business,Gstin=secondGstin,StateCode="29",Address="Synthetic Karnataka address",EffectiveFrom=new(2026,1,1)});
            await repository.SaveMappingAsync(new(){RegistrationId=secondReg,BusinessProfileCode="ELECTRICAL",StoreId=secondStore,EffectiveFrom=new(2026,1,1)});
            Check((await repository.ReportAsync(new(){RegistrationId=secondReg,From=date,To=date,RegistrationWide=true})).Count==0,"Different GSTIN never inherits the first registration's figures");
            var thirdStore=await c.ExecuteScalarAsync<long>("INSERT INTO stores(store_code,store_name) VALUES('GST-UNREG','Synthetic unregistered shop') RETURNING store_id");
            var thirdLocation=await c.ExecuteScalarAsync<long>("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@thirdStore,'Synthetic stock','SALES_FLOOR') RETURNING location_id",new{thirdStore});
            var unregistered=await repository.SaveRegistrationAsync(new(){BusinessId=business,RegistrationType="UNREGISTERED",StateCode="27",Address="Synthetic unregistered address",EffectiveFrom=new(2026,1,1)});
            await repository.SaveMappingAsync(new(){RegistrationId=unregistered,BusinessProfileCode="ELECTRICAL",StoreId=thirdStore,EffectiveFrom=new(2026,1,1)});
            await c.ExecuteAsync("SELECT set_config('app.store_id',@store,false),set_config('app.location_id',@location,false)",new{store=thirdStore.ToString(),location=thirdLocation.ToString()});
            scope.StoreId=thirdStore;scope.LocationId=thirdLocation;
            // Each shop explicitly classifies its catalog; a shared profile does not inherit new mappings.
            await repository.SaveTaxAsync(new(){ProductId=1,HsnSac="610910",Uqc="NOS",TaxRate=18,EffectiveFrom=new(2026,1,1),ReviewReference="Synthetic unregistered shop classification"});
            Check(await c.ExecuteScalarAsync<decimal>("SELECT public.sf_gst_product_rate(1)")==0,"Unregistered issuer does not collect a product's intrinsic GST rate");
            var unregisteredDoc=await Capture("GST-UNREG-1","27",rate:0);
            Check((await repository.DocumentAsync(unregisteredDoc))!.TotalGst==0,"Unregistered source snapshot records no collected GST");
            await repository.SaveTaxAsync(new(){ProductId=2,HsnSac="490110",Uqc="NOS",Treatment="EXEMPT",TaxRate=0,EffectiveFrom=new(2026,1,1),ReviewReference="Synthetic exempt classification"});
            await Capture("GST-EXEMPT-1","27",rate:0,productId:2,variantId:2);
            var unregisteredReport=await repository.ReportAsync(new(){RegistrationId=unregistered,From=date,To=date,RegistrationWide=true});
            Check(unregisteredReport.Summary.TaxableSales==100&&unregisteredReport.Summary.OtherSupplyValue==100,"Exempt supply value does not inflate the taxable-sales overview");
            await c.ExecuteAsync("UPDATE gst_shop_mappings SET status='INACTIVE' WHERE registration_id=@unregistered",new{unregistered});
            await Reject(()=>c.ExecuteScalarAsync<string>("SELECT public.sf_gst_sale_context()::text"),"Expired or inactive mapping history cannot silently fall back to legacy billing");
            Console.WriteLine("GST foundation checks passed.");
        }
        finally {NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    private static async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception ex)when(ex is ValidationException or PostgresException or UnauthorizedAccessException){Console.WriteLine("PASS: "+name);return;}throw new Exception("Expected rejection: "+name);}
}
