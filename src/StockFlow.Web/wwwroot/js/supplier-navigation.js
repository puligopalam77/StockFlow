// Presentation-only context. Product/variant reads and saves remain server-authorized.
window.StockFlowSupplierNavigation=(()=>{
 'use strict';
 const id=value=>/^[1-9][0-9]*$/.test(String(value))&&Number.isSafeInteger(Number(value));
 const editUrl=(productId,variantId,from='replenishment')=>id(productId)&&id(variantId)?'/Products/Edit?'+new URLSearchParams({id:productId,step:'pricing-stock',focus:'variant-supplier',variantId,from:from==='replenishment'?'replenishment':'product'}):null;
 function requested(){const p=new URLSearchParams(location.search);if(location.pathname.toLowerCase()!=='/products/edit'||p.get('step')!=='pricing-stock'||p.get('focus')!=='variant-supplier')return null;return {productId:p.get('id'),variantId:p.get('variantId'),from:p.get('from')};}
 function resolve(record,variants){const request=requested();if(!request)return null;if(!id(request.productId)||!id(request.variantId)||Number(request.productId)!==record.productId)return {invalid:true};const variant=variants.find(v=>v.variantId===Number(request.variantId)&&v.productId===record.productId);return variant?{...request,variant}: {invalid:true};}
 const returnUrl=context=>context?.from==='replenishment'&&id(context.variant?.variantId)?'/Replenishment?'+new URLSearchParams({focus:'policy',variantId:context.variant.variantId,term:context.variant.sku}):null;
 const createUrl=(productId,variantId,from)=>id(productId)&&id(variantId)?'/Suppliers/Create?'+new URLSearchParams({supplierReturn:'product',productId,variantId,origin:from==='replenishment'?'replenishment':'product'}):null;
 function supplierReturn(){const p=new URLSearchParams(location.search);return location.pathname.toLowerCase()==='/suppliers/create'&&p.get('supplierReturn')==='product'&&id(p.get('productId'))&&id(p.get('variantId'))?{productId:Number(p.get('productId')),variantId:Number(p.get('variantId')),from:p.get('origin')==='replenishment'?'replenishment':'product'}:null;}
 return {editUrl,resolve,returnUrl,createUrl,supplierReturn};
})();
