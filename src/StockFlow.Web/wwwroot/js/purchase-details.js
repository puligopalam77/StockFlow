(() => {
    'use strict';
    const $=id=>document.getElementById(id),id=location.pathname.split('/').filter(Boolean).pop();
    const currency=new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',minimumFractionDigits:2});
    const money=value=>value!=null&&Number.isFinite(Number(value))?currency.format(Number(value)):'\u2014';
    const missing=new Set(['','\u2014','\u2013','\u00e2\u20ac\u201c','\u00e2\u20ac\u201d','\u00e2\u20ac\u2013','\u00c3\u00a2\u00e2\u201a\u00ac\u00e2\u20ac\u009d']);
    const text=value=>value==null||missing.has(String(value).trim())?'\u2014':String(value);
    const date=value=>{if(!value)return '\u2014';const parsed=new Date(value.slice(0,10)+'T00:00:00');return Number.isNaN(parsed.getTime())?'\u2014':parsed.toLocaleDateString('en-GB',{day:'2-digit',month:'short',year:'numeric'});};
    function facts(target,entries){target.replaceChildren();for(const [label,value] of entries){const row=document.createElement('div');row.className='pd-fact';const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;if(value instanceof Node)dd.append(value);else dd.textContent=text(value);row.append(dt,dd);target.append(row);}}
    const cell=(row,value,className)=>{const td=Purchases.cell(row,value);if(className)td.className=className;return td;};
    let purchase=null,items=[],lotsPromise=null;
    const canBarcodes=$('purchase-actions').dataset.canBarcodes==='true';
    const lots=()=>lotsPromise??=Purchases.api('/api/labels/lots').then(rows=>rows.filter(l=>l.purchaseId===purchase.purchaseId));
    function barcodeLink(){const link=document.createElement('a');link.href='/Labels?'+new URLSearchParams({purchaseId:purchase.purchaseId});link.textContent='Lot barcodes';return link;}
    async function barcodeDetails(item,container){
        container.textContent='Loading received-lot barcodes...';
        try{
            const available=await lots();
            const docs=await Promise.all(available.map(l=>Purchases.api(`/api/labels/management/lots/${l.kind}/${l.lotId}`)));
            const codes=[...new Set(docs.flatMap(doc=>doc.rows.filter(r=>r.variantId===item.variantId&&r.barcode).map(r=>r.barcode)))];
            container.replaceChildren();for(const code of codes){const chip=document.createElement('code');chip.textContent=code;container.append(chip);}
            if(!codes.length)container.append(document.createTextNode('No saved barcode for this received variant. '));
            container.append(barcodeLink());
        }catch{container.replaceChildren(document.createTextNode('Open the received lot to view its saved barcodes. '),barcodeLink());}
    }
    function renderItems(){
        const term=$('purchase-item-search').value.trim().toLowerCase(),filtered=items.filter(item=>[item.productName,item.sku,item.attributeSummary,item.batchNumber,item.supplierProductCode,...(item.serialNumbers||[])].join(' ').toLowerCase().includes(term));
        const body=$('purchase-rows');body.replaceChildren();
        for(const item of filtered){
            const row=document.createElement('tr');row.dataset.itemId=item.purchaseItemId;cell(row,items.indexOf(item)+1);
            const product=cell(row,''),name=document.createElement('strong'),sku=document.createElement('small');name.className='pd-product-name';name.textContent=text(item.productName);sku.className='pd-product-sku';sku.textContent='SKU: '+text(item.sku);product.append(name,sku);
            const qty=cell(row,`${item.quantity} / ${item.receivedQuantity}`,'pd-number');if(item.damagedQuantity>0){const damaged=document.createElement('small');damaged.className='pd-damaged';damaged.textContent=item.damagedQuantity+' damaged';qty.append(damaged);}
            cell(row,money(item.purchasePrice),'pd-number');cell(row,money(item.lineTotal),'pd-number');
            const code=cell(row,'\u2014');if(canBarcodes&&item.receivedQuantity>0){code.replaceChildren(barcodeLink());}
            const action=cell(row,''),view=document.createElement('button');view.type='button';view.className='btn btn-outline-secondary';view.textContent='View';view.setAttribute('aria-expanded','false');view.setAttribute('aria-label','View details for '+item.productName);view.setAttribute('aria-controls','purchase-item-detail-'+item.purchaseItemId);action.append(view);body.append(row);
            const detail=document.createElement('tr');detail.id='purchase-item-detail-'+item.purchaseItemId;detail.className='pd-item-detail';detail.hidden=true;
            const content=cell(detail,'');content.colSpan=7;const list=document.createElement('dl');list.className='pd-item-facts';
            facts(list,[['Specifications / variant',item.attributeSummary],['SKU',item.sku],['Ordered / good / damaged',`${item.quantity} / ${item.receivedQuantity} / ${item.damagedQuantity}`],['Pack / stock unit',`${item.packQuantity??1} ${text(item.unitOfMeasure)}`],['Batch / lot',item.batchNumber],['MRP',money(item.mrp)],['GST',`${item.taxPercentage??0}%`],['Line discount',money(item.discountAmount)],['Supplier product code',item.supplierProductCode],['Manufacturing date',date(item.manufacturingDate)],['Expiry date',date(item.expiryDate)],['Serial numbers',(item.serialNumbers||[]).join('\n')],['Damaged serial numbers',(item.damagedSerialNumbers||[]).join('\n')],['Pending good / damaged',`${item.pendingReceivedQuantity??0} / ${item.pendingDamagedQuantity??0}`],['Pending serial numbers',[...(item.pendingSerialNumbers||[]),...(item.pendingDamagedSerialNumbers||[])].join('\n')]]);
            content.append(list);const codes=document.createElement('div');codes.className='pd-barcode-list';if(canBarcodes&&item.receivedQuantity>0)content.append(codes);body.append(detail);
            let loaded=false;view.onclick=()=>{detail.hidden=!detail.hidden;view.textContent=detail.hidden?'View':'Hide';view.setAttribute('aria-expanded',String(!detail.hidden));if(!detail.hidden&&!loaded&&canBarcodes&&item.receivedQuantity>0){loaded=true;barcodeDetails(item,codes);}};
        }
        if(!filtered.length){const tr=document.createElement('tr');cell(tr,'No products match your search.').colSpan=7;body.append(tr);}
        $('purchase-item-count').textContent='('+items.length+')';$('purchase-item-search-count').textContent=term?`${filtered.length} of ${items.length} product variants`:'';
    }
    function balances(ops){$('purchase-paid').textContent=money(ops?.paidTotal??purchase.paidAmount);$('purchase-outstanding').textContent=money(ops?.outstanding??purchase.supplierOutstanding);$('purchase-received').textContent=items.reduce((sum,i)=>sum+i.receivedQuantity,0);const note=$('purchase-stock-corrections');if(note){note.hidden=!purchase.stockCorrectionQuantity;note.textContent='Stock corrections: '+(purchase.stockCorrectionQuantity>0?'+':'')+(purchase.stockCorrectionQuantity||0);}}
    function section(tab){
        document.querySelectorAll('[data-pd-tab]').forEach(b=>{const active=b.dataset.pdTab===tab;b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));});
        $('purchase-products').hidden=tab!=='items';$('purchase-bottom').hidden=tab==='supplier';$('purchase-receipts-card').hidden=tab==='payments';$('purchase-settlement').hidden=tab==='receipts';$('purchase-bottom').classList.toggle('pd-single',tab!=='items');
        if(tab==='supplier')$('purchase-more-details').open=true;
    }
    window.StockFlowPurchaseDashboard={money,date,text,section,lots};
    window.StockFlowPurchaseDocument=Purchases.api('/api/purchases/'+encodeURIComponent(id));
    (async()=>{
        try{
            purchase=await window.StockFlowPurchaseDocument;items=purchase.items;
            $('purchase-title').textContent=purchase.purchaseNumber;$('purchase-subtitle').textContent=`${text(purchase.supplierName)} | ${date(purchase.purchaseDate)}`;
            $('purchase-status').hidden=false;$('purchase-status').dataset.status=purchase.status;$('purchase-status').textContent=StockFlowText.label(purchase.status);
            const crumb=$('breadcrumb-current');if(crumb)crumb.textContent=purchase.purchaseNumber;
            const terms=purchase.paymentTermsDays==null?'\u2014':purchase.paymentTermsDays===0?'Immediate':purchase.paymentTermsDays+' days';
            facts($('purchase-header'),[['Purchase #',purchase.purchaseNumber],['Order date',date(purchase.purchaseDate)],['Supplier',purchase.supplierName],['Supplier invoice date',date(purchase.supplierInvoiceDate)],['Supplier code',purchase.supplierCode],['Payment terms',terms],['Supplier GSTIN',purchase.supplierGstNumber],['Received date',date(purchase.receivedDate)]]);
            const more=[['Supplier address',purchase.supplierAddress],['Supplier details source',purchase.hasSupplierSnapshot?'Saved with this purchase':'Current supplier details (original snapshot unavailable)'],['Supplier invoice',purchase.supplierInvoiceNumber],['Internal reference',purchase.internalReference],['Created by user',purchase.createdBy],['Created on',date(purchase.createdAt)],['Notes',text(purchase.notes)],['Returned amount',money(purchase.returnedAmount)]];
            if(purchase.invoiceAttachmentName){const link=document.createElement('a');link.href=`/api/purchases/${purchase.purchaseId}/invoice-attachment`;link.textContent=purchase.invoiceAttachmentName;more.push(['Supplier invoice file',link]);}facts($('purchase-additional'),more);
            for(const [label,value] of [['Subtotal',purchase.subtotal],['Tax',purchase.taxAmount],['Delivery charges',purchase.freightAmount],['Other charges',purchase.otherLandedCost],['Discount',purchase.discountAmount],['Total',purchase.totalAmount]]){const row=document.createElement('div');row.className='pd-amount-line'+(label==='Total'?' pd-total':'');const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;dd.textContent=money(value);row.append(dt,dd);$('purchase-amounts').append(row);}
            balances();renderItems();
            if(purchase.status==='DRAFT'){
                const actions=$('purchase-actions');
                if(actions.dataset.approve==='true'&&items.some(i=>(i.pendingReceivedQuantity||0)+(i.pendingDamagedQuantity||0)>0)){
                    const complete=document.createElement('button');complete.type='button';complete.className='btn btn-primary';complete.textContent='Complete purchase';complete.onclick=async()=>{if(!await StockFlowDialog.confirm('Complete this purchase and receive the quantities saved on the draft?'))return;complete.disabled=true;try{await Purchases.api(`/api/purchases/${purchase.purchaseId}/complete`,{method:'POST'});location.reload();}catch(error){Purchases.error(error);complete.disabled=false;}};actions.append(complete);
                }
            }
            $('purchase-dashboard').setAttribute('aria-busy','false');
        }catch(error){Purchases.error(error);$('purchase-dashboard').setAttribute('aria-busy','false');}
    })();
    $('purchase-item-search').oninput=renderItems;
    document.querySelectorAll('[data-pd-tab]').forEach(button=>button.onclick=()=>section(button.dataset.pdTab));
    $('purchase-print').onclick=()=>{const more=$('purchase-more-details'),open=more.open,search=$('purchase-item-search'),term=search.value;more.open=true;if(term){search.value='';renderItems();}window.addEventListener('afterprint',()=>{more.open=open;if(term){search.value=term;renderItems();}},{once:true});window.print();};
    for(const [button,dialog] of [['purchase-pay-open','supplier-payment-dialog'],['purchase-return-open','supplier-return-dialog']])$(button)?.addEventListener('click',()=>$(dialog).showModal());
    document.querySelectorAll('[data-pd-close]').forEach(button=>button.onclick=()=>{const dialog=$(button.dataset.pdClose);if(![...dialog.querySelectorAll('button[type=submit],form button')].some(b=>b.disabled))dialog.close();});
    document.querySelectorAll('.pd-dialog').forEach(dialog=>dialog.addEventListener('cancel',event=>{if([...dialog.querySelectorAll('form button')].some(button=>button.disabled))event.preventDefault();}));
    window.addEventListener('purchase-settlement-loaded',event=>{if(purchase)balances(event.detail);});
})();
