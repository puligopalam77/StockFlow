(async()=>{
 'use strict';
 const el=id=>document.getElementById(id),workspace=el('business-profile-workspace'),form=el('business-profile-form');if(!workspace)return;
 const scope='This company and all its branches',common='Products, Suppliers, Customers, Purchasing, Receiving, Sales, Inventory, Transfers and Reports (subject to user permissions)';
 let settings,busy=false,reviewing=false;
 const text=(tag,value)=>{const n=document.createElement(tag);n.textContent=value;return n;};
 const message=error=>{el('business-profile-message').textContent=error.message;el('business-profile-message').hidden=false;};
 const selected=()=>settings.profiles.find(p=>p.code===el('business-profile-code').value),dirty=()=>!!settings&&el('business-profile-code').value!==settings.code;
 const specialist=p=>p.warrantyAndRepairs?'Warranty and Repairs available (permission required)':'Electrical Warranty and Repairs hidden and direct access is denied';
 const serials=p=>p.serialTracking?'Available (permission required)':'Not enabled for this profile';
 function chips(categories){const list=text('ul','');list.className='bp-chips';for(const name of categories)list.append(text('li',name));return list;}
 function capability(label,value,enabled){const row=text('div','');row.className='bp-capability';const indicator=text('span','');indicator.className='bp-indicator'+(enabled?' bp-enabled':'');indicator.setAttribute('aria-hidden','true');const content=text('div','');content.append(text('strong',label),text('span',value));row.append(indicator,content);return row;}
 async function api(options={}){
  const response=await fetch('/api/business-profile',{credentials:'same-origin',...options,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''}});
  let data;try{data=await response.json();}catch{throw new Error('Unable to read the profile response. Reload and review the actual configuration before retrying.');}if(!response.ok){const error=new Error(data.message||'Unable to save the shop profile. Reload and try again.');error.status=response.status;throw error;}return data;
 }
 function preview(){
  const profile=selected();if(!profile)return;const current=settings.profiles.find(p=>p.code===settings.code),changing=dirty();
  el('business-profile-preview').replaceChildren(Object.assign(text('h3',profile.name),{className:'bp-profile-name'}),text('h3','Common Workflows'),text('p',common),text('h3','Specialist Capabilities'),capability('Warranty and Repairs',specialist(profile),profile.warrantyAndRepairs),capability('Serial Tracking',serials(profile),profile.serialTracking),text('h3','Starter Categories'),profile.categories.length?chips(profile.categories):text('p','No starter categories are seeded for this profile; the existing electrical catalog is unchanged.'));
  el('business-profile-change').textContent=changing?'Review the selected profile before applying.':'No profile change selected';el('business-profile-change').className=changing?'bp-change-pending':'';
  el('business-profile-pending').hidden=!changing;
  el('business-profile-unchanged').hidden=changing;
  el('business-profile-comparison-card').hidden=!changing;
  el('business-profile-review').hidden=!changing;
  el('business-profile-unchanged-title').textContent="You're already using "+settings.name+'.';
  const counts=`${settings.impact.productCount} products \u00b7 ${settings.impact.categoryCount} categories \u00b7 ${settings.impact.transactionCount} transaction records`;
  el('business-profile-unchanged-counts').textContent=counts;
  el('business-profile-comparison-details').hidden=!changing;
  const table=el('business-profile-comparison');table.replaceChildren();
  for(const [label,before,after]of [['Business Profile',settings.name,profile.name],['Common Workflows',common,common],['Specialist Features',current?specialist(current):'Not available',specialist(profile)],['Serial Tracking',current?serials(current):'Not available',serials(profile)],['Starter Categories',current?.categories.length?current.categories.length+' defined':'No starter categories seeded',profile.categories.length?profile.categories.length+' defined':'No starter categories seeded'],['Configuration Scope',scope,scope]]){const row=text('tr',''),heading=text('th',label),value=text('td',after);heading.scope='row';if(before!==after)value.className='bp-changed';row.append(heading,text('td',before),value);table.append(row);}
  el('business-profile-category-details').hidden=!changing||JSON.stringify(current?.categories||[])===JSON.stringify(profile.categories);
  const categories=el('business-profile-category-comparison');categories.replaceChildren();for(const [label,p]of [['Current profile',current],['Selected profile',profile]]){const group=text('div','');group.append(text('h3',label),p?.categories.length?chips(p.categories):text('p','No starter categories seeded.'));categories.append(group);}
  const retained=settings.catalogSwitchingAvailable&&['ELECTRICAL','BIKE_ACCESSORIES'].includes(settings.code)&&['ELECTRICAL','BIKE_ACCESSORIES'].includes(profile.code);
  const blocked=changing&&!retained&&(settings.impact.productCount>0||settings.impact.transactionCount>0);
  el('business-profile-fields').disabled=busy||reviewing||!settings.available||!settings.canEdit;
  el('business-profile-save').disabled=busy||reviewing||!changing||blocked||!settings.available||!settings.canEdit;
  el('business-profile-save').textContent=busy?'Applying…':'Apply Shop Setup';form.setAttribute('aria-busy',String(busy));form.dataset.saving=String(busy);
  el('business-profile-categories').disabled=!changing||!profile.categories.length;
  el('business-profile-impact').textContent=`${settings.impact.productCount} products · ${settings.impact.categoryCount} categories · ${settings.impact.transactionCount} transaction records. `+(blocked?'The profile cannot be changed yet. Existing records prevent this change under the current switching rules.':'Review the current company-wide scope before applying.');
  el('business-profile-preservation').textContent=settings.catalogSwitchingAvailable?(settings.dataVisibilityAvailable?'Products, contacts, purchases, receipts, sales, payments, reports and totals follow the active setup. Switch back to the original profile to view its saved records. Company details, stores, staff and security settings remain shared configuration.':'Catalog and current stock follow the selected setup. Apply database migration 051 manually to hide other setups’ transactions and totals. Existing records are retained.'):'Populated businesses require manual migration 050 before switching between Electrical and Bike Accessories. Existing records will not be deleted or relabeled.';
  el('business-profile-seeding').textContent=settings.catalogSwitchingAvailable?'The existing Apply workflow adds missing starter categories for the selected profile, where defined. Preview creates nothing; historical categories are not reassigned.':'Starter categories are created only on confirmed Apply when the option above is selected. Existing categories are not overwritten or reassigned.';
 }
 function render(proposed){
  const current=el('business-profile-current');current.replaceChildren();for(const [label,value]of [['Active Business Profile',settings.name],['Configuration Version',String(settings.version)],['Applies To',scope]]){const cell=text('div','');cell.append(text('dt',label),text('dd',value));current.append(cell);}
  el('business-profile-code').replaceChildren(...settings.profiles.map(p=>new Option(p.name,p.code)));el('business-profile-code').value=settings.profiles.some(p=>p.code===proposed)?proposed:settings.code;
  for(const id of ['business-profile-reason','business-profile-acknowledge']){el(id).required=!settings.catalogSwitchingAvailable;if(id.endsWith('reason')){el(id).hidden=!!settings.catalogSwitchingAvailable;document.querySelector('label[for="'+id+'"]').hidden=!!settings.catalogSwitchingAvailable;}else el(id).closest('label').hidden=!!settings.catalogSwitchingAvailable;}
  el('business-profile-categories').closest('label').hidden=!!settings.catalogSwitchingAvailable;
  el('business-profile-status').textContent=!settings.available?'Apply database migration 047 manually before changing the shop profile. Existing electrical workflows remain available.':!settings.canEdit?'Only an owner or administrator with Business & Billing edit permission can apply changes.':'Select a shop type to preview its categories and features. This setting applies to the company and all its branches.';
  preview();
 }
 el('business-profile-code').onchange=preview;
 const assistant=document.querySelector('.sf-help-open');if(assistant){const dock=text('div','');dock.className='bp-assistant-dock';workspace.append(dock);dock.append(assistant);}
 try{settings=await api();render();try{const saved=JSON.parse(sessionStorage.getItem('stockflow-profile-applied')||'null');sessionStorage.removeItem('stockflow-profile-applied');if(saved?.code===settings.code&&saved.version===settings.version)el('business-profile-status').textContent='Shop setup applied successfully. '+settings.name+' · Configuration Version '+settings.version+'.';}catch{}window.StockFlowUnsaved?.register(form,()=>busy||dirty(),()=>{el('business-profile-code').value=settings.code;preview();});}catch(error){message(error);el('business-profile-status').textContent='Unable to load the current configuration. Reload to try again.';}
 form.onsubmit=async event=>{
  event.preventDefault();if(!settings||busy||reviewing||el('business-profile-save').disabled||!form.reportValidity())return;
  const profile=selected(),request={code:profile.code,expectedVersion:settings.version,reason:settings.catalogSwitchingAvailable?'Apply '+profile.name+' catalog setup':el('business-profile-reason').value.trim(),createStarterCategories:settings.catalogSwitchingAvailable||el('business-profile-categories').checked,acknowledged:settings.catalogSwitchingAvailable||el('business-profile-acknowledge').checked};
  reviewing=true;preview();let confirmed=false;
  try{const decision=window.StockFlowDialog.confirm({title:'Apply Shop Setup?',confirmText:'Confirm & Apply',cancelText:'Cancel',message:['Current Profile: '+settings.name,'Selected Profile: '+profile.name,'Scope: '+scope,'Configuration Version: '+settings.version,'Specialist Features: '+specialist(profile),'Serial Tracking: '+serials(profile),'Existing records are preserved under their original profile. No historical records or stock quantities are converted.'].join('\n')});const dialog=el('sf-confirm-dialog');if(!dialog.dataset.profileFocus){dialog.dataset.profileFocus='true';dialog.addEventListener('keydown',event=>{if(event.key!=='Tab')return;const stops=[...dialog.querySelectorAll('button:not(:disabled)')].filter(b=>b.getClientRects().length),first=stops[0],last=stops.at(-1);if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}});}confirmed=await decision;}catch{message(new Error('Confirmation is unavailable. Reload the page before applying.'));}finally{reviewing=false;}
  if(!confirmed){preview();el('business-profile-save').focus();return;}
  busy=true;preview();el('business-profile-message').hidden=true;
  try{const result=await api({method:'PUT',body:JSON.stringify(request)});try{sessionStorage.setItem('stockflow-profile-applied',JSON.stringify(result));}catch{}window.StockFlowUnsaved?.markClean(form);window.StockFlowUnsaved?.allowNavigation();location.reload();}
  catch(error){busy=false;if(error.status===409){try{settings=await api();render(request.code);message(new Error('The configuration changed in another session. Current values have been refreshed; review the new version and confirm again.'));}catch{el('business-profile-fields').disabled=true;el('business-profile-save').disabled=true;message(new Error('The configuration changed, and refresh failed. Reload before reviewing or retrying.'));}return;}message(error);preview();}
 };
})();
