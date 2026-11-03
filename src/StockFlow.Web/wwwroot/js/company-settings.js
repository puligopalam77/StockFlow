(() => {
 const form=document.getElementById('company-settings-form');if(!form)return;
 for(const label of form.querySelectorAll('label[for^="Input_"]')){
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.classList.add('sf-icon');svg.setAttribute('aria-hidden','true');
  const use=document.createElementNS('http://www.w3.org/2000/svg','use');use.setAttribute('href','/images/icons.svg#'+(label.htmlFor.includes('Tax')?'chart':label.htmlFor.includes('Salesperson')?'users':'document'));svg.append(use);label.prepend(svg);
 }
 const website=document.getElementById('Input_Website');website.addEventListener('change',()=>{if(/^www\./i.test(website.value))website.value='https://'+website.value;});
 for(const key of ['Input_GstNumber','Input_PanNumber'])document.getElementById(key).addEventListener('change',event=>{event.target.value=event.target.value.toUpperCase();});
 const logo=document.getElementById('LogoUpload'),preview=document.getElementById('company-logo-preview'),empty=document.getElementById('company-logo-empty'),remove=document.getElementById('RemoveLogo');
 const original=preview.getAttribute('src');let blob=null;
 const release=()=>{if(blob)URL.revokeObjectURL(blob);blob=null;};
 const show=src=>{preview.hidden=!src;empty.hidden=!!src;if(src)preview.src=src;else preview.removeAttribute('src');};
 function update(){for(const [id,count] of [['Input_InvoiceFooter','invoice-footer-count'],['Input_TermsAndConditions','company-terms-count']]){const input=document.getElementById(id);document.getElementById(count).textContent=`${input.value.length}/${input.maxLength}`;}}
 logo.addEventListener('change',()=>{release();const file=logo.files[0];if(!file)return;
  if(file.size>2000000||!['image/png','image/jpeg'].includes(file.type)){logo.setCustomValidity('Choose a PNG or JPG logo up to 2 MB.');logo.reportValidity();logo.value='';return;}
  logo.setCustomValidity('');blob=URL.createObjectURL(file);show(blob);remove.value='false';
 });
 document.getElementById('company-logo-remove').onclick=()=>{release();logo.value='';logo.setCustomValidity('');remove.value='true';show(null);};
 form.addEventListener('input',update);
 document.getElementById('company-reset-defaults').onclick=()=>{
  const defaults={InvoicePrefix:'INV-{YYYY}-',ReceiptPrefix:'GRN-{YYYY}-',Currency:'INR',DefaultTaxMode:'EXCLUSIVE',DefaultPaymentTermsDays:'',DefaultSalespersonId:'',InvoiceFooter:'Thank you for your business.',TermsAndConditions:''};
  for(const [key,value] of Object.entries(defaults))document.getElementById('Input_'+key).value=value;
  update();
 };
 form.addEventListener('reset',()=>setTimeout(()=>{release();logo.setCustomValidity('');remove.value='false';show(original);
  form.querySelectorAll('.is-invalid').forEach(input=>{input.classList.remove('is-invalid');input.removeAttribute('aria-invalid');});
  form.querySelectorAll('[data-valmsg-for]').forEach(message=>{message.textContent='';message.classList.remove('field-validation-error');message.classList.add('field-validation-valid');});
  form.querySelectorAll('.validation-summary-errors').forEach(summary=>{summary.replaceChildren();summary.classList.replace('validation-summary-errors','validation-summary-valid');});update();
 },0));
 window.addEventListener('pagehide',release);update();
})();
