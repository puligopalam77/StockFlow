using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class FastBillingChecks
{
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var database="fast_billing_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var setup=new NpgsqlConnection(host+database);await setup.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
            await setup.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var profile=(await setup.ExecuteScalarAsync<string>("SELECT business_profile_code FROM tenants WHERE tenant_id=1"))!;
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode=profile,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
            await setup.ExecuteAsync("SELECT set_config('app.business_profile_code',@profile,false)",new{profile});
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var factory=new DbConnectionFactory(config,scope);var sales=new SalesRepository(factory);var gst=new GstRepository(factory);
            var company=await new CompanySettingsRepository(factory).GetAsync();var inclusive=company.DefaultTaxMode=="INCLUSIVE";
            var customer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="Fast billing customer",MobileNumber="9861200044",Address="Test billing address"});
            var today=await setup.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            var rule=await gst.SaveRuleAsync(new(){RuleName="Synthetic manual goods",Description="Test classification, not statutory advice",Tax=new(){HsnSac="610910",Uqc="NOS",TaxRate=5,EffectiveFrom=today,ReviewReference="Synthetic reviewed test classification"}});
            CreateSaleRequest Request()=>new(){LocationId=1,RequestId=Guid.NewGuid(),CustomerId=customer,PricesIncludeTax=inclusive,CanDiscount=true,CanApproveDiscount=true,AdjustmentReason="Test promotional discount",Items=[new(){Quantity=2,SellingPrice=100,DiscountPercent=10,ManualItem=new(){Name="Small cord",TaxRuleId=rule,Mrp=120,Description="Transient counter item"}}]};
            async Task Pay(CreateSaleRequest r,string mode="CASH",string? type=null){var q=await sales.QuoteAsync(r);r.Payments=[new(){PaymentMode=mode,CardType=type,Amount=q.TotalAmount,ReferenceNumber=mode=="CASH"?null:Guid.NewGuid().ToString(),CollectionConfirmed=mode=="CASH"?null:true}];}
            var ordinary=Request();ordinary.Items[0].ManualItem!.TaxRuleId=0;
            var ordinaryQuote=await sales.QuoteAsync(ordinary);Check(ordinaryQuote.TotalAmount==180&&ordinaryQuote.TaxAmount==0,"GST is optional for ordinary manual billing and adds no tax");
            await Pay(ordinary);var ordinaryId=await sales.CreateSaleAsync(ordinary,1);
            Check(await sales.CreateSaleAsync(ordinary,1)==ordinaryId,"ordinary no-GST manual bill is idempotent");
            var ordinaryInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(ordinaryId))!;
            Check(ordinaryInvoice.Gst is null&&ordinaryInvoice.Items.Single().HsnSac is null&&ordinaryInvoice.Items.Single().TaxRuleId is null&&ordinaryInvoice.Items.Single().GstTreatment=="UNCLASSIFIED","ordinary billing does not invent HSN or a GST exemption");
            await Reject(()=>sales.QuoteAsync(Request()),"draft tax classifications cannot be billed");await gst.ReviewRuleAsync(rule);
            await FastPolicyChecks.Run(setup,factory,sales,Request);
            var allocated=Request();allocated.AllocateInvoiceDiscount=true;allocated.DiscountAmount=10;
            var allocatedQuote=await sales.QuoteAsync(allocated);await Pay(allocated);var allocatedId=await sales.CreateSaleAsync(allocated,1);
            var allocatedInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(allocatedId))!;
            Check(allocatedInvoice.ItemSubtotal==200 && allocatedInvoice.ItemDiscountAmount==20 && allocatedInvoice.InvoiceDiscountAmount==10,"invoice keeps original price and separate item/invoice discount snapshots");
            Check(allocatedInvoice.Items.Single().EnteredRate==100 && allocatedInvoice.Items.Single().EnteredDiscount==20,"printed entered rate and item discount are separate from allocated invoice discounts");
            Check(allocatedInvoice.TotalAmount==allocatedQuote.TotalAmount && allocatedInvoice.Items.Sum(i=>i.LineTotal)==allocatedQuote.TotalAmount && allocatedInvoice.Payments.Sum(p=>p.Amount)==allocatedQuote.TotalAmount,"allocated quote, persisted lines, invoice and full payment agree");
            var allocatedDetails=(await sales.GetByIdAsync(allocatedId))!;
            Check(allocatedDetails.ItemSubtotal==200 && allocatedDetails.InvoiceDiscountAmount==10,"invoice details read allocated-discount snapshots without repricing history");
            var percent=Request();var q=await sales.QuoteAsync(percent);Check(q.Items.Single().DiscountAmount==20&&q.TotalAmount==(inclusive?180:189),"percentage discount uses authoritative tax-inclusive/exclusive calculator");
            foreach(var rate in new[]{0m,5m,10m,2.55m,100m}){var r=Request();r.Items[0].DiscountPercent=rate;var quote=await sales.QuoteAsync(r);Check(quote.Items[0].DiscountAmount==SaleRules.Money(200*rate/100),$"{rate}% discount on multiple quantity");}
            var fixedRequest=Request();fixedRequest.Items[0].DiscountPercent=null;fixedRequest.Items[0].DiscountAmount=15;
            Check((await sales.QuoteAsync(fixedRequest)).Items[0].DiscountAmount==15,"saved fixed amount remains fixed");
            var bad=Request();bad.Items[0].ManualItem!.Mrp=50;await Reject(()=>sales.QuoteAsync(bad),"tax-inclusive customer payable amount cannot exceed MRP");
            var noMrp=Request();noMrp.Items[0].ManualItem!.Mrp=null;Check((await sales.QuoteAsync(noMrp)).Items.Single().Mrp is null,"missing manual MRP remains unknown");
            bad=Request();bad.Items[0].ManualItem!.Name=" ";await Reject(()=>sales.QuoteAsync(bad),"empty manual descriptions rejected");
            var invalidRule=Request();invalidRule.Items[0].ManualItem!.TaxRuleId=long.MaxValue;await Reject(()=>sales.QuoteAsync(invalidRule),"invalid rule rejected");
            var noCustomer=Request();await Pay(noCustomer);noCustomer.CustomerId=null;await Reject(()=>sales.CreateSaleAsync(noCustomer,1),"customer required on new invoices");
            var split=Request();await Pay(split);var amount=split.Payments[0].Amount;split.Payments=[new(){Amount=amount/2},new(){PaymentMode="UPI",Amount=amount/2,ReferenceNumber="SPLIT",CollectionConfirmed=true}];await Reject(()=>sales.CreateSaleAsync(split,1),"new split payments rejected on server");
            var unconfirmed=Request();await Pay(unconfirmed,"UPI");unconfirmed.Payments[0].CollectionConfirmed=false;await Reject(()=>sales.CreateSaleAsync(unconfirmed,1),"unverified electronic collection rejected");
            var before=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");var count=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM products");
            var request=Request();await Pay(request);var ids=await Task.WhenAll(sales.CreateSaleAsync(request,1),sales.CreateSaleAsync(request,1));Check(ids[0]==ids[1],"concurrent identical retries create one manual invoice");
            var invoice=(await new InvoiceRepository(factory).GetInvoiceAsync(ids[0]))!;var line=invoice.Items.Single();
            Check(line.IsManual&&line.ProductId is null&&line.ProductName=="Small cord"&&line.Mrp==120&&line.DiscountPercent==10&&line.DiscountType=="PERCENTAGE"&&line.UnitOfMeasure=="NOS","invoice details preserve manual identity, MRP, unit and percentage snapshots");
            Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==before&&await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM products")==count,"manual posting creates neither stock movements nor products");
            Check((await sales.GetListAsync("Small cord",null,0,10,null,null,null)).Rows.Any(r=>r.SaleId==ids[0]),"sales history searches manual item descriptions");
            var returns=new OperationsRepository(factory);var refund=new StockFlow.Application.DTOs.Operations.SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="Test manual item refund",RefundMode="CASH",RefundAmount=invoice.TotalAmount,Items=[new(){SaleItemId=line.SaleItemId,Quantity=2}]};
            var returnId=await returns.ReturnSaleAsync(ids[0],refund,1);Check(await returns.ReturnSaleAsync(ids[0],refund,1)==returnId&&await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==before,"manual refund is idempotent and does not restock");
            Check((await returns.SaleAsync(ids[0])).ReturnedLines.Single().ProductName=="Small cord","manual return history retains line description");
            // Mix an actual stocked SKU with a transient item using the same finalization.
            var catalog=await setup.QuerySingleAsync<(int Product,long Variant)>("SELECT p.product_id,v.variant_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.tenant_id=1 AND p.identifier_profile=@profile AND p.is_active AND v.is_active AND NOT v.is_serial_tracked ORDER BY p.product_id,v.variant_id LIMIT 1",new{profile});
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=100,mrp=120 WHERE variant_id=@Variant",new{catalog.Variant});
            await new StockIntakeRepository(factory).PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=today,Items=[new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=3}]},true,1);
            await using var scoped=factory.CreateConnection();
            var stockBefore=await scoped.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@Variant",new{catalog.Variant});
            await setup.ExecuteAsync("INSERT INTO billing_policy(tenant_id,discount_limit_percent) VALUES(1,10) ON CONFLICT(tenant_id,store_id) DO UPDATE SET discount_limit_percent=10");
            var reviewRequest=new CreateSaleRequest{LocationId=1,RequestId=Guid.NewGuid(),CustomerId=customer,PricesIncludeTax=inclusive,CanDiscount=true,Items=[new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=1,SellingPrice=100,DiscountPercent=0,AdjustmentReason="Line-specific promotion"}]};
            Check(!(await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.RequiresApproval,"unchanged price and zero discount require no approval");
            reviewRequest.Items[0].DiscountPercent=7;Check(!(await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.RequiresApproval,"seven percent discount is below the configured ten percent approval threshold");
            reviewRequest.Items[0].DiscountPercent=10;Check(!(await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.RequiresApproval,"exact threshold retains the existing strictly-above semantics");
            reviewRequest.Items[0].DiscountPercent=15;var reviewQuote=await sales.QuoteAsync(reviewRequest);Check(reviewQuote.AdjustmentReview!.RequiresApproval&&reviewQuote.AdjustmentReview.ApprovalStatus=="REQUIRED","above-threshold discount requires a matching approval");
            await setup.ExecuteAsync("INSERT INTO billing_approvals(tenant_id,draft_id,requested_by,financial_hash,summary) VALUES(1,@RequestId,1,@hash,CAST(@summary AS jsonb))",new{reviewRequest.RequestId,hash=BillingApproval.Hash(reviewRequest,(await BillingPolicyStore.ReadAsync(scoped)).Stamp,await sales.QuoteAsync(reviewRequest)),summary=JsonSerializer.Serialize(reviewRequest)});
            Check((await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.ApprovalStatus=="PENDING","quote distinguishes requested approval from missing reason");
            await setup.ExecuteAsync("SELECT set_config('app.user_id','2',false)");
            await setup.ExecuteAsync("UPDATE billing_approvals SET approved_by=(SELECT min(user_id) FROM users WHERE tenant_id=1 AND user_id<>1),approved_at=now() WHERE draft_id=@RequestId",new{reviewRequest.RequestId});
            await setup.ExecuteAsync("SELECT set_config('app.user_id','1',false)");
            Check((await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.ApprovalStatus=="APPROVED","quote recognizes a current approval for the exact line adjustments");
            reviewRequest.Items[0].Quantity=2;Check((await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.ApprovalStatus=="STALE","quantity changes invalidate the matching approval status");reviewRequest.Items[0].Quantity=1;
            reviewRequest.Items[0].DiscountPercent=7;reviewRequest.DiscountAmount=6;Check((await sales.QuoteAsync(reviewRequest)).AdjustmentReview!.RequiresApproval,"combined item and invoice reductions use the existing aggregate policy");
            var missingLineReason=new CreateSaleRequest{LocationId=1,RequestId=Guid.NewGuid(),CustomerId=customer,PricesIncludeTax=inclusive,CanDiscount=true,CanApproveDiscount=true,AdjustmentReason="A bill summary cannot replace line reasons",Items=[new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=1,SellingPrice=100,DiscountPercent=5,AdjustmentReason="First line promotion"},new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=1,SellingPrice=100,DiscountPercent=5}]};
            await Pay(missingLineReason);await Reject(()=>sales.CreateSaleAsync(missingLineReason,1),"new per-line adjustments cannot use a generic bill reason for another missing line reason");
            var taxEdit=new CreateSaleRequest{LocationId=1,RequestId=Guid.NewGuid(),CustomerId=customer,PricesIncludeTax=inclusive,AdjustmentReason="Counter tax correction",Items=[new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=1,SellingPrice=100,TaxOverride=12,AdjustmentReason="Specific line tax correction"}]};
            var savedSku=await setup.ExecuteScalarAsync<string>("SELECT sku FROM product_variants WHERE variant_id=@Variant",new{catalog.Variant});
            Check((await new ScanRepository(factory).FindByCodeAsync(savedSku!))!.Mrp==120,"barcode/SKU scan includes the saved variant MRP");
            var editedQuote=await sales.QuoteAsync(taxEdit);Check(editedQuote.Items.Single().Mrp==120&&editedQuote.Items.Single().TaxPercentage==12,"quote reads saved variant MRP and authorized ordinary-bill tax override");
            taxEdit.Items[0].SellingPrice=130;await Reject(()=>sales.QuoteAsync(taxEdit),"registered product MRP cap is enforced using its saved variant value");taxEdit.Items[0].SellingPrice=100;
            taxEdit.Items[0].AdjustmentReason=null;taxEdit.AdjustmentReason=null;await Reject(()=>sales.QuoteAsync(taxEdit),"tax change requires a recorded reason");taxEdit.AdjustmentReason="Counter tax correction";
            taxEdit.Items[0].TaxOverride=101;await Reject(()=>sales.QuoteAsync(taxEdit),"out-of-range tax override rejected");taxEdit.Items[0].TaxOverride=12;
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='TAX_OVERRIDE'");
            try{await sales.QuoteAsync(taxEdit);throw new Exception("Unauthorized tax override accepted");}catch(UnauthorizedAccessException){Check(true,"tax override permission enforced by backend");}
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='TAX_OVERRIDE'");
            taxEdit.Items[0].AdjustmentReason="Specific line tax correction";await Pay(taxEdit);var taxEditedSale=await sales.CreateSaleAsync(taxEdit,1);
            Check(await setup.ExecuteScalarAsync<decimal>("SELECT tax_percentage FROM sale_items WHERE sale_id=@id",new{id=taxEditedSale})==12,"successful checkout stores overridden line tax");
            Check(await setup.ExecuteScalarAsync<string>("SELECT coalesce(request_payload->>'AdjustmentReason',request_payload->>'adjustmentReason') FROM sales WHERE sale_id=@id",new{id=taxEditedSale})=="Counter tax correction","tax override reason retained in original sale request");
            Check(await setup.ExecuteScalarAsync<string>("SELECT coalesce(request_payload#>>'{Items,0,AdjustmentReason}',request_payload#>>'{items,0,adjustmentReason}') FROM sales WHERE sale_id=@id",new{id=taxEditedSale})=="Specific line tax correction","original invoice request retains the reason against the specific adjusted line");
            stockBefore=await scoped.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@Variant",new{catalog.Variant});
            var mixed=Request();mixed.Items.Add(new(){ProductId=catalog.Product,VariantId=catalog.Variant,Quantity=1,SellingPrice=100,DiscountPercent=5});await Pay(mixed);var mixedId=await sales.CreateSaleAsync(mixed,1);
            var mixedInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(mixedId))!;
            Check(mixedInvoice.Items.Count==2&&mixedInvoice.Items.Count(i=>i.IsManual)==1&&mixedInvoice.Items.Single(i=>!i.IsManual).Mrp==120,"mixed invoice preserves both lines and correct variant MRP");
            var historyBefore=await setup.ExecuteScalarAsync<string>("SELECT jsonb_build_object('sale',to_jsonb(s),'lines',(SELECT jsonb_agg(i ORDER BY i.sale_item_id) FROM sale_items i WHERE i.sale_id=s.sale_id))::text FROM sales s WHERE s.sale_id=@mixedId",new{mixedId});
            var originalProduct=await setup.ExecuteScalarAsync<string>("SELECT product_name FROM products WHERE product_id=@Product",new{catalog.Product});
            var originalCustomer=await setup.ExecuteScalarAsync<string>("SELECT customer_name FROM customers WHERE customer_id=@customer",new{customer});
            await setup.ExecuteAsync("UPDATE products SET product_name='Changed current catalog' WHERE product_id=@Product; UPDATE customers SET customer_name='Changed current contact' WHERE customer_id=@customer",new{catalog.Product,customer});
            var reread=(await new InvoiceRepository(factory).GetInvoiceAsync(mixedId))!;
            Check(reread.CustomerName==mixedInvoice.CustomerName && reread.Items.Select(i=>i.ProductName).SequenceEqual(mixedInvoice.Items.Select(i=>i.ProductName)) && reread.TotalAmount==mixedInvoice.TotalAmount,"reprinting after current master edits preserves original names and financial values");
            Check(historyBefore==await setup.ExecuteScalarAsync<string>("SELECT jsonb_build_object('sale',to_jsonb(s),'lines',(SELECT jsonb_agg(i ORDER BY i.sale_item_id) FROM sale_items i WHERE i.sale_id=s.sale_id))::text FROM sales s WHERE s.sale_id=@mixedId",new{mixedId}),"invoice reads do not rewrite issued header or lines");
            await setup.ExecuteAsync("UPDATE products SET product_name=@originalProduct WHERE product_id=@Product; UPDATE customers SET customer_name=@originalCustomer WHERE customer_id=@customer",new{catalog.Product,customer,originalProduct,originalCustomer});
            foreach(var isolatedProfile in new[]{"ELECTRICAL","BIKE_ACCESSORIES","CLOTHING","FOOTWEAR"}.Where(p=>p!=profile))
            {
                scope.BusinessProfileCode=isolatedProfile;
                Check(await new InvoiceRepository(factory).GetInvoiceAsync(mixedId) is null,"invoice data cannot cross into "+isolatedProfile);
            }
            scope.BusinessProfileCode=profile;scope.StoreId=999;
            Check(await new InvoiceRepository(factory).GetInvoiceAsync(mixedId) is null,"invoice lookup rejects another shop context");
            scope.StoreId=1;scope.TenantId=999;
            Check(await new InvoiceRepository(factory).GetInvoiceAsync(mixedId) is null,"invoice lookup rejects another tenant context");scope.TenantId=1;
            Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@id",new{id=mixedId})==1&&await scoped.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@Variant",new{catalog.Variant})==stockBefore-1,"mixed checkout posts stock only for the registered SKU");
            var mixedManual=mixedInvoice.Items.Single(i=>i.IsManual);await returns.ReturnSaleAsync(mixedId,new(){LocationId=1,RequestId=Guid.NewGuid(),Notes="Mixed cart manual refund",RefundMode="CASH",RefundAmount=mixedManual.LineTotal,Items=[new(){SaleItemId=mixedManual.SaleItemId,Quantity=mixedManual.Quantity}]},1);
            Check(await scoped.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@Variant",new{catalog.Variant})==stockBefore-1,"manual refund on a mixed invoice never restocks its registered neighbor");
            before=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");
            foreach(var (mode,type) in new (string,string?)[]{("CASH",null),("UPI",null),("CARD","DEBIT"),("CARD","CREDIT"),("BANK_TRANSFER",null)}){
                var paid=Request();await Pay(paid,mode,type);var id=await sales.CreateSaleAsync(paid,1);Check((await sales.GetByIdAsync(id))!.Payments.Single().CardType==type,"full "+mode+" "+type+" checkout posts one allocation");
                if(mode!="CASH"){
                    var noReference=Request();await Pay(noReference,mode,type);noReference.Payments[0].ReferenceNumber=null;
                    var noReferenceId=await sales.CreateSaleAsync(noReference,1);
                    Check((await sales.GetByIdAsync(noReferenceId))!.Payments.Single().ReferenceNumber is null,"confirmed "+mode+" "+type+" checkout accepts an omitted transaction reference");
                    Check(await sales.CreateSaleAsync(noReference,1)==noReferenceId,"reference-free electronic checkout remains idempotent");
                    var unverifiedNoReference=Request();await Pay(unverifiedNoReference,mode,type);unverifiedNoReference.Payments[0].ReferenceNumber=null;unverifiedNoReference.Payments[0].CollectionConfirmed=false;
                    await Reject(()=>sales.CreateSaleAsync(unverifiedNoReference,1),"reference-free "+mode+" still requires collection confirmation");
                }
                if(mode=="UPI"){var duplicate=Request();await Pay(duplicate,mode);duplicate.Payments[0].ReferenceNumber=paid.Payments[0].ReferenceNumber;await Reject(()=>sales.CreateSaleAsync(duplicate,1),"reused electronic reference rejected");}
            }
            var profit=(await returns.ProductSalesAsync(today,today)).Single(r=>r.ProductName.StartsWith("Manual items"));Check(profit.EstimatedCost is null&&profit.EstimatedMargin is null,"manual cost and profit are unknown, never zero-cost profit");
            var financial=(await new ReportingRepository(factory,scope).FinancialAsync(today,today)).Single(r=>r.LocationId==1);
            Check(financial.NonStockQuantity>0&&financial.InvoiceQuantity-financial.NonStockQuantity==financial.LedgerSalesQuantity,"financial quantity reconciliation explicitly separates non-stock lines");
            scope.StoreId=2;scope.LocationId=2;await Reject(()=>sales.QuoteAsync(Request()),"another shop cannot reuse a manual tax classification");scope.StoreId=1;scope.LocationId=1;
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='MANUAL_ITEM'");
            await Reject(()=>sales.QuoteAsync(Request()),"manual billing permission enforced inside database");
            await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='SALES' AND a.action_code='MANUAL_ITEM'");
            // A real mapped POS document exercises the unchanged statutory component calculator.
            var business=await gst.SaveBusinessAsync(new(){LegalName="Synthetic test business",Address="Synthetic registered address",StateCode="29",PinCode="560001"});
            var registration=await gst.SaveRegistrationAsync(new(){BusinessId=business,Gstin="29ABCDE1234F1ZW",StateCode="29",Address="Synthetic registered address",RegistrationType="REGULAR",EffectiveFrom=today});
            await gst.SaveMappingAsync(new(){RegistrationId=registration,StoreId=1,BusinessProfileCode=profile,EffectiveFrom=today});
            // Missing reviewed product mappings cannot be bypassed with a cashier tax.

            taxEdit.RequestId=Guid.NewGuid();taxEdit.Items[0].TaxOverride=12;
            await Reject(()=>sales.QuoteAsync(taxEdit),"GST bills cannot bypass their required reviewed tax mappings with a manual override");
            await gst.SaveTaxAsync(new(){ProductId=catalog.Product,VariantId=catalog.Variant,HsnSac="610910",Uqc="NOS",TaxRate=5,EffectiveFrom=today,ReviewReference="Synthetic reviewed catalog classification"});
            await Reject(()=>sales.QuoteAsync(taxEdit),"authorized tax override cannot replace an effective reviewed GST rate");
            var missingGst=Request();missingGst.Items[0].ManualItem!.TaxRuleId=0;
            await Reject(()=>sales.QuoteAsync(missingGst),"GST-enabled invoice still requires a reviewed classification");
            missingGst.Payments=[new(){Amount=180}];await Reject(()=>sales.CreateSaleAsync(missingGst,1),"checkout cannot bypass GST invoice checks using an ordinary held item");
            var mapped=Request();mapped.AllocateInvoiceDiscount=true;mapped.DiscountAmount=10;mapped.GstPlaceOfSupplyStateCode="29";var mappedQuote=await sales.QuoteAsync(mapped);await Pay(mapped);var mappedId=await sales.CreateSaleAsync(mapped,1);
            var taxInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(mappedId))!;Check(taxInvoice.Gst!.Lines.Single().IsManual&&taxInvoice.Gst.Lines.Single().ProductId is null&&taxInvoice.Gst.Lines.Single().HsnSac=="610910","manual lines reach GST snapshot and invoice template by original line ID");
            Check(taxInvoice.Gst.Cgst+taxInvoice.Gst.Sgst+taxInvoice.Gst.Utgst+taxInvoice.Gst.Igst==taxInvoice.TaxAmount && taxInvoice.Gst.Lines.Sum(l=>l.Total)==taxInvoice.TotalAmount,"allocated invoice tax components and GST line snapshots reconcile to the collected total");
            var rateBreakdown=mappedQuote.TaxBreakdown!;
            Check(rateBreakdown.Status=="CGST_SGST" && rateBreakdown.Rates.Sum(r=>r.Cgst)==taxInvoice.Gst.Cgst && rateBreakdown.Rates.Sum(r=>r.Sgst)==taxInvoice.Gst.Sgst && rateBreakdown.Rates.Sum(r=>r.SupplyValue)==taxInvoice.Gst.Lines.Sum(l=>l.TaxableValue),"live quote components and rate bases exactly match saved GST invoice");
            mapped.GstPlaceOfSupplyStateCode="27";var interQuote=await sales.QuoteAsync(mapped);
            Check(interQuote.TaxBreakdown!.Status=="IGST"&&interQuote.TaxBreakdown.Rates.Sum(r=>r.Igst)==interQuote.TaxAmount&&interQuote.TotalAmount==mappedQuote.TotalAmount,"place-of-supply change refreshes quote components without changing payable amount");
            mapped.GstPlaceOfSupplyStateCode=null;Check((await sales.QuoteAsync(mapped)).TaxBreakdown!.Status=="PLACE_OF_SUPPLY_REQUIRED","missing place of supply never invents components");mapped.GstPlaceOfSupplyStateCode="29";
            var hsn=await gst.ReportAsync(new(){RegistrationId=registration,From=today,To=today,Report="hsn"});Check(hsn.Hsn.Any(r=>r.HsnSac=="610910"&&r.Quantity==2),"HSN report includes posted manual goods");
            var salesReport=await gst.ReportAsync(new(){RegistrationId=registration,From=today,To=today,Report="sales"});Check(salesReport.Documents.Any(d=>d.SaleId==mappedId),"sales tax register includes manual invoices");
            var original=salesReport.Documents.Single(d=>d.SaleId==mappedId);
            var note=new GstRecordRequest{RegistrationId=registration,RequestId=Guid.NewGuid(),Kind="CREDIT_NOTE",Direction="SALES",DocumentNumber="CN-TEST-01",DocumentDate=today,ParentDocumentId=original.DocumentId,PlaceOfSupply="29",EvidenceReference="Original manual invoice and accepted return",Reason="Manual goods tax adjustment",Lines=taxInvoice.Gst.Lines};
            var source=note.Lines[0].SourceLineId;note.Lines[0].SourceLineId=long.MaxValue;await Reject(()=>gst.RecordAsync(note),"manual tax note cannot substitute another original line identity");note.Lines[0].SourceLineId=source;
            var noteId=await gst.RecordAsync(note);Check(await gst.RecordAsync(note)==noteId,"manual GST credit note records once against its original line");
            note.RequestId=Guid.NewGuid();note.DocumentNumber="CN-TEST-02";await Reject(()=>gst.RecordAsync(note),"manual goods cannot be cumulatively credited beyond the original invoice");
            using(var web=new Factory())
            using(var rendered=web.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>services.AddSingleton<StockFlow.Application.Interfaces.IInvoiceRepository>(new SnapshotInvoiceRepository(taxInvoice)))))
            using(var client=rendered.CreateClient())
            {
                client.DefaultRequestHeaders.Add("Test-Role","Admin");
                var html=await client.GetStringAsync("/Sales/Invoice/"+mappedId);
                Check(html.Contains("GST breakdown by rate"),"Razor invoice prints rate-wise breakdown from recorded GST facts");
                Check(html.Contains("Manual Item")&&html.Contains("Small cord")&&html.Contains("MRP:")&&html.Contains("Transient counter item")&&html.Contains("610910")&&html.Contains("Item Subtotal")&&html.Contains("Invoice Discount"),"actual Razor invoice renders manual name, note, MRP, percentage and HSN for A4/thermal printing");
                Directory.CreateDirectory("tests/bin/fast-billing-ui-fixtures");await File.WriteAllTextAsync("tests/bin/fast-billing-ui-fixtures/invoice-manual.html",html);
            }
            Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==before,"all manual workflows leave inventory unchanged");
            var unregisteredStore=await setup.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'OPTIONAL-GST','Synthetic unregistered shop') RETURNING store_id");
            var unregisteredLocation=await setup.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(1,@store,'Counter','SALES_FLOOR') RETURNING location_id",new{store=unregisteredStore});
            scope.StoreId=unregisteredStore;scope.LocationId=unregisteredLocation;
            var unregistered=await gst.SaveRegistrationAsync(new(){BusinessId=business,StateCode="29",Address="Synthetic unregistered shop address",RegistrationType="UNREGISTERED",EffectiveFrom=today});
            await gst.SaveMappingAsync(new(){RegistrationId=unregistered,StoreId=unregisteredStore,BusinessProfileCode=profile,EffectiveFrom=today});
            var unregisteredCustomer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="Unregistered private shop customer"});
            var unregisteredBill=Request();unregisteredBill.CustomerId=unregisteredCustomer;unregisteredBill.LocationId=unregisteredLocation;unregisteredBill.Items[0].ManualItem!.TaxRuleId=0;
            await Pay(unregisteredBill);var unregisteredId=await sales.CreateSaleAsync(unregisteredBill,1);
            Check((await new InvoiceRepository(factory).GetInvoiceAsync(unregisteredId))!.Gst is null&&await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM gst_documents WHERE sale_id=@id",new{id=unregisteredId})==0,"explicitly unregistered shop can post an ordinary bill without a GST document");
            Check((await gst.ReportAsync(new(){RegistrationId=unregistered,From=today,To=today,Report="sales"})).LegacySalesWithoutSnapshots==0,"ordinary unregistered bills are not reported as missing GST snapshots");
            Console.WriteLine("Fast billing checks passed on a disposable database clone.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private static async Task Reject(Func<Task> action,string message){try{await action();}catch(ValidationException){Check(true,message);return;}catch(PostgresException ex)when(ex.SqlState is "42501" or "23514"){Check(true,message);return;}throw new Exception("FAIL: "+message);}
    private sealed class SnapshotInvoiceRepository(StockFlow.Application.DTOs.Invoices.InvoiceResponse invoice):StockFlow.Application.Interfaces.IInvoiceRepository
    {
        public Task<StockFlow.Application.DTOs.Invoices.InvoiceResponse?> GetInvoiceAsync(long saleId)=>Task.FromResult<StockFlow.Application.DTOs.Invoices.InvoiceResponse?>(invoice);
    }
}
