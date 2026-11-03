(() => {
 'use strict';
 const el=id=>document.getElementById(id),money=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(Number(n)||0);
 const labels={CASH:'Cash',UPI:'UPI',CARD:'Card',DEBIT_CARD:'Debit Card',CREDIT_CARD:'Credit Card',BANK_TRANSFER:'Bank Transfer',BANK:'Bank Transfer',MIXED:'Mixed',CREDIT:'Pay Later / No Collection',PAID:'Paid',PARTIAL:'Partially Paid',UNPAID:'Unpaid',ADJUSTED:'Refunded / Adjusted',PARTIALLY_RETURNED:'Partially Returned',FULLY_RETURNED:'Fully Returned'};
 const label=v=>labels[v]||StockFlowText.label(v)||'\u2014';
 let offset=0,version=0,rows=[],total=0,applied=null,sort='date',ascending=false,timeZone='Asia/Kolkata',customerVersion=0;
 let limit=StockFlowPagination.defaultSize;const selected=new Set(),params=new URLSearchParams(location.search),table=document.querySelector('.sales-list-table');
 const node=(tag,value='',cls)=>{const n=document.createElement(tag);n.textContent=value;if(cls)n.className=cls;return n;};
 for(const [param,id] of [['from','sales-from'],['to','sales-to'],['status','sales-status'],['paymentStatus','sales-payment-status'],['paymentMethod','sales-payment-method']]){
  const n=el(id),value=params.get(param)||'';if(n.tagName!=='SELECT'||[...n.options].some(o=>o.value===value))n.value=value;
 }
 el('sale-search').value=params.get('term')||'';
 if(/^\d+$/.test(params.get('customerId')||'')){el('customer-filter').add(new Option('Selected customer #'+params.get('customerId'),params.get('customerId')));el('customer-filter').value=params.get('customerId');}
 if(el('sales-status').value||el('sales-payment-method').value||el('customer-filter').value)el('sales-more-filters').open=true;
 const badge=value=>node('span',label(value),'sales-badge '+String(value).toLowerCase());
 const date=value=>new Intl.DateTimeFormat('en-IN',{day:'numeric',month:'numeric',year:'numeric',hour:'numeric',minute:'2-digit',timeZone}).format(new Date(value));
 function filters(){
  const from=el('sales-from').value,to=el('sales-to').value;
  if(from&&to&&from>to)throw new Error('From date must be on or before To date.');
  const q=new URLSearchParams({term:el('sale-search').value.trim(),status:el('sales-status').value,paymentStatus:el('sales-payment-status').value,paymentMethod:el('sales-payment-method').value});
  if(from)q.set('from',from);if(to)q.set('to',to);if(el('customer-filter').value)q.set('customerId',el('customer-filter').value);return q;
 }
 function iconLink(href,name,title){
  const a=node('a');a.href=href;a.title=title;a.setAttribute('aria-label',title);
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'),use=document.createElementNS(svg.namespaceURI,'use');svg.setAttribute('class','sf-icon');svg.setAttribute('aria-hidden','true');use.setAttribute('href','/images/icons.svg#'+name);svg.append(use);a.append(svg);return a;
 }
 function render(data){
  rows=data.rows;total=data.summary.count;timeZone=data.timeZoneId||'Asia/Kolkata';selected.clear();el('sales-select-all').checked=false;el('sales-rows').replaceChildren();
  for(const row of rows){
   const tr=document.createElement('tr'),checkbox=document.createElement('input');checkbox.type='checkbox';checkbox.setAttribute('aria-label','Select '+row.invoiceNumber);
   checkbox.onchange=()=>{checkbox.checked?selected.add(row.saleId):selected.delete(row.saleId);el('sales-select-all').checked=selected.size===rows.length;};
   const invoice=node('a',row.invoiceNumber,'sales-invoice');invoice.href='/Sales/Details/'+row.saleId;
   const invoiceCell=node('div');invoiceCell.append(invoice,node('small',label(row.invoiceStatus||row.status),'d-block sales-invoice-status'));
   const customer=node('div','','sales-customer'),info=node('div');info.append(node('strong',row.customerName||'Walk-in Customer'));if(row.customerMobile)info.append(node('small',row.customerMobile));customer.append(info);
   const actions=node('div','','sales-row-actions');actions.append(iconLink(invoice.href,'eye','View invoice '+row.invoiceNumber),iconLink('/Sales/Invoice/'+row.saleId+'?print=1','print','Print invoice '+row.invoiceNumber));
   if(table.dataset.canCollect==='true'&&row.status==='COMPLETED'&&row.outstandingAmount>0)actions.append(iconLink(invoice.href+'?collect=1','document','Collect payment for '+row.invoiceNumber));
   if(table.dataset.canReturn==='true'&&row.status==='COMPLETED'&&row.invoiceStatus!=='FULLY_RETURNED')actions.append(iconLink(invoice.href+'?return=1','return','Return / refund '+row.invoiceNumber));
   const amount=node('div',money(row.totalAmount));if(row.returnedAmount>0)amount.append(node('small','Returned: '+money(row.returnedAmount),'d-block'));
   const due=node('div',money(row.outstandingAmount));if(row.outstandingAmount>0)due.append(node('small',row.paymentDueDate?'Due: '+row.paymentDueDate.slice(0,10):'No due date recorded','d-block'));
   const items=(row.lineCount??row.itemCount)+' items \u00b7 '+row.itemCount+' units';
   [checkbox,invoiceCell,date(row.saleDate),customer,items,amount,money(row.collectedAmount),due,badge(row.paymentMode),badge(row.paymentStatus),actions].forEach((value,index)=>{const td=Sales.cell(tr,value);td.dataset.label=['Select','Invoice No.','Date & Time','Customer','Items','Bill Total','Paid','Balance Due','Payment Method','Payment Status','Actions'][index];});
   el('sales-rows').append(tr);
  }
  if(!rows.length){const tr=document.createElement('tr');Sales.cell(tr,'No invoices match these filters.').colSpan=11;el('sales-rows').append(tr);}
  for(const [id,key,currency]of [['total','netSales',true],['count','invoiceCount',false],['collected','amountCollected',true],['dues','outstandingDues',true]]){
   const value=data.summary[key]||0,before=data.previous?.[key];el('sales-summary-'+id).textContent=currency?money(value):value.toLocaleString('en-IN');
   el('sales-compare-'+id).textContent=before==null?'':(before===0?(value===0?'0%':'No previous value'):((value-before)/Math.abs(before)*100).toFixed(1)+'%')+' vs previous invoice period';
  }
  el('sales-showing').textContent=total?'Showing '+(offset+1)+' to '+Math.min(offset+limit,total)+' of '+total+' invoices':'Showing 0 invoices';el('sales-page').textContent=Math.floor(offset/limit)+1;el('sales-prev').disabled=offset===0;el('sales-next').disabled=offset+limit>=total;
 }
 async function load(pagingRequest){
  const request=++version;el('sales-export').disabled=true;
  try{const q=filters();q.set('offset',offset);q.set('limit',limit);q.set('sort',sort);q.set('ascending',ascending);
   const response=await Sales.api('/api/sales/list?'+q);if(request!==version)return;const data=salesPager.capture(response,pagingRequest);if(!data)return;
   render(data);applied=q;el('sales-export').disabled=!total;
  }catch(error){if(request===version){applied=null;throw error;}}
 }
 const sortKeys=['','invoice','date','customer','items','total','paid','due','payment','paymentStatus',''];
 document.querySelectorAll('.sales-list-table thead th').forEach((th,index)=>{const key=sortKeys[index];if(!key)return;const caption=th.textContent,button=node('button',caption+' \u2195','sales-sort');button.type='button';th.replaceChildren(button);button.onclick=()=>{ascending=sort===key?!ascending:true;sort=key;offset=0;document.querySelectorAll('.sales-list-table th').forEach(n=>n.removeAttribute('aria-sort'));th.setAttribute('aria-sort',ascending?'ascending':'descending');Sales.run(load);};if(key==='date')th.setAttribute('aria-sort','descending');});
 el('sales-filter').onsubmit=event=>{event.preventDefault();offset=0;Sales.run(load);};
 el('sales-reset').onclick=()=>{el('sales-filter').reset();el('customer-filter').replaceChildren(new Option('All Customers',''));el('sales-more-filters').open=false;offset=0;++customerVersion;Sales.run(load);};
 el('sales-prev').onclick=()=>{offset=Math.max(0,offset-limit);Sales.run(load);};el('sales-next').onclick=()=>{offset+=limit;Sales.run(load);};
 for(const id of ['sales-from','sales-to'])el(id).addEventListener('input',()=>el('sales-range').value='CUSTOM');
 el('sales-range').onchange=()=>{
  if(el('sales-range').value==='CUSTOM')return;
  const parts=new Intl.DateTimeFormat('en-CA',{timeZone,year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date()),part=k=>parts.find(p=>p.type===k).value;
  const today=new Date(Date.UTC(+part('year'),+part('month')-1,+part('day'))),from=new Date(today),to=new Date(today);
  if(el('sales-range').value==='YESTERDAY'){from.setUTCDate(from.getUTCDate()-1);to.setUTCDate(to.getUTCDate()-1);}
  if(el('sales-range').value==='WEEK')from.setUTCDate(from.getUTCDate()-6);
  if(el('sales-range').value==='MONTH')from.setUTCDate(1);
  el('sales-from').value=from.toISOString().slice(0,10);el('sales-to').value=to.toISOString().slice(0,10);
 };
 let customerTimer;
 el('sales-customer-search').oninput=()=>{clearTimeout(customerTimer);const request=++customerVersion,term=el('sales-customer-search').value.trim();if(!term)return;
  customerTimer=setTimeout(()=>Sales.run(async()=>{const results=await Sales.api('/api/customers/search?term='+encodeURIComponent(term));if(request!==customerVersion)return;const current=el('customer-filter').selectedOptions[0],options=[new Option('All Customers','')];if(current?.value)options.push(new Option(current.text,current.value,true,true));for(const c of results)if(String(c.customerId)!==current?.value)options.push(new Option(c.customerName+(c.mobileNumber?' / '+c.mobileNumber:''),c.customerId));el('customer-filter').replaceChildren(...options);}),300);
 };
 el('sales-select-all').onchange=event=>{selected.clear();el('sales-rows').querySelectorAll('input[type=checkbox]').forEach((n,i)=>{n.checked=event.target.checked;if(n.checked)selected.add(rows[i].saleId);});};
 const csv=value=>'"'+String(value??'').replace(/^[=+\-@\t\r]/,"'$&").replaceAll('"','""')+'"';
 el('sales-export').onclick=()=>Sales.run(async()=>{
  if(!applied)return;el('sales-export').disabled=true;
  try{let exported=rows.filter(r=>selected.has(r.saleId));if(!selected.size){exported=[];for(let start=0;start<total;start+=100){const q=new URLSearchParams(applied);q.set('offset',start);q.set('limit','100');const page=await Sales.api('/api/sales/list?'+q);exported.push(...page.rows);if(page.rows.length<100)break;}}
   const content=[['Invoice','Date','Customer','Mobile','Lines','Units','Bill Total INR','Net Paid INR','Balance Due INR','Returned INR','Refunded INR','Payment Method','Payment Status','Invoice Status'],...exported.map(r=>[r.invoiceNumber,date(r.saleDate),r.customerName||'Walk-in Customer',r.customerMobile,r.lineCount,r.itemCount,r.totalAmount,r.collectedAmount,r.outstandingAmount,r.returnedAmount,r.refundedAmount,label(r.paymentMode),label(r.paymentStatus),label(r.invoiceStatus||r.status)])].map(row=>row.map(csv).join(',')).join('\r\n');
   const url=URL.createObjectURL(new Blob(['\ufeff'+content],{type:'text/csv;charset=utf-8'})),a=node('a');a.href=url;a.download='sales-invoices.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }finally{el('sales-export').disabled=!total;}
 });
 const salesPager=StockFlowPagination.adapt({host:'.sales-list-pagination',root:'.sales-list-table',legacy:{previous:'#sales-prev',next:'#sales-next',range:'#sales-showing',position:'#sales-page'},getOffset:()=>offset,setOffset:(value,request)=>{offset=value;limit=request.size;},load,onError:Sales.error});load=salesPager.load;
 Sales.run(load);
})();
