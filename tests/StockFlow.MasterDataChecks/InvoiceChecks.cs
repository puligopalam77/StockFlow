using System.Net.Http.Json;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
namespace StockFlow.MasterDataChecks;
internal static class InvoiceChecks
{
    public static async Task Run(Factory factory)
    {
        using var anonymous = factory.CreateClient();
        Check((int)(await anonymous.GetAsync("/api/invoices/1")).StatusCode == 401, "anonymous invoice API denied");
        foreach (var role in new[] { "Admin", "Sales", "Service" })
        {
            using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", role);
            foreach (var url in new[] { "/api/invoices/1", "/api/sales/1/invoice", "/Sales/Invoice/1", "/Sales/Invoice/2" })
            {
                var response = await client.GetAsync(url);
                Check(response.IsSuccessStatusCode && response.Headers.CacheControl?.NoStore == true, role + " invoice access/no-store " + url);
            }
            Check((int)(await client.GetAsync("/api/invoices/999")).StatusCode == 404, "missing invoice 404");
            Check((int)(await client.GetAsync("/Sales/Invoice/999")).StatusCode == 404, "missing invoice page 404");
            var html = await client.GetStringAsync("/Sales/Invoice/2");
            Check(html.Contains("CANCELLED") && html.Contains("invoice-paper") && html.Contains("save-invoice-pdf"), "cancelled invoice retains print/PDF actions");
            Check(!html.Contains("<script>alert(1)</script>") && html.Contains("&lt;script&gt;"), "invoice fields HTML-encoded");
            var customerInvoice=await client.GetStringAsync("/Sales/Invoice/3");
            Check(customerInvoice.Contains("Rajesh Kumar") && customerInvoice.Contains("9876543210") && customerInvoice.Contains("45 MG Road") && customerInvoice.Contains("36ABCDE1234F1Z5"),"invoice renders saved customer name, mobile, address and GSTIN");
            if(role=="Admin")
            {
                var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
                var output=Path.Combine(root!.FullName,"tests/bin/invoice-design-fixtures");Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output,"Invoice.html"),customerInvoice);
                foreach(var id in new[]{5,6,7,8})
                {
                    var rendered=await client.GetStringAsync("/Sales/Invoice/"+id);
                    await File.WriteAllTextAsync(Path.Combine(output,$"Invoice-{id}.html"),rendered);
                    Check(!rendered.Contains("INTERNAL-NOTE-DO-NOT-PRINT"),"unclassified sale notes stay private");
                    if(id==5)Check(!rendered.Contains("Serial / Unit ID") && !rendered.Contains("Credit recorded at sale") && !rendered.Contains("Warranty summary") && !rendered.Contains("Item Discounts") && !rendered.Contains("Invoice Discount") && !rendered.Contains("Reference</th>"),"simple receipt omits empty serial, credit, warranty, discount and reference fields");
                    if(id==6)Check(rendered.Contains("BAT001") && rendered.Contains("Warranty summary") && rendered.Contains("Manual Item") && rendered.Contains("Invoice Discount") && rendered.Contains("CGST"),"mixed saved tax, discounts, serials, warranty and manual lines retained");
                    if(id==7)Check(System.Net.WebUtility.HtmlDecode(rendered).Contains("Current outstanding: ₹ 30.00") && rendered.Contains("Refunds recorded") && rendered.Contains("Collected on"),"legacy credit, dated collections and returns remain separate from original invoice");
                    Check(rendered.Contains("Grand total"),"saved grand total retained");
                }
            }
            var settings = await client.GetAsync("/Settings/Company");
            Check(role == "Admin" ? settings.IsSuccessStatusCode : (int)settings.StatusCode == 403, "company settings role access");
            if (role == "Admin") Check((int)(await client.PostAsync("/Settings/Company", new FormUrlEncodedContent(new Dictionary<string,string> { ["Input.CompanyName"]="No token" }))).StatusCode == 400, "company settings CSRF required");
        }
        var state=((FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>()).State;
        var priorProfile=state.BusinessProfileCode;var priorAvailable=state.BusinessProfileConfigurationAvailable;var priorPersisted=state.BusinessProfilePersisted;
        try
        {
            using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
            foreach(var profile in new[]{"ELECTRICAL","BIKE_ACCESSORIES","CLOTHING","FOOTWEAR"})
            {
                state.BusinessProfileCode=profile;state.BusinessProfileConfigurationAvailable=true;state.BusinessProfilePersisted=true;
                Check((await client.GetStringAsync("/Sales/Invoice/5")).Contains("INV-PRINT-5"),profile+" renders the existing shared invoice view");
            }
        }
        finally{state.BusinessProfileCode=priorProfile;state.BusinessProfileConfigurationAvailable=priorAvailable;state.BusinessProfilePersisted=priorPersisted;}
        Console.WriteLine("PASS: invoice roles, four supported profile contexts, compact/mixed/credit fixtures, missing records, cancelled status, HTML encoding, private responses, company settings access/CSRF.");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); }
}
internal class FakeInvoiceRepository : IInvoiceRepository
{
    public Task<InvoiceResponse?> GetInvoiceAsync(long saleId)
    {
        if(saleId is >=5 and <=8)
        {
            var model=new InvoiceResponse {SaleId=saleId,InvoiceNumber="INV-PRINT-"+saleId,SaleDate=new(2026,10,1),Status="COMPLETED",HasHistoricalDetails=true,
                Company=new(){CompanyName="StockFlow",Address="Saved shop address",InvoiceFooter="Thank you for shopping with us."},CustomerName="Saved customer",Notes="INTERNAL-NOTE-DO-NOT-PRINT",
                ItemSubtotal=100,ItemDiscountAmount=0,InvoiceDiscountAmount=0,Subtotal=100,TotalAmount=100,
                Items=[new(){ProductName="Everyday item",Quantity=1,SellingPrice=100,LineTotal=100,UnitOfMeasure="PIECE"}],Payments=[new(){PaymentMode="CASH",Amount=100}]};
            if(saleId==6)
            {
                model.Items=[new(){SaleItemId=1,ProductName="Battery / Blue",SerialNumber="BAT001",WarrantyMonths=12,Quantity=2,SellingPrice=90,EnteredRate=100,DiscountAmount=20,EnteredDiscount=10,DiscountType="PERCENTAGE",DiscountPercent=5,TaxPercentage=18,LineTotal=188.80m,Sku="BAT-001"},new(){SaleItemId=2,ProductName="Small cord",IsManual=true,Quantity=1,SellingPrice=10,LineTotal=10,Description="Customer item description"}];
                model.ItemSubtotal=210;model.ItemDiscountAmount=10;model.InvoiceDiscountAmount=30;model.Subtotal=170;model.TaxAmount=28.8m;model.TotalAmount=198.8m;model.Payments=[new(){PaymentMode="UPI",Amount=198.8m}];
                model.Gst=new(){RegistrationType="REGULAR",PlaceOfSupply="29",Cgst=14.4m,Sgst=14.4m,Lines=[new(){SourceLineId=1,HsnSac="8507",Uqc="NOS",TaxableValue=160,TaxRate=18,Cgst=14.4m,Sgst=14.4m}]};
            }
            if(saleId==7){model.Payments=[new(){PaymentMode="CREDIT",Amount=100},new(){PaymentMode="CASH",Amount=60,PaymentDate=new(2026,10,5)}];model.Settlement=new(){DocumentTotal=100,ReturnedTotal=20,PaidTotal=60,RefundedTotal=10};}
            if(saleId==8){model.Items=Enumerable.Range(1,70).Select(n=>new InvoiceItemResponse{ProductName=$"Long product {n}: navy and pink retail merchandise with a detailed saved variant description",Quantity=1,SellingPrice=1,LineTotal=1,Sku=$"SKU-{n:0000}"}).ToList();model.ItemSubtotal=model.Subtotal=model.TotalAmount=70;model.Payments[0].Amount=70;}
            return Task.FromResult<InvoiceResponse?>(model);
        }
        return Task.FromResult<InvoiceResponse?>(saleId is 1 or 2 or 3 or 4 ? new()
    {
        SaleId=saleId,InvoiceNumber="INV-TEST-"+saleId,SaleDate=DateTime.Today,Status=saleId==2 ? "CANCELLED":"COMPLETED",
        Company=saleId==4 ? new() {CompanyName="Snapshot Shop",LegalBusinessName="Snapshot Legal Name",City="Bengaluru",State="Karnataka",PinCode="560038",PanNumber="ABCDE1234F",SalespersonName="Saved Salesperson",TermsAndConditions="Saved warranty terms.",LogoDataUrl="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="} : new() { CompanyName=saleId==3?"StockFlow":"Test Store" },CustomerName=saleId==3?"Rajesh Kumar":"<script>alert(1)</script>",TotalAmount=118,Subtotal=100,TaxAmount=18,
        CustomerMobile=saleId==3?"9876543210":null,CustomerAddress=saleId==3?"45 MG Road\nHyderabad, Telangana 500001":null,CustomerGstNumber=saleId==3?"36ABCDE1234F1Z5":null,HasHistoricalDetails=true,
        Items=[new() { ProductName="Battery",Quantity=1,SellingPrice=100,TaxPercentage=18,LineTotal=118,SerialNumber="BAT001",WarrantyMonths=12 }],
        Payments=[new() { PaymentMode="CASH",Amount=118 }]
    } : null);
    }
}
internal class FakeCompanySettingsRepository : ICompanySettingsRepository
{
    public CompanyGstIdentity? GstIdentity { get; set; }
    public Task<CompanyGstIdentity?> GetGstIdentityAsync()=>Task.FromResult(GstIdentity);
    public CompanySettings Settings { get; private set; } = new() { CompanyName="Test Store" };
    public int Saves { get; private set; }
    public Task<CompanySettings> GetAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<CompanySettings>(System.Text.Json.JsonSerializer.Serialize(Settings))!);
    public Task SaveAsync(CompanySettings settings,int userId) { Settings=System.Text.Json.JsonSerializer.Deserialize<CompanySettings>(System.Text.Json.JsonSerializer.Serialize(settings))!; Saves++; return Task.CompletedTask; }
    public Task SaveSectionAsync(CompanySettings settings,string section,string expectedVersion,int userId)
    {
        if(StockFlow.Application.Common.CompanySettingsViews.Version(Settings,section)!=expectedVersion)throw new System.ComponentModel.DataAnnotations.ValidationException("These settings changed. Reload before saving.");
        return SaveAsync(StockFlow.Application.Common.CompanySettingsViews.Merge(Settings,settings,section),userId);
    }
    public Task<IReadOnlyList<CompanySalesperson>> GetSalespeopleAsync() => Task.FromResult<IReadOnlyList<CompanySalesperson>>([new(1,"Store Owner")]);
}
