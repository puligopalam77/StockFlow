using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Domain.Entities;

namespace StockFlow.Infrastructure.Repositories;

// Runtime checks share the same product-first lock order as inventory writers.
internal static class ProductEditIntegrity
{
    internal static async Task<bool> UsedAsync(NpgsqlConnection c,NpgsqlTransaction? t,int productId,long? variantId=null)
    {
        // Fail closed on older installations: pricing remains editable, but unused stock identity
        // fields cannot be unlocked until the cross-location function has been installed.
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_product_edit_history(integer,bigint)') IS NOT NULL",transaction:t))return true;
        return await c.ExecuteScalarAsync<bool>("SELECT public.sf_product_edit_history(@ProductId,@VariantId)",new {ProductId=productId,VariantId=variantId},t);
    }

    internal static Task<string?> VersionAsync(NpgsqlConnection c,NpgsqlTransaction? t,int productId) => c.ExecuteScalarAsync<string?>("""
        SELECT md5(p.xmin::text||':'||COALESCE((SELECT string_agg(v.xmin::text||':'||v.variant_id,',' ORDER BY v.variant_id) FROM product_variants v WHERE v.product_id=p.product_id),'')
        ||':'||COALESCE((SELECT string_agg(b.xmin::text||':'||b.variant_barcode_id,',' ORDER BY b.variant_barcode_id) FROM variant_barcodes b JOIN product_variants v USING(tenant_id,variant_id) WHERE v.product_id=p.product_id),'')
        ||':'||COALESCE((SELECT string_agg(i.xmin::text||':'||i.supplier_id||':'||i.variant_id,',' ORDER BY i.variant_id,i.supplier_id) FROM variant_supplier_identifiers i JOIN product_variants v USING(tenant_id,variant_id) WHERE v.product_id=p.product_id),''))
        FROM products p WHERE p.product_id=@productId
        """,new {productId},t);

    internal static async Task CheckAsync(NpgsqlConnection c,NpgsqlTransaction t,Product entity)
    {
        var old=await c.QuerySingleOrDefaultAsync<Product>("SELECT product_id ProductId,parent_product_code ParentProductCode,unit_of_measure UnitOfMeasure,is_serial_tracked IsSerialTracked,has_variants HasVariants FROM products WHERE product_id=@ProductId FOR UPDATE",entity,t)
            ?? throw new ValidationException("Product not found in this shop.");
        if(entity.ExpectedEditVersion is not null && entity.ExpectedEditVersion!=await VersionAsync(c,t,entity.ProductId))
            throw new System.Data.DBConcurrencyException("This product changed since you opened it. Reload the latest product before saving. Your unsaved changes are still available.");
        if(entity.ParentProductCode is not null && entity.ParentProductCode!=old.ParentProductCode)
            throw new ValidationException("Saved Product Codes are permanent. Use a reviewed correction.");
        if(!old.HasVariants && entity.HasVariants && !string.IsNullOrWhiteSpace(old.ParentProductCode)
            && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_variants WHERE product_id=@ProductId AND upper(sku)=upper(@ParentProductCode))",new{entity.ProductId,old.ParentProductCode},t)
            && !await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_simple_variant_edit_available()') IS NOT NULL",transaction:t))
            throw new ValidationException("Apply manual migration 080 before enabling variants for an existing simple product. Its existing SKU will be preserved.");
        if(await UsedAsync(c,t,entity.ProductId) && (old.UnitOfMeasure!=entity.UnitOfMeasure || old.IsSerialTracked!=entity.IsSerialTracked || old.HasVariants!=entity.HasVariants))
            throw new ValidationException("Stock unit, tracking method and variant structure cannot change after stock or transaction history exists. Use an authorized inventory correction.");
    }
}
