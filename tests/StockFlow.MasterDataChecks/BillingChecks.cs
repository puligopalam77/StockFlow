using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.DTOs.Sales;
namespace StockFlow.MasterDataChecks;

static class BillingChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
        foreach(var tendered in new[]{850m,1000m}){
            var cash=new CreateSaleRequest{RequestId=Guid.NewGuid(),LocationId=1,CustomerId=1,CashTendered=tendered,Items=[new(){ProductId=1,Quantity=1,SellingPrice=850}],Payments=[new(){PaymentMode="CASH",Amount=850}]};
            Check(StockFlow.Application.Common.SaleRules.CalculateLines(cash,[0m],true).TotalAmount==850&&cash.Payments.Single().Amount==850,"cash tendered "+tendered+" preserves the 850 invoice and recorded payment");
            cash.CashTendered=849;
            var rejected=false;try{StockFlow.Application.Common.SaleRules.CalculateLines(cash,[0m],true);}catch(System.ComponentModel.DataAnnotations.ValidationException){rejected=true;}
            Check(rejected,"insufficient cash remains rejected by server validation");
        }
        foreach(var type in new string?[]{null," debit ","credit"}){
            var card=new CreateSaleRequest{RequestId=Guid.NewGuid(),LocationId=1,Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=100}],Payments=[new(){PaymentMode="CARD",CardType=type,Amount=118}]};
            Check(StockFlow.Application.Common.SaleRules.CalculateLines(card,[18m],true).TotalAmount==118 && card.Payments[0].PaymentMode=="CARD","generic/debit/credit card preserve CARD accounting");
            Check(type is not null || !System.Text.Json.JsonSerializer.Serialize(card).Contains("CardType"),"absent subtype preserves legacy serialized idempotency payload");
        }
        foreach(var (mode,type) in new[]{("CREDIT","CREDIT"),("UPI","DEBIT"),("CARD","UNKNOWN"),("DEBIT_CARD","DEBIT")}){
            var invalid=new CreateSaleRequest{RequestId=Guid.NewGuid(),LocationId=1,CustomerId=1,Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=100}],Payments=[new(){PaymentMode=mode,CardType=type,Amount=118}]};
            var rejected=false;try{StockFlow.Application.Common.SaleRules.CalculateLines(invalid,[18m],true);}catch(System.ComponentModel.DataAnnotations.ValidationException){rejected=true;}Check(rejected,"unsupported identifiers/subtypes cannot masquerade as card collections");
        }
        var collection=new StockFlow.Application.DTOs.Invoices.InvoiceResponse{Payments=[new(){PaymentMode="CARD",CardType="CREDIT",Amount=100},new(){PaymentMode="CREDIT",Amount=20}]};
        Check(collection.AmountReceived==100&&collection.CreditAmount==20,"Credit Card is collected money; customer CREDIT is a receivable");
        var inlineDiscount=new CreateSaleRequest{PricesIncludeTax=false,Items=[new(){ProductId=1,VariantId=1,Quantity=2,SellingPrice=4000,DiscountAmount=100}],Payments=[]};
        var quote=StockFlow.Application.Common.SaleRules.CalculateLines(inlineDiscount,[18m],false);
        Check(quote.Subtotal==7900m && quote.TaxAmount==1422m && quote.TotalAmount==9322m,"inline discount remains one line amount: 2 x 4000 minus 100 is 7900 before tax");
        var illustrative=new CreateSaleRequest{PricesIncludeTax=false,Items=[new(){ProductId=1,Quantity=5,SellingPrice=2500,DiscountPercent=7}]};
        var illustrativeQuote=StockFlow.Application.Common.SaleRules.CalculateLines(illustrative,[6m],false);
        Check(illustrativeQuote.Subtotal==11625&&illustrativeQuote.TaxAmount==697.50m&&illustrativeQuote.Items.Single().LineTotal==12322.50m,"synthetic arithmetic example: five at 2500, seven percent item discount, six percent exclusive tax includes tax in Line Total");
        inlineDiscount.DiscountAmount=50;
        Check(StockFlow.Application.Common.SaleRules.CalculateLines(inlineDiscount,[18m],false).TotalAmount==9272m,"invoice discount is applied separately after line discount and tax");
        inlineDiscount.DiscountAmount=0;inlineDiscount.PricesIncludeTax=true;
        var percentageInclusive=new CreateSaleRequest{PricesIncludeTax=true,Items=[new(){ProductId=1,Quantity=2,SellingPrice=118,DiscountPercent=10}]};
        var inclusiveQuote=StockFlow.Application.Common.SaleRules.CalculateLines(percentageInclusive,[18m],false);
        Check(inclusiveQuote.TotalAmount==212.4m&&inclusiveQuote.Subtotal==180&&inclusiveQuote.TaxAmount==32.4m&&inclusiveQuote.Items[0].DiscountAmount==23.6m,"percentage reduction on tax-inclusive prices preserves extracted tax and customer discount");
        quote=StockFlow.Application.Common.SaleRules.CalculateLines(inlineDiscount,[18m],false);
        Check(quote.TotalAmount==7900m && quote.Subtotal+quote.TaxAmount==7900m,"inclusive tax is extracted from the discounted line without adding tax again");
        var allocated=new CreateSaleRequest{AllocateInvoiceDiscount=true,PricesIncludeTax=false,DiscountAmount=25,Items=[new(){ProductId=1,Quantity=1,SellingPrice=2500,DiscountPercent=3}]};
        var allocatedQuote=StockFlow.Application.Common.SaleRules.CalculateLines(allocated,[5m],false);
        Check(allocatedQuote.ItemSubtotal==2500 && allocatedQuote.ItemDiscountAmount==75 && allocatedQuote.InvoiceDiscountAmount==25 && allocatedQuote.TaxableAmount==2400 && allocatedQuote.TaxAmount==120 && allocatedQuote.TotalAmount==2520,"invoice discount reduces taxable base before GST without double subtraction");
        allocated.Items=[new(){ProductId=1,Quantity=1,SellingPrice=100},new(){ProductId=2,Quantity=1,SellingPrice=100},new(){ProductId=3,Quantity=1,SellingPrice=100}];allocated.DiscountAmount=30;
        allocatedQuote=StockFlow.Application.Common.SaleRules.CalculateLines(allocated,[5m,18m,0m],false);
        Check(allocatedQuote.ItemSubtotal==300 && allocatedQuote.TaxableAmount==180 && allocatedQuote.Subtotal==270 && allocatedQuote.TaxAmount==20.7m && allocatedQuote.TotalAmount==290.7m,"mixed-rate discount allocation excludes exempt values from taxable amount");
        allocated.PricesIncludeTax=true;allocated.Items=[new(){ProductId=1,Quantity=1,SellingPrice=118}];allocated.DiscountAmount=11.8m;
        allocatedQuote=StockFlow.Application.Common.SaleRules.CalculateLines(allocated,[18m],false);
        Check(allocatedQuote.Subtotal==90 && allocatedQuote.TaxAmount==16.2m && allocatedQuote.TotalAmount==106.2m,"inclusive invoice discount extracts GST without adding it twice");
        var approvalHash=StockFlow.Application.Common.BillingApproval.Hash(allocated);allocated.AllocateInvoiceDiscount=null;
        Check(approvalHash!=StockFlow.Application.Common.BillingApproval.Hash(allocated),"allocation semantics invalidate an after-tax discount approval");
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var html=await client.GetStringAsync("/Sales/Create");Check(html.Contains("billing-layout")&&html.Contains("billing-simple-payment")&&html.Contains("billing-split-payment")&&!html.Contains("billing-options"),"billing combines scanner/cart with simple payment and optional controls");
        Check(html.Contains("id=\"quick-add-item\""),"authorized manual billing action is visible near scanning");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:MANUAL_ITEM");
        Check(!(await client.GetStringAsync("/Sales/Create")).Contains("id=\"quick-add-item\"")&&(int)(await client.GetAsync("/api/sales/manual-tax-rules")).StatusCode==403,"manual billing action and reviewed-rule endpoint require explicit permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check(html.Contains("id=\"product-results\"")&&!html.Contains("id=\"product-search\"")&&!html.Contains("id=\"find-customers\""),"unified product search and one customer picker replace duplicate controls");
        Check((await client.GetFromJsonAsync<JsonArray>("/api/products/variants?billing=true&term="))!.Count==0,"empty billing search returns no catalog dump");
        Check((int)(await client.GetAsync("/api/products/variants?billing=true&term="+new string('x',201))).StatusCode==400,"bounded billing search rejects oversized input");
        client.DefaultRequestHeaders.Add("Test-Deny","PRODUCT_VARIANTS");Check((int)(await client.GetAsync("/api/products/variants?billing=true&term=helmet")).StatusCode==403,"billing search retains catalog permission checks");client.DefaultRequestHeaders.Remove("Test-Deny");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;var output=Path.Combine(root!.FullName,"tests/bin/billing-ui-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Billing.html"),html);
        var invoiceHtml=await client.GetStringAsync("/Sales/Invoice/1");await File.WriteAllTextAsync(Path.Combine(output,"Invoice.html"),invoiceHtml);
        await File.WriteAllTextAsync(Path.Combine(output,"BillingSettings.html"),await client.GetStringAsync("/Settings/Company"));
        client.DefaultRequestHeaders.Add("Test-Deny","COMPANY_SETTINGS");
        Check((int)(await client.GetAsync("/api/settings/billing-policy")).StatusCode==403,"policy settings endpoint requires Administration access");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        Check(invoiceHtml.Contains("invoice-print-layout") && invoiceHtml.Contains("80 mm receipt") && invoiceHtml.Contains("New bill"),"invoice exposes receipt format and next-bill action");
        client.DefaultRequestHeaders.Add("Test-Deny","CUSTOMERS,PRODUCT_VARIANTS,INVENTORY,INVOICES,SCAN");var limited=await client.GetStringAsync("/Sales/Create");Check(!limited.Contains("id=\"customer-search\"")&&!limited.Contains("id=\"create-customer\"")&&!limited.Contains("id=\"product-search\"")&&!limited.Contains("id=\"sale-unit\"")&&!limited.Contains("id=\"sale-scan\"")&&limited.Contains("<option value=\"\">Select customer</option>"),"lookup, scanner, unit and customer controls follow module permissions; no walk-in checkout inferred from embedded help text");await File.WriteAllTextAsync(Path.Combine(output,"BillingLimited.html"),limited);client.DefaultRequestHeaders.Remove("Test-Deny");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","CUSTOMERS:CREATE");limited=await client.GetStringAsync("/Sales/Create");Check(limited.Contains("id=\"customer-search\"")&&!limited.Contains("id=\"create-customer\""),"customer lookup works independently of quick creation permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var request=new CreateSaleRequest{RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,Quantity=1,SellingPrice=10}],Payments=[new(){PaymentMode="CASH",Amount=11.8m}]};
        Check(!html.Contains("id=\"payment-due-date\"")&&!html.Contains("id=\"credit-limit-options\"")&&!html.Contains("value=\"CREDIT\""),"New Sale removes receivable controls and credit mode without removing historical APIs");
        request.PaymentDueDate=DateTime.Today.AddDays(7);
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"payment due date cannot be recorded for walk-in cash");
        request.PaymentDueDate=null;
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"new checkout requires a registered customer");
        request.CustomerId=1;
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==201,"full cash payment supports a registered customer sale");
        Check((int)(await client.PostAsJsonAsync("/api/sales/recover",request)).StatusCode==200,"previous billing draft can retrieve a matching completed invoice");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");Check((int)(await client.PostAsJsonAsync("/api/sales/recover",request)).StatusCode==403,"legacy recovery requires billing permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        request.Payments=[new(){PaymentMode="CREDIT",Amount=11.8m}];Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"walk-in credit rejected by API");
        request.CustomerId=1;Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"named customer cannot bypass fully paid checkout with CREDIT");
        request.Payments=[new(){PaymentMode="UPI",Amount=1}];Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"incomplete payments rejected by API");
        request.Payments=[new(){PaymentMode="CASH",Amount=5.9m},new(){PaymentMode="CASH",Amount=5.9m}];
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"identical split allocations rejected by backend");
        request.Payments=[new(){PaymentMode="UPI",Amount=5,ReferenceNumber="same"},new(){PaymentMode="UPI",Amount=6.8m,ReferenceNumber="SAME"}];
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"repeated split transaction references rejected by backend");
        request.Payments=[new(){PaymentMode="CASH",Amount=11.801m}];
        Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"fractional payment precision rejected before posting");
        request.Items=[new(){ProductId=1,ProductUnitId=10,Quantity=1,SellingPrice=10},new(){ProductId=1,ProductUnitId=10,Quantity=1,SellingPrice=10}];Check((int)(await client.PostAsJsonAsync("/api/sales",request)).StatusCode==400,"duplicate physical unit rejected before stock write");
        Console.WriteLine("Billing application checks passed; no application database accessed.");
    }
}
