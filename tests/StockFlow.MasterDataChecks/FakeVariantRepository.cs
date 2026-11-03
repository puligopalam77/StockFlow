using System.Text.Json;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
namespace StockFlow.MasterDataChecks;
public sealed class FakeVariantRepository(IHttpContextAccessor? context = null) : IVariantRepository
{
    private readonly List<VariantResponse> rows=[];
    private readonly Dictionary<long,IReadOnlyList<StockFlow.Application.DTOs.Purchases.StockLabelResponse>> labels=[];
    public Task LogExistingBarcodePrintAsync(long variantId,string barcode,int copies,string action,int userId)
    {
        if(!rows.Any(v=>v.VariantId==variantId&&!v.IsSerialTracked&&v.Barcodes.Contains(barcode)))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Barcode does not belong to this variant.");
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<StockFlow.Application.DTOs.Purchases.StockLabelResponse>> ItemLabelsAsync(long variantId,bool generate,int userId)
    {
        if(generate&&!labels.ContainsKey(variantId))labels[variantId]=Enumerable.Range(1,3).Select(n=>new StockFlow.Application.DTOs.Purchases.StockLabelResponse
        {LabelId=variantId*10+n,VariantId=variantId,IntakeId=1,ItemNumber=n,Quantity=1,ProductName="Received item",InternalBarcode=$"SFI{variantId}X{n}",SellingPrice=10}).ToList();
        return Task.FromResult(labels.GetValueOrDefault(variantId)??(IReadOnlyList<StockFlow.Application.DTOs.Purchases.StockLabelResponse>)[]);
    }
    public async Task<IEnumerable<VariantResponse>> GetAsync(int? productId,string? term,bool activeOnly) {
        var current=context?.HttpContext?.RequestServices;
        var scoped=current?.GetRequiredService<StockFlow.Application.Common.BusinessScope>().ProfileCatalogSwitchingAvailable==true
            ?(await current.GetRequiredService<IProductRepository>().GetAllAsync()).Select(p=>p.ProductId).ToHashSet():null;
        return rows.Where(v=>(scoped is null||scoped.Contains(v.ProductId))&&(productId is null||v.ProductId==productId)&&(!activeOnly||v.IsActive));
    }
    public Task<long> SaveAsync(long? id,VariantRequest request,int userId)
    {
        var item=JsonSerializer.Deserialize<VariantResponse>(JsonSerializer.Serialize(request))!;
        item.VariantId=id??rows.Count+1;item.IsActive=request.IsActive;
        rows.RemoveAll(v=>v.VariantId==item.VariantId);rows.Add(item);return Task.FromResult(item.VariantId);
    }
    public Task<string> GenerateBarcodeAsync(long id,int userId)=>Task.FromResult("TEST-"+id);
    public Task<string?> SaveUnitBarcodeAsync(long unitId,UnitBarcodeRequest request,int userId)=>Task.FromResult<string?>("TEST-UNIT-"+unitId);
}
