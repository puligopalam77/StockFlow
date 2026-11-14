// Address conventions from SchoolERP Views/Application/Create and wwwroot/js/
// form-interactions.js + site.js. Required fields remain owned by each workflow.
window.StockFlowAddress = (() => {
    'use strict';
    const groups=new Set(), bound=new WeakSet(), combined=new WeakSet();
    let serial=0, locations=[], loading, scanning=false;
    const node=(tag,text)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;return n;};
    const key=input=>(input.name||input.dataset.field||input.id).split(/[.\-_]/).at(-1).toLowerCase();
    const norm=value=>String(value||'').trim().toLowerCase().replace(/\s+/g,' ');
    const stateLabel=value=>String(value||'').replace(/^\s*\d{2}\s*[-–—:.)·]*\s*/,'').replace(/\s*\(?\d{2}\)?\s*$/,'').trim();
    const active=input=>input&&!input.disabled&&!input.readOnly;
    function emit(input){input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));}
    function watchValue(input,changed){
        const prototype=input instanceof HTMLSelectElement?HTMLSelectElement.prototype:input instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
        const descriptor=Object.getOwnPropertyDescriptor(prototype,'value');
        Object.defineProperty(input,'value',{configurable:true,get(){return descriptor.get.call(this);},set(value){descriptor.set.call(this,value);changed();}});
    }
    // Keep the original source/value for existing form serializers and saved records.
    function fixedCountry(source){
        if(!source)return;
        if(source.sfCountryField){source.sfCountryField.refresh();return;}
        const coded=key(source)==='countrycode',select=source instanceof HTMLSelectElement;
        const display=select?node('input'):source;
        if(!source.id)source.id='sf-country-'+(++serial);
        if(select){
            source.sfSelect?.destroy();source.removeAttribute('data-sf-select');
            source.hidden=true;source.tabIndex=-1;
            display.type='text';display.id=source.id+'-display';display.className='form-control';display.dataset.sfAddressUi='true';
            source.after(display);
            for(const label of source.labels||[])label.htmlFor=display.id;
        }
        display.classList.add('sf-country-field');display.readOnly=true;display.tabIndex=-1;display.setAttribute('aria-readonly','true');
        const descriptor=Object.getOwnPropertyDescriptor(select?HTMLSelectElement.prototype:HTMLInputElement.prototype,'value');
        const canonical=coded?'IN':'India';
        function label(value){
            if(['in','india'].includes(norm(value)))return 'India';
            if(select&&/^[A-Z]{2}$/.test(value)){try{return new Intl.DisplayNames(['en'],{type:'region'}).of(value);}catch{}}
            return value;
        }
        function assign(value){
            value=String(value||canonical);
            if(select&&![...source.options].some(o=>o.value===value))source.add(new Option(label(value),value));
            descriptor.set.call(source,value);
        }
        function refresh(){
            if(!source.value)assign(canonical);
            if(select)display.value=label(source.value);
            if(!display.readOnly)display.readOnly=true;
        }
        Object.defineProperty(source,'value',{configurable:true,get(){return descriptor.get.call(this);},set(value){assign(value);refresh();}});
        source.sfCountryField={display,refresh};source.addEventListener('change',refresh);refresh();
    }
    async function reference(){
        if(!loading)loading=fetch('/data/india-state-districts.json',{credentials:'same-origin',cache:'no-cache'}).then(async r=>{if(!r.ok)throw Error('Address options could not be loaded.');const data=await r.json();if(!Array.isArray(data)||!data.every(r=>r.state&&Array.isArray(r.districts)))throw Error('Address options could not be loaded.');locations=data;return data;}).catch(error=>{loading=null;throw error;});
        return loading;
    }
    function picker(source,label,values,allowFree=()=>false){
        const focused=document.activeElement===source;
        const api=StockFlowSelect.create(source,{address:true,label,values,allowFree,
            onSelect:()=>source.dispatchEvent(new CustomEvent('sf-address-selection',{bubbles:true}))});
        if(focused)api.input.focus();
        return api;
    }
    function bind(fields){
        if(!fields.state||!fields.city||!fields.pin||bound.has(fields.state))return;
        fixedCountry(fields.country);
        bound.add(fields.state);const group={...fields,version:0,verified:'',offices:[],dirtyPin:false,applied:false};groups.add(group);
        const strict=group.state.form?.dataset.indianAddress==='true';
        const india=()=>!group.country||['in','india',''].includes(norm(group.country.value));
        const currentState=()=>group.state instanceof HTMLSelectElement?stateLabel(group.state.selectedOptions[0]?.textContent):group.state.value;
        const stateOptions=()=>{
            if(!india())return group.state instanceof HTMLSelectElement?[...group.state.options].filter(o=>o.value).map(o=>({value:o.value,label:o.textContent})):[{value:group.state.value,label:group.state.value}].filter(o=>o.value);
            if(group.state instanceof HTMLSelectElement)return [...group.state.options].filter(o=>o.value).map(o=>({value:o.value,label:stateLabel(o.textContent)}));
            return locations.map(row=>({value:row.state,label:row.state}));
        };
        const districtOptions=()=>{const row=locations.find(row=>norm(row.state)===norm(currentState()));const names=new Set(row?.districts||[]);for(const office of group.offices)if(norm(office.State)===norm(currentState()))names.add(office.District);if(!strict&&group.district?.value)names.add(group.district.value);return [...names].sort((a,b)=>a.localeCompare(b)).map(name=>({value:name,label:name}));};
        group.statePicker=picker(group.state,'State',stateOptions,()=>!india());
        if(group.district)group.districtPicker=picker(group.district,'District',districtOptions,()=>!india());
        group.city.placeholder='Enter City or Town';group.pin.autocomplete='postal-code';
        const officeGroup=node('div');officeGroup.className='sf-address-locality';officeGroup.hidden=true;
        const office=node('select');office.id=(group.pin.id||'sf-pin-'+(++serial))+'-locality';office.className='form-select';office.dataset.sfAddressUi='true';
        office.dataset.sfAddressTransient='true';
        const label=node('label','Post Office/Locality');label.htmlFor=office.id;label.className='form-label';
        const status=node('small');status.className='sf-address-status';status.setAttribute('aria-live','polite');
        const retry=node('button','Retry PIN lookup');retry.type='button';retry.className='btn btn-outline-secondary btn-sm';retry.hidden=true;
        officeGroup.append(label,office);group.pin.after(status,retry,officeGroup);group.officeGroup=officeGroup;group.status=status;
        function error(message){if(group.pin.dataset.sfPostalError&&group.pin.validationMessage===group.pin.dataset.sfPostalError)group.pin.setCustomValidity('');group.pin.dataset.sfPostalError=message;if(message)group.pin.setCustomValidity(message);StockFlowValidation?.showField(group.pin,message);retry.hidden=!message?.includes('unavailable');}
        const postalMatch=()=>group.offices.find(o=>norm(o.State)===norm(currentState())&&(!active(group.district)||!group.district.value||norm(o.District)===norm(group.district.value))&&(!active(group.city)||!(strict?group.city.value:office.value)||[o.Name,o.Block,...(strict?[o.District]:[])].some(name=>name&&norm(name)===norm(group.city.value))));
        function validate(){
            if(strict&&active(group.district)){
                const mismatch=group.district.value&&locations.length&&!districtOptions().some(o=>norm(o.value)===norm(group.district.value));
                group.district.setCustomValidity(mismatch?'Select a district belonging to the selected State.':'');
                if(mismatch)return false;
            }
            if(!active(group.pin)||!india()){error('');return true;}
            const pin=group.pin.value.trim();if(!pin){error('');return !group.pin.required;}
            if(!/^[1-9][0-9]{5}$/.test(pin)){error('Enter a valid 6-digit Indian PIN code.');return false;}
            if(group.verified===pin){if(!postalMatch()){error(strict||office.value?'State, district and city/locality must match this PIN code. Choose a listed locality or correct the address.':'State and district must match this PIN code.');return false;}error('');return true;}
            return !group.dirtyPin||!group.pin.dataset.sfPostalError;
        }
        function assign(input,value){if(!input||!active(input))return;if(input instanceof HTMLSelectElement&&!Array.from(input.options).some(o=>o.value===value))input.add(new Option(value,value));input.value=value;emit(input);}
        function apply(selected){
            if(!selected)return;group.applied=true;
            const state=stateOptions().find(o=>norm(o.label)===norm(selected.State));
            if(!state){error('PIN details do not match a supported state. Review the address.');group.applied=false;return;}
            if(strict&&!active(group.state)&&norm(currentState())!==norm(selected.State)){group.applied=false;error('This PIN belongs to a different State. Correct the PIN or GST registration.');return;}
            assign(group.state,state.value);assign(group.district,selected.District);assign(group.city,selected.Name);group.applied=false;validate();status.textContent=[selected.Name,selected.District,selected.State].join(', ');
        }
        async function lookup(){
            const pin=group.pin.value.trim();if(group.loadingPin===pin&&group.controller&&!group.controller.signal.aborted)return;
            const version=++group.version;group.controller?.abort();group.offices=[];group.verified='';status.textContent='';office.replaceChildren(new Option('Select post office / locality',''));office.disabled=true;officeGroup.hidden=!pin||!india();
            if(!india()||!active(group.pin)||!pin){error('');return;}
            if(!/^[1-9][0-9]{5}$/.test(pin)){error('Enter a valid 6-digit Indian PIN code.');return;}
            group.dirtyPin=true;group.loadingPin=pin;error('Verifying PIN code…');group.controller=new AbortController();const controller=group.controller,timer=setTimeout(()=>controller.abort(),8000);
            try{
                const response=await fetch('https://api.postalpincode.in/pincode/'+pin,{signal:controller.signal});if(!response.ok)throw Error();const data=await response.json();
                if(version!==group.version||group.pin.value.trim()!==pin||!india()||!active(group.pin))return;
                group.offices=data?.[0]?.Status==='Success'?(data[0].PostOffice||[]).filter(o=>o.State&&o.District&&o.Name):[];
                if(!group.offices.length){error('PIN code not found. Enter a valid Indian PIN code.');return;}
                group.verified=pin;office.replaceChildren(new Option('Select post office / locality',''),...group.offices.map((o,i)=>new Option(o.Name,String(i))));office.disabled=false;officeGroup.hidden=false;
                if(strict&&!active(group.state)&&!group.offices.some(o=>norm(o.State)===norm(currentState()))){error('This PIN belongs to a different State. Correct the PIN or GST registration.');return;}
                const prior=group.offices.findIndex(o=>norm(o.Name)===norm(group.city.value));
                if(group.offices.length===1||prior>=0){const index=prior>=0?prior:0;office.value=String(index);apply(group.offices[index]);}
                else {group.applied=true;const states=new Set(group.offices.map(o=>o.State)),districts=new Set(group.offices.map(o=>o.District));if(states.size===1){const state=stateOptions().find(o=>norm(o.label)===norm(group.offices[0].State));if(state)assign(group.state,state.value);}if(districts.size===1)assign(group.district,group.offices[0].District);group.applied=false;validate();}
            }catch(failure){if(version===group.version&&group.pin.value.trim()===pin&&india()&&active(group.pin))error('PIN lookup is unavailable. Retry when connected; the address has not been verified.');}
            finally{clearTimeout(timer);if(group.version===version)group.loadingPin='';}
        }
        office.onchange=()=>{if(office.value!=='')apply(group.offices[Number(office.value)]);else validate();};retry.onclick=lookup;
        group.pin.addEventListener('input',()=>{group.dirtyPin=true;clearTimeout(group.timer);++group.version;group.controller?.abort();group.verified='';if(!group.pin.value.trim()){officeGroup.hidden=true;error('');return;}error(/^[1-9][0-9]{5}$/.test(group.pin.value.trim())?'Verifying PIN code…':'Enter a valid 6-digit Indian PIN code.');group.timer=setTimeout(lookup,300);});
        group.pin.addEventListener('blur',()=>{if(group.dirtyPin&&!group.verified){clearTimeout(group.timer);lookup();}});
        group.state.addEventListener('sf-address-selection',()=>{if(group.district){assign(group.district,'');group.districtPicker.refresh();}validate();});
        if(strict){
            let priorState=group.state.value;
            group.state.addEventListener('change',()=>{if(priorState!==group.state.value&&!group.applied&&group.district){assign(group.district,'');group.districtPicker.refresh();}priorState=group.state.value;validate();});
            group.city.addEventListener('input',()=>{if(!group.applied){if(office.value!==''&&!postalMatch())office.value='';validate();}});
            group.district?.addEventListener('input',()=>{if(!group.applied)validate();});
        }
        for(const input of [group.state,group.district,group.city].filter(Boolean))input.addEventListener('change',()=>{if(!group.applied){validate();if(strict&&active(group.pin)&&/^[1-9][0-9]{5}$/.test(group.pin.value.trim())&&group.verified!==group.pin.value.trim())lookup();}});
        group.country?.addEventListener('change',()=>{++group.version;group.controller?.abort();group.verified='';group.offices=[];group.dirtyPin=false;officeGroup.hidden=true;error('');refresh();});
        function refresh(){
            const indian=india();if(indian){group.pin.inputMode='numeric';group.pin.maxLength=6;group.pin.pattern='[1-9][0-9]{5}';}else if(group.country){group.pin.inputMode='text';group.pin.maxLength=20;if(group.pin.pattern==='[1-9][0-9]{5}')group.pin.removeAttribute('pattern');}
            group.statePicker.refresh();group.districtPicker?.refresh();if(group.districtPicker)group.districtPicker.input.placeholder=indian&&!group.state.value?'Please select State first':'Select District';
        }
        group.validate=validate;group.refresh=refresh;group.lookup=lookup;refresh();
        watchValue(group.pin,()=>{++group.version;group.controller?.abort();clearTimeout(group.timer);group.verified='';group.offices=[];group.dirtyPin=false;officeGroup.hidden=true;error('');});
        reference().then(refresh).catch(()=>{status.textContent='Address options are unavailable. Reload to retry; saved values are retained.';});
    }
    // Combined-address contracts (customers, delivery/dispatch and old legal records)
    // keep their exact stored text. Components replace it only after explicit editing.
    function combinedAddress(source){
        if(combined.has(source))return;combined.add(source);
        const focused=document.activeElement===source;
        const required=source.required,wasReadonly=source.readOnly,outer=source.closest('details'),container=node(outer?'section':'details');container.className='sf-address-details';container.id=(source.id||'address-'+(++serial))+'-details';
        const summary=node(outer?'h4':'summary','Address Details'+(required?'':' (Optional)')),saved=node('pre'),replace=node('button','Enter Address Details'),grid=node('div');saved.className='sf-address-saved';replace.type='button';replace.className='btn btn-outline-secondary btn-sm';grid.className='sf-address-grid';
        container.append(summary,saved,replace,grid);source.after(container);source.hidden=true;source.required=false;source.readOnly=true;
        let internal=false,structured=false,revision=0;
        const fields={},owned=new Set(),scope=source.closest('[data-party]')||source.form;
        const reuse=name=>scope&&[...scope.querySelectorAll('input,select,textarea')].find(i=>i!==source&&!i.dataset.sfAddressUi&&(name==='state'?['state','statecode'].includes(key(i)):name==='pin'?['pin','pincode','postalcode'].includes(key(i)):false));
        for(const [name,label]of [['line1','Address Line 1'],['line2','Address Line 2'],['state','State'],['district','District'],['city','City/Town'],...(source.id==='new-customer-address'&&source.closest('#billing-workspace')?[["pin","Pincode"],["country","Country"]]:[["country","Country"],["pin","Pincode"]])]){
            const existing=reuse(name);if(existing){fields[name]=existing;continue;}
            const group=node('div');group.className='sf-address-field'+(['line1','line2'].includes(name)?' sf-address-wide':'');const input=node('input');input.type='text';input.className='form-control';input.dataset.sfAddressUi='true';input.id=(source.id||'address-'+(++serial))+'-'+name;input.maxLength=['line1','line2'].includes(name)?250:100;const caption=node('label',label);caption.htmlFor=input.id;caption.className='form-label';group.append(caption,input);grid.append(group);fields[name]=input;owned.add(input);
        }
        fields.line1.placeholder='Building number, street or area';fields.line2.placeholder='Landmark, floor or locality (optional)';fields.country.value='India';fields.country.readOnly=!!source.closest('#gst-settings,#stores-workspace,#company-settings-workspace,[data-party]');
        function locks(){const locked=source.disabled||wasReadonly;for(const input of owned)if(input.disabled!==(locked||!structured))input.disabled=locked||!structured;if(replace.disabled!==locked)replace.disabled=locked;}
        function adopt(){if(internal)return;structured=!source.value;saved.textContent=source.value;saved.hidden=!source.value;replace.hidden=!source.value;grid.hidden=!!source.value;fields.line1.required=required&&!source.value;for(const [name,input]of Object.entries(fields))if(owned.has(input))input.value=name==='country'?'India':'';locks();}
        function update(){if(!structured||source.disabled)return;const used=[fields.line1,fields.line2,fields.city,fields.district,fields.state,fields.pin].some(i=>i.value.trim());const state=fields.state.value?(fields.state instanceof HTMLSelectElement?stateLabel(fields.state.selectedOptions[0]?.textContent):fields.state.value):'';const text=used?[fields.line1.value,fields.line2.value,[fields.city.value,fields.district.value].filter(Boolean).join(', '),[state,fields.pin.value].filter(Boolean).join(' — '),fields.country.value].filter(v=>v.trim()).join('\n'):'';fields.line1.setCustomValidity(source.maxLength>=0&&text.length>source.maxLength?'Address must contain no more than '+source.maxLength+' characters.':'');internal=true;source.value=text;emit(source);internal=false;}
        replace.onclick=()=>{structured=true;grid.hidden=false;replace.hidden=true;fields.line1.required=required;locks();fields.line1.focus();};
        for(const input of Object.values(fields)){input.addEventListener('input',update);input.addEventListener('change',update);}
        source.focus=options=>{if(outer)outer.open=true;else container.open=true;const target=structured?fields.line1:replace;if(target.disabled){summary.tabIndex=-1;summary.focus(options);}else target.focus(options);};
        function read(){if(!structured)return undefined;if(![fields.line1,fields.line2,fields.state,fields.district,fields.city,fields.pin].some(i=>i.value.trim()))return null;return {addressLine1:fields.line1.value||null,addressLine2:fields.line2.value||null,state:fields.state.value||null,district:fields.district.value||null,city:fields.city.value||null,country:fields.country.value||null,postalCode:fields.pin.value||null};}
        function restore(details){if(!details)return;structured=true;grid.hidden=false;saved.hidden=true;replace.hidden=true;const keys={line1:'addressLine1',line2:'addressLine2',state:'state',district:'district',city:'city',country:'country',pin:'postalCode'};for(const [name,input]of Object.entries(fields))if(owned.has(input))input.value=details[keys[name]]||'';fields.line1.required=required;locks();}
        watchValue(source,()=>{revision++;adopt();});source.sfAddressCombined={locks,adopt,fields,container,read,restore,get revision(){return revision;},ensure(){if(source.nextElementSibling!==container)source.after(container);}};adopt();bind({...fields,ownerSource:source,ownerContainer:container});if(focused)source.focus();
    }
    function scan(){
        for(const group of groups){if(!group.state.isConnected||group.ownerSource&&!group.ownerSource.isConnected){++group.version;group.controller?.abort();clearTimeout(group.timer);group.statePicker.destroy();group.districtPicker?.destroy();group.ownerContainer?.remove();groups.delete(group);}else group.refresh();}
        for(const country of document.querySelectorAll('input,select'))if(['country','countrycode'].includes(key(country))&&!country.dataset.sfAddressUi)fixedCountry(country);
        for(const form of document.querySelectorAll('form')){
            const inputs=[...form.querySelectorAll('input,select,textarea')].filter(i=>!i.dataset.sfAddressUi);
            const find=(...names)=>inputs.find(i=>names.includes(key(i)));
            const line1=find('addressline1');
            if(line1){bind({line1,line2:find('addressline2'),state:find('state','statecode'),district:find('district'),city:find('city'),pin:find('postalcode','pincode'),country:find('countrycode','country')});continue;}
            for(const source of inputs.filter(i=>i instanceof HTMLTextAreaElement&&(/address|delivery/.test(key(i))||/delivery$/.test(i.id))))combinedAddress(source);
            for(const source of inputs){source.sfAddressCombined?.ensure();source.sfAddressCombined?.locks();}
        }
        for(const set of document.querySelectorAll('[data-party]')){
            const source=set.querySelector('[data-field=address]');if(source&&source instanceof HTMLInputElement)combinedAddress(source);
        }
    }
    function queue(){if(scanning)return;scanning=true;queueMicrotask(()=>{scanning=false;scan();});}
    new MutationObserver(queue).observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['disabled','readonly','open','hidden','aria-invalid','aria-describedby']});
    document.addEventListener('DOMContentLoaded',queue,{once:true});queue();
    document.addEventListener('reset',event=>{const sources=[...event.target.querySelectorAll('input,textarea')].filter(source=>source.sfAddressCombined).map(source=>({source,revision:source.sfAddressCombined.revision}));queueMicrotask(()=>{
        for(const {source,revision} of sources)if(source.sfAddressCombined.revision===revision)source.sfAddressCombined.adopt();
        for(const group of groups)if(group.pin.form===event.target){++group.version;group.controller?.abort();clearTimeout(group.timer);group.verified='';group.dirtyPin=false;group.offices=[];if(group.pin.dataset.sfPostalError)group.pin.setCustomValidity('');if(group.state.form?.dataset.indianAddress==='true')group.district?.setCustomValidity('');delete group.pin.dataset.sfPostalError;group.officeGroup.hidden=true;group.status.textContent='';group.refresh();}
    });});
    document.addEventListener('submit',event=>{
        if(event.submitter?.formNoValidate)return;const failed=[];
        for(const group of groups)if(group.pin.form===event.target&&!group.validate())failed.push(group.district&&!group.district.checkValidity()?group.district:group.pin);
        for(const source of event.target.querySelectorAll('input,textarea'))if(source.sfAddressCombined){const fields=Object.values(source.sfAddressCombined.fields);for(const input of fields)if(!input.disabled&&!input.checkValidity()){StockFlowValidation?.showField(input,StockFlowValidation.fieldMessage(input));failed.push(input);}}
        if(failed.length){event.preventDefault();event.stopImmediatePropagation();for(let p=failed[0].parentElement;p;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;failed[0].focus();}
    },true);
    return {scan,bind,combinedAddress,reference,fixedCountry,restore(source,details){combinedAddress(source);source.sfAddressCombined.restore(details);}};
})();
