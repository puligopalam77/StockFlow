// Build a bill, check stock and prices, record payment, and complete the sale.
(() => {
    'use strict';

    const formatMoney=value=>Number(value).toLocaleString('en-IN',{minimumFractionDigits:2,maximumFractionDigits:2});
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const workspace = getElement('billing-workspace');
    let gstMapped=true;
    const gstContextPromise=fetch('/api/sales/gst-context').then(async r=>{if(!r.ok)throw Error('Unable to check GST issuing context.');return r.json();}).then(context=>{gstMapped=!!context.mapped;if(getElement('sale-gst-context'))getElement('sale-gst-context').hidden=!context.mapped;options();return context;}).catch(error=>{const box=getElement('sale-message');box.textContent=error.message;box.hidden=false;return null;});
    let pricesIncludeTax = workspace.dataset.taxInclusive === 'true';
    const storageKey = workspace.dataset.storageKey;
    // Keep the unfinished bill in memory while the cashier adds items and payments.
    let cart = [];
    let crmEnquiryId = null;
    let sourceQuotationId=null,sourceQuotationRevision=null,sourceQuotationVersion=null;
    let sourceQuotationNumber=null,sourceQuotationNotice='';
    let payments = [];
    let products = [];
    let productWaitingForSerial = null;
    let availableSerialItems = [];
    let priceQuote = null;
    let quoteRevision=-1,quoteSequence=0,calculationState='pending',calculationError='';
    let cartRevision = 0;
    let quoteTimer;
    let customerSearch = 0;
    // Retain a submitted sale until the server confirms whether it was saved.
    let pendingSale = null;
    let isSavingSale = false;
    let isPreparingSale = false;
    let activeLookups = 0;
    let recoveryBlocked = false;
    const isSplitPayment = () => {
        return false; // New checkout collects one method; saved completed requests retain original allocations.
    };
    let billingPolicy = {
        canOverridePrice: false,
        canDiscount: false,
        canApproveDiscount: false,
        discountLimitPercent: 10
    };
    let pendingApprovalCount = 0;
    const eligibleCollection = payment => ['CASH','UPI','CARD','BANK_TRANSFER'].includes(payment.paymentMode)
        && (!payment.cardType || payment.paymentMode==='CARD'&&['DEBIT','CREDIT'].includes(payment.cardType));
    function validateCollectedPayments(allocations) {
        if (allocations.some(payment=>!eligibleCollection(payment)))
            throw new Error('Choose collected payment methods covering the full bill. Pay Later is not available for new sales.');
        const entries=new Set(),references=new Set();
        for(const payment of allocations){
            const reference=payment.referenceNumber?.trim().toUpperCase()||null;
            const signature=JSON.stringify([payment.paymentMode,payment.cardType||null,payment.amount,reference]);
            const transaction=JSON.stringify([payment.paymentMode,reference]);
            if(entries.has(signature)||payment.paymentMode!=='CASH'&&reference&&references.has(transaction))
                throw new Error('Remove duplicate payment allocations or repeated transaction references.');
            entries.add(signature);if(reference)references.add(transaction);
        }
    }
    function lineAdjustments(item){
        const price=!item.manualItem&&Number.isFinite(item.catalogPrice)&&item.sellingPrice!==item.catalogPrice;
        return {price,discount:item.discountAmount>0,tax:item.taxOverride!=null&&item.taxOverride!==item.catalogTaxPercentage};
    }
    function lineReason(item){return item.adjustmentReason?.trim()||item.legacyAdjustmentReason?.trim()||'';}
    function reasonSummary(){return getElement('adjustment-reason').value.trim()||null;}
    function lineRequirements(item,index){
        return priceQuote?.adjustmentReview?.lineReasons?.[index]||lineAdjustments(item);
    }
    function lineMissingReason(item,index){
        const required=lineRequirements(item,index);
        return required.discount&&!lineReason(item)||required.price&&!(item.priceAdjustmentReason?.trim()||lineReason(item))||lineAdjustments(item).tax&&(item.taxAdjustmentReason?.trim().length||0)<3;
    }
    const invoiceReasonRequired=()=>priceQuote?.adjustmentReview?.invoiceReasonRequired??Number(getElement('sale-discount').dataset.committedNumber||getElement('sale-discount').value)>0;
    function approvalReview(){
        return priceQuote?.adjustmentReview||{requiresApproval:false,approvalStatus:'CHECKING',thresholdPercent:billingPolicy.discountLimitPercent};
    }
    function updateAdjustmentUI() {
        const review=approvalReview(),current=quoteIsCurrent(),needsApproval=current&&review.requiresApproval&&(['PENDING','REJECTED'].includes(review.approvalStatus)||!billingPolicy.canApproveDiscount&&review.approvalStatus!=='APPROVED');
        // Bill-level reason remains a transport/legacy field. New edits have contextual reasons.
        getElement('billing-adjustment-fields').hidden=true;getElement('adjustment-reason').required=false;
        const requestButton=getElement('request-discount-approval');
        requestButton.hidden=!needsApproval||!billingPolicy.canDiscount;
        requestButton.disabled=!current; // The dialog gathers any required exception reasons.
        requestButton.title=requestButton.disabled?'Wait for the current calculation':'';
        requestButton.textContent=review.approvalStatus==='PENDING'?'Refresh approval status':'Request discount approval';
        const status=getElement('discount-policy-status');
        status.hidden=!current||!review.requiresApproval;
        status.textContent=!review.requiresApproval?'':review.approvalStatus==='APPROVED'?'Discount approved for the current bill values.':review.approvalStatus==='REJECTED'?'Manager rejected this exception. Revise it or request a new review.':review.approvalStatus==='PENDING'?'Approval pending. A different authorized manager must approve this bill before checkout.':billingPolicy.canApproveDiscount?'This reduction is within your approval authority.':(review.approvalStatus==='STALE'||review.approvalStatus==='EXPIRED'?'The previous approval changed or expired. ':'')+'Manager approval required. The combined reduction exceeds the shop\'s '+review.thresholdPercent+'% threshold.';
        status.dataset.state=review.approvalStatus==='APPROVED'?'approved':'warning';
        getElement('show-discount-approvals').hidden=!billingPolicy.canApproveDiscount||pendingApprovalCount===0;
        getElement('billing-adjustments').hidden=status.hidden&&getElement('show-discount-approvals').hidden;
        updateLineWarnings();
    }
    const draftKey =
        storageKey + '.draft.' + (sessionStorage.getItem(storageKey + '.tab') || createRequestId());
    sessionStorage.setItem(storageKey + '.tab', draftKey.split('.').at(-1));
    let draftId = createRequestId();
    let draftNumber = null;
    const editorId=createRequestId();
    let draftRevision=0,draftStatus='NEW',unconfirmedDraftWrite=null;
    let draftSaveTimer;
    let draftSaveQueue = Promise.resolve();
    let isRestoringDraft = false;
    function tradeIn() {
        if (getElement('use-trade-in')?.checked) {
            return {
                itemType: getElement('trade-in-type').value.trim(),
                brandModel: getElement('trade-in-model').value.trim() || null,
                serialNumber: getElement('trade-in-serial').value.trim() || null,
                allowanceAmount: Number(getElement('trade-in-allowance').value)
            };
        } else {
            return null;
        }
    }
    function showDraftReference() {
        getElement('billing-draft-reference').textContent=draftNumber?'Draft No: '+draftNumber:'New Draft';
    }
    showDraftReference();
    function billHasContent() {
        return cart.length > 0 || !!getElement('sale-customer').value || !!getElement('sale-notes').value.trim()
            || !!getElement('payment-reference').value.trim() || !!getElement('cash-received').value
            || Number(getElement('sale-discount').value) !== 0 || !!getElement('adjustment-reason').value.trim()
            || !!tradeIn() || !!getElement('sale-gst-pos')?.value || !!getElement('sale-gst-delivery')?.value
            || !!getElement('sale-gst-recipient-details')?.checked || getElement('payment-mode').value !== 'CASH';
    }
    function getBillSnapshot() {
        return {
            crmEnquiryId,
            sourceQuotationId,sourceQuotationRevision,sourceQuotationVersion,
            sourceQuotationNumber,sourceQuotationNotice,
            gstPlaceOfSupplyStateCode: getElement('sale-gst-pos')?.value || null,
            gstDeliveryAddress: getElement('sale-gst-delivery')?.value || null,
            gstRecipientDetailsRequested: getElement('sale-gst-recipient-details')?.checked || false,
            draftId,
            draftNumber,
            draftRevision,
            pricesIncludeTax,
            cart,
            payments,
            split: isSplitPayment(),
            customerId: getElement('sale-customer').value || null,
            customerLabel: getElement('sale-customer').value ? getElement('sale-customer').selectedOptions[0]?.textContent : null,
            estimatedTotal: !cart.length ? 0 : quoteIsCurrent() ? priceQuote.totalAmount : null,
            discount: getElement('sale-discount').dataset.committedNumber || getElement('sale-discount').value,
            notes: getElement('sale-notes').value,
            mode: getElement('payment-mode').value,
            cardType: getElement('payment-card-type').value || null,
            reference: getElement('payment-reference').value,
            cash: getElement('cash-received').value.trim(),
            reason: getElement('adjustment-reason').value,
            tradeIn: tradeIn(),
            savedAt: Date.now()
        };
    }
    function applyDraftState(state) {
        if(state?.draftId!==draftId)return;
        draftRevision=state.revision;draftStatus=state.draftStatus;
        if(/^DRF-[0-9]{4,}$/.test(state.draftNumber))draftNumber=state.draftNumber;
        showDraftReference();
        if(draftStatus==='ACTIVE')try{localStorage.setItem(draftKey,JSON.stringify(getBillSnapshot()));}catch{}
    }
    function draftContentsEqual(a,b){
        const keys=['cart','payments','split','customerId','customerLabel','discount','notes','mode','cardType','reference','cash','reason','tradeIn','crmEnquiryId','pricesIncludeTax','gstPlaceOfSupplyStateCode','gstDeliveryAddress','gstRecipientDetailsRequested','sourceQuotationId','sourceQuotationRevision','sourceQuotationVersion'];
        return JSON.stringify(keys.map(k=>a[k]??null))===JSON.stringify(keys.map(k=>b[k]??null));
    }
    const lifecycleRequests=new Map();
    async function draftCommand(action,id=draftId,revision=draftRevision) {
        const key=action+':'+id;
        if(!lifecycleRequests.has(key))lifecycleRequests.set(key,{editorId,revision,operationId:createRequestId()});
        try{
            const result=await Sales.api('/api/sales/billing/drafts/'+id+(action==='discard'?'':'/'+action),{method:action==='discard'?'DELETE':'POST',body:JSON.stringify(lifecycleRequests.get(key))});
            lifecycleRequests.delete(key);applyDraftState(result);return result;
        }catch(error){if(error.status===409||error.status===400)lifecycleRequests.delete(key);throw error;}
    }
    function saveDraftOnThisDevice() {
        if(isRestoringDraft||pendingSale||isSavingSale||isPreparingSale||recoveryBlocked||['HELD','COMPLETED','DISCARDED'].includes(draftStatus))return;
        // Once persisted, even an entirely empty cart remains the SAME active draft.
        if(!billHasContent()&&draftRevision===0){clearTimeout(draftSaveTimer);localStorage.removeItem(draftKey);return;}
        try{localStorage.setItem(draftKey,JSON.stringify(getBillSnapshot()));getElement('draft-status').textContent='Saved on this device; syncing...';}
        catch{getElement('draft-status').textContent='Device backup unavailable. Keep this page open until server save completes.';}
        clearTimeout(draftSaveTimer);draftSaveTimer=setTimeout(()=>persistDraft().catch(()=>{}),800);
    }
    function persistDraft(includeEmpty=false) {
        clearTimeout(draftSaveTimer);
        if((!billHasContent()&&!includeEmpty&&draftRevision===0)||pendingSale||isSavingSale)return draftSaveQueue;
        if(recoveryBlocked)return Promise.reject(Error('Recover the current editing session before saving. Your device backup is retained.'));
        const saved=structuredClone(getBillSnapshot());
        draftSaveQueue=draftSaveQueue.catch(()=>{}).then(async()=>{
            // Retry an uncertain write with the SAME operation ID before sending newer edits.
            if(unconfirmedDraftWrite){const pending=unconfirmedDraftWrite;const ack=await Sales.api(pending.url,{method:'PUT',body:pending.body});unconfirmedDraftWrite=null;applyDraftState(ack);}
            const data={editorId,revision:draftRevision,operationId:createRequestId(),label:((saved.customerLabel||'Customer not selected')+' - '+saved.cart.reduce((n,i)=>n+i.quantity,0)+' units').slice(0,150),payload:saved};
            const pending={url:'/api/sales/billing/drafts/'+saved.draftId+'?reference=true',body:JSON.stringify(data)};unconfirmedDraftWrite=pending;
            const assigned=await Sales.api(pending.url,{method:'PUT',body:pending.body});unconfirmedDraftWrite=null;applyDraftState(assigned);
            getElement('draft-status').textContent='Active draft saved';
        }).catch(error=>{
            getElement('draft-status').textContent=error.message+' Device backup retained.';
            if(error.status===409){recoveryBlocked=true;clearTimeout(draftSaveTimer);pendingState();}
            throw error;
        });return draftSaveQueue;
    }
    // Heartbeats extend ownership, never change draft contents or content revision.
    setInterval(async()=>{
        if(draftStatus!=='ACTIVE'||!draftRevision||pendingSale||isSavingSale||recoveryBlocked||unconfirmedDraftWrite)return;
        const heartbeatId=draftId;
        try{await draftCommand('heartbeat');}
        catch(error){if(error.status===409&&heartbeatId===draftId){recoveryBlocked=true;pendingState();Sales.error(error);}}
    },30000);
    window.addEventListener('pagehide',()=>{
        if(draftStatus!=='ACTIVE'||!draftRevision||pendingSale||isSavingSale)return;
        fetch('/api/sales/billing/drafts/'+draftId+'/release',{method:'POST',credentials:'same-origin',keepalive:true,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''},body:JSON.stringify({editorId,revision:draftRevision,operationId:createRequestId()})}).catch(()=>{});
    });
    function cashReceivedValue() {
        const raw=getElement('cash-received').value.trim();
        if(raw==='')return null;
        return /^(\d+(\.\d{0,2})?|\.\d{1,2})$/.test(raw)?Number(raw):NaN;
    }
    function cashChange() {
        const due = isSplitPayment()
            ? payments
                  .filter((payment) => {
                      return payment.paymentMode === 'CASH';
                  })
                  .reduce((value, value2) => {
                      return value + value2.amount;
                  }, 0)
            : getElement('payment-mode').value === 'CASH'
              ? priceQuote?.totalAmount || 0
              : 0;
        const enabled=getElement('calculate-change').checked && getElement('payment-mode').value==='CASH';
        getElement('cash-change-field').hidden=!enabled;
        getElement('calculate-change').parentElement.hidden=getElement('payment-mode').value!=='CASH';
        getElement('exact-cash-help').hidden=getElement('payment-mode').value!=='CASH'||enabled;
        getElement('cash-bill-total').textContent='\u20b9'+formatMoney(due);
        const received =
            !enabled || cashReceivedValue() === null
                ? due
                : cashReceivedValue();
        getElement('cash-change').textContent =
            !moneyValid(received) ? 'Enter a valid cash amount.' : received >= due
                ? 'Change to return: ' + Sales.money(received - due)
                : 'Cash still required: ' + Sales.money(due - received);
        return {
            due,
            received
        };
    }
    function cashInputInvalid(){
        if(!getElement('calculate-change').checked||getElement('payment-mode').value!=='CASH')return false;
        const received=cashReceivedValue();
        if(received===null)return false;
        return !moneyValid(received)||received<(priceQuote?.totalAmount||0);
    }
    function moneyValid(value) {
        return (
            Number.isFinite(value) &&
            value >= 0 &&
            value <= 9999999999.99 &&
            Math.abs(value * 100 - Math.round(value * 100)) < 0.001
        );
    }
    function createRequestId() {
        if (typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = [...bytes]
            .map((value) => {
                return value.toString(16).padStart(2, '0');
            })
            .join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
    function request() {
        return {
            ...(crmEnquiryId ? {crmEnquiryId}:{}),
            ...(sourceQuotationId ? {sourceQuotationId,sourceQuotationRevision,sourceQuotationVersion}:{}),
            ...(getElement('sale-gst-pos')?.value ? {gstPlaceOfSupplyStateCode:getElement('sale-gst-pos').value}:{}),
            ...(getElement('sale-gst-delivery')?.value.trim() ? {gstDeliveryAddress:getElement('sale-gst-delivery').value.trim()}:{}),
            ...(getElement('sale-gst-recipient-details')?.checked ? {gstRecipientDetailsRequested:true}:{}),
            allocateInvoiceDiscount:true,
            requestId:draftId,
            locationId: window.StockFlowLocation,
            pricesIncludeTax,
            customerId: getElement('sale-customer').value
                ? Number(getElement('sale-customer').value)
                : null,
            discountAmount:
                Number(getElement('sale-discount').dataset.committedNumber ?? getElement('sale-discount').value) + (tradeIn()?.allowanceAmount || 0),
            notes: getElement('sale-notes').value || null,
            paymentDueDate: null,
            adjustmentReason: reasonSummary(),
            tradeIn: tradeIn(),
            cashTendered:
                getElement('calculate-change').checked && cashChange().due > 0 && getElement('cash-received').value.trim() !== ''
                    ? cashChange().received
                    : null,
            items: cart.map((unit) => {
                return {
                    productId: unit.productId,
                    ...(lineReason(unit)?{adjustmentReason:lineReason(unit)}:{}),
                    ...(unit.priceAdjustmentReason?{priceAdjustmentReason:unit.priceAdjustmentReason}:{}),
                    ...(unit.taxAdjustmentReason?{taxAdjustmentReason:unit.taxAdjustmentReason}:{}),
                    ...(unit.manualItem ? {manualItem:unit.manualItem}:{}),
                    ...(unit.discountPercent != null ? {discountPercent:unit.discountPercent}:{}),
                    ...(unit.taxOverride != null ? {taxOverride:unit.taxOverride}:{}),
                    variantId: unit.variantId,
                    productUnitId: unit.productUnitId || null,
                    quantity: unit.quantity,
                    sellingPrice: unit.sellingPrice,
                    discountAmount: unit.discountAmount
                };
            }),
            payments: isSplitPayment()
                ? payments.map((value) => {
                      return {
                          ...value
                      };
                  })
                : priceQuote?.totalAmount > 0
                  ? [
                        {
                            paymentMode: getElement('payment-mode').value,
                            amount: priceQuote.totalAmount,
                            referenceNumber: getElement('payment-reference').value || null,
                            ...(getElement('payment-mode').value==='CARD' && getElement('payment-card-type').value ? {cardType:getElement('payment-card-type').value}: {})
                        }
                    ]
                  : []
        };
    }
    const button = (label, action) => {
        const node = document.createElement('button');
        node.type = 'button';
        node.className = 'btn btn-sm btn-outline-secondary';
        node.textContent = label;
        node.addEventListener('click', action);
        return node;
    };
    let numericFieldId=0;
    function input(value, label, onChange, max = '9999999999.99', affectsQuote = true) {
        const node = document.createElement('input');
        node.className = 'form-control';
        node.type = 'number';
        node.min = '0';
        node.max = max;
        node.step = '0.01';
        node.value = value;
        node.required = true;
        node.setAttribute('aria-label', label);
        node.dataset.committedNumber=String(value);
        node.addEventListener('input', () => {
            const raw=node.value.trim(), parsed=Number(raw);
            const valid=raw!==''&&Number.isFinite(parsed)&&parsed>=Number(node.min)&&parsed<=Number(node.max)&&Math.abs(parsed*100-Math.round(parsed*100))<0.00001&&(node.step!=='1'||Number.isInteger(parsed));
            node.dataset.numericInvalid=String(!valid);node.setAttribute('aria-invalid',String(!valid));node.classList.toggle('is-invalid',!valid);
            let feedback=document.getElementById(node.dataset.numericError||'');
            if(!feedback){feedback=document.createElement('span');feedback.id='billing-number-error-'+(++numericFieldId);feedback.className='billing-number-error';feedback.setAttribute('role','status');node.dataset.numericError=feedback.id;node.setAttribute('aria-describedby',feedback.id);node.after(feedback);}
            feedback.hidden=valid;feedback.textContent=valid?'':'Enter a valid number'+(node.step==='1'?' of at least 1.':' with at most two decimals.');
            if(!valid){cartRevision++;quoteSequence++;clearTimeout(quoteTimer);calculationState='invalid';calculationError='Finish or correct the highlighted number. Displayed totals are from the last confirmed calculation.';updateSummary();return;}
            node.dataset.committedNumber=raw;onChange(parsed);
            if(affectsQuote)changed();else balance();
        });
        return node;
    }
    const numericInputsInvalid=()=>!!workspace.querySelector('[data-numeric-invalid=true]');
    const quoteIsCurrent=()=>!!priceQuote&&quoteRevision===cartRevision&&calculationState==='valid'&&!numericInputsInvalid();
    function updateSummary() {
        const quote=priceQuote;
        cart.forEach((item,index)=>{const total=getElement('line-total-'+index);if(total){total.dataset.provisional=String(!quoteIsCurrent());total.title=quoteIsCurrent()?'Calculated by the current billing quote':Number.isFinite(item.lastLineTotal)?'Last confirmed line total; calculation is pending or invalid.':'Awaiting the billing calculation.';}});
        const set=(id,value)=>{if(!cart.length)value=0;if(getElement(id))getElement(id).textContent=cart.length&&!quote?'Awaiting calculation':'\u20b9'+formatMoney(value??0);};
        set('summary-subtotal',quote?.itemSubtotal);set('summary-tax',quote?.taxAmount);
        set('summary-total',quote?.totalAmount);set('summary-taxable',quote?.taxableAmount);
        set('summary-line-discounts',quote?.itemDiscountAmount);
        if(quote?.itemDiscountAmount>0)getElement('summary-line-discounts').textContent='\u2212'+getElement('summary-line-discounts').textContent;
        const rates=[...new Set((quote?.items||[]).map(line=>line.taxPercentage))];
        getElement('summary-tax-label').textContent=rates.length===1&&rates[0]>0?'GST ('+rates[0]+'%)':'GST';
        getElement('summary-tax').title=pricesIncludeTax?'GST included in selling prices':'GST added to taxable prices';
        const allocation=quote?[{paymentMode:getElement('payment-mode').value,amount:quote.totalAmount}]:[];
        workspace.dispatchEvent(new Event('billing-payment-updated'));
        const invalidMethod=allocation.some(payment=>!eligibleCollection(payment));
        getElement('billing-payment-warning').hidden=!invalidMethod;
        if(invalidMethod)getElement('billing-payment-warning').textContent='Choose collected payment methods covering the full bill before completing. Pay Later is unavailable for new sales.';
        updateAdjustmentUI();
        const invoiceEditing=numericInputsInvalid();
        const missingReason=cart.some(lineMissingReason);
        const review=approvalReview(),approvalBlocked=quoteIsCurrent()&&review.requiresApproval&&((!billingPolicy.canApproveDiscount&&review.approvalStatus!=='APPROVED')||['PENDING','REJECTED'].includes(review.approvalStatus));
        getElement('complete-sale').disabled=!cart.length||!quoteIsCurrent()||invoiceEditing||missingReason||approvalBlocked||cashInputInvalid()||!getElement('sale-customer').value||invalidMethod||!getElement('payment-mode').value||(getElement('payment-mode').value==='CARD'&&!getElement('payment-card-type').value)||(invoiceReasonRequired()&&!getElement('adjustment-reason').value.trim());
        const status=getElement('billing-calculation-status');
        status.textContent=!cart.length?'':numericInputsInvalid()?'Finish or correct the highlighted number. Totals shown are provisional.':calculationState==='error'?calculationError+' Totals shown are provisional.':!quoteIsCurrent()?'Updating totals - previous amounts are provisional.':invoiceEditing?'Finish the invoice discount edit.':'';
        status.hidden=!status.textContent;getElement('billing-retry-calculation').hidden=calculationState!=='error'||numericInputsInvalid();
        getElement('billing-summary').setAttribute('aria-busy',String(!!cart.length&&calculationState==='pending'));
        getElement('billing-tax-review-link').hidden=workspace.dataset.canGstEdit!=='true'||!gstMapped||calculationState!=='error'||!/GST|tax|classification|HSN/i.test(calculationError);
        syncInvoiceDiscount();
    }
    function balance() {
        const total = priceQuote?.totalAmount || 0;
        const paid = payments.reduce((sum, value) => {
            return sum + value.amount;
        }, 0);
        getElement('payment-balance').textContent =
            `Entered: ${Sales.money(paid)} · Remaining: ${Sales.money(total - paid)}`;
        cashChange();
        updateSummary();
        saveDraftOnThisDevice();
    }
    function mode() {
        workspace.dispatchEvent(new Event('billing-payment-updated'));
        showDraftReference();
        getElement('payment-reference').required = false;
        getElement('payment-reference-field').hidden = !['UPI', 'CARD', 'BANK_TRANSFER'].includes(
            getElement('payment-mode').value
        );
        cashChange();
        updateSummary();
        saveDraftOnThisDevice();
    }
    function changed() {
        cart.forEach(item=>{if(item.discountPercent != null)item.discountAmount=Math.round((item.quantity*item.sellingPrice*item.discountPercent/100+Number.EPSILON)*100)/100;});
        updateLineWarnings();cartRevision++;quoteSequence++;clearTimeout(quoteTimer);
        calculationState=numericInputsInvalid()?'invalid':'pending';calculationError='';
        if(!cart.length){priceQuote=null;quoteRevision=cartRevision;calculationState='valid';getElement('sale-total-amount').textContent='0.00';balance();return;}
        updateSummary();saveDraftOnThisDevice();
        if(numericInputsInvalid())return;
        const version=cartRevision;
        quoteTimer=setTimeout(()=>recalculate().catch(error=>{if(version===cartRevision)getElement('sale-totals').textContent=error.message;}),250);
    }
    function validateItems(checkStock = true) {
        if(numericInputsInvalid())throw new Error('Finish or correct the highlighted numeric field before continuing.');

        if (!cart.length) {
            throw new Error('Add items before completing the sale.');
        }
        for (const item of cart) {
            if(item.discountAmount>item.quantity*item.sellingPrice)throw new Error('Item discount cannot exceed the line amount for '+item.productName+'.');
            if (!item.manualItem && (!Number.isInteger(item.productId) || item.productId <= 0 || !item.variantId)) {
                throw new Error(
                    'Remove and add this item again: its product selection is invalid.'
                );
            }
            if (!Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > 10000) {
                throw new Error(
                    'Enter a whole-number quantity between 1 and 10,000 for ' +
                        item.productName +
                        '.'
                );
            }
            if (
                checkStock && !item.manualItem &&
                Number.isFinite(item.currentStock) &&
                cart.filter(line=>line.variantId===item.variantId).reduce((sum,line)=>sum+line.quantity,0) > item.currentStock
            ) {
                throw new Error(
                    'Insufficient stock for ' +
                        item.productName +
                        '. Reduce quantity or refresh the item.'
                );
            }
            if (!moneyValid(item.sellingPrice) || !moneyValid(item.discountAmount)) {
                throw new Error(
                    'Prices and discounts must be nonnegative amounts with at most two decimal places.'
                );
            }
        }
        if (!moneyValid(Number(getElement('sale-discount').value))) {
            throw new Error('Enter a valid invoice discount with at most two decimal places.');
        }
    }
    async function recalculate() {
        if (!cart.length) {
            return;
        }
        const version=cartRevision,sequence=++quoteSequence;
        try{
        validateItems(false);calculationState='pending';updateSummary();
        const payload = request();
        payload.payments = [];
        const result = await Sales.api('/api/sales/quote', {
            method: 'POST',
            body: JSON.stringify(payload)
        });
        if (version !== cartRevision || sequence !== quoteSequence) {
            return;
        }
        if (
            !result ||
            !['itemSubtotal','itemDiscountAmount','invoiceDiscountAmount','taxableAmount','subtotal','taxAmount','discountAmount','totalAmount'].every(key=>Number.isFinite(result[key])&&result[key]>=0) ||
            result.items?.length !== cart.length || !result.items.every(line=>['taxPercentage','taxAmount','lineTotal','discountAmount'].every(key=>Number.isFinite(line[key])&&line[key]>=0))
        ) {
            throw new Error(
                'The total could not be confirmed. Recalculate before completing this sale.'
            );
        }
        let repriced = false;
        result.items.forEach((line, index) => {
            const item = cart[index];
            if (
                !item.manualItem && Number.isFinite(line.catalogPrice) &&
                line.catalogPrice !== item.sellingPrice &&
                (!billingPolicy.canOverridePrice || item.sellingPrice === item.catalogPrice)
            ) {
                item.sellingPrice = line.catalogPrice;
                repriced = true;
            }
        });
        if (repriced) {
            renderCart();
            getElement('draft-status').textContent =
                'Catalog prices changed. Items were updated; check the new total before payment.';
            return recalculate();
        }
        priceQuote=result;quoteRevision=version;calculationState='valid';calculationError='';
        getElement('sale-total-amount').textContent = Sales.money(result.totalAmount);
        getElement('sale-totals').textContent =
            `Subtotal: ${Sales.money(result.subtotal)} · Tax: ${Sales.money(result.taxAmount)} · Discount: ${Sales.money(result.discountAmount)} · Total: ${Sales.money(result.totalAmount)}`;
        result.items.forEach((line, index) => {
            const item = cart[index];
            item.currentStock = item.manualItem ? null : line.currentStock;
            item.mrp=line.mrp;
            getElement('line-mrp-'+index).textContent=line.mrp ? formatMoney(line.mrp) : '—';
            item.catalogPrice = line.catalogPrice;
            updateLineStock(item,index);
            const priceEditor=getElement('line-info-'+index).closest('tr').querySelector('[data-price-editor]');
            if(document.activeElement!==priceEditor)priceEditor.value=Sales.money(item.sellingPrice);
            if(item.catalogTaxPercentage==null&&item.taxOverride==null)item.catalogTaxPercentage=line.taxPercentage;
            item.taxPercentage=line.taxPercentage;
            getElement('line-tax-'+index).value=line.taxPercentage;
            const taxEditor=getElement('line-tax-override-'+index);if(document.activeElement!==taxEditor)taxEditor.value=item.taxOverride??line.taxPercentage;
            item.lastLineTotal=line.lineTotal;getElement('line-total-' + index).textContent = formatMoney(line.lineTotal);
            updateLineIdentity(item,index,line);
        });
        updateLineWarnings();
        balance();
        try{validateItems();}catch(error){getElement('complete-sale').disabled=true;throw error;}
        }catch(error){
            if(version!==cartRevision||sequence!==quoteSequence)return;
            calculationState=numericInputsInvalid()?'invalid':'error';calculationError=error.message;updateSummary();throw error;
        }
    }
    function options() {
        updateAdjustmentUI();
        for (const node of document.querySelectorAll('[data-price-editor]')) {
            node.readOnly = !billingPolicy.canOverridePrice && !cart[Number(node.dataset.lineIndex)]?.manualItem;
        }
        for (const node of document.querySelectorAll('[data-discount-editor]')) {
            node.readOnly = !billingPolicy.canDiscount;
        }
        for(const node of document.querySelectorAll('[data-tax-editor]')){
            const row=node.closest('tr'),item=cart[Number(row.querySelector('[data-price-editor]').dataset.lineIndex)];
            node.readOnly=!billingPolicy.canOverrideTax||gstMapped||!!item?.manualItem;
            row.querySelector('button[data-tax-correction]').hidden=node.readOnly;
            row.querySelector('[data-tax-display]').title='Tax from the current billing calculation. Authorized ordinary-bill corrections use the separate correction action.';
        }
        getElement('sale-discount').readOnly = !billingPolicy.canDiscount;
    }
    function updateLineIdentity(item,index,line={}) {
        const info=getElement('line-info-'+index);info.textContent=item.variantDetails||'';info.title=info.textContent;info.hidden=!info.textContent;
        const serial=getElement('line-serial-'+index);serial.textContent=item.serialNumber?(item.serialSource==='MANUFACTURER'?'Serial: ':item.serialSource==='INTERNAL'?'Unit ID: ':'Serial / Unit ID: ')+item.serialNumber:'';serial.title=serial.textContent;serial.hidden=!serial.textContent;
    }
    function updateLineStock(item,index){
        const stock=getElement('line-stock-'+index),quantity=item.currentStock,threshold=item.stockMinimum;
        stock.textContent=item.manualItem?'Non-stock':Number.isFinite(quantity)?String(quantity):'Pending';
        stock.dataset.state=item.manualItem?'non-stock':!Number.isFinite(quantity)?'pending':quantity<=0?'out':Number.isFinite(threshold)?quantity<=threshold?'low':'available':'neutral';
        stock.title=item.manualItem?'Manual item; no inventory posting':!Number.isFinite(quantity)?'Awaiting current location stock':quantity<=0?'Out of stock in this location':Number.isFinite(threshold)?'Available in this location; low-stock threshold '+threshold:'Available in this location; low-stock threshold not supplied';
    }
    function updateLineWarnings() {
        const review=approvalReview(),current=quoteIsCurrent();
        cart.forEach((item,index)=>{
            const badge=getElement('line-warning-'+index);if(!badge)return;
            const row=badge.closest('tr'),price=row.querySelector('[data-price-editor]'),discount=row.querySelector('[data-discount-editor]'),tax=getElement('line-tax-'+index);
            const changed=lineAdjustments(item),required=lineRequirements(item,index),reason=lineReason(item);
            const messages=[];
            discount.max=item.discountPercent!=null?'100':String(Math.max(0,item.quantity*item.sellingPrice));
            if(!moneyValid(item.sellingPrice))messages.push('Enter a valid nonnegative unit price.');
            if(!moneyValid(item.discountAmount)||item.discountAmount>item.quantity*item.sellingPrice)messages.push('Discount cannot exceed the line amount.');
            if(required.discount&&!reason)messages.push('Discount reason required by shop policy.');
            if(required.price&&!(item.priceAdjustmentReason?.trim()||reason))messages.push('Price change reason required.');
            const taxReasonRequired=changed.tax&&(item.taxAdjustmentReason?.trim().length||0)<3;
            if(taxReasonRequired)messages.push('Enter a tax change reason (at least 3 characters).');
            const taxError=calculationState==='error'&&/GST|tax|classification|HSN/i.test(calculationError)&&!/permission|reason.*manual tax|manual tax.*reason/i.test(calculationError);
            if(taxError)messages.push('Tax configuration needs review. '+calculationError);
            const approvalNeeded=current&&review.requiresApproval&&(['PENDING','REJECTED'].includes(review.approvalStatus)||!billingPolicy.canApproveDiscount&&review.approvalStatus!=='APPROVED')&&(changed.discount||changed.price);
            if(approvalNeeded)messages.push(review.approvalStatus==='PENDING'?'Discount approval pending.':review.approvalStatus==='REJECTED'?'Manager rejected this exception.':'Manager approval required.');
            badge.textContent=approvalNeeded?'Approval Required':taxError?'Review Tax':taxReasonRequired?'Tax Reason':'Add Reason';badge.title=messages.join(' ');badge.setAttribute('aria-label',messages.join(' '));badge.hidden=!messages.length;
            badge.classList.add('billing-approval-indicator');badge.dataset.state=taxError?'error':'warning';
            (taxError||taxReasonRequired?tax:changed.price?price:discount).parentElement.append(badge);
            const dialog=getElement('line-reason-panel-'+index),label=getElement('line-reason-label-'+index),feedback=getElement('line-reason-status-'+index);
            dialog.hidden=false;
            const reasonInput=getElement('line-reason-'+index),priceInput=getElement('line-price-reason-'+index),taxInput=getElement('line-tax-reason-'+index);
            const correction=getElement('line-tax-override-'+index),showCorrection=!taxError&&!correction.readOnly&&(dialog.dataset.taxCorrection==='true'||taxReasonRequired);
            correction.hidden=correction.previousElementSibling.hidden=!showCorrection;
            // Keep an active field visible while typing. On the next opening, ask only
            // for missing reasons; previously saved reasons remain on the cart item.
            const showDiscount=!taxError&&required.discount&&(!reason||dialog.open&&!reasonInput.hidden);
            const showPrice=!taxError&&required.price&&(!(item.priceAdjustmentReason?.trim()||reason)||dialog.open&&!priceInput.hidden);
            const showTax=!taxError&&changed.tax&&(taxReasonRequired||dialog.open&&!taxInput.hidden);
            const reasonCount=Number(showDiscount)+Number(showPrice)+Number(showTax);
            dialog.querySelector('h2').textContent=taxError?'Tax Configuration Needs Review':reasonCount>1?'Adjustment Reasons':showTax?'Tax Change Reason':showDiscount?'Discount Reason':showPrice?'Price Change Reason':showCorrection?'Tax Correction':approvalNeeded?'Manager Approval Required':'Adjustment Details';
            label.textContent='Discount Reason';label.hidden=reasonInput.hidden=!showDiscount;reasonInput.required=showDiscount;
            priceInput.hidden=priceInput.previousElementSibling.hidden=!showPrice;
            taxInput.hidden=taxInput.previousElementSibling.hidden=!showTax;
            const taxLink=dialog.querySelector('[data-tax-review]');taxLink.hidden=workspace.dataset.canGstEdit!=='true'||!taxError;
            const context=[item.productName,item.sku?'SKU: '+item.sku:'Manual Item'];
            const showApprovalSummary=approvalNeeded&&reasonCount===0&&!taxError;
            if(showDiscount||showPrice||showApprovalSummary)context.push('Qty: '+item.quantity);
            if(showDiscount||showApprovalSummary)context.push('Discount: '+(item.discountPercent!=null?item.discountPercent+'% · ':'')+formatMoney(item.discountAmount));
            if(showPrice)context.push('Price: '+formatMoney(item.catalogPrice)+' → '+formatMoney(item.sellingPrice));
            if(showTax)context.push('Tax: '+(Number.isFinite(item.catalogTaxPercentage)?item.catalogTaxPercentage+'%':'Configured tax')+' → '+item.taxOverride+'%');
            dialog.querySelector('[data-adjustment-summary]').textContent=context.join(' | ');
            dialog.querySelector('[data-request-approval]').hidden=!approvalNeeded;
            dialog.querySelector('[data-use-configured-tax]').hidden=item.taxOverride==null;
            feedback.textContent=messages.join(' ');feedback.hidden=!messages.length;
        });
    }
    function updateItemCount(){const units=cart.reduce((sum,item)=>sum+item.quantity,0);getElement('sale-item-count').textContent=cart.length ? cart.length+' item'+(cart.length===1?'':'s')+' \u00b7 '+units+' unit'+(units===1?'':'s') : '0 items';}
    function updateCartScrollHint(){const region=workspace.querySelector('.billing-cart-scroll'),hint=getElement('billing-cart-scroll-hint');if(region&&hint)hint.hidden=!cart.length||region.scrollWidth<=region.clientWidth+1;}
    new ResizeObserver(updateCartScrollHint).observe(getElement('billing-cart-card'));
    function renderCart() {
        const tradeInOptions = getElement('trade-in-options');
        if (tradeInOptions) {
            tradeInOptions.hidden = !cart.length && !tradeIn();
            if (tradeInOptions.hidden) tradeInOptions.open = false;
        }
        getElement('clear-bill-items').disabled = !cart.length || isSavingSale || !!pendingSale;
        getElement('clear-bill-items').hidden = !cart.length;
        getElement('sale-cart').replaceChildren();
        getElement('sale-empty').hidden = cart.length > 0;
        getElement('sale-cart').closest('.table-responsive').hidden=!cart.length;
        updateItemCount();
        cart.forEach((item, index) => {
            const tr = document.createElement('tr');
            const name = document.createElement('strong');
            name.textContent = item.productDisplayName||item.productName;name.title=item.productName;
            const info = document.createElement('span');
            info.id = 'line-info-' + index;
            info.className = 'billing-item-detail';
            info.textContent = item.serialNumber
                ? 'SKU: '+item.sku+' · '+(item.serialSource==='MANUFACTURER'?'Manufacturer Serial: ':item.serialSource==='INTERNAL'?'Internal Unit ID: ':'Serial / Unit ID: ') + item.serialNumber
                : [item.variantDetails, 'SKU: ' + (item.sku || '—')].filter(Boolean).join(' · ');
            const title = document.createElement('div');
            title.className='billing-product-copy';
            const serial=document.createElement('span');serial.id='line-serial-'+index;serial.className='billing-item-serial';
            title.append(name, info,serial);
            Sales.cell(tr, title);
            const skuCellValue=Sales.cell(tr,item.manualItem ? '—' : item.sku || '—');skuCellValue.className='billing-item-sku';skuCellValue.title=item.manualItem?'Non-catalog item':item.sku||'SKU not captured';
            const stockValue=document.createElement('span');stockValue.id='line-stock-'+index;stockValue.className='billing-stock-value';Sales.cell(tr,stockValue);
            const quantity = input(
                item.quantity,
                'Quantity for ' + item.productName,
                (value) => {
                    item.quantity = value;
                    updateItemCount();
                },
                '10000'
            );
            quantity.step = '1';
            quantity.min = '1';
            quantity.readOnly = !!item.productUnitId;
            const qty=document.createElement('div');qty.className='billing-quantity';
            const adjust=delta=>{const next=item.quantity+delta;if(next<1||next>10000)return;if(Number.isFinite(item.currentStock)&&next>item.currentStock){Sales.error(new Error('Only '+item.currentStock+' units are currently available.'));return;}quantity.value=next;quantity.dispatchEvent(new Event('input',{bubbles:true}));};
            const quantityButton=(label,delta)=>{const control=document.createElement('button');control.type='button';control.className='billing-qty-button';control.textContent=label;control.addEventListener('click',()=>adjust(delta));return control;};
            const minus=quantityButton('\u2212',-1),plus=quantityButton('+',1);
            minus.setAttribute('aria-label','Decrease quantity for '+item.productName);plus.setAttribute('aria-label','Increase quantity for '+item.productName);
            minus.disabled=plus.disabled=!!item.productUnitId;qty.append(minus,quantity,plus);Sales.cell(tr,qty);
            const price = input(item.sellingPrice, 'Price for ' + item.productName, (value) => {
                return (item.sellingPrice = value);
            });
            price.dataset.priceEditor = 'true';
            Sales.cell(tr,item.mrp ? formatMoney(item.mrp) : '—').id='line-mrp-'+index;
            Sales.cell(tr, price);
            price.dataset.lineIndex=String(index);
            const discount = input(
                item.discountPercent ?? item.discountAmount,
                'Discount for ' + item.productName,
                (value) => {
                    if(item.discountPercent != null)item.discountPercent=value;else item.discountAmount=value;
                }
            );
            discount.max=item.discountPercent!=null?'100':'9999999999.99';
            discount.dataset.discountEditor = 'true';
            Sales.cell(tr, discount).className = 'billing-extra-column';
            const taxDisplay=document.createElement('input');taxDisplay.id='line-tax-'+index;taxDisplay.className='form-control';taxDisplay.readOnly=true;taxDisplay.tabIndex=-1;taxDisplay.value=item.taxPercentage??'';taxDisplay.placeholder='Pending';taxDisplay.dataset.taxDisplay='true';taxDisplay.setAttribute('aria-label','Applicable tax percentage for '+item.productName);
            const tax=Sales.cell(tr,taxDisplay);tax.className='billing-extra-column';
            const taxEditor=input(item.taxOverride??item.taxPercentage??'','Correction tax percentage for '+item.productName,value=>item.taxOverride=value,'100');
            taxEditor.id='line-tax-override-'+index;taxEditor.dataset.taxEditor='true';
            Sales.cell(tr,Number.isFinite(item.lastLineTotal)?formatMoney(item.lastLineTotal):'Pending').id = 'line-total-' + index;
            Sales.cell(
                tr,
                button('Remove', () => {
                    cart.splice(index, 1);
                    renderCart();
                })
            );
            tr.lastElementChild.querySelector('button').setAttribute('aria-label','Remove '+item.productName);
            tr.lastElementChild.querySelector('button').innerHTML='<svg width=18 height=18 viewBox="0 0 24 24" fill=none stroke=currentColor stroke-width=1.8 aria-hidden=true><path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"/></svg>';
            const [productCell,skuCell,stockCell,qtyCell,mrpCell,priceCell,discountCell,taxCell,totalCell,removeCell]=Array.from(tr.cells);
            const image=document.createElement('span');image.className='billing-item-image';
            image.innerHTML='<svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="m12 3 9 5v9l-9 5-9-5V8l9-5zM3 8l9 5 9-5M12 13v9"/></svg>';
            if(!item.manualItem && item.hasProductImage!==false){const img=document.createElement('img');img.src='/api/products/'+item.productId+'/image';img.alt='';img.loading='lazy';img.onerror=()=>img.remove();image.append(img);}
            productCell.prepend(image);productCell.className='billing-item-product';
            const warning=document.createElement('button');warning.type='button';warning.id='line-warning-'+index;warning.className='billing-line-adjustment-indicator';warning.hidden=true;
            const reasonPanel=document.createElement('dialog');reasonPanel.id='line-reason-panel-'+index;reasonPanel.className='billing-adjustment-dialog';reasonPanel.dataset.sfDialog='medium';
            const heading=document.createElement('h2');heading.id='line-adjustment-title-'+index;reasonPanel.setAttribute('aria-labelledby',heading.id);
            const context=document.createElement('p');context.dataset.adjustmentSummary='';
            const reasonLabel=document.createElement('label');reasonLabel.id='line-reason-label-'+index;reasonLabel.htmlFor='line-reason-'+index;
            const reasonInput=document.createElement('input');reasonInput.id='line-reason-'+index;reasonInput.type='text';reasonInput.className='form-control';reasonInput.maxLength=500;reasonInput.value=lineReason(item);
            const priceLabel=document.createElement('label');priceLabel.textContent='Price Change Reason';priceLabel.htmlFor='line-price-reason-'+index;
            const priceInput=document.createElement('input');priceInput.id='line-price-reason-'+index;priceInput.className='form-control';priceInput.maxLength=500;priceInput.value=item.priceAdjustmentReason||'';
            const taxLabel=document.createElement('label');taxLabel.textContent='Tax Change Reason';taxLabel.htmlFor='line-tax-reason-'+index;
            const correctionLabel=document.createElement('label');correctionLabel.textContent='Correction tax (%)';correctionLabel.htmlFor=taxEditor.id;
            const taxInput=document.createElement('input');taxInput.id='line-tax-reason-'+index;taxInput.className='form-control';taxInput.maxLength=500;taxInput.minLength=3;taxInput.value=item.taxAdjustmentReason||'';
            const reasonStatus=document.createElement('p');reasonStatus.id='line-reason-status-'+index;reasonStatus.setAttribute('role','status');
            const reviewTax=document.createElement('a');reviewTax.href='/Admin/Gst';reviewTax.textContent='Review Tax Configuration';reviewTax.dataset.taxReview='';reviewTax.target='_blank';reviewTax.rel='noopener';
            const useConfiguredTax=button('Use Configured Tax',()=>{item.taxOverride=null;reasonPanel.close();changed();});useConfiguredTax.dataset.useConfiguredTax='';useConfiguredTax.hidden=item.taxOverride==null;
            const requestApproval=button('Request Approval',()=>{reasonPanel.close();Sales.run(async()=>{await recalculate();getElement('request-discount-approval').click();});});requestApproval.dataset.requestApproval='';
            const closeReason=button('Done',()=>reasonPanel.close());
            reasonPanel.append(heading,context,reasonLabel,reasonInput,priceLabel,priceInput,correctionLabel,taxEditor,taxLabel,taxInput,reasonStatus,reviewTax,useConfiguredTax,requestApproval,closeReason);
            title.append(reasonPanel);discountCell.append(warning);
            reasonInput.addEventListener('input',()=>{item.adjustmentReason=reasonInput.value;changed();});
            priceInput.addEventListener('input',()=>{item.priceAdjustmentReason=priceInput.value;changed();});
            taxInput.addEventListener('input',()=>{item.taxAdjustmentReason=taxInput.value;changed();});
            reasonPanel.addEventListener('close',()=>{delete reasonPanel.dataset.taxCorrection;if(cart.length&&!numericInputsInvalid())changed();});
            warning.onclick=()=>{updateLineWarnings();reasonPanel.showModal();};
            const correctTax=button('Correct',()=>{reasonPanel.dataset.taxCorrection='true';updateLineWarnings();reasonPanel.showModal();taxEditor.focus();});correctTax.dataset.taxCorrection='';correctTax.classList.add('billing-tax-correction');correctTax.title='Authorized ordinary-bill tax correction';correctTax.setAttribute('aria-label','Correct tax for '+item.productName);correctTax.innerHTML='<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="m16 3 5 5-13 13H3v-5ZM14 5l5 5"/></svg>';tax.append(correctTax);
            stockCell.className='billing-stock-cell';qtyCell.className='billing-qty-cell';priceCell.className='billing-item-price';discountCell.className='billing-item-discount';taxCell.className='billing-item-tax';totalCell.className='billing-item-total';removeCell.className='billing-item-actions';
            price.title=pricesIncludeTax?'Unit selling price including tax':'Unit selling price before tax';
            discount.title=item.discountPercent != null ? 'Percentage discount for this line' : 'Saved fixed-amount discount (₹)';
            if(item.discountPercent == null){const fixed=document.createElement('small');fixed.textContent='Fixed ₹ (saved bill)';discountCell.append(fixed);}
            mrpCell.className='billing-item-mrp';
            if(item.manualItem){const editManual=button('Edit',()=>openManualItem(index));editManual.classList.add('billing-manual-edit');title.append(editManual);}
            removeCell.querySelector('button').title='Remove Item';
            ['Product / Variant','SKU','Available Stock','Qty','MRP (₹)','Selling Price','Discount (%)','Tax (%)','Line Total','Actions'].forEach((label,i)=>tr.cells[i].dataset.label=label);
            getElement('sale-cart').append(tr);
            updateLineIdentity(item,index);
            updateLineStock(item,index);
        });
        options();
        requestAnimationFrame(updateCartScrollHint);
        changed();
    }
    getElement('clear-bill-items').addEventListener('click', () => Sales.run(async () => {
        if (isSavingSale || pendingSale || isPreparingSale || !cart.length) return;
        if(activeLookups)throw new Error('Wait for item and customer lookups to finish.');
        if(!await StockFlowDialog.confirm('Clear this draft bill, its items and entered payment details? This does not affect completed invoices.',{kind:'discard',title:'Clear this bill?',cancelText:'Continue Editing',confirmText:'Clear Bill'}))return;
        if(isSavingSale||pendingSale||isPreparingSale||activeLookups)return;
        cart=[];payments=[];getElement('use-split-payment').checked=false;
        for(const id of ['sale-notes','adjustment-reason','payment-reference','cash-received'])getElement(id).value='';
        if(getElement('sale-gst-pos'))getElement('sale-gst-pos').value='';
        if(getElement('sale-gst-delivery'))getElement('sale-gst-delivery').value='';
        if(getElement('sale-gst-recipient-details'))getElement('sale-gst-recipient-details').checked=false;
        getElement('adjustment-reason').classList.remove('is-invalid');
        getElement('adjustment-reason').removeAttribute('aria-invalid');
        if(getElement('adjustment-reason-error'))getElement('adjustment-reason-error').hidden=true;
        getElement('sale-discount').value=0;delete getElement('sale-discount').dataset.committedNumber;delete getElement('sale-discount').dataset.numericInvalid;getElement('sale-customer').value='';getElement('payment-mode').value='CASH';getElement('payment-card-type').value='';
        if(getElement('use-trade-in')){getElement('use-trade-in').checked=false;getElement('trade-in-fields').hidden=true;}
        getElement('unit-picker').hidden=true;productWaitingForSerial=null;
        crmEnquiryId=null;getElement('sale-crm-context')?.remove();
        sourceQuotationId=null;sourceQuotationRevision=null;sourceQuotationVersion=null;getElement('sale-quotation-context')?.remove();
        sourceQuotationNumber=null;sourceQuotationNotice='';
        renderCart();renderPayments();mode();syncCustomer();showDraftReference();getElement('sale-scan')?.focus();
    }));
    function renderPayments() {
        getElement('billing-simple-payment').hidden = isSplitPayment();
        getElement('billing-split-payment').hidden = !isSplitPayment();
        getElement('sale-payment-inputs').replaceChildren();
        if (!isSplitPayment()) {
            return;
        }
        payments.forEach((payment, index) => {
            const tr = document.createElement('tr');
            const select = document.createElement('select');
            select.className = 'form-select';
            select.setAttribute('aria-label', 'Payment mode');
            for (const [value,label] of [['CASH','Cash'],['UPI','UPI'],['CARD:DEBIT','Debit Card'],['CARD:CREDIT','Credit Card'],['BANK_TRANSFER','Bank Transfer']]) select.add(new Option(label,value));
            const savedMode = payment.paymentMode==='CARD' && payment.cardType ? 'CARD:'+payment.cardType : payment.paymentMode;
            if(!Array.from(select.options).some(o=>o.value===savedMode)){const choice=new Option('Choose collected method','');choice.disabled=true;select.prepend(choice);}
            select.value = payment.paymentMode==='CARD' && payment.cardType ? 'CARD:'+payment.cardType : payment.paymentMode;
            select.addEventListener('change', () => {
                const [mode,type]=select.value.split(':');payment.paymentMode=mode;
                if(mode==='CARD' && type)payment.cardType=type;else delete payment.cardType;
                balance();
            });
            Sales.cell(tr, select);
            const amount = input(
                payment.amount,
                'Payment amount',
                (value) => {
                    return (payment.amount = value);
                },
                '9999999999.99',
                false
            );
            amount.min = '0.01';
            Sales.cell(tr, amount);
            const reference = document.createElement('input');
            reference.className = 'form-control';
            reference.maxLength = 100;
            reference.value = payment.referenceNumber || '';
            reference.setAttribute('aria-label', 'Payment reference');
            reference.addEventListener('input', () => {
                return (payment.referenceNumber = reference.value);
            });
            Sales.cell(tr, reference);
            Sales.cell(
                tr,
                button('Remove', () => {
                    payments.splice(index, 1);
                    renderPayments();
                })
            );
            getElement('sale-payment-inputs').append(tr);
        });
        balance();
    }
    function addLine(product, unit) {
        if (isSavingSale || pendingSale || isPreparingSale) {
            throw new Error('Finish the current sale before changing items.');
        }
        if (product.isActive === false || product.isAvailable === false || !product.variantId) {
            throw new Error('This item is unavailable.');
        }
        if (
            unit &&
            cart.some((unit2) => {
                return unit2.productUnitId === unit.productUnitId;
            })
        ) {
            throw new Error('This serial number is already in the cart.');
        }
        if (unit && unit.unitStatus && unit.unitStatus !== 'IN_STOCK') {
            throw new Error('This product or physical unit is unavailable.');
        }
        const existing =
            !unit &&
            cart.find((unit2) => {
                return unit2.variantId === product.variantId && !unit2.productUnitId && unit2.sellingPrice===product.sellingPrice && unit2.discountAmount===0;
            });
        const quantity = cart.filter(i=>i.variantId===product.variantId).reduce((sum,i)=>sum+i.quantity,0) + 1;
        if (
            quantity > 10000 ||
            (Number.isFinite(product.currentStock) && quantity > product.currentStock)
        ) {
            throw new Error('Insufficient stock for ' + product.productName + '.');
        }
        if (existing) {
            existing.quantity++;
            existing.currentStock = product.currentStock;
            existing.stockMinimum=product.locationMinimumStockLevel??product.minimumStockLevel??existing.stockMinimum??null;
        } else {
            cart.push({
                productId: product.productId,
                variantId: product.variantId,
                productName: product.productName,
                productDisplayName: product.variantName&&product.variantName!=='DEFAULT'&&product.productName.endsWith(' / '+product.variantName)?product.productName.slice(0,-(' / '+product.variantName).length):product.productName,
                sku: product.sku,
                variantDetails: StockFlowVariantIdentity.summary(product),
                hasProductImage: product.hasProductImage,
                productUnitId: unit?.productUnitId || null,
                serialNumber: unit?.serialNumber,
                serialSource: unit?.serialSource,
                quantity: 1,
                sellingPrice: product.sellingPrice,
                catalogPrice: product.sellingPrice,
                discountAmount: 0,
                discountPercent: 0,
                mrp: product.mrp || null,
                currentStock: product.currentStock,
                stockMinimum: product.locationMinimumStockLevel??product.minimumStockLevel??null
            });
        }
        renderCart();
        getElement('unit-picker').hidden = true;
        getElement('sale-scan')?.focus();
    }
    async function lookup(action) {
        activeLookups++;
        try {
            await action();
        } finally {
            activeLookups--;
        }
    }
    async function loadUnits() {
        if (!productWaitingForSerial || workspace.dataset.canUnits !== 'true') {
            return;
        }
        const selected = productWaitingForSerial;
        const units = await Sales.api(
            '/api/inventory/units?' +
                new URLSearchParams({
                    productId: selected.productId,
                    variantId: selected.variantId,
                    term: getElement('serial-search').value,
                    availableOnly: true
                })
        );
        if (selected !== productWaitingForSerial) {
            return;
        }
        availableSerialItems = units.filter((variant) => {
            return variant.variantId === selected.variantId;
        });
        getElement('sale-unit').replaceChildren(
            ...availableSerialItems.map((unit) => {
                return new Option(
                    (unit.serialSource==='INTERNAL'?'Internal Unit ID: ':unit.serialSource==='MANUFACTURER'?'Manufacturer Serial: ':'Serial / Unit ID: ')+(unit.serialNumber || unit.barcode || 'Unit #' + unit.productUnitId),
                    unit.productUnitId
                );
            })
        );
        if (!availableSerialItems.length) {
            getElement('sale-unit').add(new Option('No available units found', ''));
        }
    }
    async function chooseProduct(product) {
        if (product.isSerialTracked) {
            productWaitingForSerial = product;
            getElement('unit-picker-title').textContent =
                product.productName +
                ': scan an individual serial' +
                (workspace.dataset.canUnits === 'true' ? ' or choose an available unit.' : '.');
            getElement('unit-picker').hidden = false;
            if (getElement('serial-search')) {
                getElement('serial-search').value = '';
            }
            await loadUnits();
            getElement('sale-scan')?.focus();
        } else {
            addLine(product);
        }
    }
    async function findScannedProduct(code) {
        if (isSavingSale || pendingSale || isPreparingSale) {
            return;
        }
        await lookup(async () => {
            Sales.clear();
            const result = await Sales.api(
                '/api/scan?' +
                    new URLSearchParams({
                        code
                    })
            );
            if (!result?.isAvailable) {
                throw new Error('This product or physical unit is unavailable.');
            }
            if (result.productUnitId) {
                addLine(result, result);
            } else {
                await chooseProduct(result);
            }
        });
    }
    let searchTimer,searchVersion=0,highlighted=-1;
    const searchInput=getElement('sale-scan'),resultHost=getElement('product-results');
    function closeResults(){if(resultHost)resultHost.hidden=true;searchInput?.setAttribute('aria-expanded','false');highlighted=-1;}
    async function searchProducts(term,version=++searchVersion){
        if(workspace.dataset.canSearch!=='true')return;
        if(!term.trim()){closeResults();getElement('product-result-status').textContent='';return;}
        getElement('product-result-status').textContent='Searching…';
        try{
            const found=await Sales.api('/api/products/variants?'+new URLSearchParams({term,billing:true}));
            if(version!==searchVersion)return;
            products=found;highlighted=-1;resultHost.replaceChildren();resultHost.hidden=false;searchInput.setAttribute('aria-expanded','true');
            getElement('product-result-status').textContent=found.length?(found.length===30?'Showing the first 30 matches. Refine your search.':found.length+' matching SKUs. Choose an item.'):'No products found. Try another name or code.';
            for(const product of products){
                const row=document.createElement('div');row.className='billing-result';
                const identity=document.createElement('div'),name=document.createElement('strong'),meta=document.createElement('small');name.textContent=product.productName;
                meta.textContent=[product.brandName,product.categoryName,product.variantName==='DEFAULT'?'':product.variantName].filter(Boolean).join(' · ');identity.append(name,meta);
                const thumbnail=document.createElement('span');thumbnail.className='billing-result-image';thumbnail.innerHTML='<svg width=24 height=24 viewBox="0 0 24 24" fill=none stroke=currentColor aria-hidden=true><path d="m12 3 9 5v9l-9 5-9-5V8zM3 8l9 5 9-5M12 13v9"/></svg>';if(product.hasProductImage){const img=document.createElement('img');img.src='/api/products/'+product.productId+'/image';img.alt='';img.loading='lazy';img.onerror=()=>img.remove();thumbnail.append(img);}row.append(thumbnail);
                const sku=document.createElement('div');sku.textContent=product.sku;
                const price=document.createElement('div');price.textContent='₹'+Sales.money(product.sellingPrice);const tax=document.createElement('small');tax.textContent=pricesIncludeTax?'Includes GST':'Before GST';price.append(tax);
                if(product.mrp>0){const mrp=document.createElement('small');mrp.textContent='MRP ₹'+Sales.money(product.mrp);price.append(mrp);}
                const stock=document.createElement('span');stock.className='billing-stock';
                const available=product.isActive!==false&&product.currentStock>0;
                stock.textContent=product.isActive===false?'Inactive':product.currentStock<=0?'Out of stock':product.isSerialTracked?'Serial selection required':(product.currentStock<=(product.locationMinimumStockLevel??product.minimumStockLevel??0)?'Low stock: ':'')+product.currentStock+' in stock';
                stock.dataset.state=!available?'unavailable':product.currentStock<=(product.locationMinimumStockLevel??product.minimumStockLevel??0)?'low':'available';
                const add=button('Add',()=>Sales.run(()=>lookup(async()=>{if(pendingSale||isPreparingSale||isSavingSale)return;await chooseProduct(product);closeResults();searchInput.value='';searchVersion++;})));
                add.setAttribute('aria-label','Add '+product.productName+' SKU '+product.sku);add.disabled=!available;
                row.append(identity,sku,price,stock,add);resultHost.append(row);
            }
        }catch(error){if(version===searchVersion){getElement('product-result-status').textContent='Search could not finish. Try again.';throw error;}}
    }
    async function submitSearch(code){
        clearTimeout(searchTimer);++searchVersion;closeResults();
        if(workspace.dataset.canScan!=='true'){await lookup(()=>searchProducts(code));return;}
        try{await findScannedProduct(code);getElement('product-result-status').textContent='Item identified. Ready for the next scan.';}
        catch(error){
            if([400,404,422].includes(error.status)&&workspace.dataset.canSearch==='true'){
                await lookup(()=>searchProducts(code));
                if(!products.length)throw error;
            }else throw error;
        }
    }
    // Capture deliberate result navigation; plain Enter is still an exact scan first.
    searchInput?.addEventListener('keydown',event=>{
        const buttons=[...(resultHost?.querySelectorAll('button:not(:disabled)')||[])];
        if(event.key==='Escape'){++searchVersion;clearTimeout(searchTimer);closeResults();event.preventDefault();return;}
        if(['ArrowDown','ArrowUp'].includes(event.key)&&!resultHost.hidden&&buttons.length){
            event.preventDefault();highlighted=(highlighted+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;
            buttons.forEach((b,i)=>b.closest('.billing-result').classList.toggle('highlighted',i===highlighted));buttons[highlighted].scrollIntoView({block:'nearest'});
        }
        if(event.key==='Enter'&&!event.isComposing&&highlighted>=0&&!resultHost.hidden){event.preventDefault();event.stopImmediatePropagation();if(!event.repeat)buttons[highlighted]?.click();}
    });
    const scanSubmit=StockFlowScanner.bind('sale-scan',submitSearch,Sales.error);
    if(workspace.dataset.canScan!=='true')searchInput?.parentElement.querySelector('.sf-camera-trigger')?.remove();
    getElement('scan-sale')?.addEventListener('click',scanSubmit);
    searchInput?.addEventListener('input',()=>{
        clearTimeout(searchTimer);const version=++searchVersion,term=searchInput.value;highlighted=-1;closeResults();
        if(!term.trim()){getElement('product-result-status').textContent='';return;}
        if(workspace.dataset.canSearch==='true')searchTimer=setTimeout(()=>Sales.run(()=>lookup(()=>searchProducts(term,version))),300);
    });
    getElement('find-sale-units')?.addEventListener('click', () => {
        return Sales.run(() => {
            return lookup(loadUnits);
        });
    });
    getElement('add-unit')?.addEventListener('click', () => {
        return Sales.run(() => {
            const unit = availableSerialItems.find((unit2) => {
                return unit2.productUnitId === Number(getElement('sale-unit').value);
            });
            if (!unit) {
                throw new Error('Select an available unit.');
            }
            addLine(productWaitingForSerial, unit);
        });
    });
    let customerTimer, customerMatches=[], customerActive=-1;
    function closeCustomerResults(){
        getElement('customer-results').hidden=true;
        getElement('customer-search')?.setAttribute('aria-expanded','false');
        getElement('customer-search')?.removeAttribute('aria-activedescendant');customerActive=-1;
    }
    function invalidateCustomerSearch(){
        ++customerSearch;clearTimeout(customerTimer);closeCustomerResults();customerMatches=[];
        getElement('customer-results').replaceChildren();getElement('customer-search-status').textContent='';
    }
    function syncCustomer(){
        invalidateCustomerSearch();
        const option=getElement('sale-customer').selectedOptions[0],selected=!!getElement('sale-customer').value;
        const parts=(option?.textContent||'').split(' / ');
        getElement('customer-name').textContent=parts[0];
        getElement('customer-mobile').textContent=parts.length>1?'Mobile: '+parts.slice(1).join(' / '):'';
        getElement('customer-current').hidden=!selected;
        const search=getElement('customer-search');if(search){search.hidden=selected;search.value='';}
    }
    function chooseCustomer(customer){
        getElement('sale-customer').replaceChildren(new Option('Select customer',''),new Option(customer.customerName+(customer.mobileNumber?' / '+customer.mobileNumber:''),customer.customerId,true,true));
        syncCustomer();changed();getElement('sale-scan')?.focus();
    }
    async function searchCustomers(){
        const search=getElement('customer-search'),term=search.value.trim();if(!term){invalidateCustomerSearch();return;}
        const version=++customerSearch,status=getElement('customer-search-status');status.textContent='Searching customers...';
        const customers=await Sales.api('/api/customers/search?'+new URLSearchParams({term}));
        if(version!==customerSearch||search.hidden||search.value.trim()!==term)return;
        customerMatches=customers;customerActive=-1;
        const results=getElement('customer-results');results.replaceChildren();
        customers.forEach((customer,index)=>{
            const row=document.createElement('div');row.id='customer-result-'+index;row.setAttribute('role','option');row.setAttribute('aria-selected','false');
            const name=document.createElement('strong'),mobile=document.createElement('span');name.textContent=customer.customerName;mobile.textContent=customer.mobileNumber||'';row.append(name,mobile);
            row.addEventListener('mousedown',event=>event.preventDefault());row.addEventListener('click',()=>chooseCustomer(customer));results.append(row);
        });
        results.hidden=!customers.length;search.setAttribute('aria-expanded',String(!!customers.length));status.textContent=customers.length?'':'No active customers found.';
    }
    getElement('customer-clear').addEventListener('click',()=>{
        getElement('sale-customer').replaceChildren(new Option('Select customer',''));syncCustomer();changed();getElement('customer-search')?.focus();
    });
    getElement('customer-search')?.addEventListener('input',()=>{
        invalidateCustomerSearch();if(getElement('customer-search').value.trim())customerTimer=setTimeout(()=>Sales.run(()=>lookup(searchCustomers)),300);
    });
    getElement('customer-search')?.addEventListener('keydown',event=>{
        if(event.isComposing || event.keyCode===229)return;
        if(event.key==='Escape'){event.preventDefault();invalidateCustomerSearch();return;}
        if(['ArrowDown','ArrowUp'].includes(event.key)&&customerMatches.length&&!getElement('customer-results').hidden){
            event.preventDefault();customerActive=customerActive<0?(event.key==='ArrowDown'?0:customerMatches.length-1):(customerActive+(event.key==='ArrowDown'?1:-1)+customerMatches.length)%customerMatches.length;
            const rows=getElement('customer-results').children;Array.from(rows).forEach((row,index)=>row.setAttribute('aria-selected',String(index===customerActive)));
            getElement('customer-search').setAttribute('aria-activedescendant',rows[customerActive].id);rows[customerActive].scrollIntoView({block:'nearest'});
        }
        if(event.key==='Enter'){
            event.preventDefault();clearTimeout(customerTimer);
            if(customerMatches.length && !getElement('customer-results').hidden)
                chooseCustomer(customerMatches[customerActive>=0?customerActive:0]);
            else Sales.run(()=>lookup(searchCustomers));
        }
    });
    document.addEventListener('pointerdown',event=>{if(!getElement('customer-picker').contains(event.target))invalidateCustomerSearch();});
    getElement('customer-picker').addEventListener('focusout',()=>{queueMicrotask(()=>{if(!getElement('customer-picker').contains(document.activeElement))invalidateCustomerSearch();});});
    getElement('sale-customer').addEventListener('change',()=>{syncCustomer();changed();getElement('sale-scan')?.focus();});
    workspace.addEventListener('billing-customer-opening',event=>{if(pendingSale||isSavingSale||isPreparingSale||recoveryBlocked||activeLookups)event.preventDefault();});
    workspace.addEventListener('billing-customer-selected',event=>chooseCustomer(event.detail));
    workspace.addEventListener('billing-customer-find',event=>{
        getElement('sale-customer').replaceChildren(new Option('Select customer',''));syncCustomer();changed();
        const search=getElement('customer-search');if(search){search.value=event.detail;search.focus();Sales.run(()=>lookup(searchCustomers));}
    });
    function syncInvoiceDiscount() {
        const field=getElement('sale-discount'),reason=getElement('invoice-discount-reason');
        field.readOnly=!billingPolicy.canDiscount;
        if(field.dataset.numericInvalid!=='true'){field.dataset.committedNumber=field.value;field.setAttribute('aria-invalid','false');getElement('invoice-discount-error').textContent=calculationState==='error'&&/invoice discount|eligible item amount/i.test(calculationError)?calculationError:'';}
        reason.parentElement.hidden=!invoiceReasonRequired()&&field.dataset.numericInvalid!=='true';
        if(document.activeElement!==reason)reason.value=getElement('adjustment-reason').value;
    }
    getElement('sale-discount').addEventListener('input',()=>{
        const field=getElement('sale-discount'),raw=field.value.trim(),value=Number(raw);
        const valid=/^(\d+(\.\d{0,2})?|\.\d{1,2})$/.test(raw)&&moneyValid(value)&&billingPolicy.canDiscount;
        field.dataset.numericInvalid=String(!valid);field.setAttribute('aria-invalid',String(!valid));
        getElement('invoice-discount-error').textContent=valid?'':'Enter a nonnegative discount with at most two decimals.';
        if(valid)field.dataset.committedNumber=String(value);
        changed();
    });
    getElement('sale-discount').addEventListener('blur',()=>{
        const field=getElement('sale-discount');
        if(field.value.trim()===''){field.value='0';field.dispatchEvent(new Event('input'));}
    });
    getElement('invoice-discount-reason').addEventListener('input',()=>{
        getElement('adjustment-reason').value=getElement('invoice-discount-reason').value;changed();
    });
    getElement('calculate-change').addEventListener('change',()=>{
        if(!getElement('calculate-change').checked)getElement('cash-received').value='';
        balance();
    });
    getElement('billing-retry-calculation').addEventListener('click',()=>Sales.run(recalculate));
    getElement('payment-mode').addEventListener('change', mode);
    getElement('refresh-quote').addEventListener('click', () => {
        return Sales.run(recalculate);
    });
    getElement('use-split-payment').addEventListener('change', () => {
        return Sales.run(async () => {
            if (isSplitPayment()) {
                await recalculate();
                if (!quoteIsCurrent()) {
                    getElement('use-split-payment').checked = false;
                    throw new Error('Add items first.');
                }
                if (!payments.length) {
                    payments.push({
                        paymentMode: getElement('payment-mode').value,
                        amount: priceQuote.totalAmount,
                        referenceNumber: getElement('payment-reference').value || null,
                        ...(getElement('payment-mode').value==='CARD' && getElement('payment-card-type').value ? {cardType:getElement('payment-card-type').value}: {})
                    });
                }
            }
            renderPayments();
        });
    });
    getElement('add-payment').addEventListener('click', () => {
        return Sales.run(async () => {
            await recalculate();
            if (!priceQuote) {
                throw new Error('Add items first.');
            }
            if (payments.length >= 20) {
                throw new Error('Use at most 20 payment entries.');
            }
            payments.push({
                paymentMode: 'CASH',
                amount: Math.max(
                    0,
                    Number(
                        (
                            priceQuote.totalAmount -
                            payments.reduce((value, value2) => {
                                return value + value2.amount;
                            }, 0)
                        ).toFixed(2)
                    )
                ),
                referenceNumber: null
            });
            renderPayments();
        });
    });
    function pendingState() {
        getElement('complete-sale').setAttribute('aria-busy',String(isSavingSale||isPreparingSale));
        getElement('sale-form').dataset.saving=String(!!pendingSale||isSavingSale||isPreparingSale);
        getElement('sale-editor').disabled =
            !!pendingSale ||
            isSavingSale ||
            isPreparingSale ||
            recoveryBlocked ||
            window.StockFlowLocation <= 0;
        getElement('pending-sale').hidden = !pendingSale;
        getElement('retry-sale').disabled = isSavingSale;
        getElement('confirm-payment').disabled = isSavingSale || isPreparingSale;
    }
    function manualElectronicPayments(payload) {
        return payload.payments.filter((payment) => {
            return (
                ['UPI', 'CARD', 'BANK_TRANSFER'].includes(payment.paymentMode) && payment.amount > 0
            );
        });
    }
    function requiresManualPaymentConfirmation(payload) {
        return manualElectronicPayments(payload).length > 0;
    }
    function buildPaymentConfirmation(payload, split) {
        const electronic = manualElectronicPayments(payload);
        const labels = { UPI: 'UPI', CARD: 'Card', BANK_TRANSFER: 'Bank Transfer' };
        const money = (amount) =>
            new Intl.NumberFormat('en-IN', {
                style: 'currency',
                currency: 'INR'
            }).format(amount);
        const components = getElement('payment-confirmation-components');
        components.replaceChildren();
        components.hidden = !split;
        getElement('payment-confirmation-title').textContent = split
            ? 'Confirm Electronic Payments'
            : 'Confirm ' +
              (electronic[0].paymentMode==='CARD' && electronic[0].cardType ? (electronic[0].cardType==='DEBIT'?'Debit Card':'Credit Card') : labels[electronic[0].paymentMode]) +
              (electronic[0].paymentMode === 'BANK_TRANSFER' ? '' : ' Payment');
        if (split) {
            for (const payment of electronic) {
                const row = document.createElement('p');
                row.className = 'd-flex justify-content-between gap-3 mb-2';
                const label = document.createElement('span');
                label.textContent = payment.paymentMode==='CARD' && payment.cardType ? (payment.cardType==='DEBIT'?'Debit Card':'Credit Card') : labels[payment.paymentMode];
                const amount = document.createElement('span');
                amount.textContent = money(payment.amount);
                row.append(label, amount);
                components.append(row);
            }
            const total = document.createElement('p');
            total.className = 'fw-bold border-top pt-2';
            total.textContent =
                'Total to verify: ' +
                money(electronic.reduce((sum, payment) => sum + payment.amount, 0));
            components.append(total);
        }
        const payment = electronic[0];
        getElement('payment-confirmation-message').textContent = split
            ? 'Have you verified that these payments were received?'
            : `Have you verified that the customer paid ${money(payment.amount)} ${payment.paymentMode === 'UPI' ? 'through' : 'by'} ${labels[payment.paymentMode]}?`;
    }
    function showPaymentConfirmationModal(payload, split) {
        buildPaymentConfirmation(payload, split);
        const element = getElement('payment-confirmation-modal');
        const modal = bootstrap.Modal.getOrCreateInstance(element);
        const confirm = getElement('confirm-payment');
        confirm.disabled = true;
        return new Promise((resolve) => {
            let confirmed = false;
            const accept = () => {
                if (confirmed || confirm.disabled) return;
                confirmed = true;
                confirm.disabled = true;
                modal.hide();
            };
            confirm.addEventListener('click', accept);
            element.addEventListener(
                'shown.bs.modal',
                () => {
                    confirm.disabled = false;
                    confirm.focus();
                },
                { once: true }
            );
            element.addEventListener(
                'hidden.bs.modal',
                () => {
                    confirm.removeEventListener('click', accept);
                    resolve(confirmed);
                },
                { once: true }
            );
            modal.show();
        });
    }
    async function post() {
        if (isSavingSale || !pendingSale) {
            return;
        }
        isSavingSale = true;
        pendingState();
        Sales.clear();
        try {
            sessionStorage.setItem(storageKey, JSON.stringify(pendingSale));
            const result = await Sales.api(
                pendingSale.recoverOnly ? '/api/sales/recover' : '/api/sales',
                {
                    method: 'POST',
                    body: JSON.stringify(pendingSale.request)
                    ,headers:{'X-Draft-Editor':editorId,'X-Draft-Revision':String(draftRevision)}
                }
            );
            if (!Number.isSafeInteger(result?.saleId) || result.saleId < 1) {
                throw new Error('The sale result could not be confirmed. Retry the saved sale.');
            }
            sessionStorage.removeItem(storageKey);
            localStorage.removeItem(draftKey);
            clearTimeout(draftSaveTimer);
            if (pendingSale.recoverOnly) {
                sessionStorage.removeItem('stockflow-pending-sale');
            }
            window.StockFlowUnsaved?.markSaved(getElement('sale-form'));
            draftStatus='COMPLETED';
            pendingSale = null;
            clearTimeout(quoteTimer);
            getElement('sale-form').hidden = true;
            getElement('billing-success').hidden = false;

            getElement('billing-draft-reference').textContent='Sale #'+result.saleId;
            getElement('billing-result').textContent =
                'Sale #' + result.saleId + ' saved. Stock and payment are recorded.';
            if (workspace.dataset.canInvoice === 'true') {
                getElement('billing-invoice').href = '/Sales/Invoice/' + result.saleId;
                getElement('billing-invoice').hidden = false;
                location.assign(getElement('billing-invoice').href);
            }
        } catch (error) {
            if (!pendingSale?.recoverOnly && [400, 422].includes(error.status)) {
                sessionStorage.removeItem(storageKey);
                pendingSale = null;
            }
            Sales.error(error);
        } finally {
            isSavingSale = false;
            pendingState();
        }
    }
    getElement('sale-form').addEventListener('submit', (event) => {
        event.preventDefault();
        Sales.run(async () => {
            if (pendingSale || isSavingSale || isPreparingSale || recoveryBlocked) {
                return;
            }
            if (activeLookups) {
                throw new Error('Wait for item and customer lookups to finish.');
            }
            validateItems(false);
            if(!getElement('sale-customer').value){getElement('customer-search-status').textContent='Select or add a registered customer before completing the sale.';getElement('customer-search')?.focus();throw new Error('Select or add a registered customer before completing the sale.');}
            isPreparingSale = true;
            pendingState();
            clearTimeout(quoteTimer);
            let requiredReason = null;
            try {
                await recalculate();
                if (!quoteIsCurrent()) {
                    throw new Error('Confirm the sale total before taking payment.');
                }
                validateItems();
                const payload = request();
                const cash = cashChange();
                if (
                    cash.due > 0 &&
                    (!moneyValid(cash.received) ||
                        Math.round(cash.received * 100) < Math.round(cash.due * 100))
                ) {
                    throw new Error('Cash received must cover the cash payment.');
                }
                if(cart.some(lineMissingReason))throw new Error('Complete the required adjustment reasons.');
                if(invoiceReasonRequired()&&!getElement('adjustment-reason').value.trim())throw new Error('Enter the invoice discount exception reason.');
                validateCollectedPayments(payload.payments);
                if(payload.payments.some(p=>p.paymentMode==='CARD'&&!p.cardType))throw new Error('Choose Debit Card or Credit Card.');
                if (
                    payload.payments.some((value) => {
                        return (
                            !moneyValid(value.amount) ||
                            value.amount <= 0 ||
                            value.referenceNumber?.length > 100
                        );
                    }) ||
                    Math.round(
                        payload.payments.reduce((value, value2) => {
                            return value + value2.amount;
                        }, 0) * 100
                    ) !== Math.round(priceQuote.totalAmount * 100)
                ) {
                    if(isSplitPayment())getElement('billing-split-payment').hidden=false;
                    throw new Error(
                        'Payment amounts must be positive, have at most two decimal places, and match the total.'
                    );
                }
                // Confirm the validated snapshot while the editor remains locked. Cancellation
                // must happen before creating a pending sale or entering the submission flow.
                if (
                    requiresManualPaymentConfirmation(payload) &&
                    !(await showPaymentConfirmationModal(payload, isSplitPayment()))
                ) {
                    return;
                }
                payload.payments.forEach(p=>{if(p.paymentMode!=='CASH')p.collectionConfirmed=true;});
                await persistDraft();
                pendingSale = {
                    request: {
                        ...payload,
                        requestId: draftId
                    },
                    cart,
                    customerLabel: getElement('sale-customer').selectedOptions[0]?.textContent
                };
                try {
                    sessionStorage.setItem(storageKey, JSON.stringify(pendingSale));
                } catch {
                    pendingSale = null;
                    throw new Error(
                        'Browser storage is unavailable. Enable it before billing so interrupted sales can be retried safely.'
                    );
                }
                await post();
            } finally {
                isPreparingSale = false;
                pendingState();
                if (requiredReason) {
                    // The fieldset was disabled during the final quote. Reveal and
                    // focus the reason only after restoring its interactive state.
                    updateAdjustmentUI();
                    let feedback = getElement('adjustment-reason-error');
                    if (!feedback) {
                        feedback = document.createElement('p');
                        feedback.id = 'adjustment-reason-error';
                        feedback.className = 'small text-danger mt-1';
                        feedback.setAttribute('role', 'alert');
                        feedback.textContent = 'Enter a reason for this discount or price change before completing the sale.';
                        requiredReason.after(feedback);
                        const descriptions = requiredReason.getAttribute('aria-describedby') || '';
                        requiredReason.setAttribute('aria-describedby', (descriptions + ' ' + feedback.id).trim());
                        requiredReason.addEventListener('input', () => {
                            if (requiredReason.value.trim()) {
                                requiredReason.classList.remove('is-invalid');
                                requiredReason.removeAttribute('aria-invalid');
                                feedback.hidden = true;
                                if (getElement('sale-message').textContent === 'Enter a reason for the discount or price change.') {
                                    Sales.clear();
                                }
                            }
                        });
                    }
                    feedback.hidden = false;
                    requiredReason.classList.add('is-invalid');
                    requiredReason.setAttribute('aria-invalid', 'true');
                    requiredReason.focus({ preventScroll: true });
                    requiredReason.scrollIntoView({ behavior: 'instant', block: 'center', inline: 'nearest' });
                }
            }
        });
    });
    getElement('retry-sale').addEventListener('click', () => {
        return Sales.run(post);
    });
    getElement('billing-another').addEventListener('click', () => {
        return location.reload();
    });
    getElement('sale-form').addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && ['INPUT', 'SELECT'].includes(event.target.tagName)) {
            event.preventDefault();
        }
    });
    getElement('sale-form').addEventListener('input', saveDraftOnThisDevice);
    getElement('sale-form').addEventListener('change', saveDraftOnThisDevice);
    getElement('cash-received').addEventListener('input', () => {
        const field=getElement('cash-received');
        const trimmed=field.value.trim();
        if(field.value!==trimmed)field.value=trimmed;
        balance();
    });
    getElement('payment-reference').addEventListener('input',updateSummary);
    getElement('use-trade-in')?.addEventListener('change', () => {
        getElement('trade-in-fields').hidden = !getElement('use-trade-in').checked;
        changed();
    });
    getElement('trade-in-allowance')?.addEventListener('input', changed);
    async function loadPolicy() {
        billingPolicy = await Sales.api('/api/sales/billing/policy');
        options();
        getElement('discount-policy-status').textContent =
            'Price reductions above ' +
            billingPolicy.discountLimitPercent +
            '% require discount approval permission. Price changes and discounts require a reason.';
        pendingApprovalCount = 0;
        if(billingPolicy.canApproveDiscount){
            try{pendingApprovalCount=(await Sales.api('/api/sales/billing/approvals')).length;}
            catch{pendingApprovalCount=null;} // Keep Review reachable without blocking paid checkout on an optional inbox failure.
        }
        updateAdjustmentUI();
    }
    window.addEventListener('focus',()=>{if(cart.length&&!isSavingSale&&!pendingSale&&!isPreparingSale&&!numericInputsInvalid())Sales.run(recalculate);});
    let approvalDialog;
    function collectExceptionReasons(){
        if(!approvalDialog){approvalDialog=document.createElement('dialog');approvalDialog.className='billing-adjustment-dialog';approvalDialog.dataset.sfDialog='medium';approvalDialog.id='billing-approval-dialog';workspace.append(approvalDialog);}
        approvalDialog.replaceChildren();const heading=document.createElement('h2');heading.id='billing-approval-title';heading.textContent='Manager Approval Required';approvalDialog.setAttribute('aria-labelledby',heading.id);approvalDialog.append(heading);
        for(const [index,item] of cart.entries()){
            const required=lineRequirements(item,index);if(!required.discount&&!required.price)continue;
            const summary=document.createElement('p');summary.textContent=item.productName+' | Qty: '+item.quantity+' | Discount: '+(item.discountPercent!=null?item.discountPercent+'% · ':'')+formatMoney(item.discountAmount);approvalDialog.append(summary);
            if(required.discount&&!lineReason(item))addReason('Discount Reason','',value=>{item.adjustmentReason=value;});
            else if(required.discount)addSavedReason('Discount reason',lineReason(item));
            if(required.price&&!(item.priceAdjustmentReason?.trim()||lineReason(item)))addReason('Price Change Reason','',value=>{item.priceAdjustmentReason=value;});
            else if(required.price)addSavedReason('Price change reason',item.priceAdjustmentReason?.trim()||lineReason(item));
        }
        if(Number(getElement('sale-discount').dataset.committedNumber||0)>0){const amount=document.createElement('p');amount.textContent='Invoice Discount: '+formatMoney(Number(getElement('sale-discount').dataset.committedNumber));approvalDialog.append(amount);}
        if(invoiceReasonRequired()){
            const saved=getElement('adjustment-reason').value.trim();
            if(!saved)addReason('Invoice Discount Reason','',value=>{getElement('adjustment-reason').value=value;});
            else addSavedReason('Invoice discount reason',saved);
        }
        const status=document.createElement('p');status.textContent='A different authorized manager must review this request.';approvalDialog.append(status);
        function addSavedReason(label,value){const saved=document.createElement('p');saved.textContent=label+': '+value;approvalDialog.append(saved);}
        function addReason(labelText,value,commit){const label=document.createElement('label');label.textContent=labelText;const field=document.createElement('input');field.className='form-control';field.maxLength=500;field.value=value;field.required=true;field.addEventListener('input',()=>{commit(field.value);});label.append(field);approvalDialog.append(label);}
        return new Promise(resolve=>{
            let accepted=false;
            approvalDialog.append(button('Cancel',()=>approvalDialog.close()),button('Request Approval',()=>{if([...approvalDialog.querySelectorAll('input')].every(field=>field.reportValidity())){accepted=true;approvalDialog.close();}}));
            approvalDialog.addEventListener('close',()=>{changed();resolve(accepted);},{once:true});approvalDialog.showModal();
        });
    }
    getElement('request-discount-approval').addEventListener('click', () => {
        return Sales.run(async () => {
            if (pendingSale || isSavingSale || isPreparingSale || activeLookups) {
                throw new Error('Wait for current operations to finish.');
            }
            if(priceQuote?.adjustmentReview?.approvalStatus==='PENDING'){await recalculate();return;}
            if(!await collectExceptionReasons())return;
            await recalculate();
            await persistDraft();
            const payload = request();
            payload.payments = [];
            await Sales.api('/api/sales/billing/drafts/' + draftId + '/approval', {
                method: 'POST',
                body: JSON.stringify(payload)
            });
            await recalculate();
        });
    });
    getElement('show-discount-approvals').addEventListener('click', () => {
        return Sales.run(async () => {
            const rows = await Sales.api('/api/sales/billing/approvals');
            pendingApprovalCount = rows.length;
            const host = getElement('discount-approvals');
            host.hidden = false;
            host.replaceChildren();
            if (!rows.length) {
                host.textContent = 'No pending requests.';
            }
            for (const row of rows) {
                const summary =
                    typeof row.summary === 'string' ? JSON.parse(row.summary) : row.summary;
                const card = document.createElement('div');
                card.className = 'border rounded p-2 mb-2';
                const text = document.createElement('p');
                text.textContent =
                    row.cashier +
                    ' · Customer: ' +
                    (summary.customerId || 'Customer not selected') +
                    ' · Invoice discount: ' +
                    Sales.money(summary.discountAmount) +
                    ' · Reason: ' +
                    summary.adjustmentReason;
                const lines = document.createElement('p');
                lines.textContent = (row.items || [])
                    .map((value) => {
                        return (
                            value.name +
                            ': ' +
                            value.quantity +
                            ' × ' +
                            Sales.money(value.sellingPrice) +
                            ', catalog ' +
                            Sales.money(value.catalogPrice) +
                            ', discount ' +
                            Sales.money(value.discountAmount)
                        );
                    })
                    .join('; ');
                card.append(
                    text,
                    lines,
                    button('Approve these amounts', () => {
                        return Sales.run(async () => {
                            await Sales.api('/api/sales/billing/approvals/' + row.draftId, {
                                method: 'PUT',
                                body: JSON.stringify({
                                    hash: row.hash
                                })
                            });
                            card.remove();pendingApprovalCount=Math.max(0,pendingApprovalCount-1);updateAdjustmentUI();
                        });
                    })
                );
                card.append(button('Reject exception',()=>Sales.run(async()=>{await Sales.api('/api/sales/billing/approvals/'+row.draftId,{method:'PUT',body:JSON.stringify({hash:row.hash,reject:true})});card.remove();pendingApprovalCount=Math.max(0,pendingApprovalCount-1);updateAdjustmentUI();})));
                host.append(card);
            }
        });
    });
    let manualDialog, manualEditIndex=null, manualPreviewVersion=0, manualTimer, manualTaxRules=[];
    const manualRuleText=rule=>rule.ruleName+' · '+rule.hsnSac+' · '+rule.taxRate+'% (#'+rule.ruleId+')';
    function manualRuleId(){
        const text=getElement('manual-rule').value.trim();
        if(!text||text.toLowerCase()==='no gst')return getElement('manual-rule').required?null:0;
        return manualTaxRules.find(rule=>manualRuleText(rule)===text)?.ruleId??null;
    }
    function manualFieldsValid(){
        // Inspect validity without dispatching native invalid events during live preview.
        return !!getElement('manual-name').value.trim() && manualRuleId()!==null
            && [...manualDialog.querySelector('form').elements].every(field=>!field.willValidate||field.validity.valid);
    }
    function manualInput() {
        return {productId:0,variantId:null,productUnitId:null,quantity:Number(getElement('manual-quantity').value),
            sellingPrice:Number(getElement('manual-price').value),discountAmount:0,discountPercent:Number(getElement('manual-discount').value),
            manualItem:{name:getElement('manual-name').value.trim(),description:getElement('manual-note').value.trim()||null,
                mrp:getElement('manual-mrp').value===''?null:Number(getElement('manual-mrp').value),taxRuleId:manualRuleId()}};
    }
    async function previewManual() {
        const version=++manualPreviewVersion, preview=getElement('manual-preview'),submit=getElement('manual-submit');
        submit.disabled=true;preview.textContent='Checking line total…';
        if(!manualFieldsValid()){
            preview.textContent=getElement('manual-rule').value.trim()&&manualRuleId()===null?'Choose a reviewed GST classification from the suggestions.':'';
            return;
        }
        try{
            const payload={...request(),items:[manualInput()],payments:[],discountAmount:0,tradeIn:null,cashTendered:null};
            const quote=await Sales.api('/api/sales/quote',{method:'POST',body:JSON.stringify(payload)});
            if(version!==manualPreviewVersion||!manualDialog.open)return;
            const line=quote.items[0];preview.textContent='Discount: ₹'+Sales.money(line.discountAmount)+' · Tax: ₹'+Sales.money(line.taxAmount)+' · Line total: ₹'+Sales.money(line.lineTotal);
            submit.disabled=false;
        }catch(error){if(version===manualPreviewVersion)preview.textContent=error.message;}
    }
    async function openManualItem(index=null) {
        if(pendingSale||isSavingSale||isPreparingSale||recoveryBlocked||activeLookups)return;
        if(!manualDialog){
            manualDialog=document.createElement('dialog');manualDialog.className='billing-manual-modal';manualDialog.setAttribute('aria-labelledby','manual-title');manualDialog.dataset.sfDialog='large';
            manualDialog.innerHTML=`<form novalidate><header><h2 id="manual-title">Quick Add Item</h2><button type="button" id="manual-close" class="billing-manual-close sf-dialog-close" aria-label="Close Quick Add Item" title="Cancel"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg></button><p>Add an item to this bill without registering a product.</p></header>
            <div class="billing-manual-fields"><label class="manual-wide">Item Name *<input id="manual-name" class="form-control" required maxlength="150" /></label>
            <label>Quantity *<input id="manual-quantity" class="form-control" type="number" min="1" max="10000" step="1" required value="1" /></label>
            <label>Selling Price (₹) *<input id="manual-price" class="form-control" type="number" min="0.01" max="9999999999.99" step="0.01" required /></label>
            <label>MRP (₹)<input id="manual-mrp" class="form-control" type="number" min="0.01" max="9999999999.99" step="0.01" /></label>
            <label>Discount (%)<input id="manual-discount" class="form-control" type="number" min="0" max="100" step="0.01" required value="0" /></label>
            <label class="manual-wide"><span id="manual-rule-label">GST Classification (optional)</span><input id="manual-rule" type="text" class="form-control" list="manual-rule-suggestions" autocomplete="off" placeholder="No GST, or type to search reviewed classifications" aria-describedby="manual-classification" /><datalist id="manual-rule-suggestions"></datalist></label>
            <p id="manual-classification" class="manual-wide small"></p>
            <label class="manual-wide">Notes (optional)<textarea id="manual-note" class="form-control" rows="2" maxlength="1000"></textarea></label></div>
            <p id="manual-preview" role="status"></p><footer><button type="button" class="btn btn-outline-secondary" id="manual-cancel">Cancel</button><button id="manual-submit" class="btn btn-primary" disabled>Add to Bill</button></footer></form>`;
            workspace.append(manualDialog);manualDialog.querySelector('form').addEventListener('input',()=>{++manualPreviewVersion;getElement('manual-submit').disabled=true;clearTimeout(manualTimer);manualTimer=setTimeout(previewManual,220);});
            getElement('manual-close').onclick=getElement('manual-cancel').onclick=()=>manualDialog.close();manualDialog.addEventListener('close',()=>{++manualPreviewVersion;clearTimeout(manualTimer);getElement('sale-scan')?.focus();});
            manualDialog.querySelector('form').onsubmit=event=>{event.preventDefault();if(getElement('manual-submit').disabled||!manualFieldsValid())return;
                const input=manualInput(),row={...input,productName:input.manualItem.name,hasProductImage:false,sku:null,currentStock:null,catalogPrice:input.sellingPrice,mrp:input.manualItem.mrp};
                if(manualEditIndex===null)cart.push(row);else cart[manualEditIndex]=row;
                renderCart();manualDialog.close();};
        }
        manualEditIndex=index;manualDialog.querySelector('form').reset();getElement('manual-submit').disabled=true;
        getElement('manual-discount').readOnly=!billingPolicy.canDiscount;
        getElement('manual-submit').textContent=index===null?'Add to Bill':'Update Item';
        const row=index===null?null:cart[index];
        if(row){getElement('manual-name').value=row.manualItem.name;getElement('manual-quantity').value=row.quantity;getElement('manual-price').value=row.sellingPrice;getElement('manual-mrp').value=row.manualItem.mrp??'';getElement('manual-discount').value=row.discountPercent??0;getElement('manual-note').value=row.manualItem.description||'';}
        manualDialog.showModal();getElement('manual-name').focus();getElement('manual-preview').textContent='Loading reviewed classifications…';
        try{
            const context=await gstContextPromise;if(!manualDialog.open)return;
            if(!context)throw Error('Unable to confirm this shop’s billing setup. Please reload and try again.');
            const requiresGst=context.mapped&&context.registrationType!=='UNREGISTERED';
            const rules=await Sales.api('/api/sales/manual-tax-rules');if(!manualDialog.open)return;
            manualTaxRules=rules;
            const select=getElement('manual-rule');select.required=requiresGst;
            select.placeholder=requiresGst?'Type to search reviewed GST classifications':'No GST, or type to search reviewed classifications';
            const suggestions=getElement('manual-rule-suggestions');suggestions.replaceChildren();
            if(!requiresGst)suggestions.append(new Option('No GST','No GST'));
            getElement('manual-rule-label').textContent=requiresGst?'GST Classification *':'GST Classification (optional)';
            for(const rule of rules)suggestions.append(new Option(manualRuleText(rule),manualRuleText(rule)));
            select.oninput=()=>{const rule=rules.find(r=>r.ruleId===manualRuleId());getElement('manual-classification').textContent=rule?'HSN: '+rule.hsnSac+' · Unit: '+rule.uqc:requiresGst?'This shop issues GST documents. Choose a reviewed classification.':'No GST selected. No tax will be added to this item.';};
            if(row){const rule=rules.find(r=>r.ruleId===row.manualItem.taxRuleId);select.value=rule?manualRuleText(rule):row.manualItem.taxRuleId>0?'Unavailable saved classification #'+row.manualItem.taxRuleId:'';}
            select.oninput();
            if(!rules.length)getElement('manual-classification').textContent=requiresGst?'No reviewed GST rules are available. Ask the owner to configure a classification in GST Settings.':'No GST selected. No tax will be added to this item.';
            await previewManual();
        }catch(error){getElement('manual-preview').textContent=error.message;}
    }
    getElement('quick-add-item')?.addEventListener('click',()=>Sales.run(()=>openManualItem()));

    function validateSavedBill(saved) {
        if (
            !saved ||
            !Array.isArray(saved.cart) ||
            saved.cart.length > 500 ||
            typeof saved.draftId !== 'string' ||
            !Number.isFinite(saved.savedAt) ||
            saved.cart.some(item => !item || typeof item.productName !== 'string' || !Number.isFinite(item.quantity) || !Number.isFinite(item.sellingPrice) || !Number.isFinite(item.discountAmount)) ||
            (saved.payments != null && !Array.isArray(saved.payments))
        ) {
            throw new Error('Saved bill has an invalid or incompatible structure. Its server copy is retained.');
        }
    }
    function restore(saved, deferSave = false) {
        validateSavedBill(saved);
        isRestoringDraft = true;
        try {
            crmEnquiryId = Number.isSafeInteger(saved.crmEnquiryId)&&saved.crmEnquiryId>0?saved.crmEnquiryId:null;
            sourceQuotationId=saved.sourceQuotationId||null;sourceQuotationRevision=saved.sourceQuotationRevision||null;sourceQuotationVersion=saved.sourceQuotationVersion||null;
            sourceQuotationNumber=saved.sourceQuotationNumber||null;sourceQuotationNotice=saved.sourceQuotationNotice||'';
            draftId = saved.draftId;draftNumber=/^DRF-[0-9]{4,}$/.test(saved.draftNumber)?saved.draftNumber:null;showDraftReference();
            draftRevision=saved.revision??saved.draftRevision??0;draftStatus=saved.draftStatus||'NEW';
            pricesIncludeTax = saved.pricesIncludeTax === true;
            if(getElement('sale-gst-pos'))getElement('sale-gst-pos').value=saved.gstPlaceOfSupplyStateCode||'';
            if(getElement('sale-gst-delivery'))getElement('sale-gst-delivery').value=saved.gstDeliveryAddress||'';
            if(getElement('sale-gst-recipient-details'))getElement('sale-gst-recipient-details').checked=!!saved.gstRecipientDetailsRequested;
            priceQuote=null;quoteRevision=-1;
            cart = saved.cart;
            if(saved.reason&&!cart.some(item=>item.adjustmentReason||item.legacyAdjustmentReason))cart.forEach(item=>{if(item.discountAmount>0||item.taxOverride!=null||item.sellingPrice!==item.catalogPrice)item.legacyAdjustmentReason=saved.reason;});
            payments = saved.payments || [];
            getElement('use-split-payment').checked = false;
            if(saved.split)getElement('draft-status').textContent='This held bill used split payment. Choose one payment method covering the full bill.';
            getElement('sale-discount').value = saved.discount || 0;delete getElement('sale-discount').dataset.committedNumber;delete getElement('sale-discount').dataset.numericInvalid;getElement('calculate-change').checked=false;
            getElement('sale-notes').value = saved.notes || '';
            const legacyCredit=saved.mode==='CREDIT'||payments.some(p=>p.paymentMode==='CREDIT');
            getElement('payment-mode').value = saved.mode==='CREDIT' ? '' : saved.mode || 'CASH';
            if(legacyCredit){getElement('billing-payment-warning').textContent='This saved bill used Pay Later. Choose collected payment methods covering the full amount before completing.';getElement('billing-payment-warning').hidden=false;}
            getElement('payment-card-type').value = saved.mode==='CARD' && ['DEBIT','CREDIT'].includes(saved.cardType) ? saved.cardType : '';
            workspace.dispatchEvent(new Event('billing-notes-restored'));
            getElement('payment-reference').value = saved.reference || '';
            getElement('cash-received').value = String(saved.cash || '').trim();
            getElement('adjustment-reason').value = saved.reason || '';
            getElement('sale-customer').value = '';
            if (Number.isSafeInteger(Number(saved.customerId)) && Number(saved.customerId)>0) {
                getElement('sale-customer').add(
                    new Option(
                        saved.customerLabel || 'Selected customer',
                        saved.customerId,
                        true,
                        true
                    )
                );
            }
            if (getElement('use-trade-in')) {
                getElement('use-trade-in').checked = !!saved.tradeIn;
                getElement('trade-in-fields').hidden = !saved.tradeIn;
                getElement('trade-in-options').open = !!saved.tradeIn;
                getElement('trade-in-type').value = saved.tradeIn?.itemType || '';
                getElement('trade-in-model').value = saved.tradeIn?.brandModel || '';
                getElement('trade-in-serial').value = saved.tradeIn?.serialNumber || '';
                getElement('trade-in-allowance').value = saved.tradeIn?.allowanceAmount || 0;
            }
            renderCart();
            renderPayments();
            mode();
        } finally {
            isRestoringDraft = false;
        }
        if (!deferSave) saveDraftOnThisDevice();
        syncCustomer();
        
        getElement('draft-status').textContent =
            'Bill restored. Stock, current prices and warranty are being checked.';
    }
    const savedDrawer=getElement('saved-bills-drawer'), savedList=getElement('saved-bills-list');
    const resumeDialog=getElement('resume-bill-confirm');
    let savedPage=1,savedSearch='',savedRequest=0,savedBusy=false,savedSearchTimer,recoveryView=false,pendingSwitch=null;
    const drawerFeedback=(message)=>{getElement('saved-bills-feedback').textContent=message;};
    function closeSavedBills(scan=false){savedDrawer.dataset.scanFocus=String(scan);savedDrawer.close();}
    getElement('saved-bills-close').onclick=()=>closeSavedBills();
    savedDrawer.addEventListener('cancel',event=>{event.preventDefault();if(!savedBusy)closeSavedBills();});
    savedDrawer.addEventListener('keydown',event=>{
        if(event.key!=='Tab')return;
        const controls=[...savedDrawer.querySelectorAll('button:not(:disabled),input:not(:disabled),a[href],[tabindex="0"]')].filter(el=>el.getClientRects().length);
        const first=controls[0],last=controls.at(-1);
        if(!first){event.preventDefault();return;}
        if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}
        else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}
    });
    savedDrawer.addEventListener('click',event=>{if(event.target===savedDrawer){const b=savedDrawer.getBoundingClientRect();if(event.clientX<b.left&&!savedBusy)closeSavedBills();}});
    savedDrawer.addEventListener('close',()=>{requestAnimationFrame(()=>{(savedDrawer.dataset.scanFocus==='true'?getElement('sale-scan'):getElement('show-held'))?.focus();savedDrawer.dataset.scanFocus='false';});});
    function savedTime(value){
        const date=new Date(value);if(!Number.isFinite(date.getTime()))return 'Saved time unavailable';
        // Match the configured shop timezone used by sales reports.
        const today=new Date(),yesterday=new Date();yesterday.setDate(today.getDate()-1);
        const options={timeZone:savedDrawer.dataset.timeZone||'Asia/Kolkata'},key=d=>d.toLocaleDateString('en-CA',options);
        const day=key(date)===key(today)?'Today':key(date)===key(yesterday)?'Yesterday':date.toLocaleDateString('en-IN',options);
        return day+', '+date.toLocaleTimeString('en-IN',{hour:'numeric',minute:'2-digit',timeZone:savedDrawer.dataset.timeZone||'Asia/Kolkata'});
    }
    function savedEmpty(title,message,clear=false){
        const box=document.createElement('div');box.className='billing-saved-empty';
        const icon=savedIcon('receipt');icon.setAttribute('width','40');icon.setAttribute('height','40');
        const heading=document.createElement('h3');heading.textContent=title;
        const text=document.createElement('p');text.textContent=message;box.append(icon,heading,text);
        if(clear)box.append(button('Clear Search',()=>{getElement('saved-bills-search').value='';savedSearch='';savedPage=1;loadSavedBills();}));
        savedList.replaceChildren(box);
    }
    function savedIcon(kind){
        const icon=document.createElementNS('http://www.w3.org/2000/svg','svg');
        icon.setAttribute('viewBox','0 0 24 24');icon.setAttribute('width','16');icon.setAttribute('height','16');icon.setAttribute('fill','none');icon.setAttribute('stroke','currentColor');icon.setAttribute('stroke-width','1.7');icon.setAttribute('aria-hidden','true');
        const paths={resume:'<path d="m9 5 10 7-10 7Z"/>',discard:'<path d="M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7"/>',items:'<path d="m12 3 9 5v8l-9 5-9-5V8Zm-9 5 9 5 9-5m-9 5v8"/>',clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',receipt:'<path d="M5 3h14v19l-3-2-4 2-4-2-3 2ZM8 7h8M8 11h8M8 15h5"/>'};icon.innerHTML=paths[kind];return icon;
    }
    async function loadSavedBills(){
        const revision=++savedRequest;drawerFeedback('Loading saved bills...');
        try{
            const response=await Sales.api('/api/sales/billing/drafts?paged=true&recovery='+recoveryView+'&page='+savedPage+'&search='+encodeURIComponent(savedSearch));
            if(revision!==savedRequest)return;
            const list=Array.isArray(response)?response:response.items,total=Array.isArray(response)?response.length:response.total;
            if(!Array.isArray(list))throw Error('Saved bills could not be loaded.');
            if(!list.length&&savedPage>1){savedPage=Math.max(1,Math.ceil(total/25));return loadSavedBills();}
            getElement('saved-bills-count').textContent=total+' '+(total===1?'Draft':'Drafts');
            getElement('saved-bills-prev').closest('.billing-saved-pagination').hidden=!StockFlowPagination.shouldShow(total);
            getElement('saved-bills-page').textContent='Page '+savedPage+' of '+Math.max(1,Math.ceil(total/25));
            getElement('saved-bills-prev').disabled=savedPage<=1;getElement('saved-bills-next').disabled=savedPage*25>=total;
            savedList.replaceChildren();savedList.scrollTop=0;drawerFeedback('');
            if(!list.length)savedEmpty(savedSearch?'No matching bills':'No saved bills',savedSearch?'Try another customer name, mobile number or draft reference.':'Bills you hold will appear here so you can resume them later.',!!savedSearch);
            for(const bill of list){
                const card=document.createElement('article');card.className='billing-saved-card';
                const top=document.createElement('div');top.className='billing-saved-card-top';
                const name=bill.customerName?.split(' / ')[0]||'Customer not selected';
                const avatar=document.createElement('span');avatar.className='billing-saved-avatar';avatar.setAttribute('aria-hidden','true');avatar.textContent=bill.customerName?name.split(/\s+/).filter(Boolean).slice(0,2).map(n=>n[0]).join('').toUpperCase():'?';
                const identity=document.createElement('div');identity.className='billing-saved-customer';const strong=document.createElement('strong');strong.textContent=name;
                const displayNumber=bill.draftNumber||'Reference unavailable';
                const ref=document.createElement('div');ref.className='billing-saved-reference';ref.textContent=(recoveryView?'Recovery':'Draft')+' \u00b7 '+displayNumber;
                if(bill.creationTimeSource==='UNKNOWN'){const flag=document.createElement('div');flag.className='billing-saved-reference';flag.textContent='Original creation time unknown';identity.append(strong,ref,flag);}else identity.append(strong,ref);
                const amount=document.createElement('span');amount.className='billing-saved-total';amount.textContent=Number.isFinite(bill.estimatedTotal)?new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(bill.estimatedTotal):'\u2014';amount.title=Number.isFinite(bill.estimatedTotal)?'Provisional estimate; checked again before checkout':'Estimate unavailable for this older draft; resume to calculate';top.append(avatar,identity,amount);
                const meta=document.createElement('div');meta.className='billing-saved-meta';const count=document.createElement('div');count.textContent=(bill.itemCount??'\u2014')+' '+(bill.itemCount===1?'item':'items')+' \u00b7 '+(bill.units??'\u2014')+' '+(bill.units===1?'unit':'units');const time=document.createElement('div');time.textContent=savedTime(bill.updatedAt);count.prepend(savedIcon('items'));time.prepend(savedIcon('clock'));meta.append(count,time);
                const actions=document.createElement('div');actions.className='billing-saved-actions';
                const discard=button('Discard',()=>savedAction(async()=>{
                    if(!await StockFlowDialog.confirm(displayNumber+': This unfinished bill will be permanently removed. Completed invoices and stock records will not be affected.',{kind:'discard',title:'Discard saved bill?',confirmText:'Discard Bill'}))return;
                    clearTimeout(draftSaveTimer);await draftSaveQueue.catch(()=>{});
                    await draftCommand('discard',bill.draftId,bill.revision);
                    if(bill.draftId===draftId){localStorage.removeItem(draftKey);window.StockFlowUnsaved?.markSaved(getElement('sale-form'));location.reload();return;}
                    await loadSavedBills();drawerFeedback('Saved bill discarded.');
                }));discard.className='btn btn-outline-secondary';
                const resume=button(recoveryView?'Recover Bill':'Resume Bill',()=>savedAction(()=>resumeSavedBill(bill.draftId,displayNumber)));resume.className='btn billing-saved-resume';
                discard.prepend(savedIcon('discard'));resume.prepend(savedIcon('resume'));
                discard.setAttribute('aria-label','Discard draft '+displayNumber);resume.setAttribute('aria-label',(recoveryView?'Recover Bill ':'Resume Bill ')+displayNumber);
                actions.append(discard,resume);card.append(top,meta,actions);savedList.append(card);
            }
        }catch(error){if(revision!==savedRequest)return;savedList.replaceChildren();drawerFeedback(error.message);savedList.append(button('Retry',loadSavedBills));}
    }
    async function savedAction(action){
        if(savedBusy||pendingSale||isSavingSale||isPreparingSale||activeLookups){drawerFeedback('Wait for current operations to finish, or recover the interrupted editing session.');return;}
        const trigger=document.activeElement;
        savedBusy=true;isPreparingSale=true;pendingState();getElement('saved-bills-close').disabled=true;
        getElement('saved-bills-search').disabled=true;
        savedList.querySelectorAll('button').forEach(b=>b.disabled=true);
        try{await action();}catch(error){drawerFeedback(error.message);if(pendingSwitch)getElement('saved-bills-feedback').append(button('Retry Resume',()=>savedAction(()=>resumeSavedBill(pendingSwitch.id,pendingSwitch.source.draftNumber))));}
        finally{savedBusy=false;isPreparingSale=false;pendingState();getElement('saved-bills-close').disabled=false;getElement('saved-bills-search').disabled=false;savedList.querySelectorAll('button').forEach(b=>b.disabled=false);if(savedDrawer.open&&trigger?.isConnected)trigger.focus();}
    }
    function chooseResume(displayNumber){
        return new Promise(resolve=>{
            getElement('resume-bill-message').textContent='Your current unfinished bill contains items or entered details. Choose what to do before opening '+displayNumber+'.';
            const finish=value=>{resumeDialog.close();resolve(value);};
            getElement('resume-bill-cancel').onclick=()=>finish('cancel');getElement('resume-bill-close').onclick=()=>finish('cancel');
            getElement('resume-bill-hold').onclick=()=>finish('hold');getElement('resume-bill-discard').onclick=()=>finish('discard');
            resumeDialog.oncancel=event=>{event.preventDefault();finish('cancel');};resumeDialog.showModal();getElement('resume-bill-cancel').focus();
        });
    }
    async function resumeSavedBill(id,displayNumber){
        clearTimeout(draftSaveTimer);await draftSaveQueue.catch(()=>{});
        if(!pendingSwitch){
            const same=id===draftId;
            const hasWork=!same&&billHasContent();
            const choice=hasWork?await chooseResume(displayNumber):'empty';if(choice==='cancel')return;
            // Validate the target BEFORE changing either server lifecycle or the current editor.
            const saved=await Sales.api('/api/sales/billing/drafts/'+id);validateSavedBill(saved);
            if(saved.draftId!==id)throw Error('The saved bill identity changed. Refresh and try again.');
            if(!same&&billHasContent())await persistDraft(true);
            const local=same?structuredClone(getBillSnapshot()):null;
            let source=saved;
            if(local&&!draftContentsEqual(local,saved)){
                const keep=await StockFlowDialog.confirm('Your device backup differs from the server copy. Restore the device changes after acquiring exclusive ownership? Both copies will be retained on this device.',{title:'Recover device changes?',confirmText:'Restore Device Backup',cancelText:'Use Server Copy'});
                try{localStorage.setItem(draftKey+'.recovery.'+id,JSON.stringify(keep?saved:local));}catch{throw Error('Unable to preserve both recovery copies. Free device storage and retry.');}
                if(keep)source=local;
            }
            pendingSwitch={id,source,body:{editorId,revision:saved.revision,operationId:createRequestId(),recover:recoveryView,currentDraftId:!same&&draftRevision>0?draftId:null,currentRevision:draftRevision,currentAction:choice==='hold'?'HOLD':choice==='discard'?'DISCARD':'RELEASE'}};
        }
        if(pendingSwitch.id!==id)throw Error('Retry the previous resume operation before choosing another bill.');
        try{
            const result=await Sales.api('/api/sales/billing/drafts/'+id+'/resume',{method:'POST',body:JSON.stringify(pendingSwitch.body)});
            const source=pendingSwitch.source;pendingSwitch=null;unconfirmedDraftWrite=null;recoveryBlocked=false;
            restore({...source,...result},true);applyDraftState(result);saveDraftOnThisDevice();closeSavedBills(true);
        }catch(error){if(error.status===409||error.status===400)pendingSwitch=null;else recoveryBlocked=true;throw error;}
    }
    getElement('saved-bills-recovery').onclick=()=>{
        if(savedBusy)return;recoveryView=!recoveryView;savedPage=1;savedSearch='';getElement('saved-bills-search').value='';
        getElement('saved-bills-recovery').textContent=recoveryView?'Show held bills':'Recover interrupted bills';
        getElement('saved-bills-description').textContent=recoveryView?'Recover legacy bills or sessions whose editing lease has expired. Live sessions remain protected.':'Resume a previously saved or held bill.';
        loadSavedBills();
    };
    getElement('saved-bills-search').addEventListener('input',()=>{clearTimeout(savedSearchTimer);savedRequest++;savedSearchTimer=setTimeout(()=>{savedSearch=getElement('saved-bills-search').value.trim();savedPage=1;loadSavedBills();},250);});
    getElement('saved-bills-prev').onclick=()=>{if(!savedBusy){savedPage--;loadSavedBills();}};
    getElement('saved-bills-next').onclick=()=>{if(!savedBusy){savedPage++;loadSavedBills();}};
    getElement('show-held').addEventListener('click',()=>{
        if(isPreparingSale||isSavingSale){Sales.error(Error('Wait for the current operation to finish.'));return;}
        if(savedDrawer.open)return;savedPage=1;savedSearch='';getElement('saved-bills-search').value='';savedDrawer.showModal();getElement('saved-bills-search').focus();loadSavedBills();
    });
    getElement('hold-bill').addEventListener('click', () => {
        return Sales.run(async () => {
            if (pendingSale || isSavingSale || isPreparingSale || activeLookups) {
                throw new Error('Wait for current operations to finish.');
            }
            if (!cart.length) {
                throw new Error('Add an item before holding this bill.');
            }
            if(numericInputsInvalid())throw new Error('Finish or cancel your current numeric or invoice-discount edit before holding this bill.');
            isPreparingSale = true;
            pendingState();
            try {
                if(!lifecycleRequests.has('hold:'+draftId))await persistDraft();
                await draftCommand('hold');
                localStorage.removeItem(draftKey);
                window.StockFlowUnsaved?.markSaved(getElement('sale-form'));
                location.reload();
            } catch(error) {
                if(lifecycleRequests.has('hold:'+draftId)){recoveryBlocked=true;getElement('draft-status').textContent='Hold result unconfirmed. Retry Hold Bill before continuing.';}
                throw error;
            } finally {
                isPreparingSale = false;
                pendingState();
            }
        });
    });
    document.addEventListener('keydown', (event) => {
        const editing=event.target instanceof Element&&event.target.closest('input,textarea,select,[contenteditable]:not([contenteditable="false"])');
        if (!event.altKey || event.ctrlKey || event.metaKey || event.repeat || event.isComposing || editing ||
            document.querySelector('dialog[open],.modal.show') || pendingSale || isSavingSale || isPreparingSale) {
            return;
        }
        const action = event.key.toLowerCase();
        if (!['s', 'c', 'p', 'h', 'b'].includes(action)) {
            return;
        }
        event.preventDefault();
        if (action === 's') {
            getElement('sale-scan')?.focus();
        }
        if (action === 'c') (getElement('customer-current')?.hidden?getElement('customer-search'):getElement('customer-clear'))?.focus();
        // Focus the checkout action; Enter remains a deliberate final confirmation.
        if (action === 'b') getElement('complete-sale')?.focus();
        if (action === 'p') {
            (isSplitPayment()?getElement('use-split-payment'):workspace.querySelector('.billing-payment-tile[aria-pressed="true"]'))?.focus();
        }
        if (action === 'h') {
            getElement('hold-bill').click();
        }
    });
    getElement('sale-editor').disabled=true;
    Sales.run(async () => {
        await loadPolicy();
        try {
            let saved = sessionStorage.getItem(storageKey);
            if (!saved && sessionStorage.getItem('stockflow-pending-sale')) {
                const legacy = JSON.parse(sessionStorage.getItem('stockflow-pending-sale'));
                legacy.recoverOnly = true;
                saved = JSON.stringify(legacy);
            }
            if (saved) {
                pendingSale = JSON.parse(saved);
                crmEnquiryId = pendingSale?.request?.crmEnquiryId || null;
                sourceQuotationId=pendingSale?.request?.sourceQuotationId||null;sourceQuotationRevision=pendingSale?.request?.sourceQuotationRevision||null;sourceQuotationVersion=pendingSale?.request?.sourceQuotationVersion||null;
                if (
                    !pendingSale?.request?.requestId ||
                    pendingSale.request.locationId !== window.StockFlowLocation ||
                    !Array.isArray(pendingSale.cart)
                ) {
                    pendingSale = null;
                    recoveryBlocked = true;
                    throw new Error(
                        'The saved sale cannot be restored safely. Ask your administrator to reconcile it before billing.'
                    );
                }
                cart = pendingSale.cart;
                payments = pendingSale.request.payments;
                getElement('sale-discount').value = pendingSale.request.discountAmount;
                getElement('sale-notes').value = pendingSale.request.notes || '';
                getElement('use-split-payment').checked = true;
                pendingSale.recoverOnly=true; // A pre-existing pending payload is recovered verbatim; never re-enter old allocations as a new sale.
                if (pendingSale.request.customerId) {
                    getElement('sale-customer').add(
                        new Option(
                            pendingSale.customerLabel || 'Selected customer',
                            pendingSale.request.customerId,
                            true,
                            true
                        )
                    );
                }
                renderCart();
                clearTimeout(quoteTimer);
                renderPayments();
                getElement('sale-totals').textContent = pendingSale.recoverOnly
                    ? 'Unconfirmed checkout: retry verifies the saved request against its completed invoice without posting another sale.'
                    : 'Saved sale awaiting confirmation. Retry without changing its items or payment.';
            } else {
                const draft = localStorage.getItem(draftKey);
                if (draft) {
                    try {
                        const local=JSON.parse(draft);validateSavedBill(local);restore(local,true);
                        let server=null;
                        try{server=await Sales.api('/api/sales/billing/drafts/'+draftId);}catch(error){if(error.status!==404)throw error;}
                        if(!server){
                            if(draftRevision>0)throw Error('The server draft is no longer resumable. Your device backup is retained; review it with the owner.');
                            await persistDraft();
                        }else{
                            if(server.draftStatus==='HELD')throw Error('This bill is Held. Resume it from Saved Bills; the device backup is retained.');
                            let source=server;
                            if(!draftContentsEqual(local,server)){
                                const keep=await StockFlowDialog.confirm('This device has unconfirmed changes. Restore the device backup after acquiring exclusive ownership? Both versions will be retained on this device.',{title:'Recover device changes?',confirmText:'Restore Device Backup',cancelText:'Use Server Copy'});
                                localStorage.setItem(draftKey+'.recovery.'+draftId,JSON.stringify(keep?server:local));if(keep)source=local;
                            }
                            const state=await Sales.api('/api/sales/billing/drafts/'+draftId+'/resume',{method:'POST',body:JSON.stringify({editorId,revision:server.revision,operationId:createRequestId(),recover:true})});
                            restore({...source,...state},true);applyDraftState(state);saveDraftOnThisDevice();
                        }
                    } catch(error) {
                        recoveryBlocked=true;
                        getElement('draft-status').textContent=error.message+' Use Recover interrupted bills after the previous editing lease expires (up to two minutes).';
                        Sales.error(error);
                    }
                }
            }
        } catch (error) {
            recoveryBlocked = true;
            throw error;
        } finally {
            pendingState();
        }
        const quotationParam=new URL(location.href).searchParams.get('quotationId');
        if(quotationParam && quotationParam!==sourceQuotationId){
            if(pendingSale||billHasContent())throw Error('Hold or clear your current bill before opening this quotation. Your existing bill has been retained.');
            isPreparingSale=true;pendingState();
            try {
            const detail=await Sales.api('/api/quotations/'+encodeURIComponent(quotationParam));
            const prepared=await Sales.api('/api/quotations/'+encodeURIComponent(quotationParam)+'/checkout',{method:'POST',body:JSON.stringify({expectedVersion:detail.header.version,revision:detail.header.currentRevision})});
            restore(prepared.snapshot);await persistDraft();
            getElement('draft-status').textContent=prepared.changes.length?prepared.changes.join(' '):'Quotation loaded. Review current prices, tax, stock and payment before checkout.';
            } finally {isPreparingSale=false;pendingState();}
        }
        if(sourceQuotationId){const info=document.createElement('p');info.id='sale-quotation-context';info.className='alert alert-info';info.textContent='From '+(sourceQuotationNumber||'quotation')+' revision '+sourceQuotationRevision+'. No stock was reserved. The quotation converts only after successful checkout. Revisions or lifecycle changes require reloading it. '+sourceQuotationNotice;getElement('sale-form').prepend(info);}
        const crmParam=new URL(location.href).searchParams.get('crmEnquiryId');
        if(crmParam){
            if(!/^\d+$/.test(crmParam)||!Number.isSafeInteger(Number(crmParam)))throw Error('Invalid CRM enquiry reference.');
            const target=Number(crmParam);
            if(crmEnquiryId!==target){
                if(pendingSale||cart.length||getElement('sale-customer').value)throw Error('Your existing bill has been preserved. Hold or clear it before starting a different customer enquiry.');
                const data=await Sales.api('/api/crm/enquiries/'+target),e=data.enquiry;
                if(e.saleId||['CLOSED','CANCELLED','RESOLVED'].includes(e.status))throw Error('This enquiry is closed or already linked to an invoice.');
                const customer=await Sales.api('/api/customers/'+e.customerId);
                if(!customer.isActive)throw Error('This enquiry customer is inactive. Review the customer before billing.');
                crmEnquiryId=target;
                getElement('sale-customer').add(new Option(customer.customerName,customer.customerId,true,true));
                changed();
            }
        }
        if(crmEnquiryId){const info=document.createElement('p');info.id='sale-crm-context';info.setAttribute('role','status');info.textContent='Enquiry ENQ-'+crmEnquiryId+' will link only after successful checkout. Search and add the requested items using current stock and prices.';getElement('sale-form').prepend(info);}
        syncCustomer();updateSummary();mode();
        if (!pendingSale) {
            getElement('sale-scan')?.focus();
        }
    });
})();
