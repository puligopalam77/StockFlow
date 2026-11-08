/* Presentation only. Monetary values and policy requirements come from /preview.
   Saving continues through the existing quotation writer and immutable revisions. */
window.StockFlowQuotationEditor = async function(root, options) {
    'use strict';
    const $ = id => document.getElementById(id), api = Sales.api.bind(Sales);
    const element = (tag, text, className) => { const e=document.createElement(tag);if(text!=null)e.textContent=text;if(className)e.className=className;return e; };
    const money = value => typeof value==='number' && Number.isFinite(value) ? new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(value) : '—';
    const uuid = () => crypto.randomUUID?.()||'10000000-1000-4000-8000-100000000000'.replace(/[018]/g,c=>(Number(c)^crypto.getRandomValues(new Uint8Array(1))[0]&15>>Number(c)/4).toString(16));
    const camel = value => Array.isArray(value)?value.map(camel):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([key,item])=>[key[0].toLowerCase()+key.slice(1),camel(item)])):value;
    const id = new URL(location.href).searchParams.get('id'), form=$('quotation-form');
    const canPrice=root.dataset.canPrice==='true', canDiscount=root.dataset.canDiscount==='true';
    const storageKey='stockflow.quotation.pending.'+root.dataset.scope+'.'+(id||'new');
    let quotationId=id||uuid(), version=0, items=[], quoted=null, lastQuote=null, sequence=0, timer;
    let saving=false, pending=null, customer=null, deliveryTouched=false, initialized=false, attempted=false;
    const touched=new Set();
    let request={requestId:uuid(),locationId:window.StockFlowLocation,pricesIncludeTax:options.pricesIncludeTax,allocateInvoiceDiscount:true,payments:[]};
    const summaryFields=[['Item Subtotal','itemSubtotal'],['Item Discounts','itemDiscountAmount'],['Quotation Discount','invoiceDiscountAmount'],['Taxable Amount','taxableAmount'],['GST','taxAmount'],['Estimated Total','totalAmount']];
    const cells={};
    for(const [label,key] of summaryFields){
        if(key==='invoiceDiscountAmount')continue;
        const row=element('p',null,'quote-summary-value'+(key==='totalAmount'?' quote-estimated-total':''));
        cells[key]=element('strong','—');row.append(element('span',label),cells[key]);
        if(key==='itemSubtotal'||key==='itemDiscountAmount')$('quotation-summary').insertBefore(row,$('quotation-discount-row'));
        else $('quotation-summary').append(row);
    }
    $('quotation-help-button').addEventListener('click',()=>{
        const rect=$('quotation-help-button').getBoundingClientRect(),help=$('quotation-help');
        help.style.top=Math.min(rect.bottom+6,innerHeight-150)+'px';
        help.style.left=Math.max(12,Math.min(rect.left,innerWidth-Math.min(340,innerWidth-24)-12))+'px';
    });
    function showError(error){$('quotation-message').className='alert alert-danger';$('quotation-message').textContent=error.message||String(error);$('quotation-message').hidden=false;}
    const run=fn=>Promise.resolve().then(fn).catch(showError);
    function deliveryPreview(){ $('quotation-delivery-preview').textContent=$('quotation-delivery').value.trim()||'No delivery address supplied.'; }
    function displayQuote(q, provisional=false){
        for(const [key,cell] of Object.entries(cells))cell.textContent=money(q?.[key]);
        $('quotation-summary').classList.toggle('is-provisional',provisional);
    }
    function body(){
        return {...request,pricesIncludeTax:options.pricesIncludeTax,customerId:Number($('quotation-customer').value)||null,
            discountAmount:Number($('quotation-discount').value),notes:$('quotation-notes').value||null,
            adjustmentReason:$('quotation-reason').value.trim()||null,gstPlaceOfSupplyStateCode:$('quotation-pos').value||null,
            gstDeliveryAddress:$('quotation-delivery').value||null,items:items.map(line=>{
                const {name,sku,unit,mrp,variantName,attributes,hasProductImage,...input}=line;return input;
            })};
    }
    function invalidNumbers(){return [...form.querySelectorAll('input[type=number]')].some(input=>!input.validity.valid);}
    function requiredReasons(){return [...form.querySelectorAll('[data-quote-reason]')].some(input=>input.required&&!input.value.trim());}
    function validation(){
        if(!items.length)return 'Add at least one item.';
        if(!customer)return 'Select a customer.';
        if(!$('quotation-date').validity.valid||!$('quotation-until').validity.valid||!$('quotation-date').value||!$('quotation-until').value)return 'Enter valid quotation and expiry dates.';
        if($('quotation-until').value<$('quotation-date').value)return 'Valid Until cannot be earlier than Quotation Date.';
        if(invalidNumbers())return 'Complete the highlighted quantity, price or discount.';
        if(requiredReasons())return 'Record the adjustment reason required by shop policy.';
        return '';
    }
    function updateSave(){
        const message=validation();
        const dates=!$('quotation-date').validity.valid||!$('quotation-until').validity.valid||!$('quotation-date').value||!$('quotation-until').value||$('quotation-until').value<$('quotation-date').value;
        $('quotation-customer-error').textContent=!customer&&(attempted||touched.has('quotation-customer-search'))?'Select a customer.':'';
        $('quotation-date-error').textContent=dates&&(attempted||touched.has('quotation-date')||touched.has('quotation-until'))?'Enter valid dates; Valid Until must be on or after Quotation Date.':'';
        $('quotation-items-error').textContent=!items.length&&attempted?'Add at least one item to save this quotation.':'';
        $('quotation-discount-error').textContent=!$('quotation-discount').validity.valid&&(attempted||touched.has('quotation-discount'))?'Enter a non-negative discount with at most two decimal places.':'';
        $('quotation-validation').textContent=message&&items.length&&customer&&!dates&&$('quotation-discount').validity.valid?message:'';
        $('quotation-save').disabled=saving||(!pending&&(!quoted||!!message));
    }
    function freeze(value){for(const control of form.elements)if(control.id!=='quotation-save')control.disabled=value;}
    function rememberPending(){
        try{sessionStorage.setItem(storageKey,JSON.stringify({pending,items,customer,quote:lastQuote}));}catch{ /* In-memory exact retry still works if browser storage is unavailable. */ }
    }
    function forgetPending(){try{sessionStorage.removeItem(storageKey);}catch{}}
    function invalidate(){
        if(pending||saving||!initialized)return;
        quoted=null;++sequence;clearTimeout(timer);displayQuote(lastQuote,true);updateSave();$('quotation-pos-error').textContent='';
        $('quotation-summary').setAttribute('aria-busy','true');
        if(!items.length){lastQuote=null;displayQuote(null);$('quotation-summary').setAttribute('aria-busy','false');$('quotation-calculation-status').textContent='Add items for a server estimate.';return;}
        if(invalidNumbers()){$('quotation-summary').setAttribute('aria-busy','false');$('quotation-calculation-status').textContent='Finish the number edit. Previous amounts are provisional.';return;}
        $('quotation-calculation-status').textContent='Updating server estimate… Previous amounts are provisional.';
        timer=setTimeout(()=>run(preview),250);
    }
    function applyPolicy(q){
        const review=q.adjustmentReview||{}, invoice=!!review.invoiceReasonRequired;
        $('quotation-reason-field').hidden=!invoice;$('quotation-reason').required=invoice;
        $('quotation-policy').textContent=review.requiresApproval?'Approval is required again at final checkout. This quotation does not grant discount approval.':'';
        items.forEach((line,index)=>{
            const row=$('quotation-lines').rows[index], needed=review.lineReasons?.[index]||{};
            for(const key of ['discount','price','tax']){
                const field=row.querySelector('[data-quote-reason='+key+']');
                field.required=!!needed[key];field.closest('label').hidden=!needed[key];
            }
            const details=row.querySelector('.quote-line-reason'), missing=[...details.querySelectorAll('input')].some(input=>input.required&&!input.value.trim());
            details.hidden=!needed.discount&&!needed.price&&!needed.tax;
            details.querySelector('summary').textContent=missing?'Adjustment reason required':'Adjustment details';
            if(missing)details.open=true;
        });
    }
    $('quotation-reason').dataset.quoteReason='invoice';
    async function preview(){
        if(invalidNumbers()||!items.length||pending||saving)return;
        const mine=++sequence;
        try{
            const q=await api('/api/quotations/preview',{method:'POST',body:JSON.stringify(body())});
            if(mine!==sequence||pending||saving)return;
            if(!q.items||q.items.length!==items.length||!summaryFields.every(([,key])=>typeof q[key]==='number'&&Number.isFinite(q[key]))||
                !q.items.every(line=>['taxPercentage','lineTotal'].every(key=>typeof line[key]==='number'&&Number.isFinite(line[key]))))throw Error('The server estimate could not be confirmed. Edit a field to retry.');
            quoted=q;lastQuote=q;displayQuote(q);applyPolicy(q);
            const showMrp=q.items.some(line=>typeof line.mrp==='number'&&Number.isFinite(line.mrp));
            root.querySelectorAll('[data-quote-mrp]').forEach(cell=>cell.hidden=!showMrp);
            [...$('quotation-lines').rows].forEach((row,index)=>{
                row.querySelector('[data-quote-value=tax]').textContent=q.items[index].taxPercentage+'%';
                row.querySelector('[data-quote-value=total]').textContent=money(q.items[index].lineTotal);
                row.querySelector('[data-quote-mrp]').textContent=money(q.items[index].mrp);
            });
            $('quotation-summary').setAttribute('aria-busy','false');
            $('quotation-calculation-status').textContent='Current server estimate. Stock is not reserved.';updateSave();
        }catch(error){
            if(mine!==sequence||pending||saving)return;
            quoted=null;$('quotation-summary').setAttribute('aria-busy','false');
            if(/place of supply|supply state/i.test(error.message||''))$('quotation-pos-error').textContent=error.message;
            $('quotation-calculation-status').textContent=(error.message||'Unable to calculate.')+' Previous amounts are provisional. Edit a field to retry.';updateSave();
        }
    }
    function placeholder(){const box=element('span',null,'quote-product-thumb');box.innerHTML='<svg class="sf-icon" aria-hidden="true"><use href="/images/icons.svg#box" /></svg>';return box;}
    function render(){
        $('quotation-lines').replaceChildren();$('quotation-empty').hidden=!!items.length;$('quotation-table-region').hidden=!items.length;
        $('quotation-item-count').textContent=items.length+' item'+(items.length===1?'':'s');
        items.forEach((line,index)=>{
            const row=element('tr'),product=element('td'),identity=element('div',null,'quote-product-cell'),copy=element('div',null,'quote-product-copy');
            let picture=placeholder();
            if(line.hasProductImage&&line.productId){picture=element('img',null,'quote-product-thumb');picture.src='/api/products/'+line.productId+'/image';picture.alt='';picture.loading='lazy';picture.onerror=()=>picture.replaceWith(placeholder());}
            copy.append(element('strong',line.name),element('small',line.variantName||''));identity.append(picture,copy);product.append(identity);
            const reasons=element('details',null,'quote-line-reason');reasons.hidden=true;reasons.append(element('summary','Adjustment details'));
            for(const [key,label,fieldName] of [['discount','Discount reason','adjustmentReason'],['price','Price change reason','priceAdjustmentReason'],['tax','Tax change reason','taxAdjustmentReason']]){
                const labelNode=element('label',label),input=element('input',null,'form-control');labelNode.hidden=true;input.maxLength=500;input.dataset.quoteReason=key;
                input.value=line[fieldName]||((key==='price')?line.adjustmentReason||'':'');input.setAttribute('aria-label',label+' for '+line.name);
                input.oninput=()=>{line[fieldName]=input.value.trim()||null;invalidate();};labelNode.append(input);reasons.append(labelNode);
            }
            product.append(reasons);row.append(product,element('td',line.sku||'—'));
            const discountKey=line.discountPercent==null&&line.discountAmount>0?'discountAmount':'discountPercent';
            for(const [key,min,step]of [['quantity',1,1],['sellingPrice',0,.01],[discountKey,0,.01]]){
                if(key==='sellingPrice'){const mrp=element('td','—','quote-number');mrp.dataset.quoteMrp='';mrp.hidden=true;row.append(mrp);}
                const td=element('td'),input=element('input',null,'form-control');input.type='number';input.min=min;input.step=step;input.required=true;
                if(key==='discountPercent')input.max=100;input.value=line[key]??0;input.dataset.quoteInput=key;
                input.readOnly=(key==='sellingPrice'&&!line.manualItem&&!canPrice)||(['discountPercent','discountAmount'].includes(key)&&!canDiscount);
                input.setAttribute('aria-label',key+' for '+line.name);input.oninput=()=>{if(input.validity.valid)line[key]=Number(input.value);invalidate();};td.append(input);if(key==='discountAmount')td.append(element('small','₹ fixed','d-block text-muted'));row.append(td);
            }
            const tax=element('td','—','quote-number'),total=element('td','—','quote-number quote-line-total');tax.dataset.quoteValue='tax';total.dataset.quoteValue='total';row.append(tax,total);
            const action=element('td'),remove=element('button',null,'quote-remove');remove.type='button';remove.title='Remove item';remove.setAttribute('aria-label','Remove '+line.name);
            remove.innerHTML='<svg class="sf-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7" /></svg>';
            remove.onclick=()=>{items.splice(index,1);render();invalidate();};action.append(remove);row.append(action);$('quotation-lines').append(row);
        });
    }
    let customerVersion=0, customerTimer, matches=[], active=-1;
    function closeCustomers(){matches=[];active=-1;$('quotation-customers').replaceChildren();$('quotation-customers').hidden=true;$('quotation-customer-search').setAttribute('aria-expanded','false');$('quotation-customer-search').removeAttribute('aria-activedescendant');}
    function showCustomer(){
        $('quotation-customer').replaceChildren(new Option('Select customer',''));
        if(customer)$('quotation-customer').add(new Option(customer.customerName,customer.customerId,true,true));
        $('quotation-customer-search').hidden=!!customer;$('quotation-customer-search').value='';$('quotation-customer-search').required=!customer;
        $('quotation-customer-current').hidden=!customer;$('quotation-customer-name').textContent=customer?.customerName||'';$('quotation-customer-mobile').textContent=customer?.mobileNumber||'';
    }
    function chooseCustomer(value){
        if(value.isActive===false||pending||saving)return;
        ++customerVersion;clearTimeout(customerTimer);closeCustomers();customer=value;showCustomer();
        if(!deliveryTouched){$('quotation-delivery').value=value.address||'';deliveryPreview();}
        $('quotation-customer-status').textContent='';invalidate();$('quotation-product-search').focus();
    }
    async function findCustomers(){
        const term=$('quotation-customer-search').value.trim();if(!term)return;
        const mine=++customerVersion;$('quotation-customer-status').textContent='Searching customers…';
        try{
            const rows=await api('/api/customers/search?term='+encodeURIComponent(term));
            if(mine!==customerVersion||customer||$('quotation-customer-search').value.trim()!==term||pending)return;
            closeCustomers();matches=rows.filter(row=>row.isActive!==false);
            matches.forEach((match,index)=>{const row=element('div');row.id='quotation-customer-match-'+index;row.setAttribute('role','option');row.setAttribute('aria-selected','false');row.append(element('strong',match.customerName),element('span',match.mobileNumber||''));row.onmousedown=e=>e.preventDefault();row.onclick=()=>chooseCustomer(match);$('quotation-customers').append(row);});
            $('quotation-customers').hidden=!matches.length;$('quotation-customer-search').setAttribute('aria-expanded',String(!!matches.length));$('quotation-customer-status').textContent=matches.length?'':'No active customers found.';
        }catch(error){if(mine===customerVersion)$('quotation-customer-status').textContent=error.message;}
    }
    $('quotation-customer-search').oninput=()=>{++customerVersion;clearTimeout(customerTimer);closeCustomers();$('quotation-customer-status').textContent='';if($('quotation-customer-search').value.trim())customerTimer=setTimeout(()=>run(findCustomers),250);};
    $('quotation-customer-search').onkeydown=event=>{
        if(event.isComposing)return;
        if(event.key==='Escape'){event.preventDefault();++customerVersion;clearTimeout(customerTimer);closeCustomers();}
        if(['ArrowDown','ArrowUp'].includes(event.key)&&matches.length){event.preventDefault();active=active<0?(event.key==='ArrowDown'?0:matches.length-1):(active+(event.key==='ArrowDown'?1:-1)+matches.length)%matches.length;
            [...$('quotation-customers').children].forEach((row,i)=>row.setAttribute('aria-selected',String(i===active)));const row=$('quotation-customers').children[active];$('quotation-customer-search').setAttribute('aria-activedescendant',row.id);row.scrollIntoView({block:'nearest'});}
        if(event.key==='Enter'){event.preventDefault();clearTimeout(customerTimer);if(matches.length)chooseCustomer(matches[active<0?0:active]);else run(findCustomers);}
    };
    $('quotation-customer-clear').onclick=()=>{if(pending||saving)return;customer=null;showCustomer();closeCustomers();invalidate();$('quotation-customer-search').focus();};
    document.addEventListener('pointerdown',event=>{if(!$('quotation-customer-picker').contains(event.target)){++customerVersion;clearTimeout(customerTimer);closeCustomers();}});
    $('quotation-customer-picker').addEventListener('focusout',()=>queueMicrotask(()=>{if(!$('quotation-customer-picker').contains(document.activeElement)){++customerVersion;clearTimeout(customerTimer);closeCustomers();}}));
    root.addEventListener('billing-customer-opening',event=>{if(pending||saving)event.preventDefault();});
    root.addEventListener('billing-customer-selected',event=>chooseCustomer(event.detail));
    root.addEventListener('billing-customer-find',event=>{customer=null;showCustomer();$('quotation-customer-search').value=event.detail;run(findCustomers);});
    if($('quotation-new-customer'))StockFlowQuickCustomer.mount($('quotation-new-customer'),root,$('quotation-customer-actions'),{requiredMobile:true,returnFocusId:'quotation-product-search',subtitle:'Create a customer without leaving this quotation.'});
    let productVersion=0, productTimer;
    async function selectProduct(v){
        let productUnitId=null;
        if(v.isSerialTracked){
            const units=await api('/api/inventory/units?productId='+v.productId+'&availableOnly=true'),dialog=element('dialog',null,'quote-dialog');dialog.dataset.sfDialog='medium';dialog.setAttribute('aria-label','Choose an available serial');
            dialog.append(element('h2','Choose an available serial'));const select=element('select',null,'form-select');
            select.setAttribute('aria-label','Available serial');for(const u of units.filter(u=>u.variantId===v.variantId))select.add(new Option(u.serialNumber,u.productUnitId));
            const choose=element('button','Use serial','btn btn-primary'),cancel=element('button','Cancel','btn btn-outline-secondary');choose.type=cancel.type='button';choose.disabled=!select.options.length;
            dialog.append(select,choose,cancel);root.append(dialog);productUnitId=await new Promise(resolve=>{choose.onclick=()=>{resolve(Number(select.value)||null);dialog.close();};cancel.onclick=()=>dialog.close();dialog.onclose=()=>{resolve(null);dialog.remove();};dialog.showModal();});
            if(!productUnitId)return;
        }
        if(pending||saving)return;
        items.push({productId:v.productId,variantId:v.variantId,productUnitId,quantity:1,sellingPrice:v.sellingPrice,discountPercent:0,discountAmount:0,
            name:v.productName,variantName:v.variantName,sku:v.sku,unit:v.unitOfMeasure||'PIECE',hasProductImage:!!v.hasProductImage});
        render();invalidate();++productVersion;$('quotation-products').replaceChildren();$('quotation-product-search').value='';$('quotation-product-status').textContent='';$('quotation-product-search').focus();
    }
    $('quotation-product-search').oninput=()=>{
        clearTimeout(productTimer);const mine=++productVersion,term=$('quotation-product-search').value.trim();$('quotation-products').replaceChildren();$('quotation-product-status').textContent='';if(!term)return;
        productTimer=setTimeout(()=>run(async()=>{
            $('quotation-product-status').textContent='Searching products…';
            try{const rows=await api('/api/products/variants?activeOnly=true&term='+encodeURIComponent(term));if(mine!==productVersion||pending)return;
                for(const v of rows.slice(0,30)){const add=element('button',null,'quote-result');add.type='button';add.append(element('strong',[v.productName,v.variantName].filter(Boolean).join(' / ')),element('small',[v.sku,money(v.sellingPrice),Number.isFinite(v.currentStock)?'Available: '+v.currentStock:null].filter(Boolean).join(' · ')));add.onclick=()=>run(()=>selectProduct(v));$('quotation-products').append(add);}
                $('quotation-product-status').textContent=rows.length?'Choose an exact variant. Quotation items do not reserve stock.':'No matching products found.';
            }catch(error){if(mine===productVersion)$('quotation-product-status').textContent=error.message;}
        }),250);
    };
    $('quotation-manual').hidden=!options.permissions.manual;
    $('quotation-manual').onclick=()=>{if(!pending&&!saving){$('quotation-manual-dialog').showModal();$('quotation-manual-name').focus();}};
    $('quotation-manual-close').onclick=()=>$('quotation-manual-dialog').close();
    $('quotation-manual-dialog').onclose=()=>$('quotation-product-search').focus();
    if(options.permissions.manual)run(async()=>{const rules=await api('/api/sales/manual-tax-rules');for(const rule of rules)$('quotation-manual-tax').add(new Option(rule.ruleName+' · '+rule.taxRate+'%',rule.ruleId));});
    $('quotation-manual-form').onsubmit=event=>{
        event.preventDefault();if(pending||saving||!options.permissions.manual||!event.target.reportValidity())return;
        const name=$('quotation-manual-name').value.trim(),price=Number($('quotation-manual-price').value),mrp=$('quotation-manual-mrp').value?Number($('quotation-manual-mrp').value):null;
        if(!name||!Number.isFinite(price)||price<0){showError(Error('Enter a manual item name and valid price.'));return;}
        items.push({productId:0,variantId:null,productUnitId:null,quantity:1,sellingPrice:price,discountPercent:0,discountAmount:0,manualItem:{name,mrp,taxRuleId:Number($('quotation-manual-tax').value)},name});
        render();invalidate();$('quotation-manual-dialog').close();event.target.reset();
    };
    $('quotation-date').value=options.today;$('quotation-date').max=options.today;$('quotation-date').min='2000-01-01';$('quotation-until').value=options.today;
    $('quotation-pricing-mode').textContent=options.pricesIncludeTax===true?'Selling prices include GST; the server extracts applicable tax.':options.pricesIncludeTax===false?'Selling prices exclude GST; the server adds applicable tax.':'Applicable GST is determined by the server preview.';
    $('quotation-discount').readOnly=!canDiscount;
    $('quotation-delivery').oninput=()=>{deliveryTouched=true;deliveryPreview();};
    form.addEventListener('input',event=>{touched.add(event.target.id);$('quotation-until').min=$('quotation-date').value;invalidate();});
    form.addEventListener('focusout',event=>{touched.add(event.target.id);updateSave();});
    form.addEventListener('keydown',event=>{if(event.key==='Enter'&&event.target.tagName==='INPUT')event.preventDefault();});
    if(id){
        const d=await api('/api/quotations/'+encodeURIComponent(id)),b=camel(d.selected.body),h=d.header;
        if(['CONVERTED','CANCELLED'].includes(h.status))throw Error('This quotation cannot be revised.');
        version=h.version;request={...b.request,requestId:uuid()};items=request.items.map((item,n)=>({...item,name:b.lines[n].name,sku:b.lines[n].sku,unit:b.lines[n].unit}));
        customer={...b.customer,customerId:request.customerId};showCustomer();
        $('quotation-title').textContent='Quotation '+h.quotationNumber+' · Revision '+h.currentRevision+' · '+h.status;
        $('quotation-date').value=b.documentDate;$('quotation-until').value=b.validUntil;$('quotation-reference').value=b.reference||'';$('quotation-terms').value=b.terms||'';
        $('quotation-notes').value=request.notes||'';$('quotation-discount').value=request.discountAmount;$('quotation-reason').value=request.adjustmentReason||'';
        $('quotation-pos').value=request.gstPlaceOfSupplyStateCode||'';$('quotation-delivery').value=request.gstDeliveryAddress||'';deliveryTouched=true;deliveryPreview();
        $('quotation-save').textContent='Save New Revision';
        const link=(label,url)=>{const a=element('a',label,'btn btn-sm btn-outline-secondary');a.href=url;$('quotation-saved-actions').append(a);};
        link('View saved revision','/Quotations/Details?id='+id+'&revision='+h.currentRevision);
        if(options.permissions.print)link('Print saved revision','/Quotations/Print?id='+id+'&revision='+h.currentRevision);
        if(h.status==='ACCEPTED'&&options.permissions.convert)link('Open saved revision in New Sale','/Sales/Create?quotationId='+id);
        if(request.pricesIncludeTax!==options.pricesIncludeTax){$('quotation-message').className='alert alert-warning';$('quotation-message').textContent='The shop tax mode changed. This new revision uses current tax settings. Review every price and total before saving.';$('quotation-message').hidden=false;}
    }else showCustomer();
    let recovered=null;try{recovered=JSON.parse(sessionStorage.getItem(storageKey)||'null');}catch{}
    if(recovered?.pending&&Array.isArray(recovered.items)&&recovered.items.length<=500&&typeof recovered.pending.quotationId==='string'&&
        ((!id&&recovered.pending.expectedVersion===0)||(id===recovered.pending.quotationId&&recovered.pending.expectedVersion>0))){
        pending=recovered.pending;quotationId=pending.quotationId;version=pending.expectedVersion;request=pending.request;items=recovered.items;customer=recovered.customer;
        $('quotation-date').value=pending.documentDate;$('quotation-until').value=pending.validUntil;$('quotation-reference').value=pending.reference||'';$('quotation-terms').value=pending.terms||'';
        $('quotation-notes').value=request.notes||'';$('quotation-discount').value=request.discountAmount;$('quotation-reason').value=request.adjustmentReason||'';$('quotation-pos').value=request.gstPlaceOfSupplyStateCode||'';$('quotation-delivery').value=request.gstDeliveryAddress||'';
        lastQuote=recovered.quote;showCustomer();deliveryPreview();displayQuote(lastQuote,true);
    }
    render();initialized=true;
    if(!(id?options.permissions.edit:options.permissions.create)){freeze(true);$('quotation-save').disabled=true;throw Error('You do not have permission to save this quotation.');}
    freeze(!!pending);
    if(pending){$('quotation-save').textContent='Retry Same Saved Quotation';$('quotation-calculation-status').textContent='Save outcome unconfirmed. Retry the exact saved request; fields are locked.';updateSave();}
    else invalidate();
    form.onsubmit=event=>{
        event.preventDefault();if(saving)return;attempted=true;
        run(async()=>{
            if(!pending){if(!quoted||validation()){updateSave();throw Error(validation()||'Wait for the current server calculation before saving.');}
                pending={quotationId,operationId:uuid(),expectedVersion:version,documentDate:$('quotation-date').value,validUntil:$('quotation-until').value,reference:$('quotation-reference').value,terms:$('quotation-terms').value,request:body()};rememberPending();}
            saving=true;++sequence;clearTimeout(timer);freeze(true);$('quotation-save').disabled=true;
            try{await api('/api/quotations'+(version?'/'+quotationId:''),{method:version?'PUT':'POST',body:JSON.stringify(pending)});forgetPending();location.assign('/Quotations/Details?id='+encodeURIComponent(quotationId));}
            catch(error){
                if(error.status>=400&&error.status<500){pending=null;forgetPending();freeze(false);$('quotation-save').textContent=id?'Save New Revision':'Save Quotation';}
                else{$('quotation-save').textContent='Retry Same Saved Quotation';$('quotation-calculation-status').textContent='Save outcome unconfirmed. Retry the exact saved request; fields are locked.';}
                throw error;
            }finally{saving=false;updateSave();}
        });
    };
};
