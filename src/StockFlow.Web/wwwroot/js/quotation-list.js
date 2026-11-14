// Read-only list presentation. Saved totals/status come from the scoped API.
window.StockFlowQuotationList=async function(root,options){
 'use strict';
 const $=id=>document.getElementById(id),api=Sales.api.bind(Sales);
 const node=(tag,text,cls)=>{const e=document.createElement(tag);if(text!=null)e.textContent=text;if(cls)e.className=cls;return e;};
 const states=['DRAFT','SENT','ACCEPTED','REJECTED','EXPIRED','CONVERTED','CANCELLED'];
 const label=value=>value?value[0]+value.slice(1).toLowerCase():'Unavailable';
 const money=value=>typeof value==='number'&&Number.isFinite(value)?new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(value):'Unavailable';
 const date=value=>{const iso=String(value||'').slice(0,10);return /^\d{4}-\d{2}-\d{2}$/.test(iso)?new Intl.DateTimeFormat('en-IN',{day:'2-digit',month:'short',year:'numeric',timeZone:'UTC'}).format(new Date(iso+'T00:00:00Z')):'Unavailable';};
 const fields={term:$('quote-search'),status:$('quote-status'),from:$('quote-from'),to:$('quote-to')};
 if($('quote-new'))$('quote-new').hidden=!options.permissions.create;
 if($('quote-export'))$('quote-export').hidden=!options.permissions.export;
 const query=new URL(location.href).searchParams;
 for(const [key,input]of Object.entries(fields))if(query.has(key))input.value=query.get(key);
 let sequence=0,timer,activeMenu=null,loaded=false;
 window.StockFlowLoading?.region($('quote-list-table'),{retry:()=>load()});
 $('quote-count').parentElement.hidden=true;
 const pager=StockFlowPagination.create({host:$('quote-pagination'),label:'quotations',size:Number(query.get('pageSize')),onChange:()=>load()});
 const initialPage=Number(query.get('page'));
 pager.state.page=Number.isSafeInteger(initialPage)&&initialPage>=1&&initialPage<=100000?initialPage:1;pager.state.known=false;
 const params=()=>{const p=new URLSearchParams();for(const [key,input]of Object.entries(fields))if(input.value.trim())p.set(key,input.value.trim());p.set('page',pager.state.page);p.set('pageSize',pager.state.size);return p;};
 function invalid(){
  const dates=[fields.from,fields.to];
  let error=dates.some(e=>!e.validity.valid)?'Enter valid dates between 2000 and 2100.':'';
  if(fields.from.value&&fields.to.value&&fields.from.value>fields.to.value)error='Date From must be on or before Date To.';
  if(fields.status.value&&!states.includes(fields.status.value))error='Choose a supported status.';
  $('quote-filter-error').textContent=error;$('quote-filter-error').hidden=!error;
  return !!error;
 }
 function exportState(ready){
  const a=$('quote-export');if(!a)return;
  ready=ready&&options.permissions.export;
  a.setAttribute('aria-disabled',String(!ready));
  a.tabIndex=ready?0:-1;
  a.title=ready?'Export matching quotations':'No confirmed quotation results to export.';
  if(ready&&options.permissions.export){const p=params();p.delete('page');p.delete('pageSize');a.href='/api/quotations/export?'+p;}
  else a.removeAttribute('href');
 }
 function closeMenu(){if(activeMenu){activeMenu.hidePopover();activeMenu=null;}}
 function link(text,href){const a=node('a',text);a.href=href;return a;}
 function render(rows){
  closeMenu();root.querySelectorAll('.quote-row-menu').forEach(e=>e.remove());$('quotation-rows').replaceChildren();
  for(const r of rows){
   const tr=node('tr'),identity=node('td'),customer=node('td'),status=node('td'),actions=node('td'),id=encodeURIComponent(r.quotationId);
   const detail='/Quotations/Details?id='+id;
   identity.append(link(r.quotationNumber,detail));customer.append(node('strong',r.customerName||'Unavailable'));
   if(r.customerMobile)customer.append(node('small',r.customerMobile));
   const badge=node('span',label(r.status),'quote-status quote-status-'+(states.includes(r.status)?r.status.toLowerCase():'unknown'));status.append(badge);
   const button=node('button','⋯','quote-row-trigger');button.type='button';button.setAttribute('aria-label','Actions for '+r.quotationNumber);
   const menu=node('div',null,'quote-row-menu');menu.id='quote-actions-'+r.quotationId;menu.setAttribute('popover','auto');menu.setAttribute('aria-label','Quotation actions');
   button.setAttribute('popovertarget',menu.id);menu.append(link('View',detail));
   if(options.permissions.edit&&!['CONVERTED','CANCELLED'].includes(r.status))menu.append(link('Edit','/Quotations/Edit?id='+id));
   if(options.permissions.print)menu.append(link('Print / Save PDF','/Quotations/Print?id='+id+'&revision='+r.currentRevision));
   if(options.permissions.status&&!['CONVERTED','CANCELLED'].includes(r.status))menu.append(link('Change Status',detail+'#quotation-state-form'));
   if(options.permissions.convert&&r.status==='ACCEPTED')menu.append(link('Convert to Sale','/Sales/Create?quotationId='+id));
   if(r.saleId&&root.dataset.canSaleView==='true')menu.append(link('Open Sale / Invoice','/Sales/Invoice/'+encodeURIComponent(r.saleId)));
   button.onclick=()=>{
    const rect=button.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(rect.right-190,innerWidth-198))+'px';
    menu.style.top=Math.max(8,Math.min(rect.bottom+4,innerHeight-menu.scrollHeight-12))+'px';activeMenu=menu;
   };
   menu.addEventListener('toggle',()=>{
    if(menu.matches(':popover-open')){const rect=button.getBoundingClientRect();menu.style.top=Math.max(8,Math.min(rect.bottom+4,innerHeight-menu.scrollHeight-12))+'px';}
    else if(activeMenu===menu)activeMenu=null;
   });
   actions.append(button);root.append(menu);
   tr.append(identity,customer,node('td',date(r.documentDate)),node('td',date(r.validUntil)),node('td',r.itemCount,'quote-items-count'),node('td',money(r.total),'quote-saved-total'),status,actions);
   $('quotation-rows').append(tr);
  }
 }
 async function load(){
  clearTimeout(timer);const token=++sequence;exportState(false);closeMenu();
  if(invalid()){pager.set({busy:false});return;}
  const p=params();history.replaceState(null,'',location.pathname+'?'+p);
  pager.set({busy:true});$('quote-list-status').textContent='Loading quotations…';$('quote-list-error').hidden=true;
  $('quote-list-table').hidden=false;$('quote-list-table').setAttribute('aria-busy','true');$('quote-list-empty').hidden=true;
  try{
   const result=await api('/api/quotations?'+p);if(token!==sequence)return;
   if(!Array.isArray(result.rows)||!Number.isSafeInteger(result.total)||result.total<0)throw Error('The quotation list response could not be confirmed.');
   if(pager.state.page>1&&(!result.rows.length||(pager.state.page-1)*pager.state.size>=result.total)){
    pager.state.page=Math.max(1,Math.ceil(result.total/pager.state.size));return await load();
   }
   render(result.rows);loaded=true;
   pager.set({busy:false,total:result.total,known:true});
   const start=result.total?(pager.state.page-1)*pager.state.size+1:0,end=result.total?Math.min(pager.state.page*pager.state.size,result.total):0;
   $('quote-count').textContent='Showing '+start+'–'+end+' of '+result.total+' quotations';
   $('quote-count').parentElement.hidden=!StockFlowPagination.shouldShow(result.total);
   $('quote-list-status').textContent=result.total+' saved quotation'+(result.total===1?'':'s');
   $('quote-list-table').hidden=!result.rows.length;$('quote-list-empty').hidden=!!result.rows.length;
   const filtered=Object.values(fields).some(e=>!!e.value.trim());
   $('quote-empty-title').textContent=filtered?'No quotations match your filters':'No quotations yet';
   $('quote-empty-text').textContent=filtered?'Try adjusting your search, status or date range.':options.permissions.create?'Create your first customer quotation using + New Quotation above.':'Saved customer quotations will appear here.';
   $('quote-empty-reset').hidden=!filtered;exportState(result.total>0);
  }catch(error){
   if(token!==sequence)return;
   $('quote-list-error').hidden=false;$('quote-list-error-text').textContent=error.message||'Try again.';
   $('quote-list-status').textContent=loaded?'Unable to refresh. Previous rows are shown.':'Unable to load quotations.';
   $('quote-list-empty').hidden=true;pager.set({busy:false});exportState(false);
  }finally{if(token===sequence)$('quote-list-table').setAttribute('aria-busy','false');}
 }
 function changed(debounce=false){
  ++sequence;clearTimeout(timer);pager.set({busy:false,page:1});exportState(false);
  if(invalid())return;
  if(debounce)timer=setTimeout(load,300);else load();
 }
 fields.term.addEventListener('input',()=>changed(true));
 for(const key of ['status','from','to'])fields[key].addEventListener('change',()=>changed());
 function reset(){for(const input of Object.values(fields))input.value='';changed();}
 $('quote-reset').onclick=$('quote-empty-reset').onclick=reset;$('quote-retry').onclick=()=>load();
 $('quotation-filters').onsubmit=event=>{event.preventDefault();changed();};
 $('quote-export')?.addEventListener('click',event=>{if(event.currentTarget.getAttribute('aria-disabled')==='true')event.preventDefault();});
 window.addEventListener('resize',closeMenu);await load();
};
