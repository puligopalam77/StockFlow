window.StockFlowVariantIdentity = (()=>{
    'use strict';
    function summary(item){return item.attributeSummary || Object.entries(item.attributes||{}).filter(([,v])=>v!=null&&String(v).trim()).map(([key,value])=>key.replace(/_/g,' ').replace(/\b\w/g,c=>c.toUpperCase())+': '+value).join(' · ');}
    function unit(item){const value=item.unitOfMeasure||item.unit||'';return value?value[0]+value.slice(1).toLowerCase():'';}
    function details(item){return [item.sku,summary(item),unit(item)].filter(Boolean).join(' · ');}
    function matches(item,term){return [item.productName,item.variantName,item.model,item.parentProductCode,item.brandName,item.categoryName,item.parentCategoryName,item.sku,item.supplierProductCode,...(item.supplierIdentifiers||[]).map(i=>i.supplierSku),item.barcode,...(item.barcodes||[]),summary(item)].filter(Boolean).join(' ').toLowerCase().includes(term.toLowerCase());}
    return {summary,unit,details,matches};
})();
