(() => {
 const root=document.getElementById('stock-receipt-detail'), el=id=>document.getElementById(id), empty='\u2014';
 const money=value=>value==null?empty:new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(Number(value));
 const number=value=>new Intl.NumberFormat('en-IN').format(Number(value||0));
 const date=(value,time=true)=>value?new Intl.DateTimeFormat('en-IN',{dateStyle:'medium',...(time?{timeStyle:'short'}:{})}).format(new Date(value)):empty;
 function status(value){const badge=document.createElement('span');badge.className='receipt-status'+(value==='REVERSED'?' reversed':'');badge.textContent=value==='REVERSED'?'Reversed':'Completed';return badge;}
 function field(target,label,value){
  const pair=document.createElement('div');pair.className='receipt-field';
  const dt=document.createElement('dt');dt.textContent=label;
  const dd=document.createElement('dd');if(value instanceof Node)dd.append(value);else dd.textContent=value||empty;
  pair.append(dt,dd);target.append(pair);
 }
 function totals(data){el('receipt-detail-totals').replaceChildren(...[
  ['Subtotal',data.subtotal],['Discount',data.discount],['Tax',data.tax],['Grand total',data.total]
 ].map(([label,value],index)=>{const row=document.createElement('div');row.className='receipt-total-row'+(index===3?' grand':'');const name=document.createElement('span');name.textContent=label;const amount=document.createElement('strong');amount.textContent=money(value);row.append(name,amount);return row;}));}
 function cell(row,value){const td=row.insertCell();td.textContent=value??empty;return td;}
 function itemDetails(row,item){
  const td=cell(row,''),serials=item.serialNumbers||[];
    const parts=[StockFlowVariantIdentity.details(item),item.disposition,item.batchNumber?'Batch: '+item.batchNumber:null].filter(Boolean);
  if(serials.length)parts.push('Serials: '+serials.slice(0,10).join(', '));
  if(serials.length<=10){td.textContent=parts.join(' \u00b7 ')||empty;return;}
  const details=document.createElement('details');details.className='receipt-serial-details';
  const summary=document.createElement('summary');summary.textContent=parts.join(' \u00b7 ')+`, \u2026 (+${serials.length-10} more)`;
  const list=document.createElement('ul');serials.forEach(serial=>{const li=document.createElement('li');li.textContent=serial;list.append(li);});details.append(summary,list);td.append(details);
 }
 function items(data){
  const wrap=document.createElement('div');wrap.className='table-responsive';const table=document.createElement('table');table.className='table table-bordered receipt-table';const head=table.createTHead().insertRow();
  ['#','Product / size / model','Quantity','Damaged','Buy price','Discount','GST %','Total','Details'].forEach(label=>{const th=document.createElement('th');th.scope='col';th.textContent=label;head.append(th);});
  const body=table.createTBody();data.items.forEach((item,index)=>{const row=body.insertRow();cell(row,index+1);cell(row,item.productName);cell(row,number(item.quantity));cell(row,number(item.damagedQuantity));cell(row,money(item.purchasePrice));cell(row,money(item.discount));cell(row,item.taxPercentage==null?empty:number(item.taxPercentage));cell(row,money(item.total));itemDetails(row,item);});
  wrap.append(table);el('receipt-detail-table').replaceChildren(wrap);el('receipt-items-title').textContent=`Items received (${data.items.length})`;
 }
 async function load(){try{
  const response=await fetch(`/api/stock-receipts/${encodeURIComponent(root.dataset.source)}/${encodeURIComponent(root.dataset.id)}`);const data=await response.json().catch(()=>null);if(!response.ok)throw new Error(data?.message||'Stock receipt was not found.');
  el('receipt-detail-title').textContent=data.receiptNumber;el('receipt-detail-subtitle').textContent=data.typeLabel;
  el('receipt-heading-status').replaceWith(Object.assign(status(data.status),{id:'receipt-heading-status'}));
  const fields=el('receipt-detail-fields');fields.replaceChildren();
  const groups=[
   [['Receipt no.',data.receiptNumber],['Type',data.typeLabel],['Reference',data.referenceNumber],['Notes',data.notes]],
   [['Supplier / party',data.partyName],['Supplier invoice',data.supplierInvoiceNumber],['Supplier invoice date',date(data.supplierInvoiceDate,false)]],
   [['Received date',date(data.receivedDate)],['Received by',data.receivedBy],['Status',status(data.status)]]
  ];groups.forEach(group=>{const column=document.createElement('div');column.className='receipt-info-column';group.forEach(([label,value])=>field(column,label,value));fields.append(column);});
  const additional=el('receipt-additional-fields');additional.replaceChildren();[['Supplier invoice',data.supplierInvoiceNumber],['Invoice date',date(data.supplierInvoiceDate,false)],['Reference',data.referenceNumber],['Notes',data.notes]].forEach(([label,value])=>field(additional,label,value));
  items(data);totals(data);
 }catch(error){el('receipt-detail-message').textContent=error.message;el('receipt-detail-message').hidden=false;}}
 let collapsedForPrint=[];
 window.addEventListener('beforeprint',()=>{
  collapsedForPrint=[...root.querySelectorAll('.receipt-serial-details:not([open])')];
  collapsedForPrint.forEach(details=>{details.open=true;});
 });
 window.addEventListener('afterprint',()=>{collapsedForPrint.forEach(details=>{details.open=false;});collapsedForPrint=[];});
 el('receipt-print').onclick=()=>window.print();load();
})();
