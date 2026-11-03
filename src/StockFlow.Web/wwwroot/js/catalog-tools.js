(() => {
    'use strict';
    const host=document.getElementById('catalog-tools');if(!host)return;
    const el=id=>document.getElementById(id),headers=['productName','category','brand','model','sku','variantName','unit','tracking','purchasePrice','sellingPrice','mrp','taxPercentage','barcode','manufacturerPartNumber','attributes','hsnCode','description','packQuantity','minimumStockLevel','warrantyMonths','warrantyProvider','supplierSku','otherBarcodes','supplierIdentifiers','imageUrl'];
    let products=[],busy=false,historyOffset=0,historyProduct='';
    const message=text=>el('catalog-tools-message').textContent=text;
    async function api(url,body){const r=await fetch(url,{credentials:'same-origin',cache:'no-store',...(body?{method:'POST',body:JSON.stringify(body)}:{}),headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name=csrf-token]')?.content||''}});const data=await r.json();if(!r.ok)throw new Error(data.message||Object.values(data.errors||{}).flat().join(' ')||'Unable to complete catalog action.');return data;}
    function table(captions,rows){const t=document.createElement('table');t.className='table table-sm';const tr=document.createElement('tr');captions.forEach(c=>{const th=document.createElement('th');th.textContent=c;tr.append(th);});const head=document.createElement('thead');head.append(tr);t.append(head);const body=document.createElement('tbody');rows.forEach(values=>{const row=document.createElement('tr');values.forEach(v=>{const cell=document.createElement('td');cell.textContent=String(v??'');row.append(cell);});body.append(row);});t.append(body);el('catalog-tools-results').replaceChildren(t);}
    async function action(task){if(busy)return;busy=true;host.querySelectorAll('button,input,select').forEach(n=>n.disabled=true);try{await task();}catch(e){message(e.message);}finally{busy=false;host.querySelectorAll('button,input,select').forEach(n=>n.disabled=false);if(el('catalog-commit'))el('catalog-commit').disabled=!products.length||!host.dataset.reviewed;}}
    function number(row,key,fallback=0){const text=row[key]||'';if(!text)return fallback;if(!/^\d+(\.\d+)?$/.test(text))throw new Error('Enter a valid '+key+' for '+row.productName+'.');return Number(text);}
    async function group(rows){
        const needCategories=rows.some(r=>!r.categoryId),needBrands=rows.some(r=>!r.brandId&&r.brand);
        const categories=needCategories?await api('/api/categories?activeOnly=true'):[],brands=needBrands?await api('/api/brands?activeOnly=true'):[];
        const normalize=value=>String(value||'').trim().replace(/\s+/g,' ').toLowerCase();
        for(const row of rows){
            row.productName=String(row.productName||'').trim().replace(/\s+/g,' ');
            if(!row.categoryId){
                const key=normalize(row.category),exact=categories.filter(c=>[c.categoryPath,c.categoryCode].some(v=>v&&normalize(v)===key));
                const matches=exact.length?exact:categories.filter(c=>normalize(c.categoryName)===key);
                if(matches.length!==1)throw new Error('Choose an exact category path or category code for '+row.productName+'.');row.categoryId=String(matches[0].categoryId);
            }
            if(!row.brandId&&row.brand){const key=normalize(row.brand),matches=brands.filter(b=>[b.brandName,b.brandCode,...(b.aliases||[])].some(v=>v&&normalize(v)===key));if(matches.length!==1)throw new Error('Choose an exact brand name or brand code for '+row.productName+'.');row.brandId=String(matches[0].brandId);}
        }
        const groups=new Map(),skus=new Set();
        if(rows.some(r=>'quantity'in r||'stock'in r))throw new Error('Catalog files cannot contain stock quantities. Use the opening-stock import.');
        for(const row of rows){
            if(!row.productName||!row.categoryId||!row.sku)throw new Error('Every row needs productName, categoryId and sku.');
            if(skus.has(row.sku.toUpperCase()))throw new Error('Duplicate SKU in file: '+row.sku);skus.add(row.sku.toUpperCase());
            const categoryId=number(row,'categoryId'),brandId=number(row,'brandId')||null,key=JSON.stringify([row.productName.toLowerCase(),categoryId,brandId,row.model||'']);
            let product=groups.get(key);
            if(!product){product={productName:row.productName,categoryId,brandId,model:row.model||null,unitOfMeasure:row.unit||'PIECE',trackingType:row.tracking||'QUANTITY',taxPercentage:number(row,'taxPercentage'),hsnCode:row.hsnCode||null,description:row.description||null,productImageUrl:row.imageUrl||null,warrantyMonths:number(row,'warrantyMonths'),warrantyProvider:row.warrantyProvider||null,hasVariants:false,variants:[]};groups.set(key,product);}
            if(product.unitOfMeasure!==(row.unit||'PIECE')||product.trackingType!==(row.tracking||'QUANTITY'))throw new Error('All variants of a product must use the same unit and tracking method.');
            const attributes=JSON.parse(row.attributes||'{}');if(!attributes||Array.isArray(attributes)||typeof attributes!=='object'||Object.values(attributes).some(v=>typeof v!=='string'))throw new Error('attributes must be a JSON object containing string values.');
            if(row.manufacturerPartNumber)attributes.manufacturer_part_number=row.manufacturerPartNumber;
            product.variants.push({productId:1,variantName:row.variantName||'DEFAULT',sku:row.sku,primaryBarcode:row.barcode||null,barcodes:row.barcode?[row.barcode]:[],barcodeSource:row.barcode?'MANUFACTURER':'INTERNAL',purchasePrice:number(row,'purchasePrice'),sellingPrice:number(row,'sellingPrice'),mrp:number(row,'mrp'),taxPercentage:number(row,'taxPercentage'),attributes,packQuantity:number(row,'packQuantity',1),minimumStockLevel:number(row,'minimumStockLevel'),warrantyMonths:number(row,'warrantyMonths'),supplierProductCode:row.supplierSku||null,supplierIdentifiers:row.supplierIdentifiers?JSON.parse(row.supplierIdentifiers):null});
            const aliases=(row.otherBarcodes||'').split('|').map(v=>v.trim()).filter(Boolean);product.variants.at(-1).barcodes=[...new Set([...product.variants.at(-1).barcodes,...aliases])];
            product.hasVariants=product.variants.length>1||product.variants.some(v=>v.variantName!=='DEFAULT');
        }
        if(groups.size>100)throw new Error('Use up to 100 products per file.');return [...groups.values()];
    }
    el('catalog-template').onclick=()=>StockFlowCsv.download('stockflow-catalog-template.csv',headers,[['Example item','Exact category path or CAT code','','Product model','EXAMPLE-001','DEFAULT','PIECE','QUANTITY','0','0','0','0','','','{}','','','1','0','0','','','','','']]);
    el('catalog-export')?.addEventListener('click',()=>action(async()=>{
        const data=await api('/api/products/catalog/export'),categories=await api('/api/categories'),categoryPaths=new Map(categories.map(c=>[c.categoryId,c.categoryPath||c.categoryName])),lookup=new Map(data.products.map(p=>[p.productId,p]));
        StockFlowCsv.download('stockflow-catalog.csv',headers,data.variants.filter(v=>lookup.has(v.productId)).map(v=>{const p=lookup.get(v.productId);return [p.productName,categoryPaths.get(p.categoryId)||p.categoryName,p.brandName,p.model,v.sku,v.variantName,p.unitOfMeasure,v.isSerialTracked?'SERIAL':'QUANTITY',v.purchasePrice,v.sellingPrice,v.mrp,v.taxPercentage,v.primaryBarcode,v.attributes?.manufacturer_part_number,JSON.stringify(v.attributes||{}),p.hsnCode,p.description,v.packQuantity,v.minimumStockLevel,v.warrantyMonths,p.warrantyProvider,v.supplierProductCode,(v.barcodes||[]).filter(b=>b!==v.primaryBarcode).join('|'),v.supplierIdentifiers?.length?JSON.stringify(v.supplierIdentifiers):'',p.productImageUrl];}));message('Catalog exported for the selected setup. Quantities are not included.');
    }));
    el('catalog-file')?.addEventListener('change',()=>{products=[];delete host.dataset.reviewed;el('catalog-commit').disabled=true;el('catalog-tools-results').replaceChildren();});
    el('catalog-preview')?.addEventListener('click',()=>action(async()=>{
        products=[];delete host.dataset.reviewed;const file=el('catalog-file').files[0];if(!file)throw new Error('Choose a CSV file first.');if(file.size>2000000)throw new Error('Use a CSV file up to 2 MB.');
        const candidate=await group(StockFlowCsv.parse(await file.text()));const preview=await api('/api/products/catalog/preview',{products:candidate,expectedProfileVersion:Number(host.dataset.profileVersion)});
        table(['Product','Validation'],preview.rows.map(r=>[r.productName,r.message]));
        if(preview.rows.every(r=>r.valid)){products=candidate;host.dataset.reviewed='true';message('Preview passed. Confirm to create '+products.length+' products. Stock will not be added.');}else message('Fix invalid rows and upload the corrected file. Nothing has been saved.');
    }));
    el('catalog-commit')?.addEventListener('click',()=>action(async()=>{
        const result=await api('/api/products/catalog/commit',{products,expectedProfileVersion:Number(host.dataset.profileVersion),reviewed:true});
        table(['Product','Saved ID','Result'],result.rows.map(r=>[r.productName,r.productId,r.message]));
        const completed=new Set(result.rows.filter(r=>r.saved).map(r=>r.index));products=products.filter((_,i)=>!completed.has(i));delete host.dataset.reviewed;
        document.dispatchEvent(new Event('stockflow:catalog-imported'));message(completed.size+' products saved. '+products.length+' products remain. Reconcile saved SKUs before retrying; preview the remaining file.');
    }));
    host.addEventListener('toggle',()=>{if(host.open&&el('catalog-history-product'))action(async()=>{const rows=await api('/api/products');el('catalog-history-product').replaceChildren(new Option('Choose product',''),...rows.map(p=>new Option(p.productName,p.productId)));});});
    async function history(more){if(!more){historyOffset=0;historyProduct=el('catalog-history-product').value;}if(!historyProduct)throw new Error('Choose a product.');const rows=await api('/api/inventory/history?'+new URLSearchParams({productId:historyProduct,offset:historyOffset}));table(['Date','Movement','Quantity','Reference'],rows.map(r=>[r.createdAt,r.transactionType,r.quantity,r.referenceType+' '+(r.referenceId||'')]));historyOffset+=rows.length;el('catalog-history-more').hidden=rows.length<100;message('Stock movements in the selected shop. Quantities on different shops remain independent.');}
    el('catalog-history')?.addEventListener('click',()=>action(()=>history(false)));el('catalog-history-more')?.addEventListener('click',()=>action(()=>history(true)));
})();
