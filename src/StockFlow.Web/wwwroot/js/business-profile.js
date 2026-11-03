(async()=>{
    'use strict';
    const el=id=>document.getElementById(id);
    let settings, busy=false;
    const text=(tag,value)=>{const n=document.createElement(tag);n.textContent=value;return n;};
    const message=error=>{el('business-profile-message').textContent=error.message;el('business-profile-message').hidden=false;};
    async function api(options={}) {
        const response=await fetch('/api/business-profile',{credentials:'same-origin',...options,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''}});
        const data=await response.json();if(!response.ok)throw new Error(data.message||'Unable to save the shop profile. Reload and try again.');return data;
    }
    function preview(){
        const profile=settings.profiles.find(p=>p.code===el('business-profile-code').value);
        const box=el('business-profile-preview');box.replaceChildren(text('h3',profile.name),
            text('p','Common menus: Products, Suppliers, Customers, Receive Stock, Sales, Stock, Transfers and Reports, subject to user permissions.'),
            text('p',profile.warrantyAndRepairs?'Specialist menus: Warranty and Repairs remain available.':'Specialist menus: Electrical Warranty and Repairs are hidden and direct access is denied.'),
            text('p','Serial tracking: '+(profile.serialTracking?'Available':'Not enabled for this profile')),
            text('p','Starter categories: '+(profile.categories.join(', ')||'Keep the current electrical catalog unchanged.')));
        const changing=profile.code!==settings.code;
        const reapplyBike=settings.catalogSwitchingAvailable&&profile.code==='BIKE_ACCESSORIES';
        const retainedSwitch=settings.catalogSwitchingAvailable&&['ELECTRICAL','BIKE_ACCESSORIES'].includes(settings.code)&&['ELECTRICAL','BIKE_ACCESSORIES'].includes(profile.code);
        const blocked=changing&&!retainedSwitch&&(settings.impact.productCount>0||settings.impact.transactionCount>0);
        el('business-profile-save').disabled=busy||(!changing&&!reapplyBike)||blocked||!settings.available||!settings.canEdit;
        el('business-profile-categories').disabled=!changing||profile.categories.length===0;
        el('business-profile-impact').textContent=`This business has ${settings.impact.productCount} product(s), ${settings.impact.categoryCount} category/categories and ${settings.impact.transactionCount} transaction record(s). `+(blocked?'The profile cannot be changed yet. Apply migration 050 for Electrical / Bike Accessories catalog switching.':retainedSwitch?'These records stay under their original categories. No records or stock quantities will be converted.':'');
        el('business-profile-preservation').textContent=settings.catalogSwitchingAvailable?(settings.dataVisibilityAvailable?'Products, contacts, purchases, receipts, sales, payments, reports and totals follow the selected shop setup. Saved records are retained. Switch back to Electrical to view its records again. Company details, stores, staff and security settings are shared.':'Catalog and current stock follow the selected setup. Apply database migration 051 manually to also hide other setups’ transactions and totals. Existing records are retained.'):'Populated businesses require manual migration 050 before switching between Electrical and Bike Accessories. Existing records will not be deleted or relabeled.';
    }
    try {
        settings=await api();
        if(settings.catalogSwitchingAvailable){
            el('business-profile-reason').required=false;el('business-profile-reason').hidden=true;
            document.querySelector('label[for="business-profile-reason"]').hidden=true;
            el('business-profile-acknowledge').required=false;el('business-profile-acknowledge').closest('label').hidden=true;
            el('business-profile-categories').closest('label').hidden=true;
            el('business-profile-save').textContent='Apply shop setup';
        }
        const current=el('business-profile-current');
        for(const [label,value]of [['Shop profile',settings.name],['Configuration version',String(settings.version)],['Scope','This company and all its branches']])current.append(text('dt',label),text('dd',value));
        el('business-profile-code').replaceChildren(...settings.profiles.map(p=>new Option(p.name,p.code)));el('business-profile-code').value=settings.code;
        el('business-profile-fields').disabled=!settings.available||!settings.canEdit;
        el('business-profile-status').textContent=!settings.available?'Apply database migration 047 manually before changing the shop profile. Existing electrical workflows remain available.':!settings.canEdit?'Only an owner or administrator with Business & Billing edit permission can apply changes.':'Select a shop type to review its categories and enabled features.';
        preview();el('business-profile-code').onchange=preview;
    }catch(e){message(e);}
    el('business-profile-form').onsubmit=async event=>{
        event.preventDefault();if(busy||el('business-profile-save').disabled||!event.target.reportValidity())return;
        busy=true;preview();el('business-profile-message').hidden=true;
        try{
            await api({method:'PUT',body:JSON.stringify({code:el('business-profile-code').value,expectedVersion:settings.version,reason:settings.catalogSwitchingAvailable?'Apply '+el('business-profile-code').selectedOptions[0].textContent+' catalog setup':el('business-profile-reason').value.trim(),createStarterCategories:settings.catalogSwitchingAvailable||el('business-profile-categories').checked,acknowledged:settings.catalogSwitchingAvailable||el('business-profile-acknowledge').checked})});
            // Reload fetches the new tenant context, permissions, menus and category scope.
            if(settings.catalogSwitchingAvailable){location.assign('/Products');return;}
            location.reload();
        }catch(e){message(e);busy=false;preview();}
    };
})();
