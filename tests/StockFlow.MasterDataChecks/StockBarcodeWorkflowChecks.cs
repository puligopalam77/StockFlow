using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

// Explicit isolated scratch database only. Never uses application configuration.
internal static class StockBarcodeWorkflowChecks
{
    public static async Task Run()
    {
        var cs = Environment.GetEnvironmentVariable("STOCKFLOW_BARCODE_TEST_CONNECTION") ?? throw new Exception("Supply a disposable barcode_workflow_test database.");
        var settings = new NpgsqlConnectionStringBuilder(cs);
        if (settings.Database != "barcode_workflow_test" || settings.Host != "127.0.0.1" || settings.Port != 55439)
            throw new Exception("This check only runs against the isolated local barcode_workflow_test database on port 55439.");
        await using var admin = new NpgsqlConnection(cs);
        await admin.OpenAsync();
        await admin.ExecuteAsync("""
            DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime') THEN CREATE ROLE stockflow_runtime; END IF; END $$;
            CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT current_setting('app.tenant_id')::bigint $$;
            CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT current_setting('app.location_id')::bigint $$;
            CREATE FUNCTION sf_actor() RETURNS integer LANGUAGE sql STABLE AS $$ SELECT current_setting('app.user_id')::integer $$;
            CREATE TABLE users(tenant_id bigint,user_id integer,full_name text,username text,PRIMARY KEY(tenant_id,user_id));
            INSERT INTO users VALUES(1,1,'Admin','admin');
            CREATE FUNCTION sf_visible_location(bigint) RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT $1=sf_location() $$;
            CREATE FUNCTION sf_brand_registration_details() RETURNS boolean LANGUAGE sql AS $$ SELECT true $$;
            CREATE TABLE tenants(product_image_gallery_enabled boolean);
            CREATE TABLE categories(category_id integer PRIMARY KEY,category_name text,business_profile_code text);
            CREATE TABLE brands(brand_id integer PRIMARY KEY,brand_name text);
            CREATE TABLE products(tenant_id bigint,product_id integer,product_name text,category_id integer,brand_id integer,model text,unit_of_measure text,is_active boolean,PRIMARY KEY(tenant_id,product_id));
            CREATE TABLE product_variants(tenant_id bigint,variant_id bigint,product_id integer,variant_name text,sku text,is_serial_tracked boolean,selling_price numeric,tax_percentage numeric,is_active boolean,attributes jsonb,PRIMARY KEY(tenant_id,variant_id));
            CREATE TABLE stock_intakes(tenant_id bigint,intake_id bigint,location_id bigint,intake_number text,received_date date,purchase_id bigint);
            CREATE TABLE suppliers(tenant_id bigint,supplier_id integer,supplier_name text);
            CREATE TABLE purchases(tenant_id bigint,purchase_id bigint,purchase_number text,supplier_id integer);
            CREATE TABLE purchase_items(tenant_id bigint,purchase_item_id bigint,variant_id bigint);
            CREATE TABLE purchase_receipts(tenant_id bigint,location_id bigint,receipt_id bigint,purchase_id bigint,receipt_number text,received_date date,status text);
            CREATE TABLE purchase_receipt_items(tenant_id bigint,location_id bigint,receipt_item_id bigint,receipt_id bigint,purchase_item_id bigint,quantity integer,serial_numbers text[]);
            CREATE TABLE modules(module_id integer PRIMARY KEY,module_code text,parent_module_id integer,module_name text,route text,sort_order integer,show_in_navigation boolean);
            CREATE TABLE module_navigation_permissions(navigation_module_id integer,permission_module_id integer,action_code text,PRIMARY KEY(navigation_module_id,permission_module_id,action_code));
            INSERT INTO modules(module_id,module_code) VALUES(1,'RECEIVE_STOCK'),(2,'BARCODES');
            CREATE TABLE stock_intake_items(tenant_id bigint,location_id bigint,intake_item_id bigint,intake_id bigint,product_id integer,variant_id bigint,quantity integer,serial_numbers text[],product_name_snapshot text,variant_name_snapshot text);
            CREATE TABLE product_units(tenant_id bigint,product_unit_id bigint,product_id integer,variant_id bigint,serial_number text,barcode text,internal_barcode text,manufacturer_barcode text,unit_status varchar,sale_id bigint,receipt_id bigint);
            CREATE TABLE variant_barcodes(tenant_id bigint,variant_barcode_id bigserial,variant_id bigint,barcode text,barcode_source text,is_active boolean DEFAULT true,is_primary boolean DEFAULT false);
            CREATE TABLE inventory_transactions(tenant_id bigint,location_id bigint,variant_id bigint,transaction_type text,quantity integer);
            CREATE TABLE audit_logs(user_id integer,module_name text,action_name text,table_name text,record_id bigint,reason text,new_values jsonb);
            CREATE VIEW vw_variant_current_stock WITH(security_invoker=true) AS SELECT v.variant_id,COALESCE(SUM(CASE WHEN i.transaction_type='SALE' THEN -i.quantity ELSE i.quantity END),0)::bigint current_stock
                FROM product_variants v LEFT JOIN inventory_transactions i USING(tenant_id,variant_id) GROUP BY v.variant_id;
            INSERT INTO categories VALUES(1,'Helmets','ELECTRICAL');
            INSERT INTO products VALUES(1,1,'Helmet',1,NULL,NULL,'Piece',true);
            INSERT INTO product_variants VALUES(1,1,1,'M','M',false,100,5,true,'{}'),(1,2,1,'L','L',false,120,5,true,'{}'),(1,3,1,'S','S',false,90,5,true,'{}');
            INSERT INTO stock_intakes VALUES(1,1,1,'INT-001',CURRENT_DATE,1);
            INSERT INTO suppliers VALUES(1,1,'Supplier');
            INSERT INTO purchases VALUES(1,1,'PUR-001',1),(1,2,'PUR-002',1);
            INSERT INTO purchase_items VALUES(1,1,1),(1,2,3);
            INSERT INTO purchase_receipts VALUES(1,1,1,1,'GRN-001',CURRENT_DATE,'COMPLETED'),(1,1,2,2,'GRN-002',CURRENT_DATE,'COMPLETED');
            INSERT INTO purchase_receipt_items VALUES(1,1,1,1,1,2,'{}'),(1,1,2,2,2,3,'{}');
            INSERT INTO stock_intake_items VALUES(1,1,1,1,1,1,2,'{}','Helmet','M'),(1,1,2,1,1,2,2,'{}','Helmet','L');
            INSERT INTO variant_barcodes(tenant_id,variant_id,barcode,barcode_source) VALUES(1,2,'0123456789012','MANUFACTURER');
            INSERT INTO inventory_transactions VALUES(1,1,1,'PURCHASE',5),(1,1,2,'PURCHASE',2),(1,1,3,'PURCHASE',3);
            DO $$ DECLARE tab text; BEGIN
              FOREACH tab IN ARRAY ARRAY['products','product_variants','stock_intakes','stock_intake_items','variant_barcodes','product_units','inventory_transactions','suppliers','purchases','purchase_items','purchase_receipts','purchase_receipt_items'] LOOP
                EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
                EXECUTE format('CREATE POLICY tenant ON %I USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant())',tab);
              END LOOP;
            END $$;
            CREATE POLICY location ON inventory_transactions AS RESTRICTIVE USING(location_id=sf_location());
            GRANT SELECT,INSERT ON ALL TABLES IN SCHEMA public TO stockflow_runtime;
            GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO stockflow_runtime;
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/054_unique_stock_item_labels.sql")));
        // Simulate an already printed label before the upgrade.
        await admin.ExecuteAsync("INSERT INTO stock_item_labels(tenant_id,location_id,intake_item_id,item_number) VALUES(1,1,1,1)");
        await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/059_stock_barcode_workflow.sql")));
        await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/060_purchase_barcode_lots.sql")));
        await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/061_barcode_print_requests.sql")));
        await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/062_barcode_label_sizes.sql")));
        void Check(bool ok,string message) { if(!ok)throw new Exception(message); Console.WriteLine("PASS: "+message); }
        Check(await admin.ExecuteScalarAsync<string>("SELECT barcode FROM stock_item_labels WHERE label_id=1")=="SFI1X1","migration retains previously printed codes");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
        var scope = new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1};
        var factory = new DbConnectionFactory(config,scope);
        var variants = new VariantRepository(factory);
        var before = await admin.ExecuteScalarAsync<long>("SELECT SUM(quantity) FROM inventory_transactions");
        var labels = await variants.ItemLabelsAsync(1,true,1);
        Check(labels.Count==5 && labels.Select(l=>l.InternalBarcode).Distinct().Count()==5,"receipt and existing stock receive five distinct persistent labels");
        Check(labels.Count(l=>l.IntakeId is null)==3,"only uncovered stock gets additional labels");
        var again = await variants.ItemLabelsAsync(1,true,1);
        Check(labels.Select(l=>l.InternalBarcode).SequenceEqual(again.Select(l=>l.InternalBarcode)),"repeat generation and reprint preserve every code");
        await Task.WhenAll(variants.ItemLabelsAsync(3,true,1),variants.ItemLabelsAsync(3,true,1));
        Check((await variants.ItemLabelsAsync(3,false,1)).Count==3,"concurrent requests cannot duplicate legacy stock labels");
        var scanner = new ScanRepository(factory);
        foreach (var label in labels)
            Check((await scanner.FindByCodeAsync(label.InternalBarcode!))?.VariantId==1,"generated barcode resolves the correct sale variant: "+label.InternalBarcode);
        Check((await scanner.FindByCodeAsync("0123456789012"))?.VariantId==2,"supplied manufacturer barcode resolves its sale variant");
        var receiving = new StockIntakeRepository(factory);
        var lots = new BarcodeLotRepository(factory);
        var management=new BarcodeManagementRepository(factory,lots);
        var beforePreview=await management.ReadAsync("receipt",2);
        Check(beforePreview!.Rows.Count==3&&beforePreview.Rows.All(r=>r.Barcode is null),"management opens individual received units without generating codes");
        var first=beforePreview.Rows[0];
        Check((await management.ListAsync()).Single(l=>l.Kind=="receipt"&&l.LotId==2).BarcodeStatus=="Pending","lot list shows pending barcode status without writes");
        var prepared=await management.PrepareAsync("receipt",2,[first.Key],1);
        Check(prepared!.Rows.Count(r=>r.Barcode is not null)==1,"preview generates only the selected missing code");
        Check((await management.ListAsync()).Single(l=>l.Kind=="receipt"&&l.LotId==2).BarcodeStatus=="Partial","lot list reflects partially prepared codes");
        var preparedAgain=await management.PrepareAsync("receipt",2,[first.Key],1);
        Check(prepared.Rows[0].Barcode==preparedAgain!.Rows[0].Barcode,"selected missing code is stable on retry");
        var request=new StockFlow.Application.Interfaces.BarcodePrintRequest{RequestId=Guid.NewGuid(),Items=[new(){Key=first.Key,Barcode=prepared.Rows[0].Barcode!,Copies=2}]};
        var printed=await management.PrintAsync("receipt",2,request,1);
        Check(printed!.History.Count==1&&printed.History[0].Copies==2&&printed.Rows[0].LastRequestedBy=="Admin","print request stores selection, copies, actor and time");
        await management.PrintAsync("receipt",2,request,1);
        Check((await management.ReadAsync("receipt",2))!.History.Count==1,"retrying a print request does not duplicate history");
        try{await management.PrintAsync("receipt",2,new(){RequestId=request.RequestId,Items=[new(){Key=first.Key,Barcode=prepared.Rows[0].Barcode!,Copies=3}]},1);throw new Exception("Request ID reused for a different selection");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: retry request ID cannot be reused with different copies");}
        var reprint=new StockFlow.Application.Interfaces.BarcodePrintRequest{RequestId=Guid.NewGuid(),Action="REPRINT",Format="thermal",Items=request.Items};
        var reprinted=await management.PrintAsync("receipt",2,reprint,1);
        Check(reprinted!.History.Count==2&&reprinted.Rows[0].Barcode==prepared.Rows[0].Barcode,"reprint keeps the code and records the requested format");
        foreach(var format in new[]{"thermal50x30","thermal40x25","thermal30x20"})
        {
            var sized=new StockFlow.Application.Interfaces.BarcodePrintRequest{RequestId=Guid.NewGuid(),Action="REPRINT",Format=format,Items=request.Items};
            if(format=="thermal30x20")
            {
                try{await management.PrintAsync("receipt",2,sized,1);throw new Exception("Untested small labels accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: 30 x 20 mm requires scan-test confirmation");}
                sized.SmallLabelScanTested=true;
            }
            var result=await management.PrintAsync("receipt",2,sized,1);
            Check(result!.History.Any(h=>h.RequestId==sized.RequestId&&h.Format==format),"print history saves label size: "+format);
        }
        var sheetRequest=new StockFlow.Application.Interfaces.BarcodePrintRequest{RequestId=Guid.NewGuid(),Action="REPRINT",Format="sheet",SheetTemplate=new(){WidthMm=50,HeightMm=30,Columns=3,Rows=8,LeftMm=10,TopMm=15,ColumnGapMm=5,RowGapMm=3},Items=request.Items};
        var sheetHistory=await management.PrintAsync("receipt",2,sheetRequest,1);
        Check(sheetHistory!.History.Any(h=>h.RequestId==sheetRequest.RequestId&&h.SheetTemplate?.WidthMm==50&&h.SheetTemplate?.ColumnGapMm==5),"A4 history retains measured sticker template and gaps");
        await management.PrintAsync("receipt",2,sheetRequest,1);
        Check((await management.ReadAsync("receipt",2))!.History.Count(h=>h.RequestId==sheetRequest.RequestId)==1,"A4 template retries keep one request");
        try{await management.PrintAsync("receipt",2,new(){RequestId=Guid.NewGuid(),Action="REPRINT",Format="sheet",SheetTemplate=new(){WidthMm=100,Columns=8},Items=request.Items},1);throw new Exception("Oversized A4 template accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: invalid A4 sheet dimensions are rejected");}
        try{await management.PrintAsync("receipt",2,new(){RequestId=Guid.NewGuid(),Action="REPRINT",Items=[new(){Key=beforePreview.Rows[1].Key,Barcode="uncommitted",Copies=1}]},1);throw new Exception("Missing label accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: print rejects uncommitted codes");}
        var manufacturer=await management.ReadAsync("intake",1);
        var supplied=manufacturer!.Rows.Where(r=>r.VariantId==2).ToArray();
        Check(supplied.Length==2&&supplied.All(r=>r.Barcode=="0123456789012"),"shared manufacturer code remains a model code on distinct received unit rows");
        await management.PrepareAsync("intake",1,supplied.Select(r=>r.Key).ToArray(),1);
        Check((await management.ReadAsync("intake",1))!.Rows.Where(r=>r.VariantId==2).All(r=>r.Barcode=="0123456789012"),"preview does not replace existing manufacturer codes");
        var list = await lots.ListAsync();
        Check(list.Count==2 && list.Any(l=>l.Kind=="receipt"&&l.LotId==2) && !list.Any(l=>l.Kind=="receipt"&&l.LotId==1),"purchase lots include deliveries and avoid duplicating guided receipts");
        var receiptLabels=await lots.ReceiptLabelsAsync(2,"GENERATE",1);
        Check(receiptLabels!.Count==3 && receiptLabels.All(l=>l.InternalBarcode!.StartsWith("SFR")),"purchase lot generates three distinct labels");
        var receiptAgain=await lots.ReceiptLabelsAsync(2,"GENERATE",1);
        Check(receiptLabels.Select(l=>l.InternalBarcode).SequenceEqual(receiptAgain!.Select(l=>l.InternalBarcode)),"purchase lot generation is idempotent");
        Check((await scanner.FindByCodeAsync(receiptLabels[0].InternalBarcode!))?.VariantId==3,"purchase lot label resolves its sale variant");
        var manifest = await receiving.LabelsAsync(1);
        Check(manifest!.Any(l=>l.VariantId==2 && l.ExistingBarcode=="0123456789012" && l.Quantity==2),"receipt displays supplied barcode before generation");
        await variants.LogExistingBarcodePrintAsync(2,"0123456789012",2,"REPRINT",1);
        try { await variants.LogExistingBarcodePrintAsync(1,"0123456789012",1,"PRINT",1); throw new Exception("Wrong variant accepted"); }
        catch(System.ComponentModel.DataAnnotations.ValidationException) { Console.WriteLine("PASS: rejects printing another variant's barcode"); }
        await variants.LogItemLabelPrintAsync(1,[labels[0].LabelId!.Value],"REPRINT",1);
        scope.LocationId=2;
        Check(await management.ReadAsync("receipt",2) is null,"print history and received unit rows cannot leak across locations");
        Check((await lots.ListAsync()).Count==0 && await lots.ReceiptLabelsAsync(2,null,1) is null,"purchase lots are scoped to the active location");
        Check((await variants.ItemLabelsAsync(1,false,1)).Count==0,"labels cannot leak to another stock location");
        Check(await scanner.FindByCodeAsync(labels.Last().InternalBarcode!) is null,"generated labels cannot scan across locations");
        scope.LocationId=1;scope.TenantId=2;
        Check(await scanner.FindByCodeAsync("0123456789012") is null,"manufacturer barcodes cannot leak across shops");
        Check(await admin.ExecuteScalarAsync<long>("SELECT SUM(quantity) FROM inventory_transactions")==before,"generation and printing leave stock balances unchanged");
    }
}
