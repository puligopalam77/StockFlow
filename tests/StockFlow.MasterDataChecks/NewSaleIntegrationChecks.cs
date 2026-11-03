using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class NewSaleIntegrationChecks
{
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
  var db="new_sale_test_"+Guid.NewGuid().ToString("N");
  await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
  // Clone an existing disposable 069 fixture; never connect to the business database.
  await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var setup=new NpgsqlConnection(host+db);await setup.OpenAsync();
   foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
   await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
   await setup.ExecuteAsync("SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();
   var factory=new DbConnectionFactory(config,scope);var variants=new VariantRepository(factory);
   var service=new ProductService(new ProductRepository(factory),new CategoryRepository(factory),new BrandRepository(factory),scope,null,variants);
   var sales=new SalesRepository(factory);var intake=new StockIntakeRepository(factory);var scan=new ScanService(new ScanRepository(factory));
   var company=await new CompanySettingsRepository(factory).GetAsync();
   async Task<(int Product,VariantResponse[] Variants)> Register(string name,int count=1,bool serial=false){
    await using var runtime=factory.CreateConnection();var request=Guid.NewGuid();
    var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(4,@request,NULL)",new{request});
    var codes=count>1?(await runtime.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@code,@request,@op,@count,NULL)",new{code,request,op=Guid.NewGuid(),count}))!:[code!];
    var id=await service.CreateAsync(new(){RegistrationRequestId=request,ParentProductCode=code,ProductName=name,CategoryId=4,TrackingType=serial?"SERIAL":"QUANTITY",HasVariants=count>1,PurchasePrice=40,SellingPrice=100,
     RegistrationDefaults=count>1?new(){VariantAxes=["edition"],PurchasePrice=40,SellingPrice=100}:null,
     Variants=count>1?codes.Select((sku,i)=>new RegistrationVariantRequest{Sku=sku,VariantName="Edition "+i,Attributes=new(){["edition"]=i.ToString()},PurchasePrice=40,SellingPrice=100}).ToList():null});
    return(id,(await variants.GetAsync(id,null,false)).OrderBy(v=>v.VariantId).ToArray());
   }
   var family=await Register("Counter integration variants",2);var last=await Register("Counter integration last unit");var tracked=await Register("Counter integration serialized",serial:true);
   await intake.PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=family.Product,VariantId=family.Variants[0].VariantId,Quantity=3},new(){ProductId=family.Product,VariantId=family.Variants[1].VariantId,Quantity=2},new(){ProductId=last.Product,VariantId=last.Variants[0].VariantId,Quantity=1},new(){ProductId=tracked.Product,VariantId=tracked.Variants[0].VariantId,Quantity=1,SerialNumbers=["COUNTER-SERIAL-01"]}]},true,1);
   var exact=family.Variants[1];Check((await scan.LookupAsync(exact.Sku))?.VariantId==exact.VariantId,"exact scan selects variant SKU, not parent family");
   await using(var mapping=factory.CreateConnection())await mapping.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) VALUES(@VariantId,'COUNTER-MFR-01','MANUFACTURER')",exact);
   Check((await scan.LookupAsync("COUNTER-MFR-01"))?.VariantId==exact.VariantId&&(await variants.SearchForSaleAsync("COUNTER-MFR-01")).Single().VariantId==exact.VariantId,"manufacturer barcode scan and search resolve the same SKU");
   Check((await variants.SearchForSaleAsync(family.Variants[0].ParentProductCode!)).Count(v=>v.ProductId==family.Product)==2,"parent code search exposes both sellable variants");
   var unit=(await new InventoryRepository(factory).GetUnitsAsync(tracked.Product,null,true,0)).Single();
   Check((await scan.LookupAsync(unit.SerialNumber!))?.ProductUnitId==unit.ProductUnitId,"serial lookup selects the exact physical unit");
   var customer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="Counter integration customer"});
   CreateSaleRequest Request(int product,long variant,long? unitId=null)=>new(){CustomerId=customer,LocationId=1,RequestId=Guid.NewGuid(),PricesIncludeTax=company.DefaultTaxMode=="INCLUSIVE",Items=[new(){ProductId=product,VariantId=variant,ProductUnitId=unitId,Quantity=1,SellingPrice=100}]};
   async Task QuoteAndPay(CreateSaleRequest r){r.Payments=[new(){PaymentMode="CASH",Amount=(await sales.QuoteAsync(r)).TotalAmount}];}
   var cards=await Register("Counter card compatibility");
   await intake.PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=cards.Product,VariantId=cards.Variants[0].VariantId,Quantity=3}]},true,1);
   foreach(var type in new string?[]{null,"DEBIT","CREDIT"}){
    var card=Request(cards.Product,cards.Variants[0].VariantId);var cardQuote=await sales.QuoteAsync(card);card.Payments=[new(){PaymentMode="CARD",CardType=type??"DEBIT",CollectionConfirmed=true,Amount=cardQuote.TotalAmount,ReferenceNumber="CARD-"+(type??"LEGACY")}];
    var cardId=await sales.CreateSaleAsync(card,1);
    if(type is null){card.Payments[0].CardType=null;await setup.ExecuteAsync("UPDATE payments SET request_payload=NULL WHERE sale_id=@id; UPDATE sales SET request_payload=@payload::jsonb WHERE sale_id=@id",new{id=cardId,payload=System.Text.Json.JsonSerializer.Serialize(card)});}
    Check(await sales.CreateSaleAsync(card,1)==cardId,"card request replay returns the same invoice");
    var cardInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(cardId))!;
    Check(cardInvoice.Payments.Single().PaymentMode=="CARD"&&cardInvoice.Payments.Single().CardType==type&&cardInvoice.AmountReceived==cardQuote.TotalAmount&&cardInvoice.CreditAmount==0,"generic/debit/credit cards round-trip metadata and remain collections");
    Check((await sales.GetByIdAsync(cardId))!.Payments.Single().CardType==type,"sale payment history reads optional card subtype");
    if(type is not null){
     card.Payments[0].CardType=type=="DEBIT"?"CREDIT":"DEBIT";var rejected=false;
     try{await sales.CreateSaleAsync(card,1);}catch(System.ComponentModel.DataAnnotations.ValidationException){rejected=true;}
     Check(rejected,"same request ID cannot be replayed with a different card subtype");card.Payments[0].CardType=type;
    }
   }
   var unauthorized=Request(family.Product,exact.VariantId);unauthorized.Items[0].SellingPrice=90;await QuoteAndPay(unauthorized);
   try{await sales.CreateSaleAsync(unauthorized,1);throw new Exception("Unauthorized price override accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,"server rejects unauthorized price overrides before posting stock");}
   var mixed=Request(family.Product,exact.VariantId);mixed.CustomerId=customer;var quote=await sales.QuoteAsync(mixed);mixed.Payments=[new(){PaymentMode="CASH",Amount=20},new(){PaymentMode="UPI",Amount=quote.TotalAmount-20}];mixed.CashTendered=50;
   // Create an ordinary paid fixture, then seed historical split/CREDIT metadata only in this disposable database.
   var seed=Request(family.Product,exact.VariantId);seed.RequestId=mixed.RequestId;await QuoteAndPay(seed);await sales.CreateSaleAsync(seed,1);
   await setup.ExecuteAsync("DELETE FROM payments WHERE sale_id=(SELECT sale_id FROM sales WHERE request_id=@RequestId); INSERT INTO payments(tenant_id,location_id,sale_id,payment_mode,amount,created_by) SELECT tenant_id,location_id,sale_id,'CASH',20,created_by FROM sales WHERE request_id=@RequestId; INSERT INTO payments(tenant_id,location_id,sale_id,payment_mode,amount,created_by) SELECT tenant_id,location_id,sale_id,'UPI',total_amount-20,created_by FROM sales WHERE request_id=@RequestId; UPDATE sales SET request_payload=@payload::jsonb WHERE request_id=@RequestId",new{mixed.RequestId,payload=System.Text.Json.JsonSerializer.Serialize(mixed)});
   var ids=await Task.WhenAll(sales.CreateSaleAsync(mixed,1),sales.CreateSaleAsync(mixed,1));Check(ids[0]==ids[1],"concurrent duplicate completion returns one invoice");
   // Seed the pre-policy CREDIT record only in this disposable clone. Production
   // posting below must reject new CREDIT, while this historical invoice remains collectible.
   await setup.ExecuteAsync("SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'; UPDATE payments SET payment_mode='CREDIT' WHERE sale_id=@id AND payment_mode='UPI'",new{id=ids[0]});
   mixed.Payments[1].PaymentMode="CREDIT";
   await setup.ExecuteAsync("UPDATE sales SET request_payload=@payload::jsonb WHERE sale_id=@id",new{id=ids[0],payload=System.Text.Json.JsonSerializer.Serialize(mixed)});
   Check(await sales.CreateSaleAsync(mixed,1)==ids[0],"historical completed CREDIT request replay returns original invoice without new posting");
   var rejectedCredit=Request(family.Product,family.Variants[0].VariantId);rejectedCredit.CustomerId=customer;rejectedCredit.Payments=[new(){PaymentMode="CREDIT",Amount=(await sales.QuoteAsync(rejectedCredit)).TotalAmount}];
   try{await sales.CreateSaleAsync(rejectedCredit,1);throw new Exception("New CREDIT sale accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
   Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM sales WHERE request_id=@RequestId",rejectedCredit)==0,"new-credit rejection creates no invoice or stock posting");
   var invoice=(await new InvoiceRepository(factory).GetInvoiceAsync(ids[0]))!;
   Check(invoice.TotalAmount==quote.TotalAmount&&invoice.AmountReceived==20&&invoice.CreditAmount==quote.TotalAmount-20&&invoice.CashChange==30,"invoice total, collection, cash change and credit remain distinct");
   Check(invoice.Items.Single().Sku==exact.Sku,"invoice snapshots the selected sellable SKU");
   await using var c=factory.CreateConnection();
   Check(await c.ExecuteScalarAsync<decimal>("SELECT sf_customer_credit_balance(@customer)",new{customer})==quote.TotalAmount-20,"customer receivable equals the unpaid amount once");
   Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@id",new{id=ids[0]})==1,"duplicate completion posts one stock movement");
   Check((await variants.GetAsync(family.Product,null,false)).Single(v=>v.VariantId==exact.VariantId).CurrentStock==1&&(await variants.GetAsync(family.Product,null,false)).Single(v=>v.VariantId!=exact.VariantId).CurrentStock==3,"stock is deducted from the selected variant only");
   var a=Request(last.Product,last.Variants[0].VariantId);var b=Request(last.Product,last.Variants[0].VariantId);await QuoteAndPay(a);await QuoteAndPay(b);
   async Task<bool> Attempt(CreateSaleRequest r){try{await sales.CreateSaleAsync(r,1);return true;}catch(System.ComponentModel.DataAnnotations.ValidationException){return false;}}
   Check((await Task.WhenAll(Attempt(a),Attempt(b))).Count(x=>x)==1,"two cashiers quoting the last unit cannot both finalize");
   var serialized=Request(tracked.Product,tracked.Variants[0].VariantId,unit.ProductUnitId);
   var competingSerial=Request(tracked.Product,tracked.Variants[0].VariantId,unit.ProductUnitId);
   await QuoteAndPay(serialized);await QuoteAndPay(competingSerial);
   var serialRace=await Task.WhenAll(Attempt(serialized),Attempt(competingSerial));
   Check(serialRace.Count(won=>won)==1,"two distinct checkout requests selecting the same physical serial cannot both finalize");
   var serialSale=await sales.CreateSaleAsync(serialRace[0]?serialized:competingSerial,1);
   Check(!(await scan.LookupAsync(unit.SerialNumber!))!.IsAvailable,"completion makes the exact serialized unit unavailable");
   var originalLine=(await sales.GetByIdAsync(serialSale))!.Lines.Single();var returns=new OperationsRepository(factory);
   var ret=new SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="Counter integration accepted return",Items=[new(){SaleItemId=originalLine.SaleItemId,Quantity=1}]};
   Check(await returns.ReturnSaleAsync(serialSale,ret,1)==await returns.ReturnSaleAsync(serialSale,ret,1)&&(await scan.LookupAsync(unit.SerialNumber!))!.IsAvailable,"return workflow restores the original unit exactly once");
   var checkout=await Register("Paid checkout coverage");
   await intake.PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=checkout.Product,VariantId=checkout.Variants[0].VariantId,Quantity=2}]},true,1);
   var bank=Request(checkout.Product,checkout.Variants[0].VariantId);var payable=(await sales.QuoteAsync(bank)).TotalAmount;bank.Payments=[new(){PaymentMode="BANK_TRANSFER",Amount=payable,ReferenceNumber="PAID-BANK",CollectionConfirmed=true}];
   var bankId=await sales.CreateSaleAsync(bank,1);Check(await sales.CreateSaleAsync(bank,1)==bankId,"paid bank transfer retries return one invoice");
   Check(await sales.RecoverCompletedAsync(bank,1)==bankId,"lost-success response can recover the original paid invoice");
   bank.Items[0].Quantity=2;
   try{await sales.CreateSaleAsync(bank,1);throw new Exception("Changed retry payload accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
   Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM sales WHERE request_id=@RequestId",bank)==1,"changed payload with the same request identity cannot create a second sale");bank.Items[0].Quantity=1;
   var absent=Request(checkout.Product,checkout.Variants[0].VariantId);await QuoteAndPay(absent);
   Check(await sales.RecoverCompletedAsync(absent,1) is null,"verification-only recovery never posts an unsaved request");
   var split=Request(checkout.Product,checkout.Variants[0].VariantId);split.Payments=[new(){PaymentMode="CASH",Amount=20},new(){PaymentMode="UPI",Amount=payable-20,ReferenceNumber="PAID-UPI"}];
   try{await sales.CreateSaleAsync(split,1);throw new Exception("New split accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
   split.Payments=[new(){PaymentMode="CASH",Amount=payable}];var splitId=await sales.CreateSaleAsync(split,1);Check((await new OperationsRepository(factory).SaleAsync(splitId)).Outstanding==0,"single collected method fully settles new invoice");
   Check((await variants.GetAsync(checkout.Product,null,false)).Single().CurrentStock==0,"fully paid invoices deduct each quantity once");
   await intake.PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=checkout.Product,VariantId=checkout.Variants[0].VariantId,Quantity=1}]},true,1);
   foreach(var allocations in new List<CreatePaymentRequest>[] {
    [new(){PaymentMode="CASH",Amount=payable-1}],
    [new(){PaymentMode="CASH",Amount=payable/2},new(){PaymentMode="CASH",Amount=payable/2}],
    [new(){PaymentMode="UPI",Amount=20,ReferenceNumber="DUP"},new(){PaymentMode="UPI",Amount=payable-20,ReferenceNumber="dup"}]
   }){
    var invalid=Request(checkout.Product,checkout.Variants[0].VariantId);invalid.Payments=allocations;
    try{await sales.CreateSaleAsync(invalid,1);throw new Exception("Invalid new payment accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
    Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM sales WHERE request_id=@RequestId",invalid)==0,"invalid/incomplete/duplicate allocations leave no posted invoice");
   }
   Check((await variants.GetAsync(checkout.Product,null,false)).Single().CurrentStock==1,"rejected payment allocations preserve available stock");
   // Exercise Customer Bills against the same real ledger used by posting and reports.
   var number=(await sales.GetByIdAsync(ids[0]))!.InvoiceNumber;
   var history=await sales.GetListAsync(number,null,0,10,null,null,null);
   Check(history.Summary.Count==1&&history.Summary.InvoiceCount==1&&history.Rows.Single().LineCount==1&&history.Rows.Single().PaymentStatus=="PARTIAL"
    &&history.Summary.AmountCollected==20&&history.Summary.OutstandingDues==quote.TotalAmount-20,"history aggregates invoice lines, credit allocations and cash once");
   Check((await sales.GetListAsync(exact.Sku,null,0,10,null,null,null)).Rows.Any(r=>r.SaleId==ids[0]),"history searches saved variant SKU snapshots");
   foreach(var filter in new[]{"DEBIT_CARD","CREDIT_CARD","CARD"})
    Check((await sales.GetListAsync("Counter card compatibility",null,0,10,null,null,null,paymentMethod:filter)).Summary.Count==1,"history preserves distinct "+filter+" receipts without treating credit as a card");
   var returnedHistory=await sales.GetListAsync((await sales.GetByIdAsync(serialSale))!.InvoiceNumber,null,0,10,null,null,"FULLY_RETURNED");
   Check(returnedHistory.Rows.Single().PaymentStatus=="ADJUSTED"&&returnedHistory.Summary.NetSales==0&&returnedHistory.Summary.OutstandingDues==0,"fully returned invoice is adjusted without inventing an outstanding debt");
   Check((await returns.SaleAsync(serialSale)).ReturnedLines.Single().Sku==originalLine.Sku,"return details preserve the original SKU and quantity");
   // Backdate only this disposable fixture; no business database or posted values are changed.
   await setup.ExecuteAsync("SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'; UPDATE sales SET sale_date='2001-01-15 10:00' WHERE sale_id=@id; UPDATE payments SET payment_date='2001-01-15 10:00' WHERE sale_id=@id",new{id=ids[0]});
   var reports=new ReportingRepository(factory,scope);var today=DateTime.Today;
   var beforeReport=await reports.FinancialAsync(today,today);
   var cashClosing=new CashClosingRepository(factory,scope,Options.Create(new ShopTimeOptions()),TimeProvider.System);
   var cashTodayBefore=(await cashClosing.GetAsync(today)).Totals;
   var historicalCashBefore=(await cashClosing.GetAsync(new DateTime(2001,1,15))).Totals;
   var stockBefore=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@id",new{id=ids[0]});
   var receipt=new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CARD",CardType="CREDIT",Amount=30,ReferenceNumber="HISTORY-LATER-RECEIPT"};
   var receipts=await Task.WhenAll(returns.AddCustomerPaymentAsync(ids[0],receipt,1),returns.AddCustomerPaymentAsync(ids[0],receipt,1));
   Check(receipts[0]==receipts[1],"concurrent later-payment retries create one collection");
   history=await sales.GetListAsync(number,null,0,10,new DateTime(2001,1,1),new DateTime(2001,1,31),null);
   Check(history.Summary.NetSales==quote.TotalAmount&&history.Summary.AmountCollected==50&&history.Summary.OutstandingDues==quote.TotalAmount-50
    &&history.Rows.Single().PaymentMode=="MIXED","old invoice cohort includes later settlements with unchanged sales revenue");
   var afterReport=await reports.FinancialAsync(today,today);
   Check(afterReport.Sum(r=>r.SalesValue)==beforeReport.Sum(r=>r.SalesValue)&&afterReport.Sum(r=>r.Collections)==beforeReport.Sum(r=>r.Collections)+30,"today's collection report includes old-invoice payment without increasing today's revenue");
   Check((await cashClosing.GetAsync(today)).Totals.SalesCash==cashTodayBefore.SalesCash,"Later card collection on old credit invoice is excluded from Cash Closing cash totals");
   Check((await cashClosing.GetAsync(new DateTime(2001,1,15))).Totals.SalesCash==historicalCashBefore.SalesCash&&historicalCashBefore.SalesCash>=20,"Historical Cash Closing retains original cash at its payment-date cutoff after a later collection");
   Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@id",new{id=ids[0]})==stockBefore,"later collections never deduct stock again");
   var ops=await returns.SaleAsync(ids[0]);Check(ops.Payments.Any(p=>p.CardType=="CREDIT"&&p.Amount==30)&&ops.Outstanding==quote.TotalAmount-50,"payment history retains actual receipt date, subtype and invoice-specific due");
   var rejectReceipt=new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CASH",Amount=quote.TotalAmount};
   try{await returns.AddCustomerPaymentAsync(ids[0],rejectReceipt,1);throw new Exception("Overpayment accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
   receipt=new(){LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="UPI",Amount=quote.TotalAmount-50,ReferenceNumber="HISTORY-FINAL-RECEIPT"};await returns.AddCustomerPaymentAsync(ids[0],receipt,1);
   history=await sales.GetListAsync(number,null,0,10,null,null,null,paymentStatus:"PAID");Check(history.Rows.Single().OutstandingAmount==0,"full later settlement changes payment status to Paid");
   var mixedLine=(await sales.GetByIdAsync(ids[0]))!.Lines.Single();
   await returns.ReturnSaleAsync(ids[0],new(){LocationId=1,RequestId=Guid.NewGuid(),Notes="History refund check",RefundMode="CASH",RefundAmount=quote.TotalAmount,Items=[new(){SaleItemId=mixedLine.SaleItemId,Quantity=1}]},1);
   history=await sales.GetListAsync(number,null,0,10,null,null,null);Check(history.Summary.NetSales==0&&history.Summary.AmountCollected==0&&history.Summary.OutstandingDues==0,"refund reduces net receipts once, return reduces sales once, settled balance remains zero");
   var unpaid=Request(family.Product,family.Variants[0].VariantId);unpaid.CustomerId=customer;unpaid.Items[0].Quantity=2;
   var unpaidQuote=await sales.QuoteAsync(unpaid);unpaid.Payments=[new(){PaymentMode="CASH",Amount=unpaidQuote.TotalAmount}];var unpaidId=await sales.CreateSaleAsync(unpaid,1);
   await setup.ExecuteAsync("UPDATE payments SET payment_mode='CREDIT' WHERE sale_id=@id",new{id=unpaidId});
   var unpaidSale=(await sales.GetByIdAsync(unpaidId))!;history=await sales.GetListAsync(unpaidSale.InvoiceNumber,customer,0,10,null,null,null,paymentStatus:"UNPAID",paymentMethod:"CREDIT");
   Check(history.Summary.AmountCollected==0&&history.Rows.Single().OutstandingAmount==unpaidQuote.TotalAmount,"unpaid credit is never reported as a collection");
   await returns.ReturnSaleAsync(unpaidId,new(){LocationId=1,RequestId=Guid.NewGuid(),Notes="Partial credit adjustment",Items=[new(){SaleItemId=unpaidSale.Lines.Single().SaleItemId,Quantity=1}]},1);
   history=await sales.GetListAsync(unpaidSale.InvoiceNumber,null,0,10,null,null,"PARTIALLY_RETURNED");
   Check(history.Summary.NetSales==unpaidQuote.TotalAmount/2&&history.Summary.OutstandingDues==unpaidQuote.TotalAmount/2&&history.Summary.AmountCollected==0,"partial return credit adjustment reduces receivable without inventing cash or refund");
   foreach(var invalid in new[]{new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CASH",Amount=1.001m},new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CREDIT",Amount=1},new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="UPI",CardType="CREDIT",Amount=1}}){
    try{await returns.AddCustomerPaymentAsync(unpaidId,invalid,1);throw new Exception("Invalid collection accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){}
   }
   Check((await returns.SaleAsync(unpaidId)).Outstanding==unpaidQuote.TotalAmount/2,"invalid receipt precision, receivable mode and Card subtype leave balances unchanged");
   scope.LocationId=2;Check((await sales.GetListAsync(number,null,0,10,null,null,null)).Summary.Count==0,"Customer Bills and all four totals respect location isolation");scope.LocationId=1;
   await Register("Counter search cap",30);await Register("Counter search cap extra",2);Check((await variants.SearchForSaleAsync("Counter search cap")).Count()==30,"billing search is bounded in SQL for large catalogs");
   scope.LocationId=2;Check(await scan.LookupAsync(unit.SerialNumber!) is null&&await sales.GetByIdAsync(ids[0]) is null,"foreign location cannot resolve the unit or invoice");scope.LocationId=1;
   scope.TenantId=99999;Check(!(await variants.SearchForSaleAsync("Counter")).Any()&&(await sales.GetListAsync(null,null,0,10,null,null,null)).Summary.Count==0,"foreign tenant receives no catalog or invoice totals");scope.TenantId=1;
   await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='BIKE_ACCESSORIES' WHERE tenant_id=1");
   Check(!(await variants.SearchForSaleAsync("Counter")).Any()&&await scan.LookupAsync(exact.Sku) is null&&(await sales.GetListAsync(null,null,0,10,null,null,null)).Summary.Count==0,"stale shop context cannot read the other shop catalog or invoice totals");
   Console.WriteLine("PASS: New Sale integration on a temporary clone only.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
