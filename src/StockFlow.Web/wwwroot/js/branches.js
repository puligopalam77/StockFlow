(() => {
 'use strict';const root=document.getElementById('stores-workspace');if(!root)return;
 const createPage=root.dataset.createPage==='true';
 const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;},api=window.sfBranchApi;
 if(createPage){
  const mode=el('store-gst-mode');mode.sfSelect?.destroy();mode.hidden=true;
  el('store-gst-choices').hidden=false;
  for(const radio of el('store-gst-choices').querySelectorAll('input'))radio.addEventListener('change',()=>{if(radio.checked){mode.value=radio.value;mode.dispatchEvent(new Event('change',{bubbles:true}));}});
 }
 let directory,pager,editing=null,structured=true,busy=false,savedNotice='',returnFocus,codeEdited=false,locations=[],locationError='',gstOptions={businesses:[],registrations:[]},gstLoadError='',openingStore=false;
 const show=(text,error=false)=>{el('branch-message').textContent=text;el('branch-message').className='alert '+(error?'alert-danger':'alert-success');el('branch-message').hidden=!text;};
 const run=fn=>Promise.resolve().then(fn).catch(e=>show(e.message,true));
 function action(label,fn){const b=node('button',label,'btn btn-outline-secondary');b.type='button';b.onclick=()=>run(fn);return b;}
 function link(label,href){const a=node('a',label,'btn btn-outline-secondary');a.href=href;return a;}
 function open(dialog){returnFocus=document.activeElement;if(createPage&&dialog==='store-dialog')el(dialog).hidden=false;else el(dialog).showModal();(el(dialog).querySelector('input:not([readonly]):not([disabled])')||el(dialog).querySelector('button'))?.focus();}
 async function close(dialog){if(busy)return;const form=el(dialog).querySelector('form');if(form&&window.StockFlowUnsaved?.hasChanges(form)&&!await StockFlowDialog.discard('Discard unsaved '+(dialog==='store-dialog'?'store':'stock location')+' changes?'))return;if(createPage&&dialog==='store-dialog'){window.StockFlowUnsaved?.allowNavigation();location.assign('/Admin/Stores');return;}el(dialog).close();returnFocus?.isConnected&&returnFocus.focus();}
 for(const b of root.querySelectorAll('[data-close]'))b.onclick=()=>run(()=>close(b.dataset.close));
 for(const dialog of root.querySelectorAll('dialog')){dialog.oncancel=e=>{e.preventDefault();run(()=>close(dialog.id));};dialog.addEventListener('keydown',e=>{if(e.key!=='Tab')return;const focus=[...dialog.querySelectorAll('button,a,input,select,textarea,summary')].filter(n=>!n.disabled&&n.getClientRects().length&&n.tabIndex>=0);const first=focus[0],last=focus.at(-1);if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}});}
 function setStructured(value){structured=value;el('store-legacy-address').hidden=value;el('store-address').disabled=value;el('store-structured-address').hidden=!value;el('store-addressLine2-wrap').hidden=!value;el('store-district-wrap').hidden=!value;el('store-pin-wrap').hidden=!value;for(const name of ['addressLine1','addressLine2','district','pinCode'])el('store-'+name).disabled=!value;}
 async function storeForm(row=null){
  if(openingStore)return;
  if(!directory?.managementAvailable)throw Error('Apply manual migration 089 before using store metadata forms.');
  if(!row&&!directory.shopSetupAvailable)throw Error('Apply manual migration 096 before adding shops with address and GST setup.');
  openingStore=true;
  try{
   editing=row;gstLoadError='';gstOptions={businesses:[],registrations:[]};
   if(root.dataset.gst?.toLowerCase()==='true'){
    try{gstOptions=await api('/api/gst/settings?catalog=false');}catch(e){gstLoadError='GST options could not be loaded. You can save without assigning a GSTIN. '+e.message;}
   }
   el('store-form').reset();el('store-form-error').hidden=true;el('store-additional-details').open=!!(row&&(row.addressLine2||row.district||row.alternatePhone||!row.isActive));
   for(const input of el('store-form').querySelectorAll('input,select,textarea'))if(input.name&&input.type!=='radio')input.value=row?.[input.name]??'';
   el('store-storeCode').readOnly=!!row;el('store-active').value=String(row?.isActive??true);el('store-active').disabled=false;
   el('store-type').value=String(row?.isHeadOffice??false);codeEdited=false;
   el('store-profile').value=StockFlowText.label(directory.businessProfileCode||'ELECTRICAL')+' (company-wide)';
   if(row?.state&&!Array.from(el('store-state').options).some(o=>o.value===row.state))el('store-state').add(new Option(row.state+' (saved value)',row.state));
   el('store-state').value=row?.state||'';
   for(const group of el('store-form').querySelectorAll('[data-store-edit-only]')){group.hidden=!row;for(const field of group.querySelectorAll('input,select,textarea'))field.disabled=!row;}
   setStructured(!row||!!row.addressLine1);el('store-original-address').textContent=row?.address?'Saved address for reference: '+row.address:'';
   el('store-dialog-title').textContent=row?'Edit Shop':'Add Shop';el('store-save').textContent=row?'Save Shop Changes':'Save Shop';
   el('store-main-stock-note').textContent=row?'Existing locations and IDs remain unchanged. Shop code is permanent.':'Main stock will be created automatically with this shop.';
   const businesses=el('store-gst-business');businesses.replaceChildren(new Option('Select legal business',''));
   for(const b of gstOptions.businesses||[])businesses.add(new Option(b.legalName,b.businessId));
   if(row?.gstBusinessId&&!Array.from(businesses.options).some(o=>Number(o.value)===row.gstBusinessId))businesses.add(new Option('Saved legal business',row.gstBusinessId));
   businesses.value=row?.gstBusinessId||((gstOptions.businesses||[]).length===1?gstOptions.businesses[0].businessId:'');
   const businessCount=(gstOptions.businesses||[]).length;
   businesses.disabled=!businessCount;
   el('store-gst-business-select-wrap').hidden=businessCount<2;
   el('store-gst-business-readonly-wrap').hidden=businessCount>1;
   el('store-gst-business-name').value=(gstOptions.businesses||[]).find(b=>Number(b.businessId)===Number(businesses.value))?.legalName||(row?.gstBusinessId?'Saved legal business':'Not GST Registered');
   el('store-gst-mode-wrap').hidden=!!row;el('store-gst-mode').disabled=!!row;el('store-gst-mode').value='UNREGISTERED';
   el('store-gst-settings-link').hidden=root.dataset.gst?.toLowerCase()!=='true';
   el('store-dialog').classList.toggle('stores-compact',!row);gstChoices();
   window.StockFlowUnsaved?.markSaved(el('store-form'));open('store-dialog');
  }finally{openingStore=false;}
 }
 function eligibleRegistrations(mode,business,state){
  const legalIds=new Set((gstOptions.businesses||[]).map(b=>b.businessId));
  return (gstOptions.registrations||[]).filter(r=>legalIds.has(r.businessId)&&(!business||r.businessId===business)&&(!state||r.stateCode===state)&&r.status==='ACTIVE'&&gstOptions.today&&r.effectiveFrom&&(r.effectiveFrom||'').slice(0,10)<=gstOptions.today&&(!r.effectiveTo||r.effectiveTo.slice(0,10)>=gstOptions.today)&&(mode==='UNREGISTERED'?r.registrationType==='UNREGISTERED':['REGULAR','COMPOSITION'].includes(r.registrationType)&&!!r.gstin));
 }
 function gstChoices(){
  const state=el('store-state').selectedOptions[0]?.dataset.stateCode,business=Number(el('store-gst-business').value);
  const canMap=!editing&&el('store-active').value==='true'&&root.dataset.gstEdit?.toLowerCase()==='true'&&directory.shopSetupAvailable;
  el('store-gst-mode').querySelector('option[value="EXISTING"]').disabled=!canMap||!eligibleRegistrations('EXISTING',business,state).length;
  if(el('store-gst-mode').selectedOptions[0]?.disabled)el('store-gst-mode').value='UNREGISTERED';
  const mode=editing?'UNREGISTERED':el('store-gst-mode').value,selected=el('store-gst-registration').value;
  const target=el('store-gst-registration');target.replaceChildren(new Option('Choose an applicable registration',''));
  for(const r of canMap&&business&&(state||mode==='EXISTING')?eligibleRegistrations(mode,business,state):[]){
   target.add(new Option((r.gstin||'Unregistered business')+' / '+StockFlowText.label(r.registrationType),r.registrationId));
  }
  if(Array.from(target.options).some(o=>o.value===selected))target.value=selected;
  const applicable=!editing&&(mode==='EXISTING'||!!target.value);
  const registration=mode==='EXISTING'?(gstOptions.registrations||[]).find(r=>String(r.registrationId)===target.value):null;
  const stateField=el('store-state'),stateOption=registration?Array.from(stateField.options).find(o=>o.dataset.stateCode===registration.stateCode):null;
  const changed=stateOption&&stateField.value!==stateOption.value;
  stateField.disabled=!!stateOption;
  if(changed){stateField.value=stateOption.value;stateField.dispatchEvent(new Event('change',{bubbles:true}));}
  const showRegistration=!editing&&(mode==='EXISTING'||target.options.length>1);
  target.disabled=!showRegistration;target.required=mode==='EXISTING'&&!editing;el('store-gst-registration-wrap').hidden=!showRegistration;
  for(const name of ['addressLine1','city','state','district','pinCode'])el('store-'+name).required=applicable;
  el('store-gst-business').required=applicable;
  for(const radio of el('store-gst-choices').querySelectorAll('input')){radio.checked=radio.value===mode;radio.disabled=Array.from(el('store-gst-mode').options).find(o=>o.value===radio.value)?.disabled??true;}
  el('store-gst-state-badge').hidden=!stateOption;el('store-gst-state-badge').textContent=stateOption?'State: '+stateOption.value:'';
  el('store-gst-registration-label').textContent=mode==='UNREGISTERED'?'Unregistered setup (optional)':'GST Number (GSTIN) *';
  el('store-gst-help').textContent=gstLoadError||(editing?'Mapping changes remain in GST Settings with their effective dates and audit history.':stateOption?'State is fixed by the selected GSTIN. Enter this shop\'s actual premises address; it may differ from the registered address. Select Not GST Registered to unlock State.':mode==='EXISTING'?'Select an active registration for this legal business. Its State will be applied to the shop.':target.value?'The selected unregistered setup will be assigned. Billing still checks setup readiness.':!eligibleRegistrations('EXISTING',business,state).length?'No active GST registration is available for this business and State. Save as Not GST Registered; complete the applicable tax setup before billing.':'Save without assigning a GSTIN, or select GST Registered to use an available registration. Billing remains subject to setup readiness.');
 }
 for(const id of ['store-gst-business','store-gst-mode','store-state','store-gst-registration'])el(id).addEventListener('change',gstChoices);
 el('store-active').addEventListener('change',()=>{if(!editing&&el('store-active').value==='false'){el('store-gst-mode').value='UNREGISTERED';gstChoices();}});
 el('store-storeCode').addEventListener('input',()=>{codeEdited=true;});
 el('store-storeName').addEventListener('input',()=>{if(editing||codeEdited)return;const base=el('store-storeName').value.normalize('NFKD').replace(/[\u0300-\u036f]/g,'').toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-|-$/g,'').slice(0,40);let code=base,n=2;const used=new Set((directory?.rows||[]).map(s=>s.storeCode.toUpperCase()));while(code&&used.has(code))code=base+'-'+n++;el('store-storeCode').value=code;});
 el('store-convert-address').onclick=()=>{setStructured(true);el('store-addressLine1').focus();};
 const body=form=>{const value={};for(const [key,text]of new FormData(form))if(key!=='gstModeChoice')value[key]=text===''?null:text;return value;};
 async function submit(form,button,error,save){if(busy||!form.reportValidity())return;busy=true;form.dataset.saving='true';button.disabled=true;const label=button.textContent;button.textContent='Saving...';error.hidden=true;try{await save();window.StockFlowUnsaved?.markSaved(form);if(createPage){window.StockFlowUnsaved?.allowNavigation();location.assign('/Admin/Stores?search='+encodeURIComponent(el('store-storeCode').value.trim()));return;}form.closest('dialog').close();returnFocus?.isConnected&&returnFocus.focus();show(savedNotice);await pager.reload();await refreshLocations().catch(e=>show(savedNotice+' '+e.message,true));}catch(e){error.textContent=e.message;error.hidden=false;error.scrollIntoView({block:'nearest'});}finally{busy=false;form.dataset.saving='false';button.disabled=false;button.textContent=label;}}
 el('store-form').onsubmit=e=>{e.preventDefault();const line=el('store-addressLine1');line.setCustomValidity('');if(editing&&structured&&['addressLine2','district','pinCode'].some(k=>el('store-'+k).value.trim())&&!line.value.trim())line.setCustomValidity('Enter Address Line 1 for structured address details.');submit(e.target,el('store-save'),el('store-form-error'),async()=>{const input=body(e.target);if(input.gstSetupMode==='UNREGISTERED'&&!input.gstRegistrationId)input.gstSetupMode='LATER';input.state=el('store-state').value||null;if(input.gstBusinessId)input.gstBusinessId=Number(input.gstBusinessId);if(input.gstRegistrationId)input.gstRegistrationId=Number(input.gstRegistrationId);input.isHeadOffice=el('store-type').value==='true';input.isActive=el('store-active').value==='true';if(editing){input.isActive=el('store-active').value==='true';input.expectedVersion=editing.version;await api('/api/branches/stores/'+editing.storeId,input,'PUT');savedNotice='Store details saved. Existing locations and historical documents are unchanged.';}else{const result=await api('/api/branches/stores',input);savedNotice='Shop saved (ID '+result.storeId+'). Main stock location created. '+(input.isActive?'The shop is available for staff assignment. Checkout validates address and GST setup.':'Activate the shop before assigning staff.');}});};
 el('store-addressLine1').addEventListener('input',()=>el('store-addressLine1').setCustomValidity(''));
 if(createPage){
  run(async()=>{directory=await api('/api/branches/directory?page=1&pageSize=10');if(!directory.canCreate)throw Error('Only an authorized business owner can add shops.');await storeForm();});
  return;
 }
 async function locationForm(storeId=null){const stores=await api('/api/branches/stores');el('location-form').reset();el('location-form-error').hidden=true;el('location-store').replaceChildren(new Option('Central warehouse (no retail store)',''));for(const s of stores)el('location-store').add(new Option(s.storeName,s.storeId));el('location-store').value=storeId||'';locationType();window.StockFlowUnsaved?.markSaved(el('location-form'));open('location-dialog');}
 function locationType(){const central=!el('location-store').value;for(const option of el('location-type').options)option.disabled=central&&option.value!=='WAREHOUSE';if(central)el('location-type').value='WAREHOUSE';}el('location-store').onchange=locationType;
 async function details(id){const data=await api('/api/branches/stores/'+id),s=data.store,host=el('store-details-body');host.replaceChildren();el('store-details-title').textContent=s.storeName+' / '+s.storeCode;for(const [heading,text]of [['Store identity',s.storeName+' / '+s.storeCode+' / '+(s.isActive?'Active':'Inactive')],['Physical address',s.address||'Not recorded'],['Contact',[s.mobileNumber,s.alternatePhone,s.email].filter(Boolean).join(' / ')||'Not recorded'],['Business profile',StockFlowText.label(s.businessProfileCode)+' (company-wide; not independently configurable)'],['GST registration',s.gstStatus+(s.gstRegistrationType?' / '+s.gstRegistrationType:'')],['Shop setup readiness',s.setupReadiness||'Review in GST Settings']]){const card=node('section',null,'stores-detail-card');card.append(node('h3',heading),node('p',text));host.append(card);}
 const locations=node('section',null,'stores-detail-card');locations.append(node('h3','Authorized stock locations'));for(const l of data.locations)locations.append(node('p',l.locationName+' / '+StockFlowText.label(l.locationType)+' / ID '+l.locationId));if(!data.locations.length)locations.append(node('p','No visible active stock locations.'));if(directory.canCreate)locations.append(action('Add Stock Location',()=>{el('store-details-dialog').close();return locationForm(id);}));host.append(locations);
 const next=node('section',null,'stores-detail-card');next.append(node('h3','Setup and staff access'),node('p',s.salesFloorCount?'An active sales floor exists. Billing still checks permissions, applicable GST mapping and effective tax configuration.':'No active sales floor is visible. Add one if this store will support checkout.'),node('p','GST Settings and operational workflows use the active store/location. Use the application shop/location switcher before configuring a different shop. Physical addresses have not been verified as registered additional places of business.'));
 if(root.dataset.gst?.toLowerCase()==='true')next.append(link('Manage GST Mapping','/Admin/Gst'));if(directory.canAssignAccess&&root.dataset.userAccess?.toLowerCase()==='true')next.append(link('Manage Staff Access','/Admin/Users'));if(directory.canManage&&directory.managementAvailable)next.append(action('Edit Store',()=>{el('store-details-dialog').close();storeForm(s);}));host.append(next);open('store-details-dialog');}
 const initialSearch=new URLSearchParams(location.search).get('search');if(initialSearch)el('store-search').value=initialSearch;
 pager=StockFlowPagination.remote({root,host:el('store-pager'),label:'Shops',filterRoot:el('store-filters'),load:async request=>{const query=new URLSearchParams({page:request.page,pageSize:request.size,search:el('store-search').value,status:el('store-status').value});const response=await fetch('/api/branches/directory?'+query,{signal:request.signal});const data=await response.json();if(!response.ok)throw Error(data.message||Object.values(data.errors||{}).flat().join(' ')||'Unable to load stores.');return data;},render:(rows,data)=>{directory=data;el('stores-count').textContent=data.storeCount;el('active-stores-count').textContent=data.activeStoreCount??'ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â';el('inactive-stores-count').textContent=data.inactiveStoreCount??'ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â';el('locations-count').textContent=data.locationCount;el('warehouses-count').textContent=data.warehouseCount;el('missing-floor-count').textContent=data.missingSalesFloorCount;el('store-add').disabled=!data.canCreate||!data.managementAvailable||!data.shopSetupAvailable;el('location-add').disabled=!data.canCreate;el('stores-capability').hidden=data.managementAvailable&&data.shopSetupAvailable;el('stores-capability').textContent=!data.managementAvailable?'Apply manual migration 089 to enable structured shop metadata.':'Apply manual migration 096 to enable address/GST setup for new shops. Existing shops and stock locations remain available.';el('store-rows').replaceChildren();el('store-empty').hidden=rows.length>0;for(const s of rows){const row=node('tr');for(const text of [s.storeName+' / '+s.storeCode,s.isHeadOffice?'Head Office':'Retail Shop',StockFlowText.label(s.businessProfileCode)+' (company-wide)',[s.city,s.state].filter(Boolean).join(', ')||'Not recorded',s.locationCount,s.gstStatus+(s.gstRegistrationType?' / '+s.gstRegistrationType:''),s.setupReadiness||'Review in GST Settings',s.isActive?'Active':'Inactive'])row.append(node('td',text));const actions=node('td');actions.append(action('View Details',()=>details(s.storeId)));if(data.canManage&&data.managementAvailable)actions.append(action(['Address incomplete','Legal business not selected','Sales location missing','GST setup pending','GST identity/state mismatch','Unsupported GST scheme'].includes(s.setupReadiness)?'Complete Setup':'Edit',()=>storeForm(s)));row.append(actions);el('store-rows').append(row);}if(savedNotice){show(savedNotice);savedNotice='';}},onError:e=>show((savedNotice?savedNotice+' Directory refresh failed; use Refresh Directory. ':'')+e.message,true)});
 el('store-filters').onsubmit=e=>{e.preventDefault();pager.reload({reset:true});};el('store-reset').onclick=()=>{el('store-filters').reset();pager.reload({reset:true});};el('store-refresh').onclick=()=>run(async()=>{await pager.reload();await refreshLocations();});el('store-add').onclick=()=>{location.assign('/Admin/Stores/Create');};el('location-add').onclick=()=>run(()=>locationForm());
 el('location-form').onsubmit=e=>{e.preventDefault();submit(e.target,el('location-save'),el('location-form-error'),async()=>{const input=body(e.target);input.storeId=Number(input.storeId)||null;const result=await api('/api/branches/locations',input);savedNotice='Stock location saved (ID '+result.locationId+'). No inventory movement was posted. Reload to refresh available operational locations.';});};
 function renderLocations(){const host=el('stock-location-rows');host.replaceChildren();for(const l of locations){const row=node('tr');for(const value of [l.storeName,l.locationName,StockFlowText.label(l.locationType)])row.append(node('td',value));host.append(row);}el('stock-location-status').textContent=locationError||(locations.length?locations.length+' authorized active locations':'No authorized active stock locations.');}
 async function refreshLocations(){try{const context=await api('/api/branches/context');locations=context.locations||[];locationError='';renderLocations();return context;}catch(e){locations=[];locationError='Unable to load stock locations. '+e.message;renderLocations();throw e;}}
 function selectTab(name){for(const id of ['shops','stock-locations']){const selected=id===name;el(id+'-tab').setAttribute('aria-selected',String(selected));el(id+'-tab').tabIndex=selected?0:-1;el(id+'-panel').hidden=!selected;}el('store-add').hidden=name!=='shops';}
 for(const name of ['shops','stock-locations']){el(name+'-tab').onclick=()=>selectTab(name);el(name+'-tab').addEventListener('keydown',e=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(e.key)){e.preventDefault();const next=e.key==='Home'?'shops':e.key==='End'?'stock-locations':name==='shops'?'stock-locations':'shops';selectTab(next);el(next+'-tab').focus();}});}
 const dock=()=>{const assistant=document.querySelector('.sf-help-open');if(assistant)root.querySelector('.stores-assistant-dock').append(assistant);};if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',dock,{once:true});else dock();pager.reload();
 run(refreshLocations);
})();
