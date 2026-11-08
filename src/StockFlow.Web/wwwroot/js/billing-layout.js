// Arrange existing controls; pricing, payments and posting remain in sales-create.
(() => {
    const byId=id=>document.getElementById(id), workspace=byId('billing-workspace');
    const layout=workspace.querySelector('.billing-layout'), heading=document.querySelector('.sf-page-heading');
    const toolbar=workspace.querySelector(':scope > section.card');
    const actions=toolbar.querySelector(':scope > .d-flex');actions.classList.add('billing-header-actions');heading.append(actions);
    toolbar.remove();
    const back=workspace.querySelector('.billing-checkout > a');if(back)actions.insertBefore(back,byId('draft-status'));
    workspace.dataset.billingLayout='single-page';
    ['billing-customer-card','billing-add-card','billing-cart-card','billing-payment-card'].forEach(id=>layout.append(byId(id)));
    layout.querySelector('.billing-items').remove();layout.querySelector('.billing-checkout').remove();
    const summary=document.createElement('aside');summary.id='billing-summary';summary.className='card card-body';summary.setAttribute('aria-label','Bill summary');
    summary.innerHTML='<h2>Bill Summary</h2><dl><div><dt>Item Subtotal</dt><dd id="summary-subtotal">&#8377;0.00</dd></div><div><dt>Item Discounts</dt><dd id="summary-line-discounts">&#8377;0.00</dd></div><div id="invoice-discount-row"></div><div><dt>Taxable Amount</dt><dd id="summary-taxable">&#8377;0.00</dd></div><div><dt id="summary-tax-label">GST</dt><dd id="summary-tax">&#8377;0.00</dd></div><div class="billing-summary-total"><dt>Total Payable</dt><dd id="summary-total">&#8377;0.00</dd></div></dl>';
    byId('billing-cart-card').after(summary);
    actions.id='billing-header-actions';
    const draftStatus=byId('draft-status'),draftStatusVisibility=()=>draftStatus.classList.toggle('visually-hidden',!draftStatus.textContent.trim()||draftStatus.textContent.trim()==='Draft saved');
    new MutationObserver(draftStatusVisibility).observe(draftStatus,{childList:true,characterData:true,subtree:true});draftStatusVisibility();
    const headerIcons={ 'hold-bill':'M8 5v14M16 5v14','show-held':'M6 3h12v18l-6-4-6 4z' };
    for(const button of actions.querySelectorAll('button,a')){
        const label=button.id==='hold-bill'?'Hold Bill':button.id==='show-held'?'Saved Bills':'Back to Sales';
        const icon=document.createElementNS('http://www.w3.org/2000/svg','svg'),path=document.createElementNS(icon.namespaceURI,'path');icon.setAttribute('viewBox','0 0 24 24');icon.setAttribute('fill','none');icon.setAttribute('stroke','currentColor');icon.setAttribute('stroke-width','1.8');icon.setAttribute('aria-hidden','true');path.setAttribute('d',headerIcons[button.id]||'M19 12H5m7-7-7 7 7 7');icon.append(path);
        const text=document.createElement('span');text.className='billing-action-label';text.textContent=label;button.replaceChildren(icon,text);button.title=label;button.setAttribute('aria-label',label);
    }
    byId('billing-customer-card').querySelector('h2').classList.add('visually-hidden');
    byId('billing-customer-card').querySelector('label[for="customer-current"]').textContent='Customer *';
    byId('billing-add-card').querySelector('h2').textContent='Scan or Search Product';
    byId('billing-add-card').querySelector('label[for="sale-scan"]')?.classList.add('visually-hidden');
    byId('sale-scan-help')?.classList.add('visually-hidden');
    const searchButton=byId('scan-sale');
    if(searchButton)searchButton.innerHTML='<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><circle cx="10" cy="10" r="7"/><path d="m15 15 6 6"/></svg>';
    const scanRow=workspace.querySelector('.billing-scan-row:has(#sale-scan)');
    if(scanRow){const compactCamera=()=>{const camera=scanRow.querySelector('.sf-camera-trigger');if(camera&&!camera.querySelector('svg')){camera.title='Scan with camera';camera.innerHTML='<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M3 6h4l2-3h6l2 3h4v15H3z"/><circle cx="12" cy="13" r="4"/></svg>';}};new MutationObserver(compactCamera).observe(scanRow,{childList:true,subtree:true});compactCamera();}
    const notes=byId('sale-notes'), notesLabel=workspace.querySelector('label[for="sale-notes"]');
    notes.placeholder='Add any notes for this sale (optional)...';
    // Keep optional notes beside the checkout action.
    const notesSection=document.createElement('div');notesSection.className="billing-notes";notesSection.id='billing-notes';notesSection.append(notesLabel,notes);
    byId('complete-sale').insertAdjacentHTML('afterbegin','<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3zM9 7h6M9 11h6M9 15h4"/></svg> ');
    // CardType classifies CARD collections; CREDIT remains a customer receivable.
    const select=byId('payment-mode'),cardType=byId('payment-card-type'),tiles=document.createElement('div');
    tiles.className='billing-payment-tiles';tiles.setAttribute('role','group');tiles.setAttribute('aria-label','Payment method');
    const paths={CASH:'M3 5h18v14H3zM12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8',UPI:'M3 3h6v6H3zM15 3h6v6h-6zM3 15h6v6H3zM15 15h2v2h-2zM19 15h2v6h-6v-2h2',CARD:'M3 5h18v14H3zM3 10h18M6 15h4',BANK_TRANSFER:'m3 9 9-6 9 6H3zM5 10v9m7-9v9m7-9v9M3 21h18'};
    for(const [mode,type,label] of [['CASH','','Cash'],['UPI','','UPI'],['CARD','DEBIT','Debit Card'],['CARD','CREDIT','Credit Card'],['BANK_TRANSFER','','Bank Transfer']]){
        const tile=document.createElement('button');tile.type='button';tile.className='billing-payment-tile';tile.dataset.paymentMode=mode;tile.dataset.cardType=type;tile.title=label;
        tile.innerHTML='<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+paths[mode]+'"/></svg><span class="billing-payment-label"></span><span class="billing-payment-check" aria-hidden="true">\u2713</span>';
        tile.querySelector('.billing-payment-label').textContent=label;
        tile.onclick=()=>{select.value=mode;cardType.value=type;select.dispatchEvent(new Event('change',{bubbles:true}));};tiles.append(tile);
    }
    const legacyCard=document.createElement('p');legacyCard.id='billing-card-legacy';legacyCard.className='small';legacyCard.setAttribute('role','status');legacyCard.textContent='Card payment (type unspecified). Select Debit Card or Credit Card only if known.';
    const syncTiles=()=>{for(const tile of tiles.children){tile.setAttribute('aria-pressed',String(tile.dataset.paymentMode===select.value&&(select.value!=='CARD'||tile.dataset.cardType===cardType.value)));tile.disabled=select.disabled;}legacyCard.hidden=select.value!=='CARD'||!!cardType.value;};
    select.addEventListener('change',syncTiles);syncTiles();
    select.classList.add('visually-hidden');select.tabIndex=-1;select.setAttribute('aria-hidden','true');workspace.querySelector('label[for="payment-mode"]').classList.add('visually-hidden');byId('billing-simple-payment').before(tiles,legacyCard);
    workspace.addEventListener('billing-payment-updated',syncTiles);
    const paymentFields=document.createElement('div');paymentFields.className='billing-payment-fields';
    byId('billing-simple-payment').before(paymentFields);paymentFields.append(byId('billing-simple-payment'),byId('cash-change-field'));
    const discountRow=byId('invoice-discount-row');discountRow.className='billing-summary-discount';
    const discountLabel=workspace.querySelector('label[for="sale-discount"]');discountLabel.textContent='Invoice Discount (\u20b9)';
    const discountValue=byId('sale-discount');discountValue.type='text';discountValue.inputMode='decimal';discountValue.maxLength=16;discountValue.autocomplete='off';
    discountValue.setAttribute('aria-describedby','invoice-discount-error');discountRow.append(discountLabel,discountValue);
    const discountContext=document.createElement('div');discountContext.id='invoice-discount-context';
    discountContext.innerHTML='<label for="invoice-discount-reason">Invoice discount reason</label><input id="invoice-discount-reason" class="form-control" maxlength="500"/><p id="invoice-discount-error" role="status" class="small text-danger"></p>';
    discountRow.after(discountContext);
    const changeToggle=document.createElement('label');changeToggle.className='billing-change-toggle';
    changeToggle.innerHTML='<input id="calculate-change" type="checkbox" role="switch"/> Calculate Change <span>(Optional)</span>';
    byId('cash-change-field').before(changeToggle);
    const billTotal=document.createElement('div');billTotal.className='billing-cash-total';billTotal.innerHTML='<span>Bill Total</span><strong id="cash-bill-total">&#8377;0.00</strong>';
    byId('cash-change-field').prepend(billTotal);byId('cash-change-field').hidden=true;
    const exactCash=document.createElement('p');exactCash.id='exact-cash-help';exactCash.className='small text-muted';exactCash.textContent='Completing this cash sale confirms receipt of the exact Total Payable.';changeToggle.after(exactCash);
    const calculationStatus=document.createElement('p');calculationStatus.id='billing-calculation-status';calculationStatus.className='billing-calculation-status';calculationStatus.setAttribute('role','status');calculationStatus.setAttribute('aria-live','polite');
    const retryCalculation=document.createElement('button');retryCalculation.id='billing-retry-calculation';retryCalculation.type='button';retryCalculation.className='billing-text-action';retryCalculation.textContent='Retry calculation';retryCalculation.hidden=true;
    summary.append(calculationStatus,retryCalculation);
    const taxReviewLink=document.createElement('a');taxReviewLink.id='billing-tax-review-link';taxReviewLink.href='/Admin/Gst';taxReviewLink.textContent='Review product tax mapping';taxReviewLink.hidden=true;summary.append(taxReviewLink);
    const splitToggle=byId('billing-split-toggle');splitToggle.hidden=true;tiles.after(splitToggle);
    const paymentWarning=document.createElement('p');paymentWarning.id='billing-payment-warning';paymentWarning.className='small text-danger';paymentWarning.setAttribute('role','status');paymentWarning.hidden=true;splitToggle.after(paymentWarning);
    const customer=byId('billing-new-customer');
    if(customer){StockFlowQuickCustomer.mount(customer,workspace,byId('customer-picker'),{requiredMobile:true,returnFocusId:'sale-scan'});const picker=byId('customer-picker'),add=picker.querySelector(':scope > button');if(add){const row=document.createElement('div');row.className='billing-customer-row';picker.before(row);row.append(picker,add);}}
    // One form and one set of controls for every available width.
    const left=document.createElement('div');left.className='billing-catalog-pane';
    const lookup=document.createElement('div');lookup.className='billing-lookup-card';
    lookup.append(byId('billing-customer-card'),byId('billing-add-card'));
    left.append(lookup,byId('billing-cart-card'));
    const checkout=document.createElement('div');checkout.className='billing-checkout-pane';
    const payment=byId('billing-payment-card'),complete=byId('complete-sale');
    payment.append(notesSection,complete);
    checkout.append(summary,payment);layout.append(left,checkout);
})();
