(() => {
    'use strict';
    const el = id => document.getElementById('cih-' + id);
    // The shared list script places breadcrumbs after captions; this reference
    // screen keeps the existing breadcrumb above its title/action row.
    document.querySelector('.cih-heading')?.before(document.getElementById('app-breadcrumb'));
    const key = el('workspace').dataset.storageKey;
    const locationId = Number(document.querySelector('meta[name="stockflow-location"]')?.content || 0);
    const titles = {OPENING:'Set Opening Cash',OWNER_IN:'Cash Entry',OWNER_OUT:'Cash Entry',OPENING_REVIEW:'Review Cash Payment Timing',REVERSAL:'Reverse Cash Adjustment'};
    const typeLabels={SALE:'Sale / Collection',SERVICE:'Repair Payment',EXPENSE:'Expense',REFUND:'Customer Refund',SUPPLIER:'Purchase Payment',OPENING:'Opening Cash',OWNER_IN:'Add Cash',OWNER_OUT:'Remove Cash',BANK_DEPOSIT:'Bank Deposit (legacy)',BANK_WITHDRAWAL:'Bank Withdrawal (legacy)',REVERSAL:'Reversal'};
    let report, page = 1, version = 0, timer, busy = false, confirming = false, loading = true, denied = false, pending = null, kind, original, entryTooltip, dialogOpener;
    const reasonOptions=[...el('reason-code').options].slice(1).map(option=>option.cloneNode(true));
    const manualEntry=()=>['OWNER_IN','OWNER_OUT'].includes(kind);
    const touched=new Set();let openingDialog=false;
    function fieldError(field) {
        if(field===el('amount')){
            const raw=field.value.trim(),value=Number(raw);
            return !raw?'Amount is required.':!/^\d+(?:\.\d{1,2})?$/.test(raw)||!Number.isFinite(value)||value>9999999999.99?'Enter a valid amount with up to 2 decimal places.':value<=0?'Enter an amount greater than ₹0.':'';
        }
        return window.StockFlowValidation?.fieldMessage(field)||'';
    }
    function refreshTouched() {for(const field of touched)window.StockFlowValidation?.showField(field,fieldError(field));}
    function amountLabel(text) {el('amount-label').textContent=text;const required=document.createElement('span');required.className='cih-required';required.textContent=' *';el('amount-label').append(required);}
    function entryValid() {
        const raw=el('amount').value.trim(),amount=Number(raw),reason=el('reason-code').selectedOptions[0],note=el('reason').value.trim();
        return /^\d+(?:\.\d{1,2})?$/.test(raw)&&Number.isFinite(amount)&&amount>0&&amount<=9999999999.99&&reason?.dataset.kind===kind&&note.length<=250&&!/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/.test(note)&&(reason.dataset.explanation!=='true'||note.length>=3&&/[\p{L}\p{N}]/u.test(note));
    }
    function updateNote() {
        const required=manualEntry()&&el('reason-code').selectedOptions[0]?.dataset.explanation==='true';
        if(manualEntry()){el('reason').required=required;el('reason').minLength=required?3:0;el('reason-label').textContent=required?'Note *':'Note (Optional)';el('note-counter').textContent=`${el('reason').value.length} / 250`;const note=el('reason').value.trim();el('reason').setCustomValidity(required&&(note.length<3||!/[\p{L}\p{N}]/u.test(note))?'Explain this cash movement in 3–250 characters.':/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/.test(note)?'Enter a plain-text note.':'');}
    }
    function resetEntryReason() {
        touched.delete(el('reason-code'));touched.delete(el('reason'));
        el('reason-code').replaceChildren(new Option('Select reason',''),...reasonOptions.filter(option=>option.dataset.kind===kind).map(option=>option.cloneNode(true)));
        el('direction-icon').textContent=kind==='OWNER_OUT'?'−':'+';el('direction-icon').classList.toggle('cih-outflow',kind==='OWNER_OUT');updateNote();
        window.StockFlowValidation?.reset(el('form'));refreshTouched();
    }
    function closeActions() {el('entry-menu').hidden=true;el('entry').setAttribute('aria-expanded','false');}
    const currency = v => new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(v);
    const figure = v => new Intl.NumberFormat('en-IN',{minimumFractionDigits:2,maximumFractionDigits:2}).format(v);
    function requestId() {
        if(crypto.randomUUID)return crypto.randomUUID();
        const bytes=crypto.getRandomValues(new Uint8Array(16));bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;
        const hex=[...bytes].map(value=>value.toString(16).padStart(2,'0')).join('');
        return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
    }
    const message = (text, error = true) => {el('message').textContent=text;el('message').className='alert '+(error?'alert-danger':'alert-success');el('message').hidden=!text;};
    async function request(url, body) {
        const response = await fetch(url,body ? {method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]').content},body:JSON.stringify(body)} : {});
        const data = await response.json().catch(()=>null);
        if(!response.ok || !data) {const error=new Error(data?.message||data?.title||'The cash result could not be confirmed. Please retry.');error.status=response.status;throw error;}
        return data;
    }
    function controls() {
        const locked=busy||confirming||!!pending;
        el('retry-panel').hidden=!pending;el('retry').disabled=busy;el('fields').disabled=locked;el('save').disabled=locked||(manualEntry()&&!entryValid());
        el('form').dataset.saving=String(busy||confirming);
        el('dialog-close').disabled=busy||confirming;el('cancel').disabled=busy||confirming;
        el('dialog-retry').hidden=!pending;el('dialog-retry').disabled=busy;
        el('save').textContent=busy?'Saving…':confirming?'Confirming…':kind==='OPENING'?'Set Opening Cash':manualEntry()?'Save Cash Entry':'Save Cash Movement';
        el('date').disabled=locked;el('search').disabled=locked;el('type').disabled=locked;el('direction').disabled=locked;
        el('opening').disabled=locked||loading||!report?.available;
        const entryDisabled=locked||loading||!report?.available||!report?.trackingStart;
        el('actions').hidden=locked;
        el('entry').disabled=entryDisabled;
        const explanation=denied?'You do not have permission to adjust cash for this shop.':loading?'Loading cash tracking.':!report?.available?'Cash tracking is unavailable. Ask your administrator for access.':!report?.trackingStart?'Set Opening Cash first to enable cash entries.':'';
        el('entry-explanation').textContent=explanation;
        el('entry-help').tabIndex=entryDisabled?0:-1;
        el('entry-help').setAttribute('aria-label',explanation||'Cash Entry');
        el('entry-help').title=explanation;
        entryTooltip?.dispose();entryTooltip=null;
        if(explanation&&window.bootstrap?.Tooltip)entryTooltip=bootstrap.Tooltip.getOrCreateInstance(el('entry-help'),{title:explanation,trigger:'hover focus',container:document.body,placement:'bottom',animation:false});
        if(entryDisabled)closeActions();
        document.querySelectorAll('[data-cash-kind]').forEach(button=>button.disabled=entryDisabled);
        el('prev').disabled=locked||page<=1;el('next').disabled=locked||page*20>=(report?.total||0);
    }
    function render() {
        page=Math.max(1,report.page||1);
        el('date').value=report.date.slice(0,10);el('date').max=report.today.slice(0,10);el('start').max=report.today.slice(0,10);
        el('date-caption').textContent=(report.date.slice(0,10)===report.today.slice(0,10)?'Today, ':'')+new Date(report.date.slice(0,10)+'T12:00:00').toLocaleDateString('en-IN',{day:'2-digit',month:'short',year:'numeric'});
        el('initialize').hidden=!report.available||!!report.trackingStart;
        const initialized=report.available&&report.trackingStart&&report.date.slice(0,10)>=report.trackingStart.slice(0,10);
        const active=initialized&&!report.pendingReviewCount;
        for(const prop of ['balance','received','paid','net']) {
            el(prop).textContent=active?currency(report[prop]):'—';
            el(prop).classList.toggle('cih-negative',active&&report[prop]<0);
            el(prop).classList.toggle('cih-positive',active&&prop==='net'&&report[prop]>0);
            el('caption-'+prop).textContent=report.pendingReviewCount?'Opening-day review required':prop==='balance' ? (active?'Physical cash · '+new Date(report.date.slice(0,10)+'T12:00:00').toLocaleDateString('en-IN',{day:'2-digit',month:'short',year:'numeric'}):'Opening cash not established') : (report.date.slice(0,10)===report.today.slice(0,10)?'Today':'Selected day');
        }
        el('review').hidden=!report.openingExpenses?.length;
        el('review').open=!!report.pendingReviewCount;
        el('review-title').textContent=`Opening-day cash payments${report.pendingReviewCount?' · '+report.pendingReviewCount+' need review':''}`;
        el('review-items').replaceChildren();
        for(const item of report.openingExpenses||[]) {
            const row=document.createElement('div'),text=document.createElement('span'),button=document.createElement('button');row.className='cih-review-item';
            text.textContent=`${item.reference} · ${item.description} · ${currency(Math.abs(item.amount))} ${item.amount>0?'received':'paid'}${item.timing?' · '+item.timing.toLowerCase()+' opening count':''}`;
            button.type='button';button.className='cih-control';button.textContent=item.timing?'Correct timing':'Review timing';button.onclick=()=>open('OPENING_REVIEW',item);row.append(text,button);el('review-items').append(row);
        }
        const rows=el('rows');rows.replaceChildren();
        for(const movement of report.movements) {
            const row=document.createElement('tr');
            const date=new Date(movement.occurred).toLocaleString('en-IN',{timeZone:report.timeZone,day:'2-digit',month:'short',year:'numeric',...(!movement.dateOnly?{hour:'2-digit',minute:'2-digit'}:{})})+(movement.dateOnly?' · time not recorded':'');
            for(const value of [date,movement.reference,null,movement.description,movement.amount>0?figure(movement.amount):'–',movement.amount<0?figure(-movement.amount):'–',report.pendingReviewCount||movement.balance==null?'—':figure(movement.balance)]) {
                const cell=document.createElement('td');
                if(value===null) {
                    const type=document.createElement('span');type.className='cih-type-icon '+movement.type;type.title=typeLabels[movement.type]||movement.type;type.setAttribute('aria-label',type.title);
                    const icon=movement.type==='SALE'?'cart':movement.type==='EXPENSE'?'document':movement.type==='OPENING'?'chart':'cash';
                    type.innerHTML='<svg class="sf-icon" aria-hidden="true"><use href="/images/icons.svg#'+icon+'" /></svg>';const label=document.createElement('span');label.className='cih-type-label';label.textContent=type.title;cell.append(type,label);
                } else if(value===movement.reference&&movement.sourceUrl?.startsWith('/')&&!movement.sourceUrl.startsWith('//')) {
                    const link=document.createElement('a');link.href=movement.sourceUrl;link.textContent=value;cell.append(link);
                } else {
                    cell.textContent=value;
                    if(value===movement.description&&(movement.reversesReference||movement.reversedByReference)){
                        const status=document.createElement('small');status.className='cih-reversal-status';status.textContent=movement.reversesReference?'Reversal of '+movement.reversesReference:'Reversed by '+movement.reversedByReference;cell.append(status);
                    }
                }
                row.append(cell);
            }
            const actions=document.createElement('td');
            if(movement.canReverse) {
                const reverse=document.createElement('button');reverse.type='button';reverse.className='cih-row-action';reverse.textContent='↶';reverse.setAttribute('aria-label','Reverse '+movement.reference);reverse.onclick=()=>open('REVERSAL',movement);actions.append(reverse);
            }
            row.append(actions);rows.append(row);
        }
        el('empty').hidden=!!report.movements.length;el('empty').replaceChildren();
        if(!report.movements.length) {
            const title=document.createElement('strong'),help=document.createElement('p');
            title.textContent=!report.available?'Cash tracking is not available yet':!report.trackingStart?"Cash tracking hasn't started":'No cash transactions found';
            help.textContent=!report.available?'Ask your administrator to enable it.':!report.trackingStart?'Set your opening cash to begin tracking cash received and paid at this shop.':!initialized?'Choose your tracking start date or a later date.':el('search').value||el('type').value||el('direction').value?'No cash transactions match these filters.':'No cash movements were recorded for the selected period.';el('empty').append(title,help);
        }
        el('pagination-status').textContent=report.total?`Showing ${(page-1)*20+1}–${Math.min(page*20,report.total)} of ${report.total} transactions`:'0 transactions';el('page').textContent=page;
        el('pagination').hidden=!report.total;
        const startLabel=report.trackingStartedAt?new Date(report.trackingStartedAt).toLocaleString('en-IN',{timeZone:report.timeZone}):report.trackingStart?.slice(0,10);
        el('reconciliation').textContent=report.pendingReviewCount?'Balances are unavailable until opening-day payment timing is reviewed.':report.closing?`Latest counted cash: ${currency(report.closing.countedCash)} · Recorded expected cash: ${currency(report.closing.expectedCash)} · Difference: ${currency(report.closing.difference)}. ${active&&report.balance!==report.closing.expectedCash?'Cash activity has changed; review Cash Closing.':''}`:active?'Tracking since '+startLabel+'. Closing counts do not change the posted cash balance.':'';
        controls();
    }
    async function load() {
        const current=++version;
        loading=true;denied=false;message('');el('workspace').setAttribute('aria-busy','true');controls();
        const params=new URLSearchParams({page,date:el('date').value,search:el('search').value,type:el('type').value,direction:el('direction').value});
        if(!params.get('date'))params.delete('date');
        try {const data=await request('/api/cashinhand?'+params);if(current!==version)return;report=data;loading=false;render();}
        catch(error){if(current===version){report=null;denied=error.status===403;loading=false;el('initialize').hidden=true;el('review').hidden=true;el('empty').hidden=true;el('rows').replaceChildren();el('pagination').hidden=true;el('pagination-status').textContent='';el('reconciliation').textContent='';for(const prop of ['balance','received','paid','net']){el(prop).textContent='—';el('caption-'+prop).textContent='Unavailable';}message(error.message);controls();}}
        finally {if(current===version)el('workspace').setAttribute('aria-busy','false');}
    }
    function open(next, movement) {
        if(busy||confirming||pending||loading||!report?.available||(['OWNER_IN','OWNER_OUT'].includes(next)&&!report.trackingStart))return;
        openingDialog=true;touched.clear();dialogOpener=document.activeElement;kind=next;original=movement;el('form').reset();el('dialog-title').textContent=titles[kind];
        window.StockFlowValidation?.reset(el('form'));
        el('dialog-error').hidden=true;el('amount').setCustomValidity('');el('reason').setCustomValidity('');
        if(kind==='OPENING'||manualEntry())amountLabel(kind==='OPENING'?'Opening Cash Amount (₹)':'Amount (₹)');else el('amount-label').textContent='Amount (₹)';
        el('amount').placeholder=kind==='OPENING'||manualEntry()?'0.00':'';
        el('reason-label').textContent=kind==='OPENING'||manualEntry()?'Note (Optional)':'Reason';
        el('reason').required=kind!=='OPENING'&&!manualEntry();el('reason').minLength=kind==='OPENING'||manualEntry()?0:3;
        el('reason').maxLength=manualEntry()?250:1000;
        el('reason').placeholder=kind==='OPENING'?'e.g. Cash available at start of tracking':manualEntry()?'Add details if needed':'';
        el('reason-code-field').hidden=!manualEntry();el('reason-code').required=manualEntry();el('note-counter').hidden=!manualEntry();el('entry-info').hidden=!manualEntry();
        el('opening-info').hidden=kind!=='OPENING';
        el('start-field').hidden=true;el('start').required=false;
        el('entry-field').hidden=!['OWNER_IN','OWNER_OUT'].includes(kind);el('entry-type').value=kind==='OWNER_OUT'?'OWNER_OUT':'OWNER_IN';
        el('entry-type').required=manualEntry();if(manualEntry())resetEntryReason();
        el('timing-field').hidden=kind!=='OPENING_REVIEW';el('amount-field').hidden=kind==='OPENING_REVIEW';el('amount').required=kind!=='OPENING_REVIEW';
        el('timing').value=movement?.timing||'AFTER';el('review-reference').textContent=movement?movement.reference+' · '+movement.description:'';
        el('amount').min=kind==='OPENING'?'0':'0.01';el('amount').readOnly=kind==='REVERSAL';el('amount').value=movement?Math.abs(movement.amount).toFixed(2):'';
        el('dialog-help').textContent=kind==='OPENING'?'Count the cash currently in your shop drawer and enter the total amount.':kind==='OPENING_REVIEW'?'Before: already included in the opening count. After: changes the tracked balance. Each decision and correction is retained with your reason.':kind==='REVERSAL'?'Record a reason. This creates an equal opposite movement and preserves the original.':'Record cash added to or removed from your shop drawer.';
        closeActions();controls();el('dialog').showModal();el(['REVERSAL','OPENING_REVIEW'].includes(kind)?'reason':'amount').focus();openingDialog=false;
    }
    function dialogError(text) {el('dialog-error').textContent=text;el('dialog-error').hidden=!text;if(text)el('dialog-error').focus();}
    async function postPending() {
        if(!pending||busy)return;busy=true;controls();
        const isOpening=pending.kind==='OPENING';dialogError('');
        const entrySuccess=pending.kind==='OWNER_IN'?`${currency(pending.amount)} added to Cash in Hand.`:pending.kind==='OWNER_OUT'?`${currency(pending.amount)} removed from Cash in Hand.`:'Cash movement recorded.';
        try {
            const saved=await request(pending.kind==='OPENING_REVIEW'?'/api/cashinhand/opening-review':'/api/cashinhand',pending);
            if((saved.adjustmentId||saved.requestId)!==pending.requestId)throw new Error('Cash movement could not be confirmed. Retry saved movement.');
            sessionStorage.removeItem(key);pending=null;window.StockFlowUnsaved?.markSaved(el('form'));el('dialog').close();el('date').value='';page=1;await load();message(isOpening?'Opening cash set. Cash tracking has started.':entrySuccess,false);
        } catch(error) {
            if([400,422].includes(error.status)){sessionStorage.removeItem(key);pending=null;}
            message(error.message);dialogError(error.message);
            if(isOpening&&[400,409,422].includes(error.status)){
                await load();
                if(report?.trackingStart){window.StockFlowUnsaved?.markClean(el('form'));el('dialog').close();message('Opening cash is already established for this shop. The current balance has been refreshed; your request did not overwrite it.');}
            }
        } finally {busy=false;controls();if(isOpening&&!el('dialog').open&&!el('entry').disabled)el('entry').focus();}
    }
    el('form').addEventListener('submit',async event=>{
        event.preventDefault();if(busy||confirming||pending||!report)return;
        if(kind!=='OPENING_REVIEW'){
            const raw=el('amount').value.trim(),amount=Number(raw);
            el('amount').setCustomValidity(raw&&/^\d+(?:\.\d{1,2})?$/.test(raw)&&Number.isFinite(amount)&&amount>=(kind==='OPENING'?0:0.01)&&amount<=9999999999.99?'':'Enter a valid amount with up to two decimal places.');
        }
        if((manualEntry()&&!entryValid())||!el('form').checkValidity()){window.StockFlowValidation?.validate(el('form'),true);return;}
        const body=kind==='OPENING_REVIEW'?{requestId:requestId(),locationId,kind,movementId:original.movementId,expectedVersion:original.version,timing:el('timing').value,reason:el('reason').value.trim()}:{requestId:requestId(),locationId,businessProfileCode:el('workspace').dataset.profile,kind,amount:Number(el('amount').value),reason:kind==='OPENING'?'Opening cash counted at initialization':el('reason').value.trim(),startDate:null,reversesId:original?.adjustmentId||null,...(kind==='OPENING'?{openingMode:'CURRENT',...(el('reason').value.trim()?{note:el('reason').value.trim()}:{})}:{})};
        if(manualEntry()){body.reasonCode=el('reason-code').value;body.reason=el('reason-code').selectedOptions[0].textContent;if(el('reason').value.trim())body.note=el('reason').value.trim();}
        if(kind==='OPENING'){
            confirming=true;controls();
            let accepted=false;
            try{accepted=await window.StockFlowDialog.confirm(`Set opening cash to ${currency(body.amount)} for ${el('form').dataset.shopName}? Cash tracking will start from this point.`,{title:'Confirm Opening Cash',cancelText:'Back',confirmText:'Confirm & Start Tracking'});}
            finally{confirming=false;controls();}
            if(!accepted){el('amount').focus();return;}
        }
        try {sessionStorage.setItem(key,JSON.stringify(body));}catch{message('Enable browser storage to safely retry cash movements.');return;}
        pending=body;postPending();
    });
    document.querySelectorAll('[data-cash-kind]').forEach(button=>button.onclick=()=>open(button.dataset.cashKind));
    el('entry-type').onchange=()=>{kind=el('entry-type').value;resetEntryReason();controls();};
    el('reason-code').onchange=()=>{updateNote();touched.delete(el('reason'));window.StockFlowValidation?.showField(el('reason'),'');refreshTouched();controls();};
    el('form').addEventListener('input',()=>{updateNote();if(manualEntry()){refreshTouched();controls();}});
    el('form').addEventListener('focusout',event=>{if(manualEntry()&&!openingDialog&&!busy&&!confirming&&el('dialog').open&&!event.target.disabled&&event.target.matches('input,select,textarea')){touched.add(event.target);window.StockFlowValidation?.showField(event.target,fieldError(event.target));}});
    for(const option of el('type').options)if(typeLabels[option.value])option.textContent=typeLabels[option.value];
    el('opening').onclick=()=>open('OPENING');el('retry').onclick=el('dialog-retry').onclick=postPending;
    el('amount').addEventListener('input',()=>el('amount').setCustomValidity(''));
    for(const id of ['cancel','dialog-close'])el(id).onclick=()=>{if(!busy&&!confirming)el('dialog').close();};
    el('dialog').addEventListener('cancel',event=>{if(busy||confirming)event.preventDefault();});
    el('dialog').addEventListener('close',()=>{const target=dialogOpener?.isConnected&&!dialogOpener.disabled&&dialogOpener.getClientRects().length?dialogOpener:!el('entry').disabled?el('entry'):el('opening');if(!target.disabled)target.focus();});
    el('date').onchange=()=>{page=1;load();};el('type').onchange=()=>{page=1;el('filter').open=false;load();};
    el('direction').onchange=()=>{page=1;el('filter').open=false;load();};
    el('search').oninput=()=>{++version;clearTimeout(timer);page=1;timer=setTimeout(load,180);};
    el('prev').onclick=()=>{--page;load();};el('next').onclick=()=>{++page;load();};
    el('entry').onclick=()=>{if(el('entry').disabled)return;const show=el('entry-menu').hidden;el('entry-menu').hidden=!show;el('entry').setAttribute('aria-expanded',String(show));};
    document.addEventListener('click',event=>{if(!el('actions').contains(event.target))closeActions();if(!el('filter').contains(event.target))el('filter').open=false;});
    document.addEventListener('keydown',event=>{if(event.key==='Escape'){if(!el('entry-menu').hidden){closeActions();el('entry').focus();}el('filter').open=false;}});
    try {pending=JSON.parse(sessionStorage.getItem(key)||'null');}catch{message('Saved cash request is unreadable. Review cash history before entering it again.');busy=true;}
    controls();load();
})();
