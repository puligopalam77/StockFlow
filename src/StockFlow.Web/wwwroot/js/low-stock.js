(() => {
 'use strict';
 const $=id=>document.getElementById(id),root=$('low-stock-workspace');
 if(!root)return;
 const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
 const number=n=>typeof n==='number'&&Number.isFinite(n)?n.toLocaleString('en-IN'):'—';
 const filter=$('low-stock-term'),status=$('low-stock-status'),params=new URL(location.href).searchParams;
 filter.value=params.get('term')||'';status.value=params.get('stockStatus')||'';
 let sequence=0,timer,menu=null;
 const help=$('low-stock-help'),helpButton=$('low-stock-help-button');
 help.addEventListener('toggle',()=>{if(help.matches(':popover-open')){const r=helpButton.getBoundingClientRect();help.style.left=Math.max(8,Math.min(r.left,innerWidth-help.offsetWidth-8))+'px';help.style.top=Math.max(8,Math.min(r.bottom+6,innerHeight-help.offsetHeight-8))+'px';help.focus();}else if(document.hasFocus()&&(help.contains(document.activeElement)||document.activeElement===document.body))helpButton.focus();});
 const pager=StockFlowPagination.create({host:$('low-stock-pagination'),label:'items',size:Number(params.get('pageSize')),onChange:()=>load()});
 const initial=Number(params.get('page'));pager.state.page=Number.isInteger(initial)&&initial>0&&initial<=100000?initial:1;pager.state.known=false;
 function closeMenu(){if(menu){menu.hidePopover();menu=null;}}
 const query=()=>{const p=new URLSearchParams({lowStockOnly:true,paged:true,page:pager.state.page,pageSize:pager.state.size});if(filter.value.trim())p.set('term',filter.value.trim());if(status.value)p.set('stockStatus',status.value);return p;};
 function blankMetrics(){for(const id of ['low-stock-total','low-stock-out','low-stock-required'])$(id).textContent='—';}
 function link(text,href){const a=node('a',text);a.href=href;return a;}
 function render(rows){
  closeMenu();root.querySelectorAll('.low-stock-menu').forEach(e=>e.remove());$('low-stock-results').replaceChildren();
  for(const item of rows){
   const row=node('tr'),identity=node('td'),product=node('div',null,'low-stock-product'),copy=node('div'),thumb=node('span',null,'low-stock-thumb');
   thumb.innerHTML='<svg class="sf-icon" aria-hidden="true"><use href="/images/icons.svg#box" /></svg>';
   if(item.hasProductImage){const image=node('img');image.src='/api/products/'+encodeURIComponent(item.productId)+'/image';image.alt='';image.loading='lazy';image.onerror=()=>image.remove();thumb.append(image);}
   copy.append(node('strong',item.productLabel||item.productName),node('small',item.attributeSummary||item.variantName||''));
   if(item.hasDiscrepancy)copy.append(node('small','Serial-unit count differs; review stock history.','low-stock-discrepancy'));
   product.append(thumb,copy);identity.append(product);
   const badge=node('span',item.stockStatus==='OUT'?'Out of Stock':item.stockStatus==='REVIEW'?'Review Negative Balance':'At / Below Minimum','low-stock-badge low-stock-'+item.stockStatus);
   const state=node('td');state.append(badge);const action=node('td'),button=node('button','⋯','btn btn-dark low-stock-action');button.type='button';button.setAttribute('aria-label','Actions for '+(item.sku||item.productName));
   const popover=node('div',null,'low-stock-menu');popover.id='low-stock-actions-'+item.variantId;popover.setAttribute('popover','auto');button.setAttribute('popovertarget',popover.id);
   popover.append(link('View Stock','/Inventory?term='+encodeURIComponent(item.sku||item.productName)));
   if(root.dataset.canReceive==='true')popover.append(link('Receive Stock','/Purchases/Create'));
   if(root.dataset.canTransfer==='true')popover.append(link('Review Transfers','/Transfers'));
   popover.addEventListener('toggle',()=>{if(popover.matches(':popover-open')){menu=popover;const r=button.getBoundingClientRect();popover.style.left=Math.max(8,Math.min(r.right-185,innerWidth-193))+'px';popover.style.top=Math.max(8,Math.min(r.bottom+4,innerHeight-popover.scrollHeight-12))+'px';}else if(menu===popover)menu=null;});
   root.append(popover);action.append(button);
   row.append(identity,node('td',item.sku||'—'),node('td',number(item.currentStock),'low-stock-number'),node('td',number(item.minimumStockLevel),'low-stock-number'),node('td',number(item.quantityNeeded),'low-stock-number'),state,action);
   $('low-stock-results').append(row);
  }
 }
 async function load(){
  clearTimeout(timer);const mine=++sequence;closeMenu();blankMetrics();pager.set({busy:true});
  $('low-stock-summary').textContent='Loading low-stock information…';$('low-stock-message').hidden=$('low-stock-empty').hidden=true;
  $('low-stock-table').hidden=true;$('low-stock-footer').hidden=true;root.setAttribute('aria-busy','true');
  const p=query(),url=new URL(location.href);url.search='';for(const [key,value]of p)if(!['paged','lowStockOnly'].includes(key))url.searchParams.set(key,value);history.replaceState(null,'',url);
  try{
   const response=await fetch('/api/inventory?'+p),data=await response.json();if(mine!==sequence)return;
   if(!response.ok)throw Error(data.message||'Please try again.');
   if(!Array.isArray(data.rows)||!Number.isSafeInteger(data.total)||data.total<0||!data.summary||!['lowStockVariants','outOfStock','unitsToReplenish'].every(k=>typeof data.summary[k]==='number'&&Number.isFinite(data.summary[k])))throw Error('The stock response could not be confirmed.');
   if(pager.state.page>1&&(!data.rows.length||(pager.state.page-1)*pager.state.size>=data.total)){pager.state.page=Math.max(1,Math.ceil(data.total/pager.state.size));return await load();}
   render(data.rows);pager.set({busy:false,total:data.total,known:true});
   $('low-stock-total').textContent=number(data.summary.lowStockVariants);$('low-stock-out').textContent=number(data.summary.outOfStock);$('low-stock-required').textContent=number(data.summary.unitsToReplenish);
   $('low-stock-summary').textContent=data.total+' matching shortage variant'+(data.total===1?'':'s')+'.';
   $('low-stock-table').hidden=!data.rows.length;$('low-stock-empty').hidden=!!data.rows.length;
   const filtered=!!filter.value.trim()||!!status.value;
   $('low-stock-empty-title').textContent=filtered?'No items match your filters':'No low-stock items';
   $('low-stock-empty-text').textContent=filtered?'Try adjusting the search or stock status.':'No products currently match the low-stock criteria for this location.';
   $('low-stock-empty-reset').hidden=!filtered;
   $('low-stock-footer').hidden=!StockFlowPagination.shouldShow(data.total);
   $('low-stock-range').textContent=data.total?'Showing '+((pager.state.page-1)*pager.state.size+1)+'–'+Math.min(pager.state.page*pager.state.size,data.total)+' of '+data.total+' items':'Showing 0 of 0 items';
  }catch(error){if(mine!==sequence)return;blankMetrics();$('low-stock-results').replaceChildren();$('low-stock-message').hidden=false;$('low-stock-error').textContent=error.message;$('low-stock-summary').textContent='Stock information is unavailable.';pager.set({busy:false});}
  finally{if(mine===sequence)root.setAttribute('aria-busy','false');}
 }
 function changed(debounce=false){++sequence;clearTimeout(timer);pager.set({busy:false,page:1});blankMetrics();closeMenu();$('low-stock-table').hidden=true;$('low-stock-footer').hidden=true;if(debounce)timer=setTimeout(load,300);else load();}
 filter.addEventListener('input',()=>changed(true));status.onchange=()=>changed();
 function reset(){filter.value='';status.value='';changed();}
 $('low-stock-reset').onclick=$('low-stock-empty-reset').onclick=reset;$('low-stock-retry').onclick=()=>load();
 $('low-stock-search').onsubmit=event=>{event.preventDefault();changed();};
 window.addEventListener('resize',closeMenu);load();
})();
