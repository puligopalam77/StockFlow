using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
namespace StockFlow.Application.Services;

// Legacy Electrical integrations retain DEFAULT compatibility; newly registered families require an exact SKU.
public sealed class RetailVariantSelection(BusinessScope scope,IProductRepository products,IVariantRepository variants)
{
    public async Task ValidateAsync(IEnumerable<(int ProductId,long? VariantId,long? UnitId)> lines)
    {
        var electrical=scope.BusinessProfileCode==BusinessProfiles.Electrical;
        foreach(var group in lines.GroupBy(l=>l.ProductId))
        {
            var product=await products.GetByIdAsync(group.Key);
            // Existing Electrical writers perform their own authoritative lookup/stock checks.
            if(electrical&&product is null)continue;
            if(product is null)throw new ValidationException("Choose a product belonging to this business.");
            if(electrical&&string.IsNullOrWhiteSpace(product.ParentProductCode))continue;
            // Include inactive rows for retry identity checks. Stock writers validate
            // current activity after their existing request-ID recovery checks.
            var catalog=(await variants.GetAsync(group.Key,null,false)).ToDictionary(v=>v.VariantId);
            foreach(var line in group)
            {
                if(line.UnitId.HasValue&&!electrical)throw new ValidationException("Individual serials are not enabled for this shop profile.");
                if(product.HasVariants&&!line.VariantId.HasValue&&!line.UnitId.HasValue)
                    throw new ValidationException("Select the exact size, color or model variant before posting stock or billing.");
                if(line.VariantId.HasValue&&(!catalog.TryGetValue(line.VariantId.Value,out var selected)||selected.ProductId!=group.Key))
                    throw new ValidationException("Select an active variant belonging to the selected product.");
            }
        }
    }
}
