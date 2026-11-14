window.CategoryRegistration=(()=>{
 'use strict';const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
 let prefixEdited=false,prefixTimer,prefixPending,controls,record=null,detailsAvailable=false,cleanState='',viewOnly=false,regenerateVersion=0,originalName='',generatedForName=null;
 const normalizedName=value=>(value||'').normalize('NFKC').trim().replace(/\s+/g,' ');
 const canRegenerate=()=>!!record&&!record.skuPrefixUsed&&!viewOnly&&normalizedName(controls.categoryName.value)!==originalName&&normalizedName(controls.categoryName.value)!==generatedForName;
 const state=()=>JSON.stringify({name:controls.categoryName.value,parent:controls.parentCategoryId.value,description:controls.description.value,status:el('category-status').value,code:controls.categoryCode.value,order:el('category-display-order').value,prefix:el('category-sku-prefix').value});
 function error(input,text){const id=input.id+'-category-error';let hint=el(id);if(!hint){hint=node('small',null,'category-field-error');hint.id=id;hint.setAttribute('role','alert');input.closest('.col-md-6,.col-12').append(hint);input.setAttribute('aria-describedby',id);}hint.textContent=text||'';hint.hidden=!text;input.setAttribute('aria-invalid',String(!!text));input.setCustomValidity(text||'');if(text){input.focus();}return false;}
 function trimDescription(){
  const input=controls.description,original=input.value,trimmed=original.trim();
  if(original===trimmed)return;
  const removed=original.length-original.trimStart().length,start=input.selectionStart,end=input.selectionEnd;
  input.value=trimmed;
  input.setSelectionRange(Math.max(0,Math.min(trimmed.length,start-removed)),Math.max(0,Math.min(trimmed.length,end-removed)));
  input.dispatchEvent(new Event('input',{bubbles:true}));
 }
 function failure(text,input){if(input)error(input,text);StockFlowNotify.show('error',text,{key:'category-validation'});return false;}
 async function suggestPrefix(){if(record||prefixEdited)return;const name=controls.categoryName.value;const pending=(async()=>{try{const response=await fetch('/api/categories/sku-prefix-suggestion?'+new URLSearchParams({name}),{credentials:'same-origin',cache:'no-store'});if(!response.ok)return;const result=await response.json();if(record||prefixEdited||controls.categoryName.value!==name)return;el('category-sku-prefix').disabled=result.available===false;if(result.available)el('category-sku-prefix').value=name.trim().length>=2?result.prefix:'';}catch{}})();prefixPending=pending;await pending;}
 function resetPrefixRegeneration(){
  regenerateVersion++;
  const action=el('category-prefix-regenerate');if(action){action.hidden=!canRegenerate();action.disabled=action.hidden;action.textContent='Regenerate';}
 }
 async function regeneratePrefix(){
  resetPrefixRegeneration();if(!canRegenerate())return;
  const version=regenerateVersion,name=controls.categoryName.value.trim(),id=record.categoryId,button=el('category-prefix-regenerate');
  if(name.length<2||!controls.categoryName.checkValidity()||!/[\p{L}\p{N}]/u.test(name))return failure('Enter a valid category name before regenerating the prefix.',controls.categoryName);
  button.disabled=true;button.textContent='Checking...';
  try{
   const latest=await fetch('/api/categories/'+id,{credentials:'same-origin',cache:'no-store'});if(!latest.ok)throw Error('Unable to check prefix eligibility. Try again.');const current=await latest.json();
   if(current.skuPrefixUsed||current.skuPrefix!==record.skuPrefix)throw Error('Category prefix changed or has been used. Reload the category before changing it.');
   const response=await fetch('/api/categories/sku-prefix-suggestion?'+new URLSearchParams({name}),{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw Error('Unable to suggest a prefix. Try again.');const result=await response.json();
   if(version!==regenerateVersion)return;
   if(!result.available)throw Error('SKU prefix generation is unavailable.');
   if(!/^[A-Z][A-Z0-9]{2,7}$/.test(result.prefix))throw Error('Suggested prefix is invalid. Try a different category name.');
   const prefix=el('category-sku-prefix');prefix.value=result.prefix;prefixEdited=true;generatedForName=normalizedName(name);prefix.dispatchEvent(new Event('input',{bubbles:true}));el('category-prefix-help').textContent='';
  }catch(problem){if(version===regenerateVersion)failure(problem.message,el('category-sku-prefix'));}
  finally{if(version===regenerateVersion){button.disabled=false;button.textContent='Regenerate';}}
 }
 function bind(value){
  controls=value;const fields=el('form-fields');
  function group(label,input){const wrap=node('div',null,'col-md-6'),caption=node('label',label,'form-label');caption.htmlFor=input.id;wrap.append(caption,input);return wrap;}
  const status=node('select',null,'form-select');status.id='category-status';status.dataset.sfSelect='native';status.add(new Option('Active','true'));status.add(new Option('Inactive','false'));const statusGroup=group('Status',status);
  const order=node('input',null,'form-control');order.id='category-display-order';order.type='number';order.min='0';order.max='99999';order.step='1';const orderGroup=group('Display Order (Optional)',order);
  const prefix=node('input',null,'form-control');prefix.id='category-sku-prefix';prefix.maxLength=8;prefix.pattern='[A-Za-z][A-Za-z0-9]{2,7}';prefix.placeholder='Suggested automatically';const prefixGroup=group('SKU Prefix (Optional)',prefix);const regenerate=node('button','Regenerate','category-prefix-regenerate');regenerate.id='category-prefix-regenerate';regenerate.type='button';regenerate.onclick=()=>{prefixPending=regeneratePrefix();};prefixGroup.querySelector('label').append(regenerate);const prefixHelp=node('small',null,'text-muted');prefixHelp.id='category-prefix-help';prefixGroup.append(prefixHelp);prefixHelp.setAttribute('aria-live','polite');prefix.setAttribute('aria-describedby',prefixHelp.id);prefix.title='Permanent after issuing a product code. Existing product SKUs are never renamed.';
  const description=controls.description.closest('.col-12');description.querySelector('label').textContent='Description (Optional)';controls.description.rows=2;
  const name=controls.categoryName.closest('.col-md-6'),parent=controls.parentCategoryId.closest('.col-md-6'),code=controls.categoryCode.closest('.col-md-6');parent.hidden=false;
  controls.categoryCode.readOnly=true;controls.categoryCode.placeholder='Generated on save';controls.categoryCode.removeAttribute('pattern');
  const previewGroup=el('category-path-preview').closest('section');previewGroup.hidden=true;fields.replaceChildren(name,parent,prefixGroup,orderGroup,description,code,statusGroup,previewGroup);
  const counter=node('small',null,'text-muted');counter.id='category-description-count';controls.description.after(counter);controls.description.addEventListener('input',()=>counter.textContent=controls.description.value.length+'/500');
  controls.description.addEventListener('input',event=>{if(event.inputType==='insertFromPaste')trimDescription();});
  controls.description.addEventListener('blur',trimDescription);
  for(const input of [controls.categoryName,controls.description,prefix,order])input.addEventListener('input',()=>error(input,''));controls.parentCategoryId.addEventListener('change',()=>error(controls.categoryName,''));
  prefix.addEventListener('input',()=>{prefixEdited=true;prefix.value=prefix.value.toUpperCase();resetPrefixRegeneration();if(record&&!record.skuPrefixUsed)el('category-prefix-help').textContent='';});controls.categoryName.addEventListener('input',()=>{resetPrefixRegeneration();clearTimeout(prefixTimer);prefixTimer=setTimeout(suggestPrefix,250);});
  el('master-form').addEventListener('invalid',event=>{event.preventDefault();const input=event.target;if(input===el('master-form').querySelector(':invalid'))failure(input.validationMessage,input);},true);
 }
 async function open(item){
  record=item;viewOnly=false;originalName=normalizedName(item?.categoryName);generatedForName=null;resetPrefixRegeneration();el('category-prefix-help').textContent=item?.skuPrefixUsed?'Locked: product codes have been issued. Existing SKUs and barcodes stay unchanged.':'';viewOnly=false;prefixEdited=false;clearTimeout(prefixTimer);for(const hint of el('form-fields').querySelectorAll('.category-field-error'))hint.hidden=true;for(const input of el('form-fields').querySelectorAll('input,textarea,select')){input.setCustomValidity('');input.removeAttribute('aria-invalid');}
  el('category-sku-prefix').value=item?.skuPrefix||'';el('category-sku-prefix').readOnly=item?.skuPrefixUsed===true;el('category-sku-prefix').disabled=false;await suggestPrefix();
  const response=await fetch('/api/categories/schema',{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw Error('Unable to load category settings. Try again.');detailsAvailable=!!(await response.json()).categoryDetailsAvailable;
  controls.categoryCode.closest('.col-md-6').hidden=!item;el('category-status').closest('.col-md-6').hidden=!item;controls.categoryCode.value=item?.categoryCode||'';controls.categoryCode.placeholder='Generated on save';el('category-display-order').value=item?.displayOrder||0;el('category-display-order').disabled=!detailsAvailable;
  el('category-status').value=String(item?.isActive!==false);el('category-status').querySelector('[value=false]').disabled=el('master-data').dataset.canDisable!=='true';
  controls.categoryName.closest('.col-md-6').querySelector('label').textContent='Category Name *';controls.categoryName.placeholder='e.g. Helmets';controls.description.placeholder='Optional description';controls.description.dispatchEvent(new Event('input'));el('editor-help').hidden=true;
  el('category-save').textContent=item?'Save Changes':'Save Category';const title=item?'Edit Category':'Add Category';el('editor-title').textContent=title;
  if(window.StockFlowMasterScreens?.page){const heading=document.querySelector('.master-screen-heading h1');if(heading)heading.textContent=title;}
 }
 async function beforeSave(body){
  trimDescription();body.description=controls.description.value||null;
  controls.categoryName.value=controls.categoryName.value.normalize('NFKC').trim().replace(/\s+/g,' ');body.categoryName=controls.categoryName.value;clearTimeout(prefixTimer);if(!record&&!prefixEdited)await suggestPrefix();else await prefixPending;
  if(viewOnly||!CategoryCascades.validate())return false;
  for(const input of [controls.categoryName,controls.description,el('category-sku-prefix'),el('category-display-order')])if(!input.checkValidity())return failure(input.validationMessage,input);
  if(record?.skuPrefix)body.expectedSkuPrefix=record.skuPrefix;
  body.isActive=record?el('category-status').value==='true':true;body.skuPrefix=el('category-sku-prefix').value.trim().toUpperCase()||null;
  if(detailsAvailable){body.categoryCode=record?.categoryCode||null;body.displayOrder=Number(el('category-display-order').value||0);}
  try{const response=await fetch('/api/categories',{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw Error('Unable to check existing categories. Try again.');const all=await response.json(),latest=record&&all.find(c=>c.categoryId===record.categoryId);if(record&&(!latest||latest.skuPrefix!==record.skuPrefix||(latest.skuPrefixUsed&&body.skuPrefix!==record.skuPrefix)))return failure('Category prefix changed or has been used. Reload the category before saving.',el('category-sku-prefix'));const existing=all.filter(c=>c.categoryId!==record?.categoryId),key=value=>value.normalize('NFKC').trim().replace(/\s+/g,' ').toLowerCase();
   if(existing.some(c=>(c.parentCategoryId||null)===(body.parentCategoryId||null)&&key(c.categoryName)===key(body.categoryName)))return failure('A category with this name already exists under the same parent.',controls.categoryName);
   if(body.skuPrefix&&existing.some(c=>c.skuPrefix===body.skuPrefix))return failure('SKU prefix already exists. Choose another prefix.',el('category-sku-prefix'));
  }catch(problem){return failure(problem.message);}
  if(record&&body.isActive!==record.isActive){const impact=record.subtreeProductCount??record.productCount??0;if(!await StockFlowDialog.confirm((body.isActive?'Activate':'Deactivate')+' '+record.categoryName+'? '+impact+' product records remain unchanged. '+(!body.isActive?'New assignment will be unavailable for this category and its subcategories.':'Independently inactive children stay inactive.'),{title:'Confirm category status change'}))return false;}
  StockFlowNotify.dismiss('category-validation');delete body.configuration;return true;
 }
 return {bind,open,beforeSave,saveError:problem=>{const text=problem.message||'Category could not be saved.';if(/prefix/i.test(text))error(el('category-sku-prefix'),text);else if(/name|already exists|duplicate/i.test(text))error(controls.categoryName,text);},setViewOnly:()=>{viewOnly=true;resetPrefixRegeneration();el('category-prefix-regenerate').hidden=true;},markClean:()=>cleanState=state(),hasChanges:()=>!!cleanState&&cleanState!==state()};
})();
