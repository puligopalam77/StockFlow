(() => {
 const form=document.getElementById('company-settings-form');if(!form)return;
 const byId=id=>document.getElementById(id),workspace=byId('company-settings-workspace');
 const savedFooter=byId('Input_InvoiceFooter')?.value,savedTerms=byId('Input_TermsAndConditions')?.value;
 for(const [name,count]of [['InvoiceFooter','invoice-footer-count'],['TermsAndConditions','company-terms-count']])byId('Input_'+name)?.closest('.company-field').append(byId(count));
 const snapshot=()=>JSON.stringify([...form.querySelectorAll('input,select,textarea')].map(i=>[i.name,i.type==='file'?[...i.files].map(f=>[f.name,f.size,f.lastModified]):i.type==='checkbox'?i.checked:i.value]));
 let initial=snapshot();window.StockFlowUnsaved?.register(form,()=>workspace.dataset.validationFailed==='true'||snapshot()!==initial,()=>{initial=snapshot();workspace.dataset.validationFailed='false';});
 byId('company-structure-address')?.addEventListener('click',()=>{
  if(workspace.dataset.edit!=='true')return;
  byId('UseStructuredAddress').value='true';byId('company-legacy-address').hidden=true;
  byId('Input_Address').disabled=true;byId('Input_Address').required=false;
  byId('company-structured-address').hidden=false;byId('company-district-field').hidden=false;
  for(const name of ['AddressLine1','AddressLine2','District'])byId('Input_'+name).disabled=false;
  byId('Input_AddressLine1').required=true;byId('Input_AddressLine1').focus();
 });
 for(const label of form.querySelectorAll('label[for^="Input_"]')){
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.classList.add('sf-icon');svg.setAttribute('aria-hidden','true');
  const use=document.createElementNS('http://www.w3.org/2000/svg','use');use.setAttribute('href','/images/icons.svg#'+(label.htmlFor.includes('Tax')?'chart':label.htmlFor.includes('Salesperson')?'users':'document'));svg.append(use);label.prepend(svg);
 }
 const website=document.getElementById('Input_Website');website?.addEventListener('change',()=>{if(/^www\./i.test(website.value))website.value='https://'+website.value;});
 for(const key of ['Input_GstNumber','Input_PanNumber'])document.getElementById(key)?.addEventListener('change',event=>{event.target.value=event.target.value.toUpperCase();});
 const logo=document.getElementById('LogoUpload'),preview=document.getElementById('company-logo-preview'),empty=document.getElementById('company-logo-empty'),remove=document.getElementById('RemoveLogo');
 byId('company-logo-choose')?.addEventListener('click',()=>logo.click());
 const original=preview?.getAttribute('src');let blob=null;
 const release=()=>{if(blob)URL.revokeObjectURL(blob);blob=null;};
 const show=src=>{if(!preview)return;preview.hidden=!src;empty.hidden=!!src;if(src)preview.src=src;else preview.removeAttribute('src');};
 function update(){if(!byId('Input_InvoiceFooter'))return;for(const [id,count] of [['Input_InvoiceFooter','invoice-footer-count'],['Input_TermsAndConditions','company-terms-count']]){const input=document.getElementById(id);document.getElementById(count).textContent=`${input.value.length}/${input.maxLength}`;}
  const footer=byId('Input_InvoiceFooter').value,terms=byId('Input_TermsAndConditions').value;
  byId('company-footer-preview-text').textContent=footer||'No footer message.';
  byId('company-terms-preview-text').textContent=terms;byId('company-terms-preview-text').hidden=!terms;
  byId('company-preview-state').textContent=workspace.dataset.validationFailed!=='true'&&footer===savedFooter&&terms===savedTerms?'Saved text preview':'Unsaved text preview — save Billing Settings to apply.';
  const example=name=>byId('Input_'+name).value.replaceAll('{YYYY}',String(new Date().getFullYear()))+'[sequence]';
  byId('company-number-preview').textContent='Format examples: '+example('InvoicePrefix')+' · '+example('ReceiptPrefix')+'. No number is allocated or reserved.';
 }
 logo?.addEventListener('change',()=>{release();const file=logo.files[0];if(!file)return;
  if(file.size>2000000||!['image/png','image/jpeg'].includes(file.type)){show(remove.value==='true'?null:original);logo.setCustomValidity('Choose a PNG or JPG logo up to 2 MB.');logo.reportValidity();logo.value='';return;}
  logo.setCustomValidity('');blob=URL.createObjectURL(file);show(blob);remove.value='false';
 });
 document.getElementById('company-logo-remove')?.addEventListener('click',()=>{release();logo.value='';logo.setCustomValidity('');remove.value='true';show(null);});
 form.addEventListener('input',update);
 document.getElementById('company-reset-defaults')?.addEventListener('click',()=>{
  const defaults={InvoicePrefix:'INV-{YYYY}-',ReceiptPrefix:'GRN-{YYYY}-',Currency:'INR',DefaultTaxMode:'EXCLUSIVE',DefaultPaymentTermsDays:'',DefaultSalespersonId:'',InvoiceFooter:'Thank you for your business.',TermsAndConditions:''};
  for(const [key,value] of Object.entries(defaults))document.getElementById('Input_'+key).value=value;
  update();byId('Input_InvoicePrefix').dispatchEvent(new Event('input',{bubbles:true}));
 });
 form.addEventListener('reset',()=>setTimeout(()=>{release();if(logo){logo.setCustomValidity('');remove.value='false';show(original);}
  form.querySelectorAll('.is-invalid').forEach(input=>{input.classList.remove('is-invalid');input.removeAttribute('aria-invalid');});
  form.querySelectorAll('[data-valmsg-for]').forEach(message=>{message.textContent='';message.classList.remove('field-validation-error');message.classList.add('field-validation-valid');});
  form.querySelectorAll('.validation-summary-errors').forEach(summary=>{summary.replaceChildren();summary.classList.replace('validation-summary-errors','validation-summary-valid');});update();
 },0));
 let confirming=false,policyDiscardConfirmed=false;
 form.addEventListener('submit',async event=>{
  if(form.dataset.saving==='true'||confirming){event.preventDefault();return;}if(!form.checkValidity())return;
  const policy=byId('billing-policy-form');
  if(!policyDiscardConfirmed&&policy&&window.StockFlowUnsaved?.hasChanges(policy)){
   event.preventDefault();confirming=true;
   try{if(await StockFlowDialog.discard('Save billing settings and discard the unsaved billing policy changes? The policy will not be saved.')){policyDiscardConfirmed=true;confirming=false;form.requestSubmit();}}finally{confirming=false;}
   return;
  }
  form.dataset.saving='true';const save=byId('company-settings-save');if(save){save.disabled=true;save.textContent='Saving...';}
 });
 const dockAssistant=()=>{const dock=document.querySelector('.company-assistant-dock'),assistant=document.querySelector('.sf-help-open');if(dock&&assistant)dock.append(assistant);};
 if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',dockAssistant,{once:true});else dockAssistant();
 window.addEventListener('pagehide',release);update();
 // A restored settings document must load current server values and edit tokens,
 // rather than revive a discarded draft or a disabled pre-submit Save button.
 window.addEventListener('pageshow',event=>{if(event.persisted){window.StockFlowUnsaved?.allowNavigation();location.reload();}});
 if(new URL(location.href).searchParams.has('section'))byId('company-view-title')?.focus({preventScroll:true});
})();
