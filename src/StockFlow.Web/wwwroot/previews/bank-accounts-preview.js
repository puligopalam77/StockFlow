// Standalone UI prototype. All state is synthetic and stays in this page's memory.
(() => {
 'use strict';
 const $=id=>document.getElementById(id);
 const money=value=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',maximumFractionDigits:2}).format(value);
 const initial=[
  {id:'main',displayName:'Main business account',bankName:'Sample National Bank',holderName:'Sample Trading Co.',accountType:'Current',lastFour:'4821',ifsc:'DEMO0123456',isDefault:true,opening:125000,
   transactions:[{date:'2026-11-11',description:'Customer payment settlement',reference:'DEMO-SET-001',type:'Settlement',amount:18500},{date:'2026-11-11',description:'Supplier payment',reference:'DEMO-SUP-014',type:'Payment',amount:-7200},{date:'2026-11-10',description:'UPI collection settlement',reference:'DEMO-UPI-028',type:'Settlement',amount:12450},{date:'2026-11-10',description:'Shop operating expense',reference:'DEMO-EXP-006',type:'Expense',amount:-2500}],
   pending:[{date:'2026-11-11',description:'Invoice INV-DEMO-042',reference:'DEMO-PAY-042',mode:'UPI',amount:4500},{date:'2026-11-11',description:'Invoice INV-DEMO-043',reference:'DEMO-PAY-043',mode:'Debit card',amount:2800}]},
  {id:'savings',displayName:'Business reserves',bankName:'Sample Commerce Bank',holderName:'Sample Trading Co.',accountType:'Savings',lastFour:'9036',ifsc:'DEMO0654321',isDefault:false,opening:80000,transactions:[],pending:[]},
  {id:'collections',displayName:'Collections account',bankName:'Sample City Bank',holderName:'Sample Trading Co.',accountType:'Current',lastFour:'1608',ifsc:'DEMO0112233',isDefault:false,opening:32000,
   transactions:[{date:'2026-11-11',description:'Customer payment settlement',reference:'DEMO-SET-018',type:'Settlement',amount:6200}],pending:[{date:'2026-11-11',description:'Invoice INV-DEMO-044',reference:'DEMO-PAY-044',mode:'UPI',amount:1600}]}
 ];
 let accounts=structuredClone(initial),selected='main',editing=null,activeTab='transactions';
 const node=(tag,text,className)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(className)n.className=className;return n;};
 const current=()=>accounts.find(a=>a.id===selected);
 const balance=a=>a.opening+a.transactions.reduce((sum,t)=>sum+t.amount,0);
 const notify=text=>{$('announcement').textContent=text;};
 function renderAccounts(){
  $('account-count').textContent=accounts.length;$('accounts').replaceChildren();
  for(const a of accounts){
   const card=node('button',undefined,'account-card');card.type='button';card.setAttribute('aria-pressed',String(a.id===selected));
   const top=node('div',undefined,'card-top');const icon=node('span','▤','bank-symbol');icon.setAttribute('aria-hidden','true');top.append(icon,node('span',a.isDefault?'Default receiving':'Active','badge'+(a.isDefault?' default':'')));
   const footer=node('div',undefined,'card-bottom'),value=node('div');value.append(node('small','Recorded balance'),node('strong',money(balance(a))));footer.append(value,node('span','→','card-arrow'));
   card.append(top,node('h3',a.displayName),node('span',a.bankName+' · •••• '+a.lastFour,'muted'),footer);
   card.addEventListener('click',()=>{selected=a.id;$('search').value='';render();notify('Showing '+a.displayName);});$('accounts').append(card);
  }
 }
 function renderActivity(){
  const a=current(),term=$('search').value.trim().toLowerCase();
  const matches=t=>[t.date,t.description,t.reference,t.type,t.mode].filter(Boolean).join(' ').toLowerCase().includes(term);
  let running=a.opening;
  // Accumulate in chronological order; filtering never changes the historical balance.
  const all=a.transactions.map((t,index)=>({...t,index})).sort((x,y)=>x.date.localeCompare(y.date)||x.index-y.index).map(t=>({...t,runningBalance:(running+=t.amount)})).reverse();
  const rows=all.filter(matches);$('transactions').replaceChildren();
  for(const t of rows){const row=node('tr'),description=node('td');description.append(node('strong',t.description),node('small',t.reference));row.append(node('td',t.date),description,node('td',t.type),node('td',t.amount>0?money(t.amount):'—','numeric positive'),node('td',t.amount<0?money(-t.amount):'—','numeric negative'),node('td',money(t.runningBalance),'numeric'));
   const status=node('td');status.append(node('span','Sample verified','badge'));row.append(status);$('transactions').append(row);}
  const pending=a.pending.filter(matches);$('pending').replaceChildren();
  for(const t of pending){const row=node('tr'),description=node('td');description.append(node('strong',t.description),node('small',t.reference));row.append(node('td',t.date),description,node('td',t.mode),node('td',money(t.amount),'numeric'));const status=node('td');status.append(node('span','Awaiting settlement','badge pending'));row.append(status);$('pending').append(row);}
  for(const [id,count,columns,text]of [['transactions',rows.length,7,'No transactions match this view.'],['pending',pending.length,5,'No pending payments match this view.']]){if(!count){const row=node('tr'),cell=node('td',text,'empty');cell.colSpan=columns;row.append(cell);$(id).append(row);}}
  $('pending-count').textContent=a.pending.length;
  $('row-count').textContent=activeTab==='transactions'?`${rows.length} of ${all.length} transactions`:`${pending.length} of ${a.pending.length} pending payments`;
 }
 function render(){
  renderAccounts();const a=current();$('detail-name').textContent=a.displayName;$('detail-meta').textContent=`${a.bankName} · ${a.accountType} · •••• ${a.lastFour} · ${a.ifsc}`;
  $('make-default').hidden=a.isDefault;
  const incoming=a.transactions.filter(t=>t.amount>0).reduce((sum,t)=>sum+t.amount,0),outgoing=-a.transactions.filter(t=>t.amount<0).reduce((sum,t)=>sum+t.amount,0);
  $('kpis').replaceChildren();
  for(const [label,value,hint,className]of [['Recorded bank balance',balance(a),'Opening + sample movements',''],['Money in',incoming,'All sample transactions','positive'],['Money out',outgoing,'All sample transactions','negative'],['Pending settlement',a.pending.reduce((sum,t)=>sum+t.amount,0),'Excluded from recorded balance','']]){const tile=node('div',undefined,'kpi');tile.append(node('span',label),node('strong',money(value),className),node('small',hint));$('kpis').append(tile);}
  renderActivity();
 }
 function tab(name){activeTab=name;for(const t of ['transactions','pending']){$(t+'-tab').classList.toggle('selected',t===name);$(t+'-tab').setAttribute('aria-selected',String(t===name));$(t+'-tab').tabIndex=t===name?0:-1;$(t+'-panel').hidden=t!==name;}renderActivity();}
 function open(edit){
  editing=edit?selected:null;const form=$('account-form');form.reset();$('dialog-title').textContent=edit?'Edit Bank Account':'Add Bank Account';
  if(edit){const a=current();for(const key of ['displayName','bankName','holderName','accountType','lastFour','ifsc'])form.elements.namedItem(key).value=a[key];form.elements.namedItem('isDefault').checked=a.isDefault;}
  $('account-dialog').showModal();
 }
 $('add-account').addEventListener('click',()=>open(false));$('edit-account').addEventListener('click',()=>open(true));
 for(const id of ['close-dialog','cancel-dialog'])$(id).addEventListener('click',()=>$('account-dialog').close());
 $('account-form').addEventListener('submit',event=>{
  event.preventDefault();const form=event.currentTarget;
  for(const name of ['displayName','bankName','holderName']){const field=form.elements.namedItem(name);field.value=field.value.trim();}
  if(!form.reportValidity())return;
  const data=Object.fromEntries(new FormData(form));data.ifsc=data.ifsc.toUpperCase();data.isDefault=form.elements.namedItem('isDefault').checked;
  const id=editing||'preview-'+crypto.randomUUID();if(data.isDefault)for(const a of accounts)a.isDefault=false;
  if(editing)Object.assign(current(),data);else accounts.push({...data,id,opening:0,transactions:[],pending:[]});
  selected=id;$('search').value='';render();$('account-dialog').close();notify('Sample account '+(editing?'updated':'added')+'. Refreshing the page clears preview changes.');
 });
 $('make-default').addEventListener('click',()=>{for(const a of accounts)a.isDefault=a.id===selected;render();notify('Default receiving account updated in this preview.');});
 $('reset').addEventListener('click',()=>{accounts=structuredClone(initial);selected='main';$('search').value='';tab('transactions');render();notify('Preview reset to the original sample data.');});
 $('search').addEventListener('input',renderActivity);
 for(const name of ['transactions','pending']){$(name+'-tab').addEventListener('click',()=>tab(name));$(name+'-tab').addEventListener('keydown',event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();const next=event.key==='Home'?'transactions':event.key==='End'?'pending':activeTab==='transactions'?'pending':'transactions';tab(next);$(next+'-tab').focus();}});}
 render();
})();
