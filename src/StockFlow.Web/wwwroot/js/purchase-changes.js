(() => {
 'use strict';
 const $=id=>document.getElementById(id),id=location.pathname.split('/').filter(Boolean).pop(),actions=$('purchase-actions');
 let purchase,kind='DETAILS',pending=null,busy=false,controls=[];
 const fmt=value=>value==null||value===''?'\u2014':String(value),money=value=>window.StockFlowPurchaseDashboard.money(value);
 const showError=error=>{const target=$('pc-message');target.textContent=error.message;target.hidden=false;};
 function button(label,handler){const b=document.createElement('button');b.type='button';b.className='btn btn-outline-primary';b.textContent=label;b.onclick=handler;actions.append(b);return b;}
 function field(target,label,name,value,type='text',options={}){
  const box=document.createElement('div');box.className='pc-field';const caption=document.createElement('label');caption.className='form-label';caption.htmlFor='pc-'+name;caption.textContent=label;
  const input=document.createElement(type==='textarea'?'textarea':'input');input.id='pc-'+name;input.className='form-control';if(type!=='textarea')input.type=type;input.value=value??'';
  for(const [key,val] of Object.entries(options))input.setAttribute(key,val);box.append(caption,input);target.append(box);controls.push({label,name,input,previous:String(value??'')});return input;
 }
 function editable(enabled){for(const c of controls)c.input.disabled=!enabled;$('pc-reason').disabled=!enabled;document.querySelectorAll('[data-pc-unit]').forEach(input=>input.disabled=!enabled);}
 function back(){$('pc-message').hidden=true;pending=null;editable(true);$('pc-review').hidden=true;$('pc-back').hidden=true;$('pc-submit').textContent='Review Changes';}
 async function open(mode){
  kind=mode;pending=null;controls=[];$('pc-message').hidden=true;$('pc-review').hidden=true;$('pc-back').hidden=true;$('pc-submit').textContent='Review Changes';$('pc-submit').disabled=false;$('pc-reason').disabled=false;$('pc-reason').value='';
  const fields=$('pc-fields');fields.replaceChildren();$('pc-title').textContent=mode==='DETAILS'?'Edit Purchase':mode==='ORDER'?'Edit Remaining Order':'Correct Purchase';
  $('pc-help').textContent=mode==='DETAILS'?'Update invoice details and notes. Products, stock, amounts and barcodes stay linked.':mode==='ORDER'?'Change ordered quantities. They cannot be lower than quantities already received or damaged.':mode==='FINANCIAL'?'Review corrected prices, discounts, tax and charges. Sales, returns and payments are checked before saving.':'Record a stock adjustment linked to this purchase. Original receipt quantities and barcode identities are retained. Invoice amounts are unchanged.';
  const grid=document.createElement('div');grid.className='pc-grid';fields.append(grid);
  if(mode==='DETAILS'){
   field(grid,'Supplier Invoice Number','invoice',purchase.supplierInvoiceNumber,'text',{maxlength:100});field(grid,'Supplier Invoice Date','invoice-date',purchase.supplierInvoiceDate?.slice(0,10),'date');
   field(grid,'Internal Reference','reference',purchase.internalReference,'text',{maxlength:100});field(grid,'Invoice Attachment (PDF, JPG or PNG, up to 5 MB)','file','','file',{accept:'.pdf,.jpg,.jpeg,.png'});
   field(grid,'Notes / Remarks','notes',purchase.notes,'textarea',{maxlength:4000,rows:3});
  }else if(mode==='STOCK'){
   const select=document.createElement('select');select.id='pc-stock-item';select.className='form-select';select.setAttribute('aria-label','Purchase item');
   purchase.items.forEach(i=>select.add(new Option(i.productName,i.purchaseItemId)));grid.append(select);
   const type=document.createElement('select');type.id='pc-stock-type';type.className='form-select';type.setAttribute('aria-label','Correction type');for(const [value,label] of [['ADJUSTMENT_OUT','Reduce stock'],['ADJUSTMENT_IN','Add missing stock'],['DAMAGED','Mark stock damaged']])type.add(new Option(label,value));grid.append(type);
   controls.push({label:'Purchase item',name:'stock-item',input:select,previous:''},{label:'Correction type',name:'stock-type',input:type,previous:''});
   field(grid,'Quantity','stock-qty',1,'number',{min:1,max:10000,step:1,required:''});
   const serials=field(grid,'New serial numbers (one per line, for serial-tracked incoming stock)','stock-serials','','textarea',{rows:3});
   const unitbox=document.createElement('div');unitbox.id='pc-unit-options';grid.append(unitbox);
   async function units(){
    const item=purchase.items.find(i=>i.purchaseItemId===Number(select.value));unitbox.replaceChildren();serials.closest('.pc-field').hidden=!item.isSerialTracked||type.value!=='ADJUSTMENT_IN';
    if(!item.isSerialTracked||type.value==='ADJUSTMENT_IN')return;
    unitbox.textContent='Loading available serials...';
    try{const rows=await Purchases.api('/api/inventory/units?'+new URLSearchParams({productId:item.productId,variantId:item.variantId,availableOnly:true}));unitbox.replaceChildren();
     for(const unit of rows.filter(u=>u.purchaseId===purchase.purchaseId&&(u.purchaseItemId==null||u.purchaseItemId===item.purchaseItemId))){const label=document.createElement('label'),check=document.createElement('input');check.type='checkbox';check.value=unit.productUnitId;check.dataset.pcUnit='true';label.append(check,document.createTextNode(' '+unit.serialNumber));unitbox.append(label);}
     if(!unitbox.childElementCount)unitbox.textContent='No available serials from this purchase.';
    }catch(error){unitbox.textContent=error.message;}
   }
   select.onchange=units;type.onchange=units;await units();
  }else{
   const table=document.createElement('table');table.className='table pd-table';const head=table.createTHead().insertRow();for(const label of ['Product / Variant','Ordered','Unit cost','Line discount','GST %']){const th=document.createElement('th');th.textContent=label;head.append(th);}const body=table.createTBody();const scroll=document.createElement('div');scroll.className='table-responsive';fields.append(scroll);scroll.append(table);
   purchase.items.forEach(item=>{const tr=body.insertRow();const name=tr.insertCell();name.textContent=item.productName;for(const [key,label,type,attrs] of [['quantity','Quantity','number',{min:Math.max(1,item.receivedQuantity+item.damagedQuantity),step:1}],['purchasePrice','Unit cost','number',{min:0,step:'.01'}],['discountAmount','Line discount','number',{min:0,step:'.01'}],['taxPercentage','GST %','number',{min:0,max:100,step:'.01'}]]){const cell=tr.insertCell();const input=field(cell,label+' - '+item.productName,item.purchaseItemId+'-'+key,item[key],type,{...attrs,required:''});input.setAttribute('aria-label',label+' for '+item.productName);if(mode==='ORDER'?key!=='quantity':key==='quantity'){input.readOnly=true;input.classList.add('pc-readonly');}}});
   if(mode==='FINANCIAL'){field(grid,'Invoice Discount','discount',purchase.discountAmount,'number',{min:0,step:'.01'});field(grid,'Delivery Charges','freight',purchase.freightAmount,'number',{min:0,step:'.01'});field(grid,'Other Charges','other',purchase.otherLandedCost,'number',{min:0,step:'.01'});}
  }
  $('purchase-change-dialog').showModal();
 }
 async function requestBody(){
  const base={requestId:crypto.randomUUID(),locationId:window.StockFlowLocation,expectedVersion:purchase.editVersion??0,reason:$('pc-reason').value.trim()};
  if(kind==='STOCK'){
   const item=purchase.items.find(i=>i.purchaseItemId===Number($('pc-stock-item').value));return {...base,purchaseId:purchase.purchaseId,purchaseItemId:item.purchaseItemId,productId:item.productId,variantId:item.variantId,transactionType:$('pc-stock-type').value,quantity:Number($('pc-stock-qty').value),serialNumbers:$('pc-stock-type').value==='ADJUSTMENT_IN'&&item.isSerialTracked?$('pc-stock-serials').value.split(/\r?\n/).map(s=>s.trim()).filter(Boolean):[],productUnitIds:[...document.querySelectorAll('[data-pc-unit]:checked')].map(i=>Number(i.value))};
  }
  const result={...base,kind,items:[]};
  if(kind==='DETAILS'){
   Object.assign(result,{supplierInvoiceNumber:$('pc-invoice').value||null,supplierInvoiceDate:$('pc-invoice-date').value||null,internalReference:$('pc-reference').value||null,notes:$('pc-notes').value||null});
   const file=$('pc-file').files[0];if(file){if(file.size<1||file.size>5000000||!['application/pdf','image/jpeg','image/png'].includes(file.type))throw new Error('Attach a PDF, JPG or PNG up to 5 MB.');const bytes=new Uint8Array(await file.arrayBuffer());let binary='';for(const byte of bytes)binary+=String.fromCharCode(byte);Object.assign(result,{invoiceAttachmentName:file.name,invoiceAttachmentContentType:file.type,invoiceAttachmentData:btoa(binary)});}
  }else{
   result.items=purchase.items.map(item=>({purchaseItemId:item.purchaseItemId,...Object.fromEntries(['quantity','purchasePrice','discountAmount','taxPercentage'].map(key=>[key,Number($('pc-'+item.purchaseItemId+'-'+key).value)]))}));
   if(kind==='FINANCIAL')Object.assign(result,{discountAmount:Number($('pc-discount').value),freightAmount:Number($('pc-freight').value),otherLandedCost:Number($('pc-other').value)});
  }
  return result;
 }
 function review(body){
  const target=$('pc-review');target.replaceChildren();const table=document.createElement('table');table.className='table';const head=table.createTHead().insertRow();for(const label of ['Field','Previous','New']){const th=document.createElement('th');th.textContent=label;head.append(th);}const tbody=table.createTBody();let count=0;
  for(const c of controls){const value=c.input.type==='file'?c.input.files[0]?.name||'':c.input.value;if(value===c.previous)continue;const tr=tbody.insertRow();for(const v of [c.label,c.previous,value])Purchases.cell(tr,fmt(v));count++;}
  if(kind==='STOCK'){const tr=tbody.insertRow();for(const v of ['Stock adjustment','Original receipt retained',body.transactionType+' '+body.quantity])Purchases.cell(tr,v);count++;}
  if(!count)throw new Error('No changes to save.');
  if(kind==='FINANCIAL'||kind==='ORDER'){
   let total=0;const round=n=>Math.round((n+Number.EPSILON)*100)/100;for(const i of body.items){const sub=round(i.quantity*i.purchasePrice)-i.discountAmount;total+=sub+round(sub*i.taxPercentage/100);}total+=body.freightAmount??purchase.freightAmount;total+=body.otherLandedCost??purchase.otherLandedCost;total-=body.discountAmount??purchase.discountAmount;
   const row=tbody.insertRow();for(const value of ['Purchase total',money(purchase.totalAmount),money(total)])Purchases.cell(row,value);
  }
  target.append(table);target.hidden=false;editable(false);$('pc-back').hidden=false;$('pc-submit').textContent='Save '+(kind==='DETAILS'||kind==='ORDER'?'Changes':'Correction');
 }
 async function history(){
  const target=$('purchase-change-rows');
  try{const rows=await Purchases.api('/api/purchases/'+id+'/changes');target.replaceChildren();if(!rows.length){target.textContent='No changes recorded.';return;}
   const table=document.createElement('table');table.className='table';const h=table.createTHead().insertRow();for(const label of ['When / By','Change','Reason','Amount Change','Details']){const th=document.createElement('th');th.textContent=label;h.append(th);}const tbody=table.createTBody();
   for(const row of rows){const tr=tbody.insertRow();Purchases.cell(tr,new Date(row.changedAt).toLocaleString()+' / '+row.changedBy);Purchases.cell(tr,row.changeType.replaceAll('_',' '));Purchases.cell(tr,row.reason);Purchases.cell(tr,money(row.amountDelta));const cell=tr.insertCell(),details=document.createElement('details'),summary=document.createElement('summary');summary.textContent=row.adjustmentId?'Adjustment #'+row.adjustmentId:row.changeType==='FINANCIAL'?'Financial Correction #'+row.changeId:'Review changes';details.append(summary);
    const list=document.createElement('dl');list.className='pc-history-diff';const before=row.oldValues||{},after=row.newValues||{};
    for(const group of ['details','items','stock']){const a=group==='items'?Object.fromEntries((before.items||[]).map(i=>[i.purchase_item_id,i])):before[group]||{},b=group==='items'?Object.fromEntries((after.items||[]).map(i=>[i.purchase_item_id,i])):after[group]||{};
     for(const key of new Set([...Object.keys(a),...Object.keys(b)])){if(['edit_version','updated_at'].includes(key)||JSON.stringify(a[key])===JSON.stringify(b[key]))continue;const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=group==='items'?'Item #'+key:key.replaceAll('_',' ');dd.textContent=fmt(typeof a[key]==='object'?JSON.stringify(a[key]):a[key])+' \u2192 '+fmt(typeof b[key]==='object'?JSON.stringify(b[key]):b[key]);list.append(dt,dd);}}
    if(row.changeType==='STOCK'){const p=document.createElement('p');p.textContent='Stock ledger adjustment #'+row.adjustmentId+'. Original receipt quantities and invoice amounts are retained.';details.append(p);}details.append(list);cell.append(details);
   }target.append(table);
  }catch(error){target.textContent='Edit history could not be loaded. '+error.message;}
 }
 $('purchase-history-refresh').onclick=history;
 (async()=>{try{
  purchase=await window.StockFlowPurchaseDocument;await history();if(purchase.status==='CANCELLED'||actions.dataset.edit!=='true')return;
  if(purchase.status==='DRAFT'){
   const link=document.createElement('a');link.href='/Purchases/Create?id='+purchase.purchaseId;link.className='btn btn-outline-primary';link.textContent='Edit Purchase';actions.append(link);
  }else button('Edit Purchase',()=>open('DETAILS'));
  if(purchase.status==='PARTIALLY_RECEIVED')button('Edit Remaining Order',()=>open('ORDER'));
  if(['PARTIALLY_RECEIVED','COMPLETED'].includes(purchase.status)&&actions.dataset.approve==='true'){
   const menu=document.createElement('details');menu.className='pc-correct-menu';const summary=document.createElement('summary');summary.className='btn btn-outline-primary';summary.textContent='Correct Purchase';menu.append(summary);actions.append(menu);
   const financial=document.createElement('button');financial.type='button';financial.className='btn';financial.textContent='Correct Prices / Charges';financial.onclick=()=>{menu.open=false;open('FINANCIAL');};menu.append(financial);
   if(actions.dataset.adjust==='true'){const stock=document.createElement('button');stock.type='button';stock.className='btn';stock.textContent='Correct Stock / Damage';stock.onclick=()=>{menu.open=false;open('STOCK');};menu.append(stock);}
  }
  if(new URLSearchParams(location.search).get('edit')==='1'&&purchase.status!=='DRAFT')open('DETAILS');
 }catch(error){Purchases.error(error);}})();
 if(!$('purchase-change-form'))return;
 $('pc-back').onclick=back;
 $('purchase-change-form').onsubmit=async event=>{event.preventDefault();if(busy)return;
  try{busy=true;$('pc-submit').disabled=true;$('pc-back').disabled=true;if(!pending){const body=await requestBody();review(body);pending=body;return;}
   await Purchases.api(kind==='STOCK'?'/api/inventory/adjustments':'/api/purchases/'+id+'/changes',{method:'POST',body:JSON.stringify(pending)});location.reload();
  }catch(error){showError(error);}finally{busy=false;$('pc-submit').disabled=false;$('pc-back').disabled=false;}
 };
})();
