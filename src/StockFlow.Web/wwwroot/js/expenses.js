(() => {
 'use strict';const root=document.getElementById('expenses');if(!root)return;const $=id=>document.getElementById(id),mode=root.dataset.mode;
 const message=(text)=>{$('expense-message').textContent=text;$('expense-message').hidden=!text;};
 const money=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(Number(n));
 const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Kolkata',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
 const csrf=()=>document.querySelector('meta[name="csrf-token"]').content;
 async function api(url,body,form){const r=await fetch(url,body?{method:'POST',headers:form?{'X-CSRF-TOKEN':csrf()}:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf()},body:form?body:JSON.stringify(body)}:{});const data=await r.json().catch(()=>null);if(!r.ok){const e=new Error(data?.message||data?.title||'The result could not be confirmed.');e.status=r.status;throw e;}return data;}
 function row(table,values,link){const tr=document.createElement('tr');for(const value of values){const td=document.createElement('td');td.textContent=value??'';tr.append(td);}if(link){const td=document.createElement('td'),a=document.createElement('a');a.href='/Expenses/Details?id='+encodeURIComponent(link);a.textContent='Details';td.append(a);tr.append(td);}table.append(tr);return tr;}
 const state=e=>e.status==='POSTED'?(Number(e.paid)===0?'Unpaid':Number(e.paid)===Number(e.amount)?'Paid':'Partially paid'):e.status;
 if(mode==='list'||mode==='report'){
  let page=1;$('expense-from').value=today.slice(0,8)+'01';$('expense-to').value=today;
  async function load(){try{const params=new URLSearchParams({from:$('expense-from').value,to:$('expense-to').value,page,pageSize:20});if(mode==='list')params.set('term',$('expense-term').value);
   const data=await api('/api/expenses'+(mode==='report'?'/report':'')+'?'+params);$('expense-rows').replaceChildren();
   for(const e of mode==='report'?data.expenses:data.rows)row($('expense-rows'),mode==='report'?[e.incurred_date,e.category,e.payee,money(e.amount),money(e.paid),state(e)]:[e.incurred_date,e.category,e.payee,e.description,money(e.amount),money(e.paid),state(e)],e.expense_id);
   if(mode==='list'){$('expense-page').textContent=`Page ${page} · ${data.total} expenses`;$('expense-prev').disabled=page===1;$('expense-next').disabled=page*20>=data.total;}
   else{$('expense-summary').textContent=`Incurred: ${money(data.incurred)} · Net recorded payments: ${money(data.paid)} · Current-basis outstanding at cutoff: ${money(data.outstandingAtCutoff)}`;$('expense-definition').textContent=data.definition;$('expense-export').href='/api/expenses/export?'+params;$('expense-report-payments').replaceChildren();for(const p of data.payments)row($('expense-report-payments'),[p.paid_date,p.expense_id,p.kind,p.payment_mode,money(p.amount)]);}
   message('');}catch(e){message(e.message);}}
  $('expense-filters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});if(mode==='list'){$('expense-prev').onclick=()=>{page--;load();};$('expense-next').onclick=()=>{page++;load();};}load();return;
 }
 const key='stockflow.expense.pending.'+root.dataset.scope,createKey=key+'.create';let pending=JSON.parse(sessionStorage.getItem(key)||'null'),options,expense=null,busy=false;
 let id=root.dataset.id||pending?.id||sessionStorage.getItem(createKey)||crypto.randomUUID();if(!root.dataset.id)sessionStorage.setItem(createKey,id);
 if(pending&&pending.id!==id){location.replace('/Expenses/Details?id='+encodeURIComponent(pending.id));return;}
 const can=action=>Boolean(options?.permissions[action]);
 function controls(){const locked=busy||Boolean(pending),editable=expense?can('EDIT'):can('CREATE');$('expense-fields').disabled=locked||expense?.status==='CANCELLED';for(const input of $('expense-fields').querySelectorAll('input,select,textarea'))input.disabled=!editable; $('expense-reason').disabled=!(editable||can('CANCEL'));$('expense-save').disabled=!editable;
  $('expense-retry').hidden=!pending;$('expense-retry').disabled=busy;$('expense-post').hidden=!expense||expense.status!=='DRAFT'||!can('POST');$('expense-cancel').hidden=!expense||expense.status==='CANCELLED'||Number(expense.paid)!==0||!can('CANCEL');
  // Posting/cancellation permissions are independent of draft editing authority.
  for(const x of ['expense-post','expense-cancel'])$(x).disabled=locked;
  $('expense-payment').hidden=expense?.status!=='POSTED'||!(can('PAY')||can('REVERSE'));$('expense-attachment-form').hidden=!expense||expense.status==='CANCELLED'||!can('ATTACH');
  for(const el of $('expense-payment').elements)el.disabled=locked;for(const el of $('expense-attachment-form').elements)el.disabled=locked;
  $('expense-payment-action').querySelector('[value="PAY"]').disabled=!can('PAY');$('expense-payment-action').querySelector('[value="REFUND"]').disabled=!can('REVERSE');
  if(!can('PAY')&&can('REVERSE'))$('expense-payment-action').value='REFUND';for(const el of root.querySelectorAll('[data-master]'))el.hidden=!can('EDIT');
 }
 function fillOptions(){for(const [name,rows] of [['category',options.categories],['payee',options.payees]]){const select=$('expense-'+name),value=select.value;select.replaceChildren(new Option('Select '+name,''));for(const r of rows)select.add(new Option(r.name,r.id));select.value=value;}}
 async function load(){options=await api('/api/expenses/options');fillOptions();if(root.dataset.id||expense||pending){try{expense=await api('/api/expenses/'+id);}catch(e){if(!(e.status===404&&pending?.action==='SAVE'))throw e;}}
  if(expense){for(const [field,value] of Object.entries({category:expense.category_id,payee:expense.payee_id,incurred:expense.incurred_date,amount:expense.amount,description:expense.description}))$('expense-'+field).value=value;
   $('expense-state').textContent=state(expense)+' · version '+expense.version;$('expense-save').textContent=expense.status==='POSTED'?'Save Correction':'Save Draft';$('expense-balance').textContent=`Expense ${money(expense.amount)} · paid ${money(expense.paid)} · outstanding ${money(Number(expense.amount)-Number(expense.paid))}`;
   $('expense-payments').replaceChildren();const reversed=new Set(expense.payments.map(p=>p.reverses_id));for(const p of expense.payments){const tr=row($('expense-payments'),[p.paid_date,p.kind,p.payment_mode,money(p.amount),p.reference]);const td=document.createElement('td');if(can('REVERSE')&&expense.status==='POSTED'&&p.kind!=='REVERSAL'&&!reversed.has(p.payment_id)){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-secondary';b.textContent='Reverse';b.onclick=()=>run(()=>{if($('expense-payment-reason').value.trim().length<3)throw new Error('Enter a reversal reason in the payment form.');return write('REVERSE',{paymentId:p.payment_id,paidDate:$('expense-paid-date').value,reason:$('expense-payment-reason').value});});td.append(b);}tr.append(td);}
   $('expense-history').replaceChildren();for(const e of expense.events)row($('expense-history'),[e.version,e.action,e.at,e.actor,e.reason,money(e.snapshot.amount)]);
   $('expense-attachments').replaceChildren();for(const a of expense.attachments){const li=document.createElement('li'),link=document.createElement('a');link.textContent=a.name;link.href='/api/expenses/'+id+'/attachments/'+a.id;li.append(link);$('expense-attachments').append(li);}
  }else{$('expense-state').textContent='Unsaved draft';$('expense-incurred').value=today;}
  if(!$('expense-paid-date').value)$('expense-paid-date').value=today;controls();
 }
 async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){message(e.message);}finally{busy=false;controls();}}
 async function send(){const r=pending;try{await api('/api/expenses/'+r.id+'/'+r.action.toLowerCase(),r.request);sessionStorage.removeItem(key);pending=null;
   if(!root.dataset.id){sessionStorage.removeItem(createKey);location.replace('/Expenses/Details?id='+encodeURIComponent(id));return;}await load();message('Saved.');
  }catch(e){if(e.status&&e.status<500){sessionStorage.removeItem(key);pending=null;}else message('Result unknown. Retry the unchanged request before making another change.');throw e;}}
 async function write(action,body){if(pending)throw new Error('Resolve the pending request first.');pending={id,action,request:{operationId:crypto.randomUUID(),expectedVersion:expense?.version||0,body}};sessionStorage.setItem(key,JSON.stringify(pending));controls();await send();}
 $('expense-form').onsubmit=e=>{e.preventDefault();run(()=>write(expense?.status==='POSTED'?'CORRECT':'SAVE',{categoryId:$('expense-category').value,payeeId:$('expense-payee').value,incurredDate:$('expense-incurred').value,amount:Number($('expense-amount').value),description:$('expense-description').value,reason:$('expense-reason').value}));};
 $('expense-post').onclick=()=>run(()=>write('POST',{}));$('expense-cancel').onclick=()=>run(()=>write('CANCEL',{reason:$('expense-reason').value}));$('expense-retry').onclick=()=>run(send);
 $('expense-payment').onsubmit=e=>{e.preventDefault();run(()=>write($('expense-payment-action').value,{paidDate:$('expense-paid-date').value,amount:Number($('expense-payment-amount').value),paymentMode:$('expense-method').value,reference:$('expense-reference').value,reason:$('expense-payment-reason').value}));};
 let masterKind;for(const b of root.querySelectorAll('[data-master]'))b.onclick=()=>{masterKind=b.dataset.master;$('expense-master-title').textContent='New '+masterKind.toLowerCase();$('expense-master-name').value='';$('expense-master-dialog').showModal();};$('expense-master-close').onclick=()=>$('expense-master-dialog').close();
 $('expense-master-form').onsubmit=e=>{e.preventDefault();run(async()=>{const r=await api('/api/expenses/masters/'+masterKind,{id:crypto.randomUUID(),name:$('expense-master-name').value});options=await api('/api/expenses/options');fillOptions();$('expense-'+(masterKind==='CATEGORY'?'category':'payee')).value=r.id;$('expense-master-dialog').close();});};
 $('expense-attachment-form').onsubmit=e=>{e.preventDefault();run(async()=>{const file=$('expense-file').files[0];if(!file||file.size>5242880)throw new Error('Select a PDF, PNG or JPEG of up to 5 MB.');const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',await file.arrayBuffer()))).map(x=>x.toString(16).padStart(2,'0')).join('');const attachKey=key+'.attachment.'+id;
  let retry=JSON.parse(sessionStorage.getItem(attachKey)||'null');if(retry&&(retry.hash!==hash||retry.name!==file.name))throw new Error('Reselect the same unconfirmed receipt before uploading a different file.');if(!retry){retry={op:crypto.randomUUID(),version:expense.version,hash,name:file.name};sessionStorage.setItem(attachKey,JSON.stringify(retry));}
  const body=new FormData();body.set('operationId',retry.op);body.set('expectedVersion',retry.version);body.set('file',file);try{await api('/api/expenses/'+id+'/attachments',body,true);sessionStorage.removeItem(attachKey);await load();message('Receipt attached.');}catch(error){if(error.status&&error.status<500)sessionStorage.removeItem(attachKey);throw error;}
 });};load().catch(e=>message(e.message));
})();
