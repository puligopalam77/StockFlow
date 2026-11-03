using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;

internal static class RetailTransactionChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    public static async Task Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/retail-phase56-fixtures");Directory.CreateDirectory(output);
        foreach(var profile in new[]{BusinessProfiles.Clothing,BusinessProfiles.Footwear,BusinessProfiles.BikeAccessories})
        {
            using var original=new Factory();
            using var factory=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{
                s.RemoveAll<IPurchaseRepository>();s.AddSingleton<IPurchaseRepository,RetailPurchaseFake>();
                s.RemoveAll<IInvoiceRepository>();s.AddSingleton<IInvoiceRepository,RetailInvoiceFake>();
            }));
            using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            var branch=(FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();branch.State.BusinessProfileCode=profile;branch.State.BusinessProfilePersisted=true;branch.State.BusinessProfileConfigurationAvailable=true;branch.State.BusinessProfileVersion=1;
            var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
            var products=factory.Services.GetRequiredService<IProductRepository>();var variants=factory.Services.GetRequiredService<IVariantRepository>();
            var product=await products.CreateAsync(new(){ProductName="Retail item",CategoryId=1,HasVariants=true,IsActive=true});
            var other=await products.CreateAsync(new(){ProductName="Other item",CategoryId=1,HasVariants=true,IsActive=true});
            var v1=await variants.SaveAsync(null,new(){ProductId=product,VariantName="M / Black",Sku="RETAIL-M",Attributes=new(){["size"]="M",["color"]="Black"}},1);
            var v2=await variants.SaveAsync(null,new(){ProductId=other,VariantName="L / Black",Sku="OTHER-L"},1);
            var purchase=new CreatePurchaseRequest{SupplierId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=product,Quantity=2,PurchasePrice=10}]};
            Check((await client.PostAsJsonAsync("/api/purchases",purchase)).StatusCode==HttpStatusCode.BadRequest,"retail purchase requires exact variant: "+profile);
            purchase.Items[0].VariantId=v2;Check((await client.PostAsJsonAsync("/api/purchases",purchase)).StatusCode==HttpStatusCode.BadRequest,"variant from another product rejected");
            purchase.Items[0].VariantId=v1;(await client.PostAsJsonAsync("/api/purchases",purchase)).EnsureSuccessStatusCode();
            Check(((RetailPurchaseFake)factory.Services.GetRequiredService<IPurchaseRepository>()).Writes==1,"only exact variant reaches purchase writer");
            var sale=new CreateSaleRequest{RequestId=Guid.NewGuid(),Items=[new(){ProductId=product,Quantity=1,SellingPrice=10}],Payments=[new(){PaymentMode="CASH",Amount=11.8m}]};
            Check((await client.PostAsJsonAsync("/api/sales/quote",sale)).StatusCode==HttpStatusCode.BadRequest,"quote cannot choose default size implicitly");
            Check((await client.PostAsJsonAsync("/api/sales",sale)).StatusCode==HttpStatusCode.BadRequest,"billing cannot choose default size implicitly");
            sale.Items[0].VariantId=v1;(await client.PostAsJsonAsync("/api/sales/quote",sale)).EnsureSuccessStatusCode();(await client.PostAsJsonAsync("/api/sales",sale)).EnsureSuccessStatusCode();
            Check((await client.PostAsJsonAsync("/api/inventory/adjustments",new{requestId=Guid.NewGuid(),productId=product,transactionType="ADJUSTMENT_IN",quantity=1,reason="Test variant selection"})).StatusCode==HttpStatusCode.BadRequest,"adjustment requires exact variant before repository access");
            var listing=(await client.GetFromJsonAsync<JsonArray>("/api/products/variants?productId="+product))!;
            Check(listing[0]!["attributeSummary"]!.GetValue<string>().Contains("Size: M"),"catalog exposes readable configured values");
            var transfer=(await client.GetFromJsonAsync<JsonArray>("/api/transfers/variants"))!;
            Check(transfer.Any(v=>v!["attributeSummary"]!.GetValue<string>().Contains("Size: M")),"transfer lookup retains attribute identity");
            var invoice=(RetailInvoiceFake)factory.Services.GetRequiredService<IInvoiceRepository>();invoice.Pair=profile==BusinessProfiles.Footwear;
            foreach(var (url,name) in new[]{("/Purchases/Create","Receive"),("/Sales/Create","Billing"),("/Inventory","Stock"),("/Transfers","Transfers"),("/Sales/Invoice/1","Invoice"),("/StockInflow/Details/PURCHASE/1","Receipt")}){
                var html=await client.GetStringAsync(url);await File.WriteAllTextAsync(Path.Combine(output,profile+"-"+name+".html"),html);
                if(name=="Invoice")Check(html.Contains("Size: M")&&html.Contains("RETAIL-M")&&!html.Contains("<script>unsafe"),"invoice renders saved attributes/SKU safely");
            }
            client.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:APPROVE");purchase.CompleteImmediately=true;
            Check((await client.PostAsJsonAsync("/api/purchases",purchase)).StatusCode==HttpStatusCode.Forbidden,"receiving approval remains mandatory");
        }
        var snapshot=new SaleItemResponse{AttributesJson="{\"footwear_size\":\"7\",\"color\":\"Black\"}",UnitOfMeasure="PAIR",Sku="PAIR-7"};
        Check(snapshot.AttributeSummary=="Footwear Size: 7 · Color: Black","document summary uses stored values and readable codes");
        Console.WriteLine("PASS: retail transaction API/DTO/fixture checks. Fakes only; no PostgreSQL or SQL scripts executed.");
    }
}
internal sealed class RetailPurchaseFake : IPurchaseRepository
{
    public int Writes {get;private set;}
    public Task<long> CreatePurchaseAsync(CreatePurchaseRequest r,int userId){Writes++;return Task.FromResult(1L);}
    public Task<bool> UpdateDraftAsync(long id,CreatePurchaseRequest r,int userId)=>Task.FromResult(true);
    public Task<bool> CompleteAsync(long id,int userId)=>Task.FromResult(true);
    public Task<IEnumerable<PurchaseResponse>> GetAllAsync()=>Task.FromResult<IEnumerable<PurchaseResponse>>([]);
    public Task<PurchaseResponse?> GetByIdAsync(long id)=>Task.FromResult<PurchaseResponse?>(null);
    public Task<PurchaseAttachment?> GetAttachmentAsync(long id)=>Task.FromResult<PurchaseAttachment?>(null);
}
internal sealed class RetailInvoiceFake : IInvoiceRepository
{
    public bool Pair {get;set;}
    public Task<InvoiceResponse?> GetInvoiceAsync(long id)=>Task.FromResult<InvoiceResponse?>(new(){SaleId=id,InvoiceNumber="RETAIL-INV",SaleDate=DateTime.Today,Status="COMPLETED",Company=new(){CompanyName="Retail test shop"},Items=[new(){ProductName="Saved retail item",Sku="RETAIL-M",UnitOfMeasure=Pair?"PAIR":"PIECE",AttributesJson="{\"size\":\"M\",\"color\":\"Black\",\"material\":\"<script>unsafe</script>\"}",Quantity=1,SellingPrice=100,LineTotal=100}],Subtotal=100,TotalAmount=100});
}
