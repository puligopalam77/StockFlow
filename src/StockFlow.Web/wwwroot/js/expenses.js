(() => {
 'use strict';const root=document.getElementById('expenses');if(!root)return;const $=id=>document.getElementById(id),mode=root.dataset.mode,createMode=root.classList.contains('expense-create');
 const message=(text)=>{$('expense-message').textContent=text;$('expense-message').hidden=!text;};
 const money=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(Number(n));
 const paise=n=>Math.round(Number(n||0)*100);
 const paymentMethod=code=>({CASH:'Cash',UPI:'UPI',DEBIT_CARD:'Debit Card',CREDIT_CARD:'Credit Card',BANK_TRANSFER:'Bank Transfer'}[code]||code);
 const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Kolkata',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
 const csrf=()=>document.querySelector('meta[name="csrf-token"]').content;
 async function api(url,body,form){const r=await fetch(url,body?{method:'POST',headers:form?{'X-CSRF-TOKEN':csrf()}:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf()},body:form?body:JSON.stringify(body)}:{});const data=await r.json().catch(()=>null);if(!r.ok){const e=new Error(data?.message||data?.title||'The result could not be confirmed.');e.status=r.status;throw e;}return data;}
 function row(table,values,link){const tr=document.createElement('tr');for(const value of values){const td=document.createElement('td');td.textContent=value??'';tr.append(td);}if(link){const td=document.createElement('td'),a=document.createElement('a');a.href='/Expenses/Details?id='+encodeURIComponent(link);a.textContent='Details';td.append(a);tr.append(td);}table.append(tr);return tr;}
 function expenseListRow(e){
  const tr=row($('expense-rows'),[e.incurred_date,'',e.payee,money(e.amount),e.status==='POSTED'?money((paise(e.amount)-paise(e.paid))/100):'—',e.status==='POSTED'?state(e):lifecycle(e)],e.expense_id);
  const category=document.createElement('strong'),description=document.createElement('small');category.textContent=e.category;description.textContent=e.description;tr.children[1].append(category,description);
  tr.children[3].classList.add('expense-currency');tr.children[4].classList.add('expense-currency');
  tr.lastElementChild.querySelector('a').setAttribute('aria-label','View expense: '+e.description);
 }
 const state=e=>e.status==='POSTED'?(Number(e.paid)===0?'Unpaid':Number(e.paid)===Number(e.amount)?'Paid':'Partially paid'):e.status;
 const lifecycle=e=>e?.status==='POSTED'?'Recorded':e?.status==='CANCELLED'?'Cancelled':'Draft';
 if(mode==='list'||mode==='report'){
  $('expense-from').value=today.slice(0,8)+'01';$('expense-to').value=today;
  if(mode==='list'){
   const pager=StockFlowPagination.remote({root,host:$('expense-pager'),filterRoot:$('expense-filters'),label:'expenses',load:async r=>{const p=new URLSearchParams({from:$('expense-from').value,to:$('expense-to').value,term:$('expense-term').value,page:r.page,pageSize:r.size});for(const [param,k]of [['category','category'],['payee','payee'],['status','status'],['paymentStatus','payment']]){const v=$('expense-filter-'+k).value;if(v)p.set(param,v);}return api('/api/expenses?'+p);},render:rows=>{$('expense-rows').replaceChildren();for(const e of rows)expenseListRow(e);$('expense-list-empty').hidden=rows.length>0;message('');},onError:e=>message(e.message)});
   $('expense-filters').onsubmit=e=>{e.preventDefault();pager.reload({reset:true});};$('expense-filter-reset').onclick=()=>{$('expense-filters').reset();$('expense-from').value=today.slice(0,8)+'01';$('expense-to').value=today;pager.reload({reset:true});};
   api('/api/expenses/options').then(o=>{for(const [k,rows]of [['category',o.categories],['payee',o.payees]])for(const r of rows)$('expense-filter-'+k).add(new Option(r.name,r.id));}).catch(e=>message(e.message));pager.reload();return;
  }
  let reportRevision=0;
  async function load(){const revision=++reportRevision;root.setAttribute('aria-busy','true');try{const p=new URLSearchParams({from:$('expense-from').value,to:$('expense-to').value}),d=await api('/api/expenses/report?'+p);if(revision!==reportRevision)return;$('expense-rows').replaceChildren();for(const e of d.expenses)row($('expense-rows'),[e.incurred_date,e.category,e.payee,money(e.amount),money(e.paid),lifecycle(e)+' / '+(e.status==='POSTED'?state(e):'Not recorded')],e.expense_id);
   $('expense-summary').textContent='Recorded expenses: '+money(d.incurred)+' · Payments in period: '+money(d.paid)+' · Balance due at cutoff: '+money(d.outstandingAtCutoff);$('expense-definition').textContent=d.definition;$('expense-export').href='/api/expenses/export?'+p;$('expense-report-payments').replaceChildren();for(const x of d.payments)row($('expense-report-payments'),[x.paid_date,x.expense_id,x.kind,paymentMethod(x.payment_mode),money(x.amount)]);
   const monthly=d.monthly||[],categories=d.categories||[],outstanding=d.outstanding||[];
   $('expense-report-monthly').replaceChildren();for(const x of monthly)row($('expense-report-monthly'),[new Intl.DateTimeFormat('en-IN',{month:'long',year:'numeric',timeZone:'UTC'}).format(new Date(x.month+'-01T00:00:00Z')),x.count,money(x.total)]);
   $('expense-report-categories').replaceChildren();for(const x of categories)row($('expense-report-categories'),[x.category,x.count,money(x.total)]);
   $('expense-report-outstanding').replaceChildren();for(const x of outstanding)row($('expense-report-outstanding'),[x.incurred_date,x.payee,x.category,money(x.amount),money(x.paid),money((paise(x.amount)-paise(x.paid))/100)],x.expense_id);
   $('expense-monthly-empty').hidden=monthly.length>0;$('expense-categories-empty').hidden=categories.length>0;$('expense-outstanding-empty').hidden=outstanding.length>0;$('expense-outstanding-note').textContent='Balances through '+p.get('to')+'; includes earlier unpaid expenses.';message('');}catch(e){if(revision===reportRevision)message(e.message);}finally{if(revision===reportRevision)root.setAttribute('aria-busy','false');}}
  $('expense-filters').onsubmit=e=>{e.preventDefault();load();};load();return;
 }
 const key='stockflow.expense.pending.'+root.dataset.scope,createKey=key+'.create';let pending=JSON.parse(sessionStorage.getItem(key)||'null'),options,expense=null,busy=false;
 let id=root.dataset.id||pending?.id||sessionStorage.getItem(createKey)||crypto.randomUUID();if(!root.dataset.id)sessionStorage.setItem(createKey,id);
 if(pending&&pending.id!==id){location.replace('/Expenses/Details?id='+encodeURIComponent(pending.id));return;}
 const attachmentKey=key+'.attachment.'+id,receiptKey=key+'.receipt.'+id,completionKey=key+'.receipt-completion.'+id;
 // File bytes stay in memory. After reload, ask for the file again and retain its retry identity.
 let receiptFile=null,receiptInfo=JSON.parse(sessionStorage.getItem(receiptKey)||sessionStorage.getItem(attachmentKey)||'null'),receiptCompletion=JSON.parse(sessionStorage.getItem(completionKey)||'null'),receiptReading=false;
 const can=action=>Boolean(options?.permissions[action]);
 let correcting=false,initialForm=null,taxBusy=false,taxQuote=null,taxConfirmed='',taxRevision=0,taxTimer;
 const formSnapshot=()=>JSON.stringify([...$('expense-form').querySelectorAll('input,select,textarea'),...$('expense-gst-fields').querySelectorAll('input,select'),...$('expense-initial-payment').querySelectorAll('input,select,textarea')].filter(e=>!e.dataset.sfAddressTransient).map(e=>[e.id,['checkbox','radio'].includes(e.type)?e.checked:e.value]));
 const includesGst=()=>createMode?$('expense-gst-includes').checked:$('expense-gst-applicable').value==='INCLUDES_GST';
 const gstInput=()=>includesGst()?{ruleId:Number($('expense-gst-rule').value),supplierGstin:$('expense-gst-gstin').value.trim().toUpperCase()||null,invoiceNumber:$('expense-gst-invoice').value.trim()||null,itcStatus:$('expense-gst-itc').value}:null;
 const taxRequest=()=>({amount:Number($('expense-amount').value),incurredDate:$('expense-incurred').value,gst:gstInput()});
 const taxReady=()=>!includesGst()||!taxBusy&&taxConfirmed===JSON.stringify(taxRequest());
 const hasTaxRates=()=>[...$('expense-gst-rule').options].some(o=>o.value&&!o.disabled);
 function fillTaxOptions(){
  const select=$('expense-gst-rule'),value=select.value,day=$('expense-incurred').value||today,rows=(options?.taxRules||[]).filter(r=>r.effectiveFrom<=day&&(!r.effectiveTo||r.effectiveTo>=day));
  const saved=expense?.gst_details;if(saved&&day===saved.expenseDate&&!rows.some(r=>String(r.id)===String(saved.ruleId)))rows.push({id:saved.ruleId,name:saved.ruleName,rate:saved.rate,treatment:saved.treatment,supported:true,historical:true});
  rows.sort((a,b)=>a.rate-b.rate||a.name.localeCompare(b.name));
  select.replaceChildren(new Option('Select GST rate',''));
  for(const r of rows){const name=r.name.replace(/^TEST ONLY\s*-\s*/i,'').trim(),rate='GST '+r.rate+'%',label=name.toUpperCase()===rate.toUpperCase()?rate:name+' · '+r.rate+'%';const o=new Option(label+(r.treatment!=='TAXABLE'?' · '+r.treatment.replaceAll('_',' '):'')+(r.historical?' (saved)':''),r.id);o.disabled=r.supported===false;select.add(o);}select.value=value;
  // Keep a short rate list native; reuse the existing search only for larger catalogs.
  select.dataset.sfSelect=rows.length>10?'search':'native';if(rows.length<=10)select.sfSelect?.destroy();window.StockFlowSelect?.scan();
 }
 const taxNote=()=>$('expense-gst-itc').value==='NOT_ELIGIBLE'?'GST credit is not claimed.':'GST credit has not been verified.';
 function taxPrompt(){
  const rows=[...$('expense-gst-rule').options].filter(o=>o.value);
  if(!rows.length)return (options?.taxRules||[]).length?'No GST rates are available for the expense date.':'GST rates are not configured for this store.';
  return rows.some(o=>!o.disabled)?'Choose a GST rate and enter the expense amount and date.':'The configured GST rates need tax review before use.';
 }
 function showTax(){
  $('expense-gst-section').hidden=!includesGst();$('expense-gst-rule').required=includesGst();$('expense-gst-amounts').hidden=!taxQuote||taxBusy;
  if($('expense-gst-settings'))$('expense-gst-settings').hidden=!includesGst()||taxBusy||hasTaxRates();
  if(taxQuote){$('expense-gst-taxable').textContent=money(taxQuote.taxableAmount);$('expense-gst-tax').textContent=money(taxQuote.gstAmount);}
  const preview=$('expense-summary-gst');if(preview){preview.hidden=!includesGst()||!taxQuote||!taxReady();
   if(!preview.hidden){$('expense-summary-gst-rate').textContent=taxQuote.rate+'%';$('expense-summary-taxable').textContent=money(taxQuote.taxableAmount);$('expense-summary-gst-amount').textContent=money(taxQuote.gstAmount);}}
 }
 function setTax(value){
  clearTimeout(taxTimer);taxRevision++;taxBusy=false;taxQuote=null;taxConfirmed='';
  if(createMode){$('expense-gst-no').checked=!value;$('expense-gst-includes').checked=!!value;}else $('expense-gst-applicable').value=value?'INCLUDES_GST':'NO_GST';fillTaxOptions();
  $('expense-gst-rule').value=value?.ruleId||'';$('expense-gst-gstin').value=value?.supplierGstin||'';$('expense-gst-invoice').value=value?.invoiceNumber||'';$('expense-gst-itc').value=value?.itcStatus||'NOT_ASSESSED';
  if(value?.taxableAmount!=null){taxQuote=value;taxConfirmed=JSON.stringify(taxRequest());}$('expense-gst-message').textContent=value?taxNote():'';showTax();
 }
 function refreshTax(){
  clearTimeout(taxTimer);const revision=++taxRevision;taxBusy=false;
  if(!includesGst()){taxQuote=null;taxConfirmed='';$('expense-gst-message').textContent='';showTax();controls();return;}
  const input=taxRequest();if(taxConfirmed===JSON.stringify(input)){showTax();controls();return;}
  taxQuote=null;taxConfirmed='';
  if(!input.gst.ruleId||!input.incurredDate||input.amount<=0){$('expense-gst-message').textContent=taxPrompt();showTax();controls();return;}
  taxBusy=true;$('expense-gst-message').textContent='Checking GST…';showTax();controls();
  taxTimer=setTimeout(async()=>{try{const quote=await api('/api/expenses/tax-preview',input);if(revision!==taxRevision)return;taxQuote=quote;taxConfirmed=JSON.stringify(input);$('expense-gst-message').textContent=taxNote();}catch(error){if(revision!==taxRevision)return;taxQuote=null;$('expense-gst-message').textContent=error.message;}finally{if(revision===taxRevision){taxBusy=false;showTax();controls();}}},250);
 }
 function clean(){initialForm=formSnapshot();window.StockFlowUnsaved?.register($('expense-form'),()=>initialForm!==null&&initialForm!==formSnapshot(),()=>{initialForm=formSnapshot();});}
 function summary(){
  if(!$('expense-summary-amount'))return;const preview=!expense||initialForm!==null&&initialForm!==formSnapshot();
  if(createMode&&(!expense||expense.status==='DRAFT')){
   // Display-only preview in paise. Posting still uses the unchanged request builder.
   const total=Math.max(0,Math.round((Number($('expense-amount').value)||0)*100));
   const paymentState=$('expense-initial-payment').hidden?'UNPAID':$('expense-initial-state').value;
   const paid=total===0?0:paymentState==='PAID'?total:paymentState==='PARTIAL'?Math.max(0,Math.round((Number($('expense-initial-amount').value)||0)*100)):0;
   const balance=paid<=total?money((total-paid)/100):'—';
   $('expense-summary-title').textContent='Expense Preview';const hint=expense?'Saved draft; payment is not recorded.':'Not saved yet.';if($('expense-summary-state').textContent!==hint)$('expense-summary-state').textContent=hint;
   $('expense-summary-amount').textContent=money(total/100);$('expense-summary-paid').textContent=money(paid/100);$('expense-summary-outstanding').textContent=balance;
   $('expense-summary-payment').textContent=total===0?'Not set':paymentState==='PAID'?'Paid':paymentState==='PARTIAL'?'Partially Paid':'Unpaid';
   $('expense-initial-balance').textContent=balance;return;
  }
  if(createMode)$('expense-summary-title').textContent='Expense Summary';
  $('expense-summary-state').textContent=preview?'Unsaved preview; recorded balances below are unchanged.':'Saved expense balances';
  $('expense-summary-amount').textContent=money($('expense-amount').value||0);
  $('expense-summary-paid').textContent=expense?money(expense.paid):'Not recorded';
  $('expense-summary-outstanding').textContent=expense?.status==='POSTED'?money(Number(expense.amount)-Number(expense.paid)):'Not recorded';
  $('expense-summary-payment').textContent=expense?.status==='POSTED'?state(expense):'Not recorded';
  for(const k of ['category','payee'])if($('expense-summary-'+k))$('expense-summary-'+k).textContent=$('expense-'+k).selectedOptions[0]?.value?$('expense-'+k).selectedOptions[0].textContent:'Not selected';
  if($('expense-summary-attachments'))$('expense-summary-attachments').textContent=expense?.attachments.length||0;
 }
 function initialFields(){const v=$('expense-initial-state').value,show=v!=='UNPAID';$('expense-initial-fields').hidden=!show;$('expense-initial-amount-wrap').hidden=v!=='PARTIAL';$('expense-initial-amount').required=v==='PARTIAL';$('expense-initial-date').required=show;$('expense-initial-confirm').required=show;$('expense-initial-date').min=$('expense-incurred').value;$('expense-initial-date').max=today;summary();}
 function controls(){const locked=busy||Boolean(pending)||receiptReading||Boolean(receiptCompletion),draft=!expense||expense.status==='DRAFT',editable=(expense?can('EDIT'):can('CREATE'))&&(draft||correcting)&&expense?.status!=='CANCELLED';
  $('expense-state').hidden=createMode||!expense;
  $('expense-fields').disabled=locked||!editable;$('expense-reason').disabled=locked;
  $('expense-save').hidden=!editable;$('expense-save').disabled=locked||!taxReady();($('expense-save-label')||$('expense-save')).textContent=expense?.status==='POSTED'?'Save Correction':'Save Draft';
  $('expense-record').hidden=!!expense||!can('CREATE')||!can('POST');$('expense-record').disabled=locked||!taxReady();
  $('expense-post').hidden=!expense||!draft||!can('POST');$('expense-post').disabled=locked||!taxReady();
  $('expense-gst-applicable').disabled=locked||!editable;$('expense-gst-fields').disabled=locked||!editable||!includesGst();for(const field of $('expense-gst-fields').querySelectorAll('input,select'))if(!field.dataset.sfAddressTransient)field.disabled=$('expense-gst-fields').disabled||createMode&&field.id==='expense-gst-itc'||field.id==='expense-gst-rule'&&!hasTaxRates();showTax();
  $('expense-initial-payment').hidden=!draft||!editable||!can('POST')||!can('PAY');for(const x of $('expense-initial-payment').querySelectorAll('input,select'))x.disabled=locked;
  $('expense-retry').hidden=!pending;$('expense-retry').disabled=busy;
  $('expense-more').hidden=!expense||expense.status==='CANCELLED'||!(can('EDIT')||can('CANCEL'));
  $('expense-correct-open').hidden=expense?.status!=='POSTED'||!can('EDIT');$('expense-cancel-open').hidden=!expense||expense.status==='CANCELLED'||Number(expense.paid)!==0||!can('CANCEL');
  for(const x of ['expense-correct-open','expense-cancel-open','expense-cancel'])$(x).disabled=locked;
  $('expense-settlement').hidden=correcting||expense?.status!=='POSTED';
  $('expense-payment-open').hidden=!can('PAY')||!expense||paise(expense.amount)<=paise(expense.paid);$('expense-payment-open').disabled=locked;
  $('expense-payment').hidden=correcting||expense?.status!=='POSTED'||!can('REVERSE');$('expense-refund-tools').hidden=$('expense-payment').hidden;for(const x of $('expense-payment').elements)x.disabled=locked||correcting;
  for(const x of $('expense-later-payment').elements)x.disabled=locked;
  for(const name of ['expense-payment-cancel','expense-payment-x'])$(name).disabled=busy;
  $('expense-payment-save').disabled=busy||receiptReading||Boolean(pending&&pending.action!=='PAY');$('expense-payment-save').textContent=busy?'Saving…':pending?.action==='PAY'?'Retry Payment':'Save Payment';
  $('expense-attachment-form').hidden=correcting||expense?.status==='CANCELLED'||!can('ATTACH');
  $('expense-receipt-choose').hidden=$('expense-attachment-form').hidden;
  for(const x of $('expense-attachment-form').elements)x.disabled=busy||Boolean(pending)||receiptReading||correcting;
  // A file may need to be reselected after reload while an expense result is unknown.
  $('expense-receipt-choose').disabled=busy||receiptReading||correcting||Boolean(pending&&!receiptInfo);
  $('expense-file').disabled=$('expense-receipt-choose').disabled;
  $('expense-attachment-note').hidden=false;renderReceipt();
  for(const x of root.querySelectorAll('[data-master]')){x.hidden=!can('EDIT');x.disabled=locked;}
  for(const x of root.querySelectorAll('[data-reverse]'))x.disabled=locked;for(const x of $('expense-master-form').elements)x.disabled=locked;
  $('expense-master-contact').disabled=locked||masterKind!=='PAYEE';
  if(createMode)summary();
 }
 function fillOptions(){for(const [name,rows] of [['category',options.categories],['payee',options.payees]]){const select=$('expense-'+name),value=select.value;select.replaceChildren(new Option('Select '+name,''));for(const r of rows)select.add(new Option(r.name,r.id));select.value=value;}}
 async function load(){options=await api('/api/expenses/options');fillOptions();if(root.dataset.id||expense||pending||receiptCompletion){try{expense=await api('/api/expenses/'+id);}catch(e){if(!(e.status===404&&['SAVE','RECORD'].includes(pending?.action)))throw e;}}
  if(expense){for(const [field,value] of Object.entries({category:expense.category_id,payee:expense.payee_id,incurred:expense.incurred_date,amount:expense.amount,description:expense.description}))$('expense-'+field).value=value;
   $('expense-state').textContent=expense.status==='DRAFT'?'Draft':state(expense)+' / '+lifecycle(expense)+' / version '+expense.version;($('expense-save-label')||$('expense-save')).textContent=expense.status==='POSTED'?'Save Correction':'Save Draft';$('expense-balance').textContent=`Amount Paid: ${money(expense.paid)} · Balance Due: ${money((paise(expense.amount)-paise(expense.paid))/100)}`;
   $('expense-payments').replaceChildren();const reversed=new Set(expense.payments.map(p=>p.reverses_id));for(const p of expense.payments){const tr=row($('expense-payments'),[p.paid_date,p.kind,paymentMethod(p.payment_mode),money(p.amount),p.reference,p.actor_id]);const td=document.createElement('td');if(can('REVERSE')&&expense.status==='POSTED'&&p.kind!=='REVERSAL'&&!reversed.has(p.payment_id)){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-secondary';b.textContent='Reverse';b.dataset.reverse='true';b.onclick=()=>run(()=>{$('expense-refund-tools').open=true;if($('expense-payment-reason').value.trim().length<3){$('expense-payment-reason').focus();throw new Error('Enter a reversal reason in the refund/reversal form.');}return write('REVERSE',{paymentId:p.payment_id,paidDate:$('expense-paid-date').value,reason:$('expense-payment-reason').value});});td.append(b);}tr.append(td);}
   $('expense-history').replaceChildren();for(const e of expense.events)row($('expense-history'),[e.version,e.action,e.at,e.actor,e.reason,money(e.snapshot.amount)]);
   $('expense-attachments').replaceChildren();for(const a of expense.attachments){const li=document.createElement('li'),link=document.createElement('a');link.textContent=a.name;link.href='/api/expenses/'+id+'/attachments/'+a.id;li.append(link);$('expense-attachments').append(li);}
  }else{$('expense-state').textContent='';if(!$('expense-incurred').value)$('expense-incurred').value=today;}
  const restore=pending?.action==='RECORD'?pending.request.body.expense:pending?.action==='SAVE'?pending.request.body:null;
  if(restore)for(const [field,value]of Object.entries({category:restore.categoryId,payee:restore.payeeId,incurred:restore.incurredDate,amount:restore.amount,description:restore.description,reason:restore.reason||''}))$('expense-'+field).value=value;
  setTax(restore&&Object.hasOwn(restore,'gst')?restore.gst:expense?.gst_details);
  if(includesGst()&&!taxQuote)refreshTax();
  const plan=pending?.action==='RECORD'?pending.request.body.payment:(!expense||expense.status==='DRAFT')?JSON.parse(sessionStorage.getItem(key+'.plan.'+id)||'null'):null;
  if(plan){$('expense-initial-state').value=Number(plan.amount)===Number($('expense-amount').value)?'PAID':'PARTIAL';for(const [field,value]of Object.entries({method:plan.paymentMode,date:plan.paidDate,amount:plan.amount,reference:plan.reference||'',notes:plan.reason||''}))$('expense-initial-'+field).value=value;$('expense-initial-confirm').checked=!!pending&&plan.confirmed;}
  for(const k of ['expense-payment-history','expense-correction-history','expense-audit'])$(k).hidden=!expense;
  if(expense){$('expense-payments-empty').hidden=expense.payments.length>0;$('expense-history-empty').hidden=expense.events.length>0;$('expense-created-by').textContent='User '+expense.created_by;$('expense-created-at').textContent=new Date(expense.created_at).toLocaleString();$('expense-updated-at').textContent=new Date(expense.updated_at).toLocaleString();}
  $('expense-incurred').max=today;$('expense-paid-date').max=today;$('expense-paid-date').min=expense?.incurred_date||'2000-01-01';if(!$('expense-initial-date').value)$('expense-initial-date').value=today;
  if(!$('expense-paid-date').value)$('expense-paid-date').value=today;correcting=false;if(!pending)$('expense-reason').value='';$('expense-reason').required=false;$('expense-correction').hidden=true;$('expense-cancel').hidden=true;clean();initialFields();controls();
  if(pending)message('An earlier result is unconfirmed. Retry the unchanged request before making another change.');
  if(receiptCompletion)receiptError(receiptFile?'Expense saved, but receipt upload failed.':'Expense saved. Reselect the receipt, then choose Retry Upload.');
  const flash=sessionStorage.getItem(key+'.success.'+id);if(flash){sessionStorage.removeItem(key+'.success.'+id);const draft=expense?.status==='DRAFT',text=draft?'Draft saved successfully.':flash;message(draft?'':text);window.StockFlowNotify?.show('success',text,{key:'expense-saved',duration:4000});}
 }
 async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){message(e.message);}finally{busy=false;controls();}}
 async function send(){const r=pending;try{await api('/api/expenses/'+r.id+'/'+r.action.toLowerCase(),r.request);
  }catch(e){if(e.status&&e.status<500){sessionStorage.removeItem(key);pending=null;}if(paymentDialog.open)laterError(pending?'Payment result unknown. Choose Retry Payment to confirm it.':e.message);throw e;}
  if(r.action==='SAVE'){const plan=initialPayment();if(plan)sessionStorage.setItem(key+'.plan.'+id,JSON.stringify(plan));else sessionStorage.removeItem(key+'.plan.'+id);}else if(['RECORD','POST','CANCEL'].includes(r.action))sessionStorage.removeItem(key+'.plan.'+id);
  // Confirm the expense separately: an upload failure must never replay this financial operation.
  if(receiptInfo&&['SAVE','RECORD','POST'].includes(r.action)){receiptCompletion={action:r.action};sessionStorage.setItem(completionKey,JSON.stringify(receiptCompletion));}
  sessionStorage.removeItem(key);pending=null;
  if(receiptCompletion){await completeReceipt();return;}
  if(!root.dataset.id){await finishSaved(r.action);return;}
  await load();if(['PAY','REFUND','REVERSE'].includes(r.action)){window.StockFlowUnsaved?.markSaved($('expense-payment'));window.StockFlowUnsaved?.markSaved(paymentForm);paymentDialog.close();}
  const text=r.action==='SAVE'?'Draft saved successfully.':r.action==='PAY'?'Payment recorded successfully':'Saved and confirmed by the server.';message(r.action==='SAVE'?'':text);window.StockFlowNotify?.show('success',text,{key:'expense-saved',duration:4000});
 }
 async function write(action,body){if(pending)throw new Error('Resolve the pending request first.');pending={id,action,request:{operationId:crypto.randomUUID(),expectedVersion:expense?.version||0,body}};sessionStorage.setItem(key,JSON.stringify(pending));controls();await send();}
 const details=()=>({categoryId:$('expense-category').value,payeeId:$('expense-payee').value,incurredDate:$('expense-incurred').value,amount:Number($('expense-amount').value),description:$('expense-description').value,reason:$('expense-reason').value,gst:gstInput()});
 const initialPayment=()=>!$('expense-initial-payment').hidden&&$('expense-initial-state').value!=='UNPAID'?{paidDate:$('expense-initial-date').value,amount:$('expense-initial-state').value==='PAID'?Number($('expense-amount').value):Number($('expense-initial-amount').value),paymentMode:$('expense-initial-method').value,reference:$('expense-initial-reference').value,reason:$('expense-initial-notes').value,confirmed:$('expense-initial-confirm').checked}:null;
 function validRecord(){if(!$('expense-form').reportValidity())return false;const s=$('expense-initial-state').value;if($('expense-initial-payment').hidden||s==='UNPAID')return true;for(const e of $('expense-initial-payment').querySelectorAll('input,select'))if(!e.disabled&&!e.reportValidity())return false;if(s==='PARTIAL'&&Number($('expense-initial-amount').value)>=Number($('expense-amount').value)){message('A partial payment must be less than the expense amount.');return false;}return true;}
 async function record(){if(!validRecord()||!receiptReady()||!taxReady())return;return run(()=>{if(expense&&!can('EDIT'))return write('POST',{});let payment=null;if(!$('expense-initial-payment').hidden&&$('expense-initial-state').value!=='UNPAID')payment={paidDate:$('expense-initial-date').value,amount:$('expense-initial-state').value==='PAID'?Number($('expense-amount').value):Number($('expense-initial-amount').value),paymentMode:$('expense-initial-method').value,reference:$('expense-initial-reference').value,reason:$('expense-initial-notes').value,confirmed:$('expense-initial-confirm').checked};return write('RECORD',{expense:details(),payment});});}
 $('expense-form').onsubmit=e=>{e.preventDefault();if(!$('expense-form').reportValidity()||!receiptReady()||!taxReady())return;run(()=>write(expense?.status==='POSTED'?'CORRECT':'SAVE',details()));};
 $('expense-record').onclick=record;$('expense-post').onclick=record;
 $('expense-correct-open').onclick=()=>{correcting=true;$('expense-correction').hidden=false;controls();$('expense-reason').required=true;$('expense-reason').minLength=3;$('expense-reason').focus();};
 $('expense-cancel-open').onclick=()=>{$('expense-correction').hidden=false;$('expense-cancel').hidden=false;$('expense-reason').required=true;$('expense-reason').minLength=3;$('expense-reason').focus();};
 $('expense-cancel').onclick=()=>{if(!$('expense-reason').reportValidity())return;run(()=>write('CANCEL',{reason:$('expense-reason').value}));};$('expense-retry').onclick=()=>run(send);
 $('expense-initial-state').onchange=initialFields;for(const form of [$('expense-form'),$('expense-initial-payment')])form.addEventListener('input',initialFields);
 for(const field of ['expense-gst-applicable','expense-gst-rule','expense-gst-gstin','expense-gst-invoice','expense-gst-itc','expense-amount','expense-incurred'])$(field).addEventListener(field.endsWith('gstin')||field.endsWith('invoice')||field.endsWith('amount')||field.endsWith('incurred')?'input':'change',()=>{if(field==='expense-incurred')fillTaxOptions();refreshTax();});
 $('expense-payment').onsubmit=e=>{e.preventDefault();run(()=>write($('expense-payment-action').value,{paidDate:$('expense-paid-date').value,amount:Number($('expense-payment-amount').value),paymentMode:$('expense-method').value,reference:$('expense-reference').value,reason:$('expense-payment-reason').value}));};
 const paymentDialog=$('expense-payment-dialog'),paymentForm=$('expense-later-payment');
 const laterError=text=>{$('expense-later-error').textContent=text;$('expense-later-error').hidden=!text;};
 $('expense-payment-open').onclick=()=>{
  if(busy||pending||correcting||!can('PAY')||expense?.status!=='POSTED')return;
  const balance=(paise(expense.amount)-paise(expense.paid))/100;if(balance<=0)return;
  paymentForm.reset();$('expense-later-amount').max=balance;$('expense-later-amount').value=balance;
  $('expense-later-date').value=today;$('expense-later-date').min=expense.incurred_date;$('expense-later-date').max=today;
  $('expense-later-balance').textContent='Balance Due: '+money(balance);laterError('');window.StockFlowUnsaved?.markSaved(paymentForm);paymentDialog.showModal();$('expense-later-amount').focus();
 };
 const closePayment=()=>{if(busy)return;window.StockFlowUnsaved?.markSaved(paymentForm);paymentDialog.close();$('expense-payment-open').focus();};
 $('expense-payment-cancel').onclick=closePayment;$('expense-payment-x').onclick=closePayment;paymentDialog.addEventListener('cancel',e=>{e.preventDefault();closePayment();});
 paymentForm.onsubmit=e=>{
  e.preventDefault();if(busy)return;if(pending?.action==='PAY'){run(send);return;}if(pending||!can('PAY')||expense?.status!=='POSTED')return;
  if(!paymentForm.reportValidity())return;const amount=paise($('expense-later-amount').value),balance=paise(expense.amount)-paise(expense.paid);
  if(amount<=0||amount>balance){laterError('Enter an amount greater than zero and no more than the balance due.');return;}
  laterError('');run(()=>write('PAY',{paidDate:$('expense-later-date').value,amount:amount/100,paymentMode:$('expense-later-method').value,reference:$('expense-later-reference').value}));
 };
 let masterKind,masterButton,masterId;
 const masterForm=$('expense-master-form'),masterDialog=$('expense-master-dialog'),masterName=$('expense-master-name'),masterContact=$('expense-master-contact'),masterSave=$('expense-master-save');
 const masterError=text=>{$('expense-master-error').textContent=text;$('expense-master-error').hidden=!text;};
 for(const button of root.querySelectorAll('[data-master]'))button.onclick=()=>{
  if(busy||pending)return;masterKind=button.dataset.master;masterButton=button;masterId=crypto.randomUUID();const payee=masterKind==='PAYEE';
  masterForm.reset();masterForm.toggleAttribute('data-skip-unsaved-warning',payee);
  $('expense-master-title').textContent=payee?'Add Payee':'Add Expense Category';$('expense-master-subtitle').textContent=payee?'Add a new payee to record expenses.':'Create a category to organize your expenses.';
  $('expense-master-payee-icon').hidden=!payee;$('expense-master-category-icon').hidden=payee;
  $('expense-master-name-label').textContent=payee?'Payee Name *':'Category Name *';masterName.placeholder=payee?'e.g., Shop Owner, Electricity Board':'e.g., Shop Rent, Electricity';
  $('expense-master-contact-field').hidden=!payee;masterContact.disabled=!payee;masterSave.textContent=payee?'Save Payee':'Save Category';
  masterError('');for(const field of [masterName,masterContact])window.StockFlowValidation?.showField(field,'');
  window.StockFlowUnsaved?.markSaved(masterForm);masterDialog.showModal();masterName.focus();
 };
 const closeMaster=async()=>{
  if(busy)return;
  if(masterKind!=='PAYEE'&&window.StockFlowUnsaved?.hasChanges(masterForm)&&!await StockFlowDialog.discard('Discard the unsaved category?'))return;
  window.StockFlowUnsaved?.markSaved(masterForm);masterDialog.close();masterButton?.focus();
 };
 $('expense-master-close').onclick=closeMaster;$('expense-master-x').onclick=closeMaster;
 masterDialog.addEventListener('cancel',e=>{e.preventDefault();closeMaster();});
 for(const field of [masterName,masterContact])field.addEventListener('input',()=>{window.StockFlowValidation?.showField(field,'');masterError('');});
 masterForm.onsubmit=async e=>{
  e.preventDefault();if(busy)return;const payee=masterKind==='PAYEE',name=masterName.value.trim(),mobileNumber=masterContact.value.trim();masterName.value=name;masterContact.value=mobileNumber;
  const nameError=!name?(payee?'Enter a payee name.':'Enter a category name.'):name.length>150?'Use no more than 150 characters.':'';
  const mobileError=payee&&mobileNumber&&!/^[6-9][0-9]{9}$/.test(mobileNumber)?'Enter a valid 10-digit mobile number starting with 6, 7, 8 or 9.':'';
  window.StockFlowValidation?.showField(masterName,nameError);window.StockFlowValidation?.showField(masterContact,mobileError);
  if(nameError||mobileError){(nameError?masterName:masterContact).focus();return;}
  const duplicateMessage=payee?'A payee with this name already exists. Select it from the Payee dropdown.':'A category with this name already exists. Select it from the Category dropdown.';
  if((payee?options.payees:options.categories).some(p=>p.name.trim().toLowerCase()===name.toLowerCase())){masterError(duplicateMessage);return;}
  await run(async()=>{
   masterForm.dataset.saving='true';masterForm.setAttribute('aria-busy','true');masterSave.textContent='Saving…';masterError('');
   try{
    const r=await api('/api/expenses/masters/'+masterKind,{id:masterId,name,...payee?{mobileNumber:mobileNumber||null}:{}});
    const rows=payee?options.payees:options.categories,select=$('expense-'+(payee?'payee':'category'));
    if(!rows.some(p=>p.id===r.id)){rows.push({id:r.id,name});rows.sort((a,b)=>a.name.localeCompare(b.name));}
    if(![...select.options].some(o=>o.value===r.id))select.add(new Option(rows.find(p=>p.id===r.id).name,r.id));
    if(r.existing){masterError(duplicateMessage);return;}
    select.value=r.id;select.dispatchEvent(new Event('change',{bubbles:true}));
    window.StockFlowUnsaved?.markSaved(masterForm);masterDialog.close();summary();
    window.StockFlowNotify?.show('success',payee?'Payee added successfully':'Expense category added successfully',{key:payee?'expense-payee-added':'expense-category-added',duration:4000});
   }catch(error){masterError(error.status?error.message:(payee?'Could not save the payee. Please try again.':'Could not save the category. Please try again.'));}
   finally{delete masterForm.dataset.saving;masterForm.removeAttribute('aria-busy');masterSave.textContent=payee?'Save Payee':'Save Category';}
  });
  if(!masterDialog.open)masterButton?.focus();
 };
 const receiptForm=$('expense-attachment-form');
 let receiptPreviewFile=null,receiptPreviewUrl=null;
 function receiptError(text){$('expense-receipt-error').textContent=text;$('expense-receipt-error').hidden=!text;$('expense-file').setAttribute('aria-invalid',String(Boolean(text)));}
 function renderReceipt(){
  if(receiptPreviewFile!==receiptFile){
   if(receiptPreviewUrl)URL.revokeObjectURL(receiptPreviewUrl);
   receiptPreviewFile=receiptFile;receiptPreviewUrl=receiptFile?URL.createObjectURL(new Blob([receiptFile],{type:receiptInfo.contentType})):null;
   const visual=$('expense-receipt-visual');visual.querySelector('img')?.remove();visual.querySelector('svg').removeAttribute('hidden');
   if(receiptPreviewUrl&&receiptInfo.contentType?.startsWith('image/')){const image=new Image();image.alt='';image.src=receiptPreviewUrl;image.onerror=()=>{if(image.parentElement!==visual)return;image.remove();visual.querySelector('svg').removeAttribute('hidden');};visual.querySelector('svg').setAttribute('hidden','');visual.append(image);}
  }
  $('expense-receipt-selection').hidden=!receiptInfo;$('expense-receipt-name').textContent=receiptInfo?(receiptFile?receiptInfo.name:'Reselect '+receiptInfo.name):'';$('expense-receipt-name').title=receiptInfo?.name||'';
  const size=receiptInfo?.size;$('expense-receipt-meta').textContent=receiptInfo?[receiptInfo.contentType==='application/pdf'?'PDF':receiptInfo.contentType==='image/jpeg'?'JPG':receiptInfo.contentType==='image/png'?'PNG':'',size==null?'':size<1024?size+' B':size<1048576?Math.round(size/1024)+' KB':(size/1048576).toFixed(1)+' MB'].filter(Boolean).join(' · '):'';
  $('expense-receipt-view').hidden=!receiptPreviewUrl;if(receiptPreviewUrl)$('expense-receipt-view').href=receiptPreviewUrl;else $('expense-receipt-view').removeAttribute('href');
  $('expense-receipt-choose').hidden=receiptForm.hidden||Boolean(receiptInfo);($('expense-receipt-choose-label')||$('expense-receipt-choose')).textContent=receiptReading?'Checking…':expense?'Choose Receipt':'Upload Receipt';
  $('expense-receipt-replace').disabled=$('expense-receipt-choose').disabled;$('expense-receipt-replace').setAttribute('aria-label',receiptFile?'Replace selected receipt':'Reselect receipt');
  $('expense-receipt-remove').disabled=busy||Boolean(pending)||receiptReading||Boolean(sessionStorage.getItem(attachmentKey));
  $('expense-attachment-upload').hidden=!expense||!receiptInfo;$('expense-attachment-upload').textContent=busy?'Uploading…':receiptCompletion||sessionStorage.getItem(attachmentKey)?'Retry Upload':'Attach Receipt';
  $('expense-receipt-saved').hidden=!receiptCompletion;$('expense-receipt-saved').href='/Expenses/Details?id='+encodeURIComponent(id);
 }
 window.addEventListener('pagehide',event=>{if(!event.persisted&&receiptPreviewUrl)URL.revokeObjectURL(receiptPreviewUrl);});
 function receiptReady(){if(receiptReading)return false;if(receiptInfo&&!receiptFile){receiptError('Reselect the receipt before saving, or remove it.');return false;}return true;}
 function clearReceipt(){receiptFile=null;receiptInfo=null;$('expense-file').value='';sessionStorage.removeItem(receiptKey);receiptError('');window.StockFlowUnsaved?.markSaved(receiptForm);renderReceipt();}
 $('expense-receipt-choose').onclick=()=>$('expense-file').click();
 $('expense-receipt-replace').onclick=()=>$('expense-file').click();
 $('expense-file').onchange=async()=>{
  const file=$('expense-file').files[0];if(!file)return;receiptReading=true;controls();receiptError('');
  try{
   if(file.size<1||file.size>5242880)throw new Error('Choose a PDF, JPG or PNG up to 5 MB.');
   const bytes=new Uint8Array(await file.arrayBuffer()),starts=signature=>signature.every((value,i)=>bytes[i]===value);
   const contentType=starts([37,80,68,70,45])?'application/pdf':starts([137,80,78,71,13,10,26,10])?'image/png':starts([255,216,255])?'image/jpeg':null;
   if(!contentType)throw new Error('Choose a valid PDF, JPG or PNG receipt.');
   const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))).map(x=>x.toString(16).padStart(2,'0')).join('');
   const retry=JSON.parse(sessionStorage.getItem(attachmentKey)||'null');
   if((retry&&(retry.hash!==hash||retry.name!==file.name))||(pending&&receiptInfo&&(receiptInfo.hash!==hash||receiptInfo.name!==file.name)))throw new Error('Reselect the same receipt to finish the pending upload.');
   receiptFile=file;receiptInfo={name:file.name,hash,size:file.size,contentType};sessionStorage.setItem(receiptKey,JSON.stringify(receiptInfo));
  }catch(error){$('expense-file').value='';receiptError(error.message);}finally{receiptReading=false;controls();}
 };
 $('expense-receipt-remove').onclick=()=>{if(busy||pending||sessionStorage.getItem(attachmentKey))return;clearReceipt();$('expense-receipt-choose').focus();if(receiptCompletion)run(()=>finishSaved(receiptCompletion.action));};
 window.StockFlowUnsaved?.register(receiptForm,()=>Boolean(receiptInfo),()=>{
  clearReceipt();if(receiptCompletion){sessionStorage.removeItem(completionKey);receiptCompletion=null;if(!root.dataset.id)sessionStorage.removeItem(createKey);}
 });
 async function uploadReceipt(){
  if(!receiptFile||!receiptInfo)throw new Error('Reselect the receipt, then choose Retry Upload.');
  let retry=JSON.parse(sessionStorage.getItem(attachmentKey)||'null');
  if(retry&&(retry.hash!==receiptInfo.hash||retry.name!==receiptFile.name))throw new Error('Reselect the same unconfirmed receipt before uploading a different file.');
  if(!retry){const saved=await api('/api/expenses/'+id);retry={op:crypto.randomUUID(),version:saved.version,hash:receiptInfo.hash,name:receiptFile.name};sessionStorage.setItem(attachmentKey,JSON.stringify(retry));}
  const body=new FormData();body.set('operationId',retry.op);body.set('expectedVersion',retry.version);body.set('file',receiptFile);
  try{await api('/api/expenses/'+id+'/attachments',body,true);sessionStorage.removeItem(attachmentKey);clearReceipt();}
  catch(error){if(error.status&&error.status<500)sessionStorage.removeItem(attachmentKey);throw error;}
 }
 async function finishSaved(action){
  sessionStorage.removeItem(completionKey);receiptCompletion=null;
  const text=action==='SAVE'?'Draft saved successfully.':'Expense recorded successfully';
  if(!root.dataset.id){sessionStorage.setItem(key+'.success.'+id,text);sessionStorage.removeItem(createKey);clean();window.StockFlowUnsaved?.allowNavigation();location.replace('/Expenses/Details?id='+encodeURIComponent(id));return;}
  await load();message(action==='SAVE'?'':text);window.StockFlowNotify?.show('success',text,{key:'expense-saved',duration:4000});
 }
 async function completeReceipt(){
  const action=receiptCompletion.action;try{await load();await uploadReceipt();await finishSaved(action);}
  catch(error){message('Expense saved, but receipt upload failed.');receiptError(error.message);controls();}
 }
 receiptForm.onsubmit=e=>{e.preventDefault();if(!expense||busy||pending||receiptReading)return;run(async()=>{
  if(receiptCompletion){await completeReceipt();return;}
  const snapshot=formSnapshot(),baseline=initialForm;
  try{await uploadReceipt();}catch(error){receiptError(error.message);return;}
  await load();for(const [field,value]of JSON.parse(snapshot)){const input=$(field);if(['checkbox','radio'].includes(input.type))input.checked=value;else input.value=value;}initialForm=baseline;initialFields();fillTaxOptions();refreshTax();message('Receipt attached.');
 });};
 const dock=()=>{const assistant=document.querySelector('.sf-help-open');if(assistant)$('expenses').querySelector('.expense-assistant-dock').append(assistant);};if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',dock,{once:true});else dock();load().catch(e=>message(e.message));
})();
