// Arrange existing controls; pricing, payments and posting remain in sales-create.
(() => {
    const byId=id=>document.getElementById(id), workspace=byId('billing-workspace');
    const layout=workspace.querySelector('.billing-layout'), heading=document.querySelector('.sf-page-heading');
    const toolbar=workspace.querySelector(':scope > section.card');
    const actions=toolbar.querySelector(':scope > .d-flex');actions.classList.remove('gap-2');actions.classList.add('billing-header-actions');heading.append(actions);
    toolbar.remove();
    workspace.dataset.billingLayout='single-page';
    ['billing-customer-card','billing-add-card','billing-cart-card','billing-payment-card'].forEach(id=>layout.append(byId(id)));
    layout.querySelector('.billing-items').remove();layout.querySelector('.billing-checkout').remove();
    const summary=document.createElement('aside');summary.id='billing-summary';summary.className='card card-body';summary.setAttribute('aria-label','Bill summary');
    summary.innerHTML='<h2>Bill Summary</h2><dl><div><dt>Item Subtotal</dt><dd id="summary-subtotal">&#8377;0.00</dd></div><div><dt>Item Discounts</dt><dd id="summary-line-discounts">&#8377;0.00</dd></div><div id="invoice-discount-row"></div><div><dt>Taxable Amount</dt><dd id="summary-taxable">&#8377;0.00</dd></div><div><dt id="summary-tax-label">GST</dt><dd id="summary-tax">&#8377;0.00</dd></div><div class="billing-summary-total"><dt>Total Payable</dt><dd id="summary-total">&#8377;0.00</dd></div></dl>';
    byId('billing-cart-card').after(summary);
    const withoutGst=document.createElement('div');withoutGst.id='summary-without-gst-row';withoutGst.hidden=true;
    withoutGst.innerHTML='<dt>Items without GST</dt><dd id="summary-without-gst"></dd>';
    summary.querySelector('#summary-taxable').parentElement.after(withoutGst);
    const allowance=document.createElement('div');allowance.id='summary-allowance-row';allowance.hidden=true;
    allowance.innerHTML='<dt>Other allowance (after tax)</dt><dd id="summary-allowance"></dd>';
    summary.querySelector('.billing-summary-total').before(allowance);
    const taxDetails=document.createElement('details');taxDetails.id='billing-tax-details';taxDetails.hidden=true;
    taxDetails.innerHTML='<summary>Tax breakdown</summary><p id="billing-tax-status" role="status"></p><div id="billing-tax-rates"></div>';
    summary.append(taxDetails);
    actions.id='billing-header-actions';
    const draftStatus=byId('draft-status'),draftStatusVisibility=()=>{const text=draftStatus.textContent.trim();draftStatus.classList.toggle('visually-hidden',!text);draftStatus.dataset.saved=String(text==='Draft saved');};
    new MutationObserver(draftStatusVisibility).observe(draftStatus,{childList:true,characterData:true,subtree:true});draftStatusVisibility();
    const headerIcons={ 'hold-bill':'M8 5v14M16 5v14','show-held':'M6 3h12v18l-6-4-6 4z' };
    for(const button of actions.querySelectorAll('#hold-bill,#show-held')){
        const label=button.id==='hold-bill'?'Hold Bill':'Saved Bills';
        const icon=document.createElementNS('http://www.w3.org/2000/svg','svg'),path=document.createElementNS(icon.namespaceURI,'path');icon.setAttribute('viewBox','0 0 24 24');icon.setAttribute('fill','none');icon.setAttribute('stroke','currentColor');icon.setAttribute('stroke-width','1.8');icon.setAttribute('aria-hidden','true');path.setAttribute('d',headerIcons[button.id]);icon.append(path);
        const text=document.createElement('span');text.className='billing-action-label';text.textContent=label;button.replaceChildren(icon,text);button.title=label;button.setAttribute('aria-label',label);
    }
    byId('billing-customer-card').querySelector('h2').classList.add('visually-hidden');
    byId('billing-customer-card').querySelector('label[for="customer-current"]').textContent='Customer *';
    byId('billing-add-card').querySelector('h2').textContent='Scan or Search Product';
    byId('billing-add-card').querySelector('label[for="sale-scan"]')?.classList.add('visually-hidden');
    byId('sale-scan-help')?.classList.add('visually-hidden');
    // Reuse the application's tooltip component; help never submits or searches.
    const searchInfo=byId('sale-search-info');
    if(searchInfo&&window.bootstrap?.Tooltip){
        const infoTip=bootstrap.Tooltip.getOrCreateInstance(searchInfo,{trigger:'hover focus',placement:'bottom',fallbackPlacements:['right','left','top'],offset:[0,8],container:document.body,customClass:'billing-search-tooltip',animation:false,popperConfig:config=>({...config,modifiers:[...config.modifiers,{name:'preventOverflow',options:{padding:8,altAxis:true}}]})});
        searchInfo.addEventListener('click',()=>{searchInfo.focus();infoTip.show();});
        document.addEventListener('pointerdown',event=>{if(!searchInfo.contains(event.target))infoTip.hide();});
        document.addEventListener('keydown',event=>{if(event.key==='Escape')infoTip.hide();});
    }
    const scanRow=workspace.querySelector('.billing-scan-row:has(#sale-scan)');
    if(scanRow){const compactCamera=()=>{const camera=scanRow.querySelector('.sf-camera-trigger');if(camera&&camera.parentElement!==scanRow)scanRow.querySelector('.billing-product-search-field').after(camera);if(camera&&!camera.querySelector('svg')){camera.title='Scan with camera';camera.innerHTML='<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M3 6h4l2-3h6l2 3h4v15H3z"/><circle cx="12" cy="13" r="4"/></svg>';}};new MutationObserver(compactCamera).observe(scanRow,{childList:true,subtree:true});compactCamera();}
    const notes=byId('sale-notes'), notesLabel=workspace.querySelector('label[for="sale-notes"]');
    notes.placeholder='Add any notes for this sale (optional)...';
    // Reuse the textarea so collapsing notes never changes binding or draft data.
    const notesSection=document.createElement('details');notesSection.className="billing-notes";notesSection.id='billing-notes';
    const notesSummary=document.createElement('summary');notesSummary.textContent='Add Notes (Optional)';
    notesLabel.classList.add('visually-hidden');notesLabel.classList.remove('mt-2');
    notesSection.append(notesSummary,notesLabel,notes);
    const restoreNotes=()=>{notesSection.open=!!notes.value.trim();};
    workspace.addEventListener('billing-notes-restored',restoreNotes);restoreNotes();
    byId('complete-sale').insertAdjacentHTML('afterbegin','<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3zM9 7h6M9 11h6M9 15h4"/></svg> ');
    // CardType classifies CARD collections; CREDIT remains a customer receivable.
    const select=byId('payment-mode'),cardType=byId('payment-card-type'),tiles=document.createElement('div');
    tiles.className='billing-payment-tiles';tiles.setAttribute('role','group');tiles.setAttribute('aria-label','Payment method');
    const paths={CASH:'M3 5h18v14H3zM12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8',UPI:'M3 3h6v6H3zM15 3h6v6h-6zM3 15h6v6H3zM15 15h2v2h-2zM19 15h2v6h-6v-2h2',CARD:'M3 5h18v14H3zM3 10h18M6 15h4',BANK_TRANSFER:'m3 9 9-6 9 6H3zM5 10v9m7-9v9m7-9v9M3 21h18'};
    for(const [mode,type,label] of [['CASH','','Cash'],['UPI','','UPI'],['CARD','DEBIT','Debit Card'],['CARD','CREDIT','Credit Card'],['BANK_TRANSFER','','Bank Transfer']]){
        const tile=document.createElement('button');tile.type='button';tile.className='billing-payment-tile';tile.dataset.paymentMode=mode;tile.dataset.cardType=type;tile.title=label;
        tile.innerHTML='<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+paths[mode]+'"/></svg><span class="billing-payment-label"></span><span class="billing-payment-check" aria-hidden="true">\u2713</span>';
        tile.querySelector('.billing-payment-label').textContent=label;
        tile.onclick=()=>{
            if(select.value!==mode||cardType.value!==type){
                byId('payment-reference').value='';
                byId('cash-received').value='';
                const change=byId('calculate-change');if(change)change.checked=false;
            }
            select.value=mode;cardType.value=type;select.dispatchEvent(new Event('change',{bubbles:true}));
        };tiles.append(tile);
    }
    const legacyCard=document.createElement('p');legacyCard.id='billing-card-legacy';legacyCard.className='small';legacyCard.setAttribute('role','status');legacyCard.textContent='Card payment (type unspecified). Select Debit Card or Credit Card only if known.';
    const syncTiles=()=>{for(const tile of tiles.children){tile.setAttribute('aria-pressed',String(tile.dataset.paymentMode===select.value&&(select.value!=='CARD'||tile.dataset.cardType===cardType.value)));tile.disabled=select.disabled;}legacyCard.hidden=select.value!=='CARD'||!!cardType.value;};
    select.addEventListener('change',syncTiles);syncTiles();
    select.hidden=true;select.tabIndex=-1;select.setAttribute('aria-hidden','true');workspace.querySelector('label[for="payment-mode"]').hidden=true;byId('billing-simple-payment').before(tiles,legacyCard);
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
    changeToggle.innerHTML='<input id="calculate-change" type="checkbox" aria-controls="cash-change-field"/> Calculate Change <span>(Optional)</span>';
    byId('cash-change-field').before(changeToggle);
    const billTotal=document.createElement('div');billTotal.className='billing-cash-total';billTotal.innerHTML='<span>Bill Total</span><strong id="cash-bill-total">&#8377;0.00</strong>';
    byId('cash-change-field').prepend(billTotal);byId('cash-change-field').hidden=true;
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
    // The left page context and right checkout header have independent vertical flow.
    left.append(document.querySelector('.sf-main .sf-breadcrumb'),heading,lookup,byId('billing-cart-card'));
    const checkout=document.createElement('div');checkout.className='billing-checkout-pane';
    const payment=byId('billing-payment-card'),complete=byId('complete-sale');
    payment.append(notesSection);
    // Keep the original submit control in its form, with space reserved in normal flow.
    const checkoutActions=document.createElement('div');checkoutActions.className='billing-checkout-actions';
    checkoutActions.append(complete);
    checkout.append(actions,summary,payment,checkoutActions);layout.append(left,checkout);
    // Stacked layouts retain the actions near the title, ahead of a potentially long cart.
    const placeActions=()=>{const parent=workspace.clientWidth>=980?checkout:heading;if(actions.parentElement!==parent){if(parent===checkout)parent.prepend(actions);else parent.append(actions);}};
    placeActions();new ResizeObserver(placeActions).observe(workspace);
})();
