using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
public sealed class FakeBarcodeLotRepository : IBarcodeLotRepository
{
    private bool generated;
    public Task<IReadOnlyList<BarcodeLotResponse>> ListAsync()=>Task.FromResult<IReadOnlyList<BarcodeLotResponse>>([
        new(){LotId=1,Kind="intake",LotNumber="INT-001",ReceivedDate=new(2026,10,30),PurchaseId=1,PurchaseNumber="PUR-001",SupplierName="Supplier A",Items=1,Quantity=3},
        new(){LotId=2,Kind="receipt",LotNumber="GRN-002",ReceivedDate=new(2026,10,30),PurchaseId=2,PurchaseNumber="PUR-002",SupplierName="Supplier B",Items=1,Quantity=2}]);
    public Task<IReadOnlyList<StockLabelResponse>?> ReceiptLabelsAsync(long receiptId,string? action,int userId)
    {
        if(receiptId!=2)return Task.FromResult<IReadOnlyList<StockLabelResponse>?>(null);
        if(action=="GENERATE")generated=true;
        return Task.FromResult<IReadOnlyList<StockLabelResponse>?>(generated
            ? Enumerable.Range(1,2).Select(n=>new StockLabelResponse{LabelId=100+n,VariantId=1,ItemNumber=n,Quantity=1,ProductName="Helmet / M",InternalBarcode="SFR2X"+n,SellingPrice=100}).ToList()
            : [new(){VariantId=1,Quantity=2,ProductName="Helmet / M",ExistingBarcode="0123456789012",SellingPrice=100}]);
    }
}
