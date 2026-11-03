(() => {
    const form=document.getElementById('billing-policy-form');if(!form)return;
    const byId=id=>document.getElementById(id),input=byId('discount-limit-percent'),save=byId('save-billing-policy'),reload=byId('reload-billing-policy'),message=byId('billing-policy-message');
    const fields={fastBillingEnabled:'fast-billing-enabled',allowRoutineDiscounts:'allow-routine-discounts',requireRoutineReason:'require-routine-reason',requireApprovalAboveLimit:'require-approval-above-limit',requireExceptionalReason:'require-exceptional-reason'};
    let busy=false,canManage=false,version=null;
    const show=(text,error)=>{message.textContent=text;message.className='alert '+(error?'alert-danger':'alert-success');message.hidden=false;};
    const lock=()=>{input.disabled=save.disabled=busy||!canManage;reload.disabled=busy;for(const id of Object.values(fields))byId(id).disabled=busy||!canManage;};
    async function api(options={}){
        const response=await fetch('/api/settings/billing-policy',{credentials:'same-origin',...options,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''}});
        const data=await response.json().catch(()=>null);
        if(!response.ok)throw new Error(data?.message||'Billing policy could not be saved or loaded. Check your permissions and try again.');
        return data;
    }
    async function load(){
        if(busy)return;busy=true;lock();
        try{
            const policy=await api();input.value=policy.discountLimitPercent;version=policy.version??null;canManage=policy.canManage&&policy.shopScoped!==false;
            for(const [key,id] of Object.entries(fields))byId(id).checked=policy[key]??key!=='fastBillingEnabled';
            message.hidden=true;if(policy.shopScoped===false)show('Apply migration 082 before editing the shop-specific Fast Billing Policy.',true);
            window.StockFlowUnsaved?.markSaved(form);
        }catch(error){canManage=false;show(error.message,true);}
        finally{busy=false;lock();}
    }
    byId('fast-billing-enabled').addEventListener('change',()=>{if(byId('fast-billing-enabled').checked)byId('require-routine-reason').checked=false;});
    reload.addEventListener('click',load);
    form.addEventListener('submit',async event=>{
        event.preventDefault();if(busy||!canManage||!form.reportValidity())return;
        const payload={discountLimitPercent:Number(input.value),version};for(const [key,id] of Object.entries(fields))payload[key]=byId(id).checked;
        busy=true;lock();
        try{const result=await api({method:'PUT',body:JSON.stringify(payload)});version=result?.version??version;show('Billing discount policy saved for this shop.',false);window.StockFlowUnsaved?.markSaved(form);}
        catch(error){show(error.message,true);}
        finally{busy=false;lock();}
    });
    load();
})();
