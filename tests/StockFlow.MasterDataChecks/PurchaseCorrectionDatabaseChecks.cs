using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class PurchaseCorrectionDatabaseChecks
{
 public static async Task Run()
 {
  var cs=Environment.GetEnvironmentVariable("STOCKFLOW_PURCHASE_CORRECTION_TEST_CONNECTION")??throw new Exception("Supply an isolated correction test database.");
  var settings=new NpgsqlConnectionStringBuilder(cs);if(settings.Host!="127.0.0.1"||settings.Port!=55439||settings.Database!="purchase_corrections_test")throw new Exception("Only isolated purchase_corrections_test on local port 55439 is allowed.");
  await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
  await c.ExecuteAsync("""
   CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT current_setting('app.tenant_id')::bigint $$;
   CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT current_setting('app.location_id')::bigint $$;
   CREATE FUNCTION sf_actor() RETURNS integer LANGUAGE sql STABLE AS $$ SELECT current_setting('app.user_id')::integer $$;
   CREATE FUNCTION sf_visible_location(bigint) RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT $1=sf_location() $$;
   CREATE TABLE users(tenant_id bigint,user_id integer,username text,full_name text,PRIMARY KEY(tenant_id,user_id));INSERT INTO users VALUES(1,1,'admin','Admin'),(2,2,'other','Other');
   CREATE TABLE barcode_print_requests(sheet_template jsonb);
   CREATE TABLE categories(category_id integer PRIMARY KEY,business_profile_code text);INSERT INTO categories VALUES(1,'ELECTRICAL');
   CREATE TABLE suppliers(supplier_id integer PRIMARY KEY,supplier_name text,supplier_code text,gst_number text,address text,is_active boolean DEFAULT true);INSERT INTO suppliers VALUES(1,'Supplier','SUP',null,null,true);
   CREATE TABLE products(product_id integer PRIMARY KEY,product_name text,is_serial_tracked boolean DEFAULT false,is_active boolean DEFAULT true,category_id integer DEFAULT 1,model text);INSERT INTO products(product_id,product_name) VALUES(1,'Helmet');
   CREATE TABLE product_variants(variant_id bigint PRIMARY KEY,product_id integer,variant_name text,is_serial_tracked boolean DEFAULT false,is_active boolean DEFAULT true,tax_percentage numeric DEFAULT 0,warranty_months integer DEFAULT 0,selling_price numeric DEFAULT 100,is_default boolean DEFAULT true);INSERT INTO product_variants(variant_id,product_id,variant_name) VALUES(1,1,'M');
   CREATE TABLE purchases(tenant_id bigint DEFAULT sf_tenant(),location_id bigint DEFAULT sf_location(),purchase_id bigserial PRIMARY KEY,supplier_id integer DEFAULT 1,purchase_number text DEFAULT 'PUR',supplier_invoice_number text,supplier_invoice_date date,purchase_date date DEFAULT CURRENT_DATE,received_date date,
    supplier_snapshot_at timestamptz,supplier_name_snapshot text,supplier_code_snapshot text,supplier_gst_snapshot text,supplier_address_snapshot text,payment_terms_days integer DEFAULT 0,
    subtotal numeric DEFAULT 1000,tax_amount numeric DEFAULT 0,discount_amount numeric DEFAULT 0,total_amount numeric DEFAULT 1000,freight_amount numeric DEFAULT 0,other_landed_cost numeric DEFAULT 0,
    invoice_attachment_name text,invoice_attachment_content_type text,invoice_attachment_data bytea,status text DEFAULT 'COMPLETED',notes text,created_by integer DEFAULT 1,created_at timestamptz DEFAULT now(),UNIQUE(tenant_id,purchase_id));
   CREATE TABLE purchase_items(tenant_id bigint DEFAULT sf_tenant(),location_id bigint DEFAULT sf_location(),purchase_item_id bigserial PRIMARY KEY,purchase_id bigint,product_id integer DEFAULT 1,variant_id bigint DEFAULT 1,variant_name_snapshot text DEFAULT 'M',quantity integer DEFAULT 10,received_quantity integer DEFAULT 10,damaged_quantity integer DEFAULT 0,
    pending_received_quantity integer DEFAULT 0,pending_damaged_quantity integer DEFAULT 0,serial_numbers text[] DEFAULT '{}',damaged_serial_numbers text[] DEFAULT '{}',pending_serial_numbers text[] DEFAULT '{}',pending_damaged_serial_numbers text[] DEFAULT '{}',purchase_price numeric DEFAULT 100,tax_percentage numeric DEFAULT 0,discount_amount numeric DEFAULT 0,line_total numeric DEFAULT 1000,
    is_serial_tracked boolean DEFAULT false,unit_of_measure text DEFAULT 'PIECE',pack_quantity integer DEFAULT 1,mrp numeric DEFAULT 150,supplier_product_code text,batch_number text,manufacturing_date date,expiry_date date);
   CREATE TABLE supplier_payments(tenant_id bigint DEFAULT sf_tenant(),purchase_id bigint,amount numeric);
   CREATE TABLE purchase_returns(tenant_id bigint DEFAULT sf_tenant(),purchase_id bigint,return_total numeric);
   CREATE TABLE purchase_return_items(purchase_item_id bigint,quantity integer);
   CREATE TABLE purchase_receipts(purchase_id bigint);
   CREATE TABLE inventory_transactions(inventory_transaction_id bigserial PRIMARY KEY,product_id integer,variant_id bigint,product_unit_id bigint,transaction_type text,quantity integer,reference_type text,reference_id bigint,remarks text,created_by integer,created_at timestamptz DEFAULT now());
   CREATE TABLE product_units(product_unit_id bigserial PRIMARY KEY,product_id integer,variant_id bigint,serial_number text UNIQUE,unit_status text,purchase_id bigint,purchase_item_id bigint,updated_at timestamptz);
   CREATE TABLE audit_logs(audit_log_id bigserial PRIMARY KEY,user_id integer,module_name text,action_name text,table_name text,record_id bigint,new_values jsonb,reason text);
   CREATE VIEW vw_variant_current_stock AS SELECT 1::bigint variant_id,coalesce(sum(CASE WHEN transaction_type IN('SALE','ADJUSTMENT_OUT','DAMAGED') THEN -quantity ELSE quantity END),0)::bigint current_stock FROM inventory_transactions;
   GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO stockflow_runtime;GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO stockflow_runtime;
   ALTER TABLE purchases ENABLE ROW LEVEL SECURITY;ALTER TABLE purchases FORCE ROW LEVEL SECURITY;
   CREATE POLICY scope ON purchases USING(tenant_id=sf_tenant() AND location_id=sf_location()) WITH CHECK(tenant_id=sf_tenant() AND location_id=sf_location());
   INSERT INTO purchases(tenant_id,location_id,purchase_id) VALUES(1,1,1),(1,2,2),(2,1,3);
   INSERT INTO purchase_items(tenant_id,location_id,purchase_id) VALUES(1,1,1);
   INSERT INTO inventory_transactions(product_id,variant_id,transaction_type,quantity) VALUES(1,1,'PURCHASE',10);
   """);
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
  await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/063_purchase_corrections.sql")));
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1};var f=new DbConnectionFactory(config,scope);var repo=new PurchaseRepository(f);var inventory=new InventoryRepository(f);
  var request=new PurchaseChangeRequest{RequestId=Guid.NewGuid(),ExpectedVersion=0,LocationId=1,Reason="Invoice correction",SupplierInvoiceNumber="INV-NEW",Notes="Checked invoice"};
  var change=await repo.ChangeAsync(1,request,1);Check(change>0,"safe details correction saved");Check((await repo.GetByIdAsync(1))!.EditVersion==1,"version advances atomically");Check(await repo.ChangeAsync(1,request,1)==change,"identical retry replays one change");
  await Reject(()=>repo.ChangeAsync(1,new(){RequestId=Guid.NewGuid(),ExpectedVersion=0,Reason="Stale edit",SupplierInvoiceNumber="OLD"},1),"stale edit rejected");
  await Reject(()=>repo.ChangeAsync(2,new(){RequestId=Guid.NewGuid(),ExpectedVersion=0,Reason="Other location"},1),"other location rejected");await Reject(()=>repo.ChangeAsync(3,new(){RequestId=Guid.NewGuid(),ExpectedVersion=0,Reason="Other tenant"},1),"other tenant rejected");
  request=new(){RequestId=Guid.NewGuid(),ExpectedVersion=1,Kind="FINANCIAL",Reason="Supplier price correction",Items=[new(){PurchaseItemId=1,Quantity=10,PurchasePrice=90,TaxPercentage=0}]};
  await repo.ChangeAsync(1,request,1);Check((await repo.GetByIdAsync(1))!.TotalAmount==900,"financial correction recalculates purchase total");var history=await repo.HistoryAsync(1);Check(history[0].AmountDelta==-100&&history[0].OldValues.GetProperty("details").GetProperty("total_amount").GetDecimal()==1000,"before/after values and adjustment delta retained");
  await c.ExecuteAsync("INSERT INTO supplier_payments VALUES(1,1,850)");request.RequestId=Guid.NewGuid();request.ExpectedVersion=2;request.Items[0].PurchasePrice=80;await Reject(()=>repo.ChangeAsync(1,request,1),"total below paid rejected without history");Check((await repo.HistoryAsync(1)).Count==2,"rejected correction rolled back");
  await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,transaction_type,quantity) VALUES(1,1,'SALE',9)");request.Items[0].PurchasePrice=95;await Reject(()=>repo.ChangeAsync(1,request,1),"outbound stock blocks valuation rewrite");
  var stock=new StockAdjustmentRequest{PurchaseId=1,PurchaseItemId=1,ExpectedVersion=2,RequestId=Guid.NewGuid(),ProductId=1,VariantId=1,TransactionType="ADJUSTMENT_OUT",Quantity=2,Reason="Incorrect original receipt count",LocationId=1};await Reject(()=>inventory.AdjustAsync(stock,1),"sold stock cannot be removed again");
  stock.Quantity=1;var adjustment=await inventory.AdjustAsync(stock,1);Check(await inventory.AdjustAsync(stock,1)==adjustment,"stock adjustment retry does not duplicate movement");Check(await c.ExecuteScalarAsync<int>("SELECT received_quantity FROM purchase_items WHERE purchase_item_id=1")==10,"original received quantity retained");Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock")==0,"stock correction uses available ledger");Check((await repo.HistoryAsync(1))[0].AdjustmentId==adjustment,"stock adjustment linked to edit history");
  await c.ExecuteAsync("UPDATE purchases SET status='CANCELLED' WHERE purchase_id=1");await Reject(()=>repo.ChangeAsync(1,new(){RequestId=Guid.NewGuid(),ExpectedVersion=4,Reason="Cancelled edit"},1),"cancelled purchase view only");
  await c.ExecuteAsync("INSERT INTO purchases(tenant_id,location_id,purchase_id,status) VALUES(1,1,4,'PARTIALLY_RECEIVED'),(1,1,5,'DRAFT'); INSERT INTO purchase_items(tenant_id,location_id,purchase_id,received_quantity) VALUES(1,1,4,4); INSERT INTO purchase_items(tenant_id,location_id,purchase_id,quantity,received_quantity,line_total) VALUES(1,1,5,2,0,200)");
  var partialItem=await c.ExecuteScalarAsync<long>("SELECT purchase_item_id FROM purchase_items WHERE purchase_id=4");
  var order=new PurchaseChangeRequest{RequestId=Guid.NewGuid(),ExpectedVersion=0,Kind="ORDER",Reason="Correct remaining order",Items=[new(){PurchaseItemId=partialItem,Quantity=3,PurchasePrice=100}]};
  await Reject(()=>repo.ChangeAsync(4,order,1),"partial order cannot be reduced below received quantity");
  order.Items[0].Quantity=6;await repo.ChangeAsync(4,order,1);Check((await repo.GetByIdAsync(4))!.Items[0].PurchaseItemId==partialItem,"partial edit retains receipt-linked item identity");
  order.RequestId=Guid.NewGuid();order.ExpectedVersion=1;order.Items[0].Quantity=4;await repo.ChangeAsync(4,order,1);Check((await repo.GetByIdAsync(4))!.Status=="COMPLETED","reduced order completes when no quantities remain");
  var draft=new CreatePurchaseRequest{ExpectedVersion=0,ChangeReason="Correct draft quantities",SupplierId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=1,VariantId=1,Quantity=3,PurchasePrice=100}]};await repo.UpdateDraftAsync(5,draft,1);Check((await repo.GetByIdAsync(5))!.TotalAmount==300&&(await repo.HistoryAsync(5)).Single().ChangeType=="DRAFT_EDIT","full draft edit recalculates and audits");await Reject(()=>repo.UpdateDraftAsync(5,draft,1),"stale legacy draft update blocked");
  await c.ExecuteAsync("UPDATE purchases SET invoice_attachment_name='old.pdf',invoice_attachment_content_type='application/pdf',invoice_attachment_data=decode('0102','hex') WHERE purchase_id=4");
  var fileChange=new PurchaseChangeRequest{RequestId=Guid.NewGuid(),ExpectedVersion=3,Reason="Replace supplier invoice",InvoiceAttachmentName="new.pdf",InvoiceAttachmentContentType="application/pdf",InvoiceAttachmentData=[3,4]};await repo.ChangeAsync(4,fileChange,1);Check(await c.ExecuteScalarAsync<int>("SELECT octet_length(old_attachment_data) FROM purchase_changes WHERE request_id=@RequestId",fileChange)==2,"replaced invoice attachment retained in immutable history");
  Check((await repo.GetByIdAsync(1))!.StockCorrectionQuantity==-1,"net stock correction is visible without rewriting receipts");
  await using var runtime=f.CreateConnection();await runtime.OpenAsync();await RejectSql(()=>runtime.ExecuteAsync("DELETE FROM purchase_changes"),"runtime cannot erase history");
  Console.WriteLine("PASS: isolated purchase correction transactions and migration 063.");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static async Task Reject(Func<Task> action,string message){try{await action();}catch(ValidationException){Console.WriteLine("PASS: "+message);return;}throw new Exception("Expected rejection: "+message);}
 static async Task RejectSql(Func<Task> action,string message){try{await action();}catch(PostgresException e)when(e.SqlState=="42501"){Console.WriteLine("PASS: "+message);return;}throw new Exception("Expected SQL rejection: "+message);}
}
