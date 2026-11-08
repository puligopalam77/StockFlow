/* Persisted estimates use the existing server billing calculator. No client totals are authoritative. */
(() => {
 'use strict';
 const root=document.getElementById('quotations');if(!root)return;
 const $=id=>document.getElementById(id),api=Sales.api.bind(Sales),money=v=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(v??0),uuid=()=>crypto.randomUUID?.()||'10000000-1000-4000-8000-100000000000'.replace(/[018]/g,c=>(Number(c)^crypto.getRandomValues(new Uint8Array(1))[0]&15>>Number(c)/4).toString(16));
 const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
 const camel=v=>Array.isArray(v)?v.map(camel):v&&typeof v==='object'?Object.fromEntries(Object.entries(v).map(([k,x])=>[k[0].toLowerCase()+k.slice(1),camel(x)])):v;
 const error=e=>{$('quotation-message').className='alert alert-danger';$('quotation-message').textContent=e.message||String(e);$('quotation-message').hidden=false;};
 const run=f=>Promise.resolve().then(f).catch(error);
 const id=new URL(location.href).searchParams.get('id');let options;
 if($('quotation-form'))for(const control of $('quotation-form').elements)control.disabled=true;
 function summary(target,q){target.replaceChildren();for(const [label,value]of [['Item subtotal',q.itemSubtotal],['Item discounts',-q.itemDiscountAmount],['Quotation discount',-q.invoiceDiscountAmount],['Taxable amount',q.taxableAmount],['GST',q.taxAmount],['Estimated total',q.totalAmount]]){const p=node('p');p.append(node('span',label+' '),node('strong',money(value)));target.append(p);}}
 function action(label,url){const a=node('a',label,'btn btn-sm btn-outline-secondary');a.href=url;return a;}
 const detailUrl=(value,revision)=>'/Quotations/Details?id='+encodeURIComponent(value)+(revision?'&revision='+revision:'');
 async function list(){
  return window.StockFlowQuotationList(root,options);
 }
 async function details(){
  const revision=new URL(location.href).searchParams.get('revision');const d=await api('/api/quotations/'+encodeURIComponent(id)+(revision?'?revision='+revision:'')),h=d.header,b=camel(d.selected.body),current=d.selected.revision===h.currentRevision,terminal=['CONVERTED','CANCELLED'].includes(h.status);
  $('quotation-title').textContent=h.quotationNumber+' · Revision '+d.selected.revision;$('quotation-status').textContent=h.status+' · Estimate only; no stock reserved';
  for(const rev of d.revisions)$('quotation-revisions').add(new Option('Revision '+rev,rev,rev===d.selected.revision,rev===d.selected.revision));$('quotation-revisions').onchange=()=>location.assign(detailUrl(id,$('quotation-revisions').value));
  if(options.permissions.print)$('quotation-print').href='/Quotations/Print?id='+id+'&revision='+d.selected.revision;
  if(current&&!terminal&&options.permissions.edit)$('quotation-edit').href='/Quotations/Edit?id='+id;
  if(current&&h.status==='ACCEPTED'&&options.permissions.convert)$('quotation-convert').href='/Sales/Create?quotationId='+id;
  if(h.saleId){$('quotation-invoice').hidden=false;$('quotation-invoice').href='/Sales/Invoice/'+h.saleId;}
  $('quotation-customer-detail').textContent=b.customer.customerName+' · '+b.documentDate+' · Valid until '+b.validUntil+' · Prepared by '+d.selected.preparedBy+(b.reference?' · '+b.reference:'');
  for(const line of b.lines){const tr=node('tr');for(const value of [line.name+(line.sku?' / '+line.sku:''),line.quantity+' '+line.unit,line.calculation.mrp==null?'—':money(line.calculation.mrp),money(line.sellingPrice),money(line.calculation.discountAmount),line.calculation.taxPercentage,money(line.calculation.lineTotal)])tr.append(node('td',value));$('quotation-details-lines').append(tr);}
  summary($('quotation-detail-summary'),b.quote);$('quotation-terms-detail').textContent=[b.request.notes,b.terms].filter(Boolean).join('\n\n');
  const transitions=terminal||!current?[]:h.status==='EXPIRED'?['EXPIRED','CANCELLED']:h.status==='DRAFT'?['SENT','CANCELLED']:h.status==='SENT'?['ACCEPTED','REJECTED','CANCELLED']:['CANCELLED'];
  $('quotation-state-form').hidden=!options.permissions.status||!transitions.length;for(const state of transitions)$('quotation-state').add(new Option(state==='SENT'?'Record manually sent':state,state));
  let pending=null;$('quotation-state-form').onsubmit=e=>{e.preventDefault();run(async()=>{pending??={operationId:uuid(),expectedVersion:h.version,status:$('quotation-state').value,notes:$('quotation-state-notes').value.trim()};const button=e.target.querySelector('button');button.disabled=true;try{await api('/api/quotations/'+id+'/state',{method:'POST',body:JSON.stringify(pending)});location.assign(detailUrl(id));}catch(err){if(err.status)pending=null;throw err;}finally{button.disabled=false;}});};
  for(const ev of d.history)$('quotation-history').append(node('p',`${ev.createdAt} · r${ev.revision} · ${ev.action} · ${ev.actor} · ${ev.notes}`));
 }
 async function editor(){ return window.StockFlowQuotationEditor(root, options); }
 run(async()=>{options=await api('/api/quotations/options');if(root.dataset.mode==='list')await list();else if(root.dataset.mode==='details')await details();else await editor();});
})();
