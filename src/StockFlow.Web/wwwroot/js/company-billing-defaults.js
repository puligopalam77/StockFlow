(() => {
 const workspace=document.getElementById('billing-workspace'),due=document.getElementById('payment-due-date'),customer=document.getElementById('sale-customer'),payment=document.getElementById('payment-mode');
 if(!workspace||!due||!customer||!payment||workspace.dataset.paymentTerms==null||workspace.dataset.paymentTerms==='')return;
 const suggest=()=>{
  const credit=document.getElementById('use-split-payment')?.checked
   ? [...document.querySelectorAll('#sale-payment-inputs select')].some(select=>select.value==='CREDIT') : payment.value==='CREDIT';
  if(due.value||!customer.value||!credit)return;
  const parts=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Kolkata',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());
  const get=key=>parts.find(part=>part.type===key).value;
  const date=new Date(Date.UTC(Number(get('year')),Number(get('month'))-1,Number(get('day'))+Number(workspace.dataset.paymentTerms)));
  due.value=date.toISOString().slice(0,10);due.dispatchEvent(new Event('input',{bubbles:true}));
 };
 workspace.addEventListener('change',suggest);
})();
