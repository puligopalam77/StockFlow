// The physical receiving workspace. Product registration remains in /Products.
(async () => {
    'use strict';
    const el = id => document.getElementById(id);
    const workspace = el('purchase-workspace');
    if (!workspace?.classList.contains('goods-workspace')) return;
    const api = Purchases.api.bind(Purchases);
    const key = workspace.dataset.storageKey;
    const guide=document.getElementById('retail-screen-guide');
    function placeGuide(){if(guide){guide.classList.add('goods-guide');guide.querySelector('summary').textContent='ⓘ Show Steps';document.querySelector('.purchase-page-heading')?.append(guide);}}
    placeGuide();
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',placeGuide,{once:true});
    const canComplete = workspace.dataset.canComplete === 'true';
    const canScan = workspace.dataset.canScan === 'true';
    const locationId = Number(workspace.dataset.locationId);
    const headerIds = ['goods-supplier','goods-date','goods-invoice','goods-invoice-date','goods-notes','goods-credit','goods-freight','goods-charges','goods-discount'];
    const money = n => Math.round((Number(n) + Number.EPSILON) * 100) / 100;
    const currency = n => new Intl.NumberFormat('en-IN', { style:'currency', currency:'INR' }).format(n);
    const today = new Intl.DateTimeFormat('en-CA', { timeZone:'Asia/Kolkata', year:'numeric',month:'2-digit',day:'2-digit' }).format(new Date());
    const uuid = () => {
        const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
        const hex = Array.from(bytes, b => b.toString(16).padStart(2,'0')).join('');
        return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
    };
    let products = [], suppliers = [], orders = [], cart = [], order = null, pending = null;
    let busy = false, loading = true, reviewing = false, lookups = 0, draftTimer, attachment = null, previewUrl, activeItem = null, sequence = 0, selection = -1;
    let storage;
    const node = (tag, className, text) => { const n=document.createElement(tag); if(className) n.className=className; if(text!==undefined) n.textContent=text; return n; };
    const button = (text, className, action) => { const b=node('button',className,text); b.type='button'; b.onclick=action; return b; };
    const error = e => { el('purchase-message').textContent=e.message || String(e); el('purchase-message').hidden=false; };
    const clearError = () => { el('purchase-message').hidden=true; };
    const run = action => Promise.resolve().then(action).catch(error);
    const kind = () => document.querySelector('[name="goods-kind"]:checked').value;
    const accepted = item => Math.max(0,Number(item.quantity)-Number(item.damaged));
    const serials = text => String(text || '').split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
    function openStorage() {
        return new Promise((resolve,reject) => {
            const request=indexedDB.open('stockflow-receive-goods',1);
            request.onupgradeneeded=()=>request.result.createObjectStore('drafts');
            request.onsuccess=()=>resolve(request.result);
            request.onerror=()=>reject(new Error('Browser draft storage is unavailable. Enable site storage before receiving stock.'));
        });
    }
    function stored(operation, value) {
        return new Promise((resolve,reject)=>{
            const tx=storage.transaction('drafts',operation==='get'?'readonly':'readwrite');
            const store=tx.objectStore('drafts');
            const request=operation==='get'?store.get(key):operation==='delete'?store.delete(key):store.put(value,key);
            tx.oncomplete=()=>resolve(request.result);
            tx.onerror=()=>reject(new Error('The receipt could not be saved in this browser. Check available storage and try again.'));
            tx.onabort=tx.onerror;
        });
    }
    function snapshot() {
        return { version:1, header:Object.fromEntries(headerIds.map(id=>[id,el(id).value])), kind:kind(), order, cart:cart.map(({view,controls,...item})=>item), attachment, pending };
    }
    async function saveDraft(announce=false) {
        if(loading || !storage) return;
        clearTimeout(draftTimer);
        await stored('put',snapshot());
        el('goods-draft-state').textContent=announce?'Draft saved on this browser. No stock has been updated.':'Draft saved on this browser';
    }
    function changed() {
        if(loading || busy || pending) return;
        clearTimeout(draftTimer);
        draftTimer=setTimeout(()=>run(()=>saveDraft()),400);
    }
    function freeze() {
        el('goods-form').dataset.saving=String(busy||reviewing||!!pending);
        el('goods-fields').disabled=loading || busy || reviewing || !!pending || locationId<=0;
        el('purchase-save').disabled=!canComplete || busy || !!pending || locationId<=0;
        el('goods-confirm').disabled=busy || !canComplete;
        el('goods-recovery').hidden=!pending;
        el('goods-retry').disabled=busy;
        if(pending) el('goods-recovery-text').textContent='This receipt has an unconfirmed result. Retry the same saved receipt to check it without adding stock twice. ';
    }
    function cache(rows){const byId=new Map(products.map(p=>[Number(p.variantId),p]));for(const p of rows)byId.set(Number(p.variantId),p);products=[...byId.values()];return rows;}
    function contextValid(focus=false){
        let control,message;
        if(!suppliers.some(s=>s.supplierId===Number(el('goods-supplier').value))){control=el('goods-supplier');message='Select a supplier before adding receipt items.';}
        else if(!el('goods-date').value||!el('goods-date').validity.valid){control=el('goods-date');message='Enter a valid goods received date.';}
        else if(Number(el('goods-location').value)!==locationId||locationId<=0){control=el('goods-location');message='Select the active stock location before adding receipt items.';}
        else if(kind()==='order'&&(!order||String(order.purchaseId)!==el('goods-order').value)){control=el('goods-order');message='Choose an open purchase order before adding receipt items.';}
        el('goods-context').hidden=!message;el('goods-context-message').textContent=message||'';
        if(focus&&control){if(control.id==='goods-supplier')el('goods-supplier-picker').open=true;window.StockFlowValidation?.showField(control,message);control.focus();}
        return !message;
    }
    function supplierInfo(){
        const supplier=suppliers.find(s=>s.supplierId===Number(el('goods-supplier').value));
        el('goods-supplier-label').textContent=supplier?[supplier.supplierCode,supplier.supplierName].filter(Boolean).join(' - '):(el('goods-supplier').value?el('goods-supplier').selectedOptions[0]?.textContent:'Choose supplier');
        el('goods-supplier-info').textContent=supplier?[supplier.gstNumber?'GSTIN: '+supplier.gstNumber:'',supplier.mobileNumber?'Phone: '+supplier.mobileNumber:'',supplier.address].filter(Boolean).join(' · '):'';
        el('goods-supplier').disabled=!!order;el('goods-supplier-search').disabled=!!order;
        el('goods-supplier-label').setAttribute('aria-disabled',String(!!order));
    }
    async function revalidateOrder(){
        if(!order)return;
        const current=await api('/api/purchases/'+order.purchaseId);
        if(!['DRAFT','PARTIALLY_RECEIVED'].includes(current.status))throw new Error('This purchase order is no longer open. Choose another order or review this draft.');
        if(current.supplierId!==order.supplierId)throw new Error('The purchase order supplier changed. Select the order again before receiving.');
        for(const item of cart){
            const line=current.items.find(i=>i.purchaseItemId===item.purchaseItemId);
            if(!line||Number(line.purchasePrice)!==Number(item.price)||Number(line.taxPercentage)!==Number(item.tax)||Number(line.discountAmount)!==Number(item.orderDiscount))throw new Error('Purchase order lines or prices changed. Select the order again and review the receipt.');
            item.orderedQuantity=line.quantity;item.previouslyReceived=line.receivedQuantity+line.damagedQuantity;item.remaining=line.quantity-item.previouslyReceived;refreshItem(item);
        }
        order=current;
    }
    async function revalidateCart(){
        const ids=[...new Set(cart.map(i=>i.productId))];
        const rows=(await Promise.all(ids.map(productId=>api('/api/products/variants?'+new URLSearchParams({productId,activeOnly:true}))))).flat();cache(rows);
        const valid=new Map(rows.filter(p=>p.isActive!==false).map(p=>[Number(p.variantId),p]));
        for(const item of cart){const current=valid.get(Number(item.variantId));item.catalogInvalid=!current||!!current.isSerialTracked!==!!item.isSerialTracked||Number(current.packQuantity||1)!==Number(item.pack||1);if(current){item.currentStock=current.currentStock;item.sku=current.sku;}}
        if(cart.some(i=>i.catalogInvalid))el('goods-status').textContent='Some draft products are inactive or their tracking/pack configuration changed. Review and reselect those receipt items before confirming.';
    }
    function image(product) {
        const box=node('span','goods-image','◇');
        let source=product.hasProductImage?`/api/products/${product.productId}/image`:product.productImageUrl;
        if(source) {
            try { const url=new URL(source,location.origin); if(!['http:','https:'].includes(url.protocol)) return box; source=url.href; } catch { return box; }
            const img=document.createElement('img'); img.alt=''; img.src=source; img.onerror=()=>box.replaceChildren(document.createTextNode('◇')); box.replaceChildren(img);
        }
        return box;
    }
    function productName(product) {
        const box=node('div','goods-product'); const copy=node('div');
        copy.append(node('strong','',product.productName),node('small','',product.unitOfMeasure || 'Piece'));
        const attributes=StockFlowVariantIdentity.summary(product);if(attributes)copy.append(node('small','',attributes));
        box.append(image(product),copy); return box;
    }
    function field(labelText, type, value, onChange, options={}) {
        const wrap=node('div','goods-field'), label=node('label','form-label goods-field-label',labelText);
        const input=document.createElement(type==='select'?'select':type==='textarea'?'textarea':'input');
        if(input.tagName==='INPUT') input.type=type;
        input.id='goods-control-'+(++sequence); input.className=type==='select'?'form-select':'form-control'; label.htmlFor=input.id;
        if(type==='select') ['PIECE','BOX','PAIR','SET'].forEach(u=>input.add(new Option(u[0]+u.slice(1).toLowerCase(),u)));
        Object.assign(input,options); input.value=value??'';
        input.addEventListener(type==='select'?'change':'input',()=>{ onChange(input.value); changed(); });
        wrap.append(label,input); return {wrap,input};
    }
    function financial(item) {
        // Match the existing invoice rules: damaged deliveries remain part of the
        // supplier invoice until a return/credit is recorded. Never hide that value.
        const net=money(Number(item.quantity)*Number(item.price))-Number(item.discount);
        const tax=money(net*Number(item.tax)/100);
        return {net,tax,total:money(net+tax)};
    }
    function summary() {
        const result={received:0,damaged:0,accepted:0,subtotal:0,tax:0,acceptedValue:0};
        cart.forEach(item=>{const f=financial(item); result.received+=Number(item.quantity); result.damaged+=Number(item.damaged); result.accepted+=accepted(item); result.subtotal+=f.net; result.tax+=f.tax; result.acceptedValue+=money(accepted(item)*Number(item.price));});
        result.charges=kind()==='order'?0:Number(el('goods-freight').value||0)+Number(el('goods-charges').value||0);
        result.total=money(result.subtotal+result.tax+result.charges-(kind()==='order'?0:Number(el('goods-discount').value||0)));
        return result;
    }
    function totalsInto(host, totals) {
        host.replaceChildren(...[['Items',cart.length,false],['Received Qty',totals.received,false],['Subtotal',totals.subtotal,true],['Tax',totals.tax,true],['Freight & charges',totals.charges,true],['Invoice discount',kind()==='order'?0:Number(el('goods-discount').value||0),true],['Grand Total',totals.total,true]].map(([label,value,isMoney])=>{const cell=node('div');cell.append(node('span','',label),node('strong','',isMoney?currency(value):String(value)));return cell;}));
    }
    function updateTotals() {
        const t=summary(); totalsInto(el('goods-totals'),t);
        el('goods-items-title').lastElementChild.textContent=`Items in This Receipt (${cart.length})`;
        el('goods-clear').disabled=!cart.length||busy||!!pending;el('goods-add-item').hidden=!cart.length;
        el('goods-empty').hidden=cart.length>0; el('goods-list').hidden=!cart.length;
        el('goods-damage-warning').hidden=!t.damaged;
        el('goods-damage-warning').textContent=`${t.damaged} damaged unit(s) will be excluded from available stock. Invoice totals include received units; resolve damaged goods with the supplier. Accepted goods value before discounts and tax: ${currency(t.acceptedValue)}.`;
        updateDueDate();
    }
    function refreshItem(item) {
        if(item.purchaseItemId) {
            // Same cumulative rounding as PurchaseReceiptRepository.
            item.discount=money(item.orderDiscount*(item.previouslyReceived+Number(item.quantity))/item.orderedQuantity)-money(item.orderDiscount*item.previouslyReceived/item.orderedQuantity);
        }
        if(item.controls) {
            item.controls.accepted.value=accepted(item);
            item.controls.total.textContent=currency(financial(item).total);
            for(const name of ['quantity','damaged','price','mrp','discount','tax']) if(document.activeElement!==item.controls[name]) item.controls[name].value=item[name];
        }
        if(activeItem===item) {
            el('goods-detail-accepted').value=accepted(item);
            el('goods-detail-total').textContent=currency(financial(item).total);
            for(const type of ['good','damaged']) {
                const required=type==='good'?accepted(item):Number(item.damaged);
                el(`goods-${type}-serial-help`).textContent=`Enter ${required} unique serial number(s), one per line. ${serials(item[type==='good'?'serials':'damagedSerials']).length} entered.`;
            }
        }
        updateTotals();
    }
    function renderRows() {
        el('goods-items').replaceChildren();
        cart.forEach((item,index)=>{
            const row=node('div','goods-row goods-item'); item.view=row;
            row.append(node('span','goods-index',String(index+1)),productName(item),node('span','goods-sku',[item.variantName==='DEFAULT'?'':item.variantName,item.sku].filter(Boolean).join(' / ')));
            const stock=node('div','goods-field');stock.append(node('span','goods-field-label','Current stock'),node('strong','goods-current-stock',String(item.currentStock??'—')));row.append(stock);
            const controls={};
            for(const [name,label,min,step] of [['quantity','Received Quantity *',1,1],['damaged','Damaged Quantity',0,1],['accepted','Accepted Quantity',0,1],['price','Unit Price (₹) *',0,0.01],['mrp','MRP (₹)',0,0.01],['discount','Discount (₹)',0,0.01],['tax','Tax (%)',0,0.01]]) {
                const f=field(label,'number',name==='accepted'?accepted(item):item[name],value=>{item[name]=value;refreshItem(item);},{min:String(min),max:name==='tax'?'100':step===1?'10000':'9999999999.99',step:String(step),required:true,readOnly:name==='accepted'||!!item.purchaseItemId&&['price','discount','tax'].includes(name)});
                if(name==='accepted') f.input.classList.add('goods-accepted');
                controls[name]=f.input;
                if(name==='quantity'){
                    const group=node('div','goods-quantity');f.input.before(group);
                    const adjust=delta=>{const value=Number(f.input.value)+delta;if(value<1||value>10000||item.purchaseItemId&&value>item.remaining)return;f.input.value=value;f.input.dispatchEvent(new Event('input',{bubbles:true}));};
                    const minus=button('−','btn btn-outline-secondary',()=>adjust(-1)),plus=button('+','btn btn-outline-secondary',()=>adjust(1));
                    minus.setAttribute('aria-label','Decrease quantity for '+item.productName);plus.setAttribute('aria-label','Increase quantity for '+item.productName);group.append(minus,f.input,plus);
                }
                row.append(f.wrap);
            }
            const amount=node('div','goods-field'); amount.append(node('span','goods-field-label','Line total')); controls.total=node('strong','goods-amount',currency(financial(item).total));amount.append(controls.total);row.append(amount);
            const actions=node('div','goods-item-actions');
            const edit=button('✎','goods-icon-button',()=>openItem(item));edit.setAttribute('aria-label',`Edit details for ${item.productName}`); edit.title='Product details';
            const remove=button('×','goods-icon-button',()=>{cart=cart.filter(line=>line!==item);renderRows();changed();});remove.setAttribute('aria-label',`Remove ${item.productName}`); remove.title='Remove item';actions.append(edit,remove);row.append(actions);
            item.controls=controls; el('goods-items').append(row);
        }); updateTotals();
    }
    function addProduct(product, serial=null) {
        if(busy || pending || !contextValid(true)) return;
        if(product.isActive===false||!product.variantId)throw new Error('Choose an active registered variant.');
        if(kind()==='order') {
            const matches=cart.filter(i=>Number(i.variantId)===Number(product.variantId));
            if(matches.length!==1) throw new Error(matches.length?'This product appears on multiple order lines. Edit the matching line directly.':'This product is not on the selected purchase order.');
            const item=matches[0]; if(Number(item.quantity)>=item.remaining) throw new Error('The received quantity already matches the outstanding order quantity.');
            item.quantity=Number(item.quantity)+1; refreshItem(item);changed();return;
        }
        // Batch-bearing lines remain separate. A serial scan belongs to one unit.
        let item=cart.find(i=>Number(i.variantId)===Number(product.variantId)&&!i.batch&&!i.manufactured&&!i.expiry&&!Number(i.damaged)&&Number(i.discount)===0&&Number(i.price)===Number(product.purchasePrice||0)&&Number(i.tax)===Number(product.taxPercentage||0)&&Number(i.mrp)===Number(product.mrp||0));
        if(serial && cart.some(i=>[...serials(i.serials),...serials(i.damagedSerials)].some(s=>s.toLowerCase()===serial.toLowerCase()))) throw new Error('This serial number has already been scanned.');
        if(item && Number(item.quantity)>=10000) throw new Error('The maximum received quantity is 10,000 per line.');
        if(!item) {
            if(cart.length>=200) throw new Error('A receipt can contain at most 200 product lines.');
            item={...product,quantity:1,damaged:0,price:product.purchasePrice||0,discount:0,tax:product.taxPercentage||0,unit:product.unitOfMeasure||'PIECE',pack:product.packQuantity||1,mrp:product.mrp||0,supplierCode:product.supplierIdentifiers?.find(i=>i.supplierId===Number(el('goods-supplier').value))?.supplierSku||product.supplierProductCode||'',batch:'',manufactured:'',expiry:'',serials:'',damagedSerials:'',damageReason:''};cart.push(item);
        } else item.quantity=Number(item.quantity)+1;
        if(serial) item.serials+=(item.serials?'\n':'')+serial;
        renderRows(); changed();
        el('goods-scan-status').textContent=`${item.productName} added · Received ${item.quantity}`;
        if(item.isSerialTracked&&!serial&&!el('goods-item-dialog').open) openItem(item);
    }
    function detailField(host,item,name,label,type='text',options={}) {
        const f=field(label,type,item[name],value=>{item[name]=value;refreshItem(item);},options);
        f.wrap.querySelector('label').classList.remove('goods-field-label');host.append(f.wrap);return f.input;
    }
    function openItem(item) {
        activeItem=item;
        const host=el('goods-item-details');host.replaceChildren();
        const identity=node('div','goods-detail-identity');identity.append(productName(item),node('span','goods-sku',`SKU: ${item.sku||'—'}`),node('span','goods-badge',item.isSerialTracked?'Serial Tracked':'Standard Product'));host.append(identity);
        const primary=node('div','goods-detail-grid');
        detailField(primary,item,'quantity','Received Quantity *','number',{min:1,max:10000,step:1,required:true});
        detailField(primary,item,'damaged','Damaged Quantity','number',{min:0,max:10000,step:1,required:true});
        const good=field('Accepted Quantity','number',accepted(item),()=>{},{readOnly:true}); good.input.id='goods-detail-accepted';good.wrap.querySelector('label').htmlFor=good.input.id;good.wrap.querySelector('label').classList.remove('goods-field-label');good.input.classList.add('goods-accepted');primary.append(good.wrap);
        detailField(primary,item,'price','Unit Price (₹) *','number',{min:0,max:9999999999.99,step:0.01,required:true,readOnly:!!item.purchaseItemId});host.append(primary);
        const lineTotal=node('p','goods-detail-hint','Line total: ');lineTotal.append(node('strong','',currency(financial(item).total)));lineTotal.lastChild.id='goods-detail-total';host.append(lineTotal);
        if(item.purchaseItemId) host.append(node('p','goods-detail-hint',`Ordered ${item.orderedQuantity} · Previously received/damaged ${item.previouslyReceived} · Outstanding ${item.remaining}. Prices and tax follow the purchase order.`));
        const more=node('details','goods-detail-more');more.open=true;more.append(node('summary','','Additional Information'));const grid=node('div','goods-detail-grid');more.append(grid);
        detailField(grid,item,'unit','Purchase Unit','select'); detailField(grid,item,'pack','Pack Size','number',{min:1,max:10000,step:1,required:true,readOnly:true});
        detailField(grid,item,'mrp','MRP (₹)','number',{min:0,max:9999999999.99,step:0.01}); detailField(grid,item,'supplierCode','Supplier SKU','text',{maxLength:100});
        detailField(grid,item,'batch','Batch / Lot number','text',{maxLength:100}); detailField(grid,item,'manufactured','Manufacturing date','date');detailField(grid,item,'expiry','Expiry date','date');
        detailField(grid,item,'damageReason','Damage reason','text',{maxLength:2000});host.append(more);
        if(Number(item.pack)>1) host.append(node('p','goods-detail-hint',`Pack size: ${item.pack}. Enter received quantities in inventory units; ${item.pack} pieces = 1 pack. This receipt adds ${accepted(item)} accepted inventory units.`));
        const tracking=node('div','goods-serials');tracking.hidden=!item.isSerialTracked;
        for(const [type,name,label] of [['good','serials','Serial / Unit IDs (for accepted units)'],['damaged','damagedSerials','Serial / Unit IDs (for damaged units)']]) {
            const column=node('div'), help=node('p');help.id=`goods-${type}-serial-help`;
            const text=detailField(column,item,name,label,'textarea',{maxLength:1510000});column.append(help);
            const entry=node('div','goods-serial-entry');const input=document.createElement('input');input.className='form-control';input.placeholder='Scan or type serial';input.maxLength=150;input.setAttribute('aria-label',`Scan ${type} serial`);
            const append=()=>{
                const serial=input.value.trim(); if(!serial) return;
                if(cart.some(i=>[...serials(i.serials),...serials(i.damagedSerials)].some(s=>s.toLowerCase()===serial.toLowerCase()))) return error(new Error('This serial number is already entered.'));
                const count=type==='good'?accepted(item):Number(item.damaged);
                if(serials(item[name]).length>=count) return error(new Error('The serial count already matches this quantity.'));
                item[name]+=(item[name]?'\n':'')+serial;text.value=item[name];input.value='';refreshItem(item);changed();
            };
            input.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();append();}});
            entry.append(input,button('Add Serial','btn btn-outline-primary',append));if(canScan) window.StockFlowCamera?.attach(input,append);column.append(entry);
            const generate=button('Generate internal unit IDs','btn btn-outline-secondary mt-2',()=>run(async()=>{
                if(generate.disabled) return;
                clearError();
                const count=(type==='good'?accepted(item):Number(item.damaged))-serials(item[name]).length;
                if(count===0) return;
                if(count<0||count>10000) throw new Error('Correct the quantity before generating unit IDs.');
                const requestKey=name+'IdentifierRequest';
                const request=item[requestKey] ||= {requestId:uuid(),count};
                if(request.count!==count) throw new Error('Retry the pending unit ID request before changing its quantity.');
                generate.disabled=true;
                try {
                    await saveDraft();
                    const result=await api('/api/receiving/unit-identifiers',{method:'POST',body:JSON.stringify({variantId:item.variantId,requestId:request.requestId,count:request.count,locationId,businessProfileCode:workspace.dataset.businessProfile})});
                    if(!Array.isArray(result.codes)||result.codes.length!==request.count) throw new Error('Unable to allocate unit IDs. Retry the same request.');
                    const entered=serials(item[name]);
                    for(const code of result.codes) if(!entered.some(value=>value.toLowerCase()===code.toLowerCase())) entered.push(code);
                    item[name]=entered.join('\n');delete item[requestKey];text.value=item[name];refreshItem(item);await saveDraft();
                } finally {generate.disabled=false;}
            }));column.append(generate,node('small','goods-detail-hint','Enter the manufacturer serial when present. Internal unit IDs identify items without one; they are not manufacturer serials. Printing a barcode does not change either identifier.'));tracking.append(column);
        }
        host.append(tracking); refreshItem(item);el('goods-item-dialog').showModal();
    }
    function updateDueDate() {
        const date=el('goods-invoice-date').value||el('goods-date').value;
        if(!date){el('goods-due-date').textContent='';return;}
        const due=new Date(date+'T12:00:00'); due.setDate(due.getDate()+Math.max(0,Math.min(365,Number(el('goods-credit').value||0))));
        if(!Number.isFinite(due.getTime())){el('goods-due-date').textContent='';return;}
        el('goods-due-date').textContent='Due '+new Intl.DateTimeFormat('en-IN',{dateStyle:'medium'}).format(due);
    }
    function duplicateInvoice() {
        const invoice=el('goods-invoice').value.trim().toLowerCase(),supplier=Number(el('goods-supplier').value);
        const duplicate=orders.some(p=>p.supplierId===supplier && p.supplierInvoiceNumber?.trim().toLowerCase()===invoice && p.purchaseId!==order?.purchaseId);
        el('goods-invoice-warning').hidden=!invoice||!duplicate;
        el('goods-invoice-warning').textContent='This invoice number already exists for this supplier. Verify it before confirming.';
    }
    function filterOrders() {
        const supplier=Number(el('goods-supplier').value);const selected=el('goods-order').value;
        el('goods-order').replaceChildren(new Option('Choose open purchase order',''),...orders.filter(p=>['DRAFT','PARTIALLY_RECEIVED'].includes(p.status)&&(!supplier||p.supplierId===supplier)).map(p=>new Option(`${p.purchaseNumber||'PO-'+p.purchaseId} (${p.purchaseDate?.slice(0,10)||''})`,p.purchaseId)));
        el('goods-order').value=selected;
    }
    function applyMode() {
        const po=kind()==='order';el('goods-order-field').hidden=!po;el('goods-order').required=po;
        el('goods-order-invoice-note').hidden=!po;
        supplierInfo();contextValid();
        for(const id of ['goods-credit','goods-freight','goods-charges','goods-discount','goods-file']) el(id).disabled=po;
        duplicateInvoice(); updateTotals();
    }
    async function loadOrder(id) {
        if(!id){order=null;cart=[];renderRows();return;}
        loading=true;freeze();
        try {
            const result=await api('/api/purchases/'+encodeURIComponent(id));
            if(!['DRAFT','PARTIALLY_RECEIVED'].includes(result.status)) throw new Error('This purchase order is no longer open.');
            if(Number(el('goods-supplier').value)&&Number(el('goods-supplier').value)!==result.supplierId) throw new Error('The purchase order belongs to a different supplier.');
            cache((await Promise.all([...new Set(result.items.map(i=>i.productId))].map(productId=>api('/api/products/variants?'+new URLSearchParams({productId,activeOnly:true}))))).flat());
            order=result;el('goods-supplier').value=order.supplierId;el('goods-invoice').value=order.supplierInvoiceNumber||'';el('goods-invoice-date').value=order.supplierInvoiceDate?.slice(0,10)||'';el('goods-credit').value=order.paymentTermsDays||0;
            el('goods-order-help').textContent='Partial deliveries leave the remaining quantities open.';
            cart=order.items.filter(i=>i.quantity-i.receivedQuantity-i.damagedQuantity>0).map(i=>{
                const p=products.find(p=>Number(p.variantId)===Number(i.variantId))||i;
                const prior=i.receivedQuantity+i.damagedQuantity,remaining=i.quantity-prior;
                return {...p,purchaseItemId:i.purchaseItemId,orderedQuantity:i.quantity,previouslyReceived:prior,remaining,orderDiscount:i.discountAmount,quantity:remaining,damaged:0,price:i.purchasePrice,tax:i.taxPercentage,discount:money(i.discountAmount)-money(i.discountAmount*prior/i.quantity),unit:i.unitOfMeasure||'PIECE',pack:i.packQuantity||1,mrp:i.mrp||0,supplierCode:i.supplierProductCode||'',batch:i.batchNumber||'',manufactured:i.manufacturingDate?.slice(0,10)||'',expiry:i.expiryDate?.slice(0,10)||'',serials:'',damagedSerials:'',damageReason:''};
            });renderRows();
        } finally {loading=false;freeze();applyMode();changed();}
    }
    function closeSuggestions() {el('goods-suggestions').replaceChildren();el('goods-suggestions').hidden=true;el('goods-search').setAttribute('aria-expanded','false');selection=-1;}
    let searchTimer,searchVersion=0,registrationOpened=false;
    async function searchRows(term){return cache(await api('/api/products/variants?'+new URLSearchParams({term,receiving:true})));}
    async function suggestions(version=++searchVersion){
        const term=el('goods-search').value.trim();closeSuggestions();if(!term||!contextValid())return;
        lookups++;
        try{
            const rows=await searchRows(term);if(version!==searchVersion)return;
            const host=el('goods-suggestions');host.replaceChildren();
            const table=node('table','goods-search-table');const head=node('thead'),heading=node('tr');
            for(const caption of ['Product','Variant / Model','SKU','Barcode','Brand','Stock','Action'])heading.append(node('th','',caption));head.append(heading);table.append(head);
            const body=node('tbody');
            for(const product of rows){
                const row=node('tr');const identity=node('td');identity.append(productName(product));
                if(workspace.dataset.canManage==='true'){const manage=node('a','goods-manage','Manage variants');manage.href='/Products/Edit?id='+product.productId;manage.target='_blank';manage.rel='noopener';manage.onclick=navigateRegistration;identity.append(manage);}
                row.append(identity);
                for(const value of [product.variantName==='DEFAULT'?'Standard':product.variantName||product.attributeSummary||'Standard',product.sku,product.barcode||product.primaryBarcode||'—',product.brandName||'—'])row.append(node('td','',value));
                const stock=node('td'),badge=node('span','goods-stock-badge',String(product.currentStock??0)+' in stock');if(!product.currentStock)badge.classList.add('goods-stock-empty');stock.append(badge);row.append(stock);
                const cell=node('td'),add=button('Add','btn btn-outline-secondary',()=>run(()=>{addProduct(product);el('goods-search').value='';++searchVersion;closeSuggestions();if(!el('goods-item-dialog').open)el('goods-search').focus();}));add.dataset.result='true';add.disabled=product.isActive===false;add.setAttribute('aria-label','Add '+product.productName+' SKU '+product.sku);cell.append(add);row.append(cell);body.append(row);
            }
            table.append(body);host.append(table);
            if(!rows.length)host.append(node('p','p-2 mb-0','No registered products found. Try another name, SKU or barcode.'));
            el('goods-scan-status').textContent=rows.length===30?'Showing 30 matching SKUs. Refine your search.':rows.length+' registered SKU(s). Select the exact size, color or model.';
            host.hidden=false;el('goods-search').setAttribute('aria-expanded','true');
        }finally{lookups--;}
    }
    async function lookup(code){
        clearTimeout(searchTimer);++searchVersion;
        if(busy||pending||loading||!contextValid(true))return;
        lookups++;clearError();
        try{
            closeSuggestions();let match,ambiguous=false;
            if(canScan){try{match=await api('/api/scan?code='+encodeURIComponent(code));}catch(e){if(e.status===400||e.status===422)ambiguous=true;else if(e.status!==404)throw e;}}
            if(match?.productUnitId)throw new Error('This serial already exists in inventory. Choose its product model and enter a new unit identifier.');
            if(match?.variantId){
                const current=cache(await api('/api/products/variants?'+new URLSearchParams({productId:match.productId,activeOnly:true}))).find(p=>Number(p.variantId)===Number(match.variantId));
                if(!current||current.isActive===false)throw new Error('This product or variant is inactive in this shop.');
                addProduct(current);return;
            }
            const rows=await searchRows(code);
            if(rows.length===1&&!ambiguous){addProduct(rows[0]);return;}
            el('goods-search').value=code;await suggestions();
            if(ambiguous)el('goods-scan-status').textContent='This code is ambiguous. Choose the exact registered SKU; existing unit conflicts need administrator review.';
        }finally{lookups--;}
    }
    function navigateRegistration(event){
        if(loading||busy||reviewing||pending){event.preventDefault();return;}
        if(!contextValid(true)){event.preventDefault();return;}
        registrationOpened=true;el('goods-registration-status').hidden=false;
        // The existing Product screen opens in a separate tab, preserving this receipt.
        // No stock or variant is created by this navigation handler.
        run(()=>saveDraft(true));
    }
    async function refreshRegistered(){
        if(busy||pending||loading)return;
        lookups++;
        try{await revalidateCart();renderRows();if(el('goods-search').value.trim())await suggestions();}
        finally{lookups--;}
    }
    function focusItemError(item,name,message) {
        if(!el('goods-item-dialog').open)openItem(item);
        const controls=el('goods-item-details').querySelectorAll('input,select,textarea');
        const labels={quantity:'Received Quantity *',damaged:'Damaged Quantity',serials:'Serial / Unit IDs (for accepted units)',damagedSerials:'Serial / Unit IDs (for damaged units)',expiry:'Expiry date',damageReason:'Damage reason'};
        const label=[...el('goods-item-details').querySelectorAll('label')].find(l=>l.textContent===labels[name]);
        const input=label?el(label.htmlFor):controls[0];
        error(new Error(message));window.StockFlowValidation?.showField(input,message);input.focus();return false;
    }
    function validate() {
        if(!contextValid(true))return false;
        if(!el('goods-form').reportValidity())return false;
        if(!canComplete)throw new Error('You do not have permission to receive stock.');
        if(lookups)throw new Error('Wait for the current product lookup to finish.');
        if(kind()==='order'&&!order)throw new Error('Choose an open purchase order.');
        if(!cart.length)throw new Error('Add at least one product.');
        if(el('goods-invoice-date').value>today)throw new Error('Invoice date cannot be in the future.');
        if(order&&el('goods-date').value<order.purchaseDate.slice(0,10))throw new Error('Received date cannot precede the purchase order date.');
        const seen=new Set();
        for(const item of cart) {
            if(!Number.isInteger(Number(item.quantity))||Number(item.quantity)<1||Number(item.quantity)>10000)return focusItemError(item,'quantity','Enter a received quantity from 1 to 10,000.');
            if(!Number.isInteger(Number(item.damaged))||Number(item.damaged)<0||Number(item.damaged)>Number(item.quantity))return focusItemError(item,'damaged','Damaged quantity cannot exceed received quantity.');
            if(item.purchaseItemId&&Number(item.quantity)>item.remaining)return focusItemError(item,'quantity',`This order has ${item.remaining} outstanding unit(s). Excess deliveries cannot be received against this order.`);
            if(item.catalogInvalid||!products.some(p=>Number(p.variantId)===Number(item.variantId)))throw new Error('A product is inactive or its tracking/pack configuration changed. Review and reselect that receipt item.');
            if(![item.price,item.discount,item.tax,item.mrp].every(n=>Number.isFinite(Number(n))&&Number(n)>=0)||Number(item.tax)>100||Number(item.discount)>money(Number(item.quantity)*Number(item.price)))return focusItemError(item,'quantity','Check unit price, discount, MRP and tax. Discount cannot exceed the line amount.');
            if(item.expiry&&item.manufactured&&item.expiry<item.manufactured)return focusItemError(item,'expiry','Expiry date cannot precede manufacturing date.');
            if(Number(item.damaged)>0&&!item.damageReason.trim())return focusItemError(item,'damageReason','Enter a reason for the damaged stock.');
            if(item.isSerialTracked) {
                for(const [name,count] of [['serials',accepted(item)],['damagedSerials',Number(item.damaged)]]) {
                    const values=serials(item[name]);
                    if(values.length!==count)return focusItemError(item,name,`Enter exactly ${count} ${name==='serials'?'accepted':'damaged'} serial number(s), one per line.`);
                    for(const serial of values) {if(serial.length>150||seen.has(serial.toLowerCase()))return focusItemError(item,name,'Serial numbers must be unique across this receipt and at most 150 characters.');seen.add(serial.toLowerCase());}
                }
            }
        }
        if(summary().total<0)throw new Error('Invoice discount cannot exceed the receipt value.');
        return true;
    }
    function renderReview() {
        const host=el('goods-review-content');host.replaceChildren();
        const header=node('div','goods-review-header');
        for(const [label,value] of [['Receipt type',order?'Purchase Order':'Direct Receipt'],['Purchase Order',order?.purchaseNumber||order?.purchaseId||'Not applicable'],['Invoice Date',el('goods-invoice-date').value||'Not provided'],['Notes',el('goods-notes').value||'—'],['Attachment',attachment?.name||'None'],['Supplier',suppliers.find(s=>s.supplierId===Number(el('goods-supplier').value))?.supplierName],['Invoice Number',el('goods-invoice').value||'Not provided'],['Received Date',el('goods-date').value],['Stock Location',el('goods-location').selectedOptions[0]?.textContent]]) {const group=node('div');group.append(node('small','',label),node('strong','',value));header.append(group);}host.append(header);
        const tableWrap=node('div','goods-review-table-wrap'),table=node('table','goods-review-table');const head=node('thead'),headRow=node('tr');
        for(const title of ['#','Product','Received','Damaged','Accepted','Unit Price','Line total'])headRow.append(node('th','',title));head.append(headRow);table.append(head);const body=node('tbody');
        cart.forEach((item,index)=>{const row=node('tr');[index+1,[item.productName,item.variantName,item.sku,item.batch?'Batch: '+item.batch:'',item.serials?'Accepted IDs: '+item.serials:'',item.damagedSerials?'Damaged IDs: '+item.damagedSerials:''].filter(Boolean).join(' · '),item.quantity,item.damaged,accepted(item),currency(item.price),currency(financial(item).total)].forEach(value=>row.append(node('td','',String(value))));body.append(row);});table.append(body);tableWrap.append(table);host.append(tableWrap);
        const t=summary(),counts=node('div','goods-review-counts');
        for(const [label,value] of [['Total Products',cart.length],['Total Received',t.received],['Total Accepted',t.accepted],['Total Damaged',t.damaged]]){const group=node('div');group.append(node('span','',label),node('strong','',String(value)));counts.append(group);}host.append(counts);
        const total=node('div','goods-totals');totalsInto(total,t);host.append(total);
        if(t.damaged)host.append(node('p','goods-warning mt-2',`${t.damaged} damaged units are excluded from available stock. The invoice includes received units; accepted goods value before discounts/tax is ${currency(t.acceptedValue)}. Arrange a supplier return or credit for any disputed invoice value.`));
        if(!el('goods-invoice-warning').hidden)host.append(node('p','goods-warning',el('goods-invoice-warning').textContent));
        if(cart.some(i=>i.expiry&&i.expiry<el('goods-date').value))host.append(node('p','goods-warning','One or more items have already expired. Check their expiry dates before confirming.'));
        if(order)host.append(node('p','goods-detail-hint',`Receiving against ${order.purchaseNumber||'PO-'+order.purchaseId}. Any undelivered quantities remain open. The amount shown is this delivery's item value; order-level invoice charges remain on the purchase order.`));
    }
    function request() {
        const notes=[el('goods-notes').value,...cart.filter(i=>Number(i.damaged)>0).map(i=>`${i.sku||i.productName}: ${i.damaged} damaged — ${i.damageReason}`)].filter(Boolean).join('\n');
        if(notes.length>2000)throw new Error('Notes and damage reasons together must be at most 2,000 characters.');
        const common={locationId,receivedDate:el('goods-date').value,supplierInvoiceNumber:el('goods-invoice').value.trim()||null,supplierInvoiceDate:el('goods-invoice-date').value||null,notes};
        const items=cart.map(item=>({productId:item.productId,variantId:item.variantId,purchaseItemId:item.purchaseItemId,quantity:Number(item.quantity),receivedQuantity:accepted(item),damagedQuantity:Number(item.damaged),purchasePrice:Number(item.price),discountAmount:Number(item.discount),taxPercentage:Number(item.tax),serialNumbers:item.isSerialTracked?serials(item.serials):[],damagedSerialNumbers:item.isSerialTracked?serials(item.damagedSerials):[],unitOfMeasure:item.unit,packQuantity:Number(item.pack),mrp:Number(item.mrp),supplierProductCode:item.supplierCode||null,batchNumber:item.batch||null,manufacturingDate:item.manufactured||null,expiryDate:item.expiry||null}));
        if(order)return {url:`/api/purchases/${order.purchaseId}/receipts`,purchaseId:order.purchaseId,body:{...common,requestId:uuid(),items:items.map(i=>({...i,quantity:i.receivedQuantity}))},totals:summary()};
        return {url:'/api/purchases',body:{...common,requestId:uuid(),supplierId:Number(el('goods-supplier').value),purchaseDate:common.receivedDate,paymentTermsDays:Number(el('goods-credit').value||0),freightAmount:Number(el('goods-freight').value||0),otherLandedCost:Number(el('goods-charges').value||0),discountAmount:Number(el('goods-discount').value||0),invoiceAttachmentName:attachment?.name||null,invoiceAttachmentContentType:attachment?.type||null,invoiceAttachmentData:attachment?.data||null,completeImmediately:true,items},totals:summary()};
    }
    async function finish() {
        if(!pending||busy)return;
        busy=true;freeze();clearError();
        try {
            const result=await api(pending.url,{method:'POST',body:JSON.stringify(pending.body)});
            const purchaseId=pending.purchaseId||result?.purchaseId;
            if(!purchaseId)throw new Error('The receipt response was incomplete. Retry this saved receipt.');
            let receipts=[];try{receipts=await api(`/api/purchases/${purchaseId}/receipts`);}catch{/* Posting succeeded; the purchase page remains available. */}
            const receipt=receipts.find(r=>Number(r.receiptId)===Number(result?.receiptId))||(!pending.purchaseId?receipts.at(-1):null);
            const totals=pending.totals;
            el('goods-success-number').textContent='Receipt No: '+(receipt?.receiptNumber||(result?.receiptId?'Receipt #'+result.receiptId:'Purchase #'+purchaseId));
            el('goods-success-date').textContent='Date: '+pending.body.receivedDate;
            el('goods-success-stock').textContent=`${totals.accepted} units have been added to available stock. ${totals.damaged} units have been recorded as damaged.`;
            el('receipt-view').href=receipt?.receiptId||result?.receiptId?`/StockInflow/Details/PURCHASE/${receipt?.receiptId||result.receiptId}`:`/Purchases/Details/${purchaseId}`;
            const summaryHost=el('goods-success-summary');summaryHost.replaceChildren();
            for(const [label,value] of [['Supplier',suppliers.find(s=>s.supplierId===Number(el('goods-supplier').value))?.supplierName],['Invoice Number',pending.body.supplierInvoiceNumber||'—'],['Stock Location',el('goods-location').selectedOptions[0]?.textContent],['Total Products',cart.length],['Total Received',totals.received],['Total Accepted',totals.accepted],['Total Damaged',totals.damaged],['Total Amount',currency(totals.total)]])summaryHost.append(node('dt','',label),node('dd','',String(value)));
            // A successful post must never be retried merely because clearing a
            // browser draft failed. Persist the success marker first.
            pending={...pending,completed:true,purchaseId};
            try{await stored('delete');}catch{await stored('put',snapshot());}
            pending=null;clearTimeout(draftTimer);window.StockFlowUnsaved?.markSaved(el('goods-form'));el('receipt-confirm').close();el('goods-form').hidden=true;el('goods-recovery').hidden=true;el('receipt-success').hidden=false;el('receipt-success').focus();
        } catch(e) {
            // Validation failures are definitive. Connection/5xx failures keep the
            // exact payload and request ID for an idempotent retry.
            if([400,422].includes(e.status)){pending=null;await saveDraft();}
            error(e);
        } finally {busy=false;freeze();}
    }
    for(const b of document.querySelectorAll('[data-close]'))b.onclick=()=>el(b.dataset.close).close();
    el('goods-item-dialog').addEventListener('close',()=>{activeItem=null;renderRows();changed();});
    el('receipt-confirm').addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    el('goods-confirm').onclick=()=>run(async()=>{
        if(busy||pending)return;
        // Review is immutable: the background cannot be edited while this dialog is open.
        pending=request();
        try { await saveDraft(); }
        catch(e) { pending=null; throw e; }
        el('receipt-confirm').close();await finish();
    });
    el('goods-retry').onclick=()=>run(finish);
    el('goods-draft').onclick=()=>run(()=>saveDraft(true));
    el('goods-form').addEventListener('submit',event=>{event.preventDefault();run(async()=>{clearError();if(busy||reviewing||pending||lookups)throw new Error('Wait for current receiving operations to finish.');if(!contextValid(true))return;lookups++;reviewing=true;freeze();try{await revalidateOrder();await revalidateCart();}finally{lookups--;reviewing=false;freeze();}if(validate()){renderReview();el('receipt-confirm').showModal();}});});
    el('goods-form').addEventListener('keydown',event=>{if(event.key==='Enter'&&event.target.tagName==='INPUT')event.preventDefault();});
    for(const id of headerIds)el(id).addEventListener('input',()=>{duplicateInvoice();updateTotals();contextValid();changed();});
    el('goods-supplier').addEventListener('change',()=>run(async()=>{
        const supplier=suppliers.find(s=>s.supplierId===Number(el('goods-supplier').value));el('goods-credit').value=supplier?.paymentTermsDays||0;
        if(order&&order.supplierId!==Number(el('goods-supplier').value)){order=null;cart=[];el('goods-order').value='';renderRows();}
        el('goods-supplier-picker').open=false;supplierInfo();filterOrders();duplicateInvoice();updateDueDate();contextValid();changed();
    }));
    for(const radio of document.querySelectorAll('[name="goods-kind"]'))radio.onchange=async()=>{
        if(cart.length && !await StockFlowDialog.confirm('Change receiving type and clear the current item list?')){document.querySelector(`[name="goods-kind"][value="${radio.value==='order'?'direct':'order'}"]`).checked=true;return;}
        order=null;cart=[];el('goods-order').value='';renderRows();applyMode();changed();
    };
    el('goods-order').onchange=()=>run(()=>loadOrder(el('goods-order').value));
    el('goods-add-item').onclick=()=>{el('goods-search').focus();el('goods-search').scrollIntoView({block:'center'});suggestions();};
    el('goods-register')?.addEventListener('click',navigateRegistration);
    el('goods-search').addEventListener('input',()=>{clearTimeout(searchTimer);const version=++searchVersion;closeSuggestions();if(contextValid())searchTimer=setTimeout(()=>run(()=>suggestions(version)),250);});
    // Keyboard suggestions are handled before the queued scanner Enter handler.
    el('goods-search').addEventListener('keydown',event=>{
        const buttons=[...el('goods-suggestions').querySelectorAll('button[data-result]:not(:disabled)')];
        if(event.key==='Escape'){++searchVersion;clearTimeout(searchTimer);closeSuggestions();return;}
        if(!buttons.length)return;
        if(['ArrowDown','ArrowUp'].includes(event.key)){event.preventDefault();selection=(selection+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons.forEach((b,i)=>b.classList.toggle('is-active',i===selection));buttons[selection].scrollIntoView({block:'nearest'});}
        if(event.key==='Enter'&&selection>=0){event.preventDefault();event.stopImmediatePropagation();buttons[selection].click();}
    });
    let lookupAction;
    if(canScan) {
        lookupAction=StockFlowScanner.bind('goods-search',lookup,error);
        const camera=el('goods-search').parentElement.querySelector('.sf-camera-trigger');
        if(camera){camera.textContent='Scan with Camera';el('goods-lookup').before(camera);}
    } else {
        lookupAction=()=>{const value=el('goods-search').value.trim();if(value){el('goods-search').value='';return run(()=>lookup(value));}};
        el('goods-search').addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();lookupAction();}});
    }
    el('goods-lookup').onclick=lookupAction;
    document.addEventListener('pointerdown',event=>{if(!el('goods-search').closest('.goods-add-products').contains(event.target))closeSuggestions();});
    el('goods-file').onchange=()=>run(async()=>{
        const file=el('goods-file').files[0];attachment=null;
        if(previewUrl)URL.revokeObjectURL(previewUrl);el('goods-file-preview').hidden=true;
        if(file) {
            if(file.size>5000000||!['application/pdf','image/jpeg','image/png'].includes(file.type)){el('goods-file').value='';throw new Error('Attach one PDF, JPG or PNG up to 5 MB.');}
            lookups++;
            try {
                const data=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]);reader.onerror=()=>reject(new Error('Unable to read the invoice.'));reader.readAsDataURL(file);});
                attachment={name:file.name,type:file.type,data};el('goods-file-remove').hidden=false;previewUrl=URL.createObjectURL(file);el('goods-file-preview').href=previewUrl;el('goods-file-preview').textContent='Preview '+file.name;el('goods-file-preview').hidden=false;
            }finally{lookups--;}
        }changed();
    });
    el('goods-location').onchange=()=>run(async()=>{
        const id=Number(el('goods-location').value);if(id===locationId)return;
        await saveDraft(true);const option=el('goods-location').selectedOptions[0];
        try{await api('/api/branches/context',{method:'POST',body:JSON.stringify({locationId:id,storeId:Number(option.dataset.storeId)||0})});location.reload();}
        catch(e){el('goods-location').value=String(locationId);throw e;}
    });
    window.addEventListener('pagehide',()=>{clearTimeout(draftTimer);if(!el('goods-form').hidden&&!loading)run(()=>saveDraft());});
    el('goods-context-focus').onclick=()=>contextValid(true);
    el('goods-supplier-search').oninput=()=>{const term=el('goods-supplier-search').value.toLowerCase(),selected=el('goods-supplier').value;for(const option of el('goods-supplier').options){const supplier=suppliers.find(s=>String(s.supplierId)===option.value);option.hidden=!!option.value&&option.value!==selected&&![supplier?.supplierName,supplier?.supplierCode,supplier?.gstNumber,supplier?.mobileNumber].join(' ').toLowerCase().includes(term);}};
    el('goods-supplier-label').onclick=event=>{if(order)event.preventDefault();};
    el('goods-supplier-picker').addEventListener('toggle',()=>{if(el('goods-supplier-picker').open&&!order)el('goods-supplier-search').focus();});
    el('goods-refresh').onclick=()=>run(refreshRegistered);
    window.addEventListener('focus',()=>{if(registrationOpened)run(refreshRegistered);});
    el('goods-clear').onclick=()=>run(async()=>{if(!cart.length||busy||pending||lookups)return;if(!await StockFlowDialog.confirm('Remove all draft receipt items? Supplier and invoice information will be preserved.',{kind:'discard',title:'Clear receipt items?',cancelText:'Continue Editing',confirmText:'Clear Items'}))return;if(busy||pending||lookups)return;cart=[];renderRows();changed();});
    el('goods-file-remove').onclick=()=>{attachment=null;if(previewUrl)URL.revokeObjectURL(previewUrl);previewUrl=null;el('goods-file').value='';el('goods-file-preview').hidden=true;el('goods-file-remove').hidden=true;changed();};
    try {
        storage=await openStorage();
        const [supplierList,orderList,context,draft]=await Promise.all([api('/api/suppliers?activeOnly=true'),api('/api/purchases'),api('/api/branches/context'),stored('get')]);
        suppliers=supplierList;orders=orderList;
        el('goods-date').value=today;
        el('goods-supplier').replaceChildren(new Option('Choose supplier',''),...suppliers.map(s=>new Option([s.supplierCode,s.supplierName].filter(Boolean).join(' - '),s.supplierId)));
        const locations=context.locations||[];
        el('goods-location').replaceChildren(...locations.map(l=>{const option=new Option([l.storeName,l.locationName].filter(Boolean).join(' / '),l.locationId);option.dataset.storeId=l.storeId||0;return option;}));
        if(locationId>0&&!locations.some(l=>Number(l.locationId)===locationId))el('goods-location').add(new Option(workspace.dataset.locationName||'Current stock location',locationId));
        el('goods-location').value=String(locationId);el('goods-location').disabled=!context.canReviewStores;
        if(locationId<=0)el('goods-location-help').textContent='Select one stock location in the top bar before receiving goods.';
        if(draft?.version===1&&!draft.pending?.completed) {
            for(const id of headerIds)if(draft.header[id]!==undefined){
                if(id==='goods-supplier'&&draft.header[id]&&![...el(id).options].some(o=>o.value===String(draft.header[id])))el(id).add(new Option('Saved supplier #'+draft.header[id]+' (review required)',draft.header[id]));
                el(id).value=draft.header[id];
            }
            cart=draft.cart||[];order=draft.order||null;pending=draft.pending||null;attachment=draft.attachment||null;
            document.querySelector(`[name="goods-kind"][value="${draft.kind==='order'?'order':'direct'}"]`).checked=true;
            el('goods-status').textContent='Your saved draft has been restored. Review the details before confirming.';
            if(attachment) {const raw=atob(attachment.data);const bytes=Uint8Array.from(raw,c=>c.charCodeAt(0));previewUrl=URL.createObjectURL(new Blob([bytes],{type:attachment.type}));el('goods-file-preview').href=previewUrl;el('goods-file-remove').hidden=false;el('goods-file-preview').textContent='Saved invoice: '+attachment.name;el('goods-file-preview').hidden=false;}
        }
        filterOrders();if(order)el('goods-order').value=order.purchaseId;
        if(cart.length&&!pending){await revalidateCart();try{await revalidateOrder();}catch(e){el('goods-status').textContent='Draft restored. '+e.message;}}
        renderRows();applyMode();loading=false;freeze();supplierInfo();
        if(!canComplete)el('goods-status').textContent='You can prepare and save a draft. Stock approval permission is required to confirm a receipt.';
        if(!pending&&!draft)el('goods-supplier-label').focus();
    } catch(e){error(e);loading=false;freeze();el('goods-fields').disabled=true;}
})();
