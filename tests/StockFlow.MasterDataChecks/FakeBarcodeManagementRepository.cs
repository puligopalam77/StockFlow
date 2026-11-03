using StockFlow.Application.Interfaces;
using System.ComponentModel.DataAnnotations;
namespace StockFlow.MasterDataChecks;
public sealed class FakeBarcodeManagementRepository : IBarcodeManagementRepository
{
    private readonly BarcodeWorkspace workspace=new(){HistoryAvailable=true,Lot=new(){Kind="receipt",LotId=2,LotNumber="GRN-002",PurchaseId=2,PurchaseNumber="PUR-002",ReceivedDate=new(2026,10,30),SupplierName="Supplier",Items=2,Quantity=3},Rows=[new(){Key="2:1",SourceItemId=2,ItemNumber=1,VariantId=1,ProductName="Helmet / S / Black",Sku="HELM-S",Barcode="0123456789012",SellingPrice=100},new(){Key="3:1",SourceItemId=3,ItemNumber=1,VariantId=2,ProductName="Helmet / M / Black",Sku="HELM-M",SellingPrice=120},new(){Key="3:2",SourceItemId=3,ItemNumber=2,VariantId=2,ProductName="Helmet / M / Black",Sku="HELM-M",SellingPrice=120}]};
    private readonly HashSet<Guid> requests=[];
    public Task<IReadOnlyList<BarcodeLotResponse>> ListAsync()=>Task.FromResult<IReadOnlyList<BarcodeLotResponse>>([new(){Kind="receipt",LotId=2,LotNumber="GRN-002",ReceivedDate=new(2026,10,30),Quantity=3,WithBarcode=workspace.Rows.Count(r=>r.Barcode is not null),Items=2,PurchaseId=2,PurchaseNumber="PUR-002",SupplierName="Supplier"}]);
    public Task<BarcodeWorkspace?> ReadAsync(string kind,long id)=>Task.FromResult<BarcodeWorkspace?>(kind=="receipt"&&id==2?workspace:null);
    public async Task<BarcodeWorkspace?> PrepareAsync(string kind,long id,string[] keys,int userId)
    {
        var doc=await ReadAsync(kind,id);if(doc is null)return null;
        if(keys.Distinct().Count()!=keys.Length||keys.Any(k=>!doc.Rows.Any(r=>r.Key==k)))throw new ValidationException("Invalid selection.");
        foreach(var r in doc.Rows.Where(r=>keys.Contains(r.Key)&&r.Barcode is null))r.Barcode=$"SFR{r.SourceItemId}X{r.ItemNumber}";
        return doc;
    }
    public async Task<BarcodeWorkspace?> PrintAsync(string kind,long id,BarcodePrintRequest request,int userId)
    {
        var doc=await ReadAsync(kind,id);if(doc is null)return null;
        if(request.RequestId==Guid.Empty||request.Items.Any(i=>!doc.Rows.Any(r=>r.Key==i.Key&&r.Barcode==i.Barcode))||request.Items.Sum(i=>i.Copies)>500)throw new ValidationException("Invalid print request.");
        if(requests.Add(request.RequestId))foreach(var item in request.Items)
        {
            var row=doc.Rows.Single(r=>r.Key==item.Key);row.LastRequestedAt=DateTime.UtcNow;row.LastRequestedBy="Admin";
            doc.History=[new(){RequestId=request.RequestId,RowKey=row.Key,ProductName=row.ProductName,Barcode=item.Barcode,RequestedAt=row.LastRequestedAt.Value,RequestedBy="Admin",Copies=item.Copies,Format=request.Format,Action=request.Action,SheetTemplate=request.SheetTemplate,SmallLabelScanTested=request.SmallLabelScanTested},..doc.History];
        }
        return doc;
    }
}
