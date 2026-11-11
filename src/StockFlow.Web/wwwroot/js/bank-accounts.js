(() => {
 'use strict';
 const el=id=>document.getElementById('bank-'+id),workspace=el('workspace');if(!workspace)return;
 const account=workspace.dataset.account,posting=workspace.dataset.posting==='true';
 let report,page=1,version=0,timer,operation,pending,busy=false;
 const money=value=>value===null||value===undefined?'—':new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(value);
 const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
 const error=(text,id='message')=>{el(id).textContent=text;el(id).hidden=!text;};
 const today=()=>new Date(Date.now()-new Date().getTimezoneOffset()*60000).toISOString().slice(0,10);
 function button(text,click,primary=false){const n=node('button',text,'btn '+(primary?'btn-primary':'btn-outline-secondary'));n.type='button';n.addEventListener('click',click);return n;}
 async function request(url,body){
  let response;try{response=await fetch(url,body?{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name=csrf-token]').content},body:JSON.stringify(body)}:{cache:'no-store'});}catch{throw Object.assign(new Error(body?'Result is unconfirmed. Retry the same saved request; do not create another bank entry.':'Bank account data could not be loaded. Check the connection and try again.'),{unknown:!!body});}
  const data=await response.json().catch(()=>null);
  if(!response.ok||!data){
   // A write may commit even if its successful response is empty or truncated.
   // Preserve its identity and payload for retry instead of issuing a new request.
   const unknown=!!body&&(!data||response.status>=500);
   throw Object.assign(new Error(unknown?'Result is unconfirmed. Retry the same saved request; do not create another bank entry.':data?.message||data?.title||'Bank request failed. Refresh and check permissions.'),{unknown,status:response.status});
  }
  return data;
 }
 function render(){
  el('add').hidden=!report.canCreate||!!account;el('back').hidden=!account;
  el('list').hidden=!!account;el('detail').hidden=!account;
  if(!account){
   el('list').replaceChildren();
   if(!report.accounts.length){el('list').append(node('h2','No Bank Accounts Added'),node('p','Add your business bank account to track digital payments and bank transactions.'));return;}
   for(const a of report.accounts){const row=node('div',undefined,'bank-account-row'),info=node('div');info.append(node('strong',a.displayName),node('span',`${a.bankName} · ${a.maskedNumber} · ${a.active?'Active':'Inactive'}${a.defaultHere?' · Default for this shop':''}`));
    if(Object.hasOwn(a,'recordedBalance'))info.append(node('p','Recorded Balance: '+money(a.recordedBalance)));
    const link=node('a','View Transactions','btn btn-outline-primary');link.href='/Settings/BankAccounts/'+a.id;row.append(info,link);el('list').append(row);
   }return;
  }
  const a=report.account;el('title').textContent=a.displayName;document.title=a.displayName+' · StockFlow';
  const current=document.getElementById('breadcrumb-current');if(current)current.textContent=a.displayName;
  el('subtitle').textContent=`${a.bankName} · ${a.maskedNumber} · ${a.active?'Active':'Inactive'}`;
  el('settings').replaceChildren();
  if(report.canEdit)el('settings').append(button('Edit Account',()=>open('edit')));
  if(report.canAssign)el('settings').append(button('Shop Assignments',()=>open('assign')));
  if(report.canDefault&&a.active)el('settings').append(button('Set Shop Default',()=>open('default')));
  if(report.canStatus)el('settings').append(button(a.active?'Deactivate':'Reactivate',()=>open('status')));
  el('initialize').hidden=a.initialized;el('open').hidden=!report.canOpen||!posting||!a.active;
  el('kpis').replaceChildren();
  const tiles=report.ownerOverview?[['Recorded Bank Balance',report.recordedBalance]]:[];
  tiles.push(['Money In',report.moneyIn],['Money Out',report.moneyOut],['Net Movement',report.netMovement]);
  for(const [label,value]of tiles){const card=node('section',undefined,'bank-kpi');card.append(node('span',label),node('strong',money(value)));el('kpis').append(card);}
  el('scope').textContent=report.ownerOverview?'Whole-account recorded bank activity. Opening is excluded from daily Money In/Out.':'Authorized active shop/location activity only. Shared account totals and running balances are not disclosed.';
  el('running-head').hidden=!report.ownerOverview;el('rows').replaceChildren();
  for(const entry of report.transactions){const tr=node('tr');for(const value of [entry.date,entry.reference,entry.type.replaceAll('_',' '),entry.amount>0?money(entry.amount):'—',entry.amount<0?money(-entry.amount):'—'])tr.append(node('td',value));
   if(report.ownerOverview)tr.append(node('td',money(entry.runningBalance)));
   tr.append(node('td',entry.status+' · '+new Date(entry.verifiedAt).toLocaleString()+' · User '+entry.verifiedBy));const actions=node('td');
   if(posting&&report.canReverse&&entry.reversible)actions.append(button('Reverse',()=>open('reverse',entry)));tr.append(actions);el('rows').append(tr);
  }
  if(!report.transactions.length){const td=node('td','No bank transactions for this date.');td.colSpan=report.ownerOverview?8:7;const tr=node('tr');tr.append(td);el('rows').append(tr);}
  el('count').textContent=report.total+' transactions';el('page').textContent=page;el('prev').disabled=page<=1;el('next').disabled=page*20>=report.total;
  el('candidate').replaceChildren(new Option('Choose a recorded payment',''));
  for(const s of report.candidates)el('candidate').append(new Option(`${s.reference} · ${s.kind} · ${s.mode} · ${money(s.amount)} · ${s.day}`,s.kind+':'+s.key));
  el('link').disabled=!a.active||!report.candidates.length;
  el('sources').replaceChildren();
  for(const source of report.sources){const tr=node('tr'),cell=node('td'),check=document.createElement('input');check.type='checkbox';check.value=source.id;check.setAttribute('aria-label','Select '+source.kind+' '+source.key);check.disabled=!posting||!report.canVerify||source.remaining<=0;cell.append(check);tr.append(cell);
   for(const value of [source.kind+' / '+source.key,source.date,source.direction,money(source.remaining)])tr.append(node('td',value));el('sources').append(tr);
  }
  el('verify').hidden=!posting||!report.canVerify||!a.active||!a.initialized;
  el('opening-audit').hidden=!report.opening;if(report.opening)el('opening-audit').querySelector('p').textContent=`${money(report.opening.amount)} · Tracking boundary ${new Date(report.opening.trackingStartedAt).toLocaleString()} · ${report.opening.reference} · Initialized ${new Date(report.opening.initializedAt).toLocaleString()}`;
 }
 async function load(){
  const current=++version;workspace.setAttribute('aria-busy','true');
  try{const data=await request('/api/bankaccounts'+(account?'/'+account:'')+'?'+new URLSearchParams({date:el('date').value||today(),page,search:el('search').value}));if(current!==version)return;report=data;el('date').max=data.today;render();error('');}
  catch(e){if(current!==version)return;error(e.message);el('list').replaceChildren(node('p','Bank account data unavailable.'));el('kpis').replaceChildren();el('rows').replaceChildren();el('sources').replaceChildren();el('settings').replaceChildren();el('opening-audit').hidden=true;el('initialize').hidden=true;el('verify').hidden=true;el('add').hidden=true;el('open').hidden=true;el('link').disabled=true;el('prev').disabled=true;el('next').disabled=true;}
  finally{if(current===version)workspace.removeAttribute('aria-busy');}
 }
 function field(name,label,type='text',required=true,value=''){
  const wrap=node('label',label+(required?' *':'')),input=document.createElement(type==='textarea'?'textarea':type==='select'?'select':'input');input.name=name;input.id='bank-field-'+name;if(type!=='textarea'&&type!=='select')input.type=type;input.required=required;input.value=type==='checkbox'?'true':value;input.setAttribute('data-sf-select','native');wrap.append(input);el('fields').append(wrap);return input;
 }
 function shops(values=[]){const group=node('div');group.append(node('strong','Authorized shops *'));for(const s of report.shops||[]){const label=node('label',undefined,'bank-check'),check=document.createElement('input');check.type='checkbox';check.name='stores';check.value=s.id;check.checked=values.includes(s.id);label.append(check,document.createTextNode(s.name));group.append(label);}el('fields').append(group);}
 function evidence(){const note=field('evidence','Bank evidence / explanation','textarea');note.minLength=10;note.maxLength=1000;}
 function open(action,data){
  if(busy||pending)return;operation={action,accountId:action==='create'?crypto.randomUUID():account,requestId:crypto.randomUUID(),data};
  el('fields').replaceChildren();el('fields').disabled=false;error('','form-error');el('save').textContent='Save';
  const a=report.account;
  el('dialog-title').textContent=({create:'Add Bank Account',edit:'Edit Bank Account',assign:'Shop Assignments',default:'Default Receiving Account',status:a?.active?'Deactivate Account':'Reactivate Account',open:'Set Opening Bank Balance',link:'Link Recorded Payment',verify:'Verify Bank Evidence',reverse:'Reverse Bank Verification'})[action];
  el('help').textContent=['open','verify','reverse'].includes(action)?'Use actual bank evidence. This changes Recorded Bank Balance only; it does not establish Actual/Live Bank Balance.':'Account configuration does not move money or change existing payment destinations.';
  if(['create','edit'].includes(action)){
   field('bankName','Bank Name','text',true,a?.bankName||'').maxLength=120;field('displayName','Account Display Name','text',true,a?.displayName||'').maxLength=120;field('holderName','Account Holder Name','text',true,a?.holderName||'').maxLength=150;
   const number=field('accountNumber',action==='edit'?'Account Number (blank keeps existing)':'Account Number','text',action==='create');number.inputMode='numeric';number.pattern='[0-9]{6,34}';number.maxLength=34;number.autocomplete='off';
   field('ifsc','IFSC','text',true,a?.ifsc||'').pattern='[A-Za-z]{4}0[A-Za-z0-9]{6}';const type=field('accountType','Account Type','select');for(const v of ['CURRENT','SAVINGS','OTHER'])type.append(new Option(v,v));type.value=a?.type||'CURRENT';
   field('upiId',action==='edit'?'New UPI ID (blank keeps existing)':'UPI ID','text',false).maxLength=150;
   if(action==='create')shops();
  }else if(action==='assign')shops(a.stores);
  else if(action==='default'){const select=field('storeId','Shop','select');for(const s of report.shops.filter(s=>a.stores.includes(s.id)))select.append(new Option(s.name,s.id));}
  else if(action==='status'){field('reason','Reason','textarea').minLength=3;}
  else if(action==='open'){
   const amount=field('amount','Opening Bank Balance (₹)','number');amount.min='0';amount.step='0.01';amount.max='9999999999.99';field('trackingStartedAt','Evidence tracking boundary','datetime-local');field('reference','Opening bank reference');evidence();
  }else if(action==='link'){
   const candidate=report.candidates.find(s=>s.kind+':'+s.key===el('candidate').value);if(!candidate){error('Choose a recorded payment first.');return;}operation.data=candidate;el('fields').append(node('p',`${candidate.reference} · ${candidate.mode} · ${money(candidate.amount)}. Link to ${a.displayName}? No bank money is posted.`));
  }else if(action==='verify'){
   const ids=[...el('sources').querySelectorAll('input:checked')].map(n=>n.value),sources=report.sources.filter(s=>ids.includes(s.id));if(!sources.length){error('Select pending recorded payments to verify.');return;}
   operation.data=sources;let total=0;for(const s of sources){const amount=field(s.id,s.kind+' / '+s.key+' · '+s.direction+' (₹)','number',true,s.remaining);amount.min='0.01';amount.step='0.01';amount.max=s.remaining;total+=s.remaining*(s.direction==='IN'?1:-1);}
   const amount=field('amount','Actual verified bank amount (₹)','number',true,Math.abs(total));amount.step='0.01';amount.min='0.01';const dir=field('direction','Bank direction','select');dir.append(new Option('Money In','IN'),new Option('Money Out','OUT'));dir.value=total>=0?'IN':'OUT';field('effectiveDate','Bank-effective date','date',true,el('date').value||today()).max=report.today;field('reference','Bank reference / UTR');evidence();const check=field('afterOpening','Evidence is after opening and not already included in its balance','checkbox');check.className='bank-check';
  }else if(action==='reverse'){el('fields').append(node('p',`Reverse ${data.reference} (${money(data.amount)}) as a record correction. This does not send a bank refund.`));field('effectiveDate','Correction effective date','date',true,today()).max=report.today;evidence();}
  el('dialog').showModal();el('fields').querySelector('input,select,textarea')?.focus();
 }
 function close(){if(busy||pending)return;el('dialog').close();operation=null;el('fields').replaceChildren();}
 async function save(event){
  event.preventDefault();if(busy||!el('form').reportValidity())return;
  if(!pending){
   const f=new FormData(el('form')),body=Object.fromEntries(f),action=operation.action;
   if(['edit','assign','default','status'].includes(action))body.expectedVersion=report.account.version;
   if(['create','assign'].includes(action)){body.stores=f.getAll('stores').map(Number);if(!body.stores.length){error('Select at least one shop.','form-error');return;}}
   if(action==='edit'&&!body.upiId)delete body.upiId;
   if(action==='default')body.storeId=Number(body.storeId);
   if(action==='status')body.active=!report.account.active;
   if(action==='open'){body.amount=Number(body.amount);body.trackingStartedAt=new Date(body.trackingStartedAt).toISOString();}
   if(action==='link'){body.kind=operation.data.kind;body.key=operation.data.key;}
   if(action==='verify'){body.allocations=operation.data.map(s=>({linkId:s.id,amount:Number(f.get(s.id))}));for(const s of operation.data)delete body[s.id];body.amount=Number(body.amount);body.afterOpening=f.has('afterOpening');}
   pending={action,accountId:operation.accountId,requestId:operation.requestId,body};
  }
  busy=true;el('form').dataset.saving='true';el('fields').disabled=true;el('save').disabled=true;el('cancel').disabled=true;el('close').disabled=true;error('','form-error');
  try{
   await request('/api/bankaccounts/'+pending.accountId+'/'+pending.action,{requestId:pending.requestId,body:pending.body});
   const created=pending.action==='create'?pending.accountId:null;pending=null;busy=false;window.StockFlowUnsaved?.markSaved(el('form'));el('dialog').close();el('fields').replaceChildren();
   if(created){location.href='/Settings/BankAccounts/'+created;return;}await load();
  }catch(e){error(e.message,'form-error');if(!e.unknown){pending=null;el('fields').disabled=false;}el('save').textContent=e.unknown?'Retry Same Request':'Save';el('form-error').focus();}
  finally{busy=false;el('form').dataset.saving=pending?'true':'false';el('save').disabled=false;el('cancel').disabled=!!pending;el('close').disabled=!!pending;}
 }
 el('date').value=today();el('add').addEventListener('click',()=>open('create'));el('open').addEventListener('click',()=>open('open'));el('link').addEventListener('click',()=>open('link'));el('verify').addEventListener('click',()=>open('verify'));
 el('close').addEventListener('click',close);el('cancel').addEventListener('click',close);el('dialog').addEventListener('cancel',event=>{event.preventDefault();close();});el('form').addEventListener('submit',save);
 el('date').addEventListener('change',()=>{page=1;load();});el('search').addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(()=>{page=1;load();},200);});el('prev').addEventListener('click',()=>{page--;load();});el('next').addEventListener('click',()=>{page++;load();});
 load();
})();
