window.CatalogAttributes = (() => {
    'use strict';
    let schema = {attributes:[],categories:[]}, sequence=0;
    const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
    async function load() {
        const response=await fetch('/api/categories/schema',{credentials:'same-origin',cache:'no-store'});
        if(!response.ok) throw new Error('Unable to load category fields. Reopen registration before saving.');
        schema=await response.json();
        return schema;
    }
    function fields(categoryId) {
        return ((schema.effectiveCategories||schema.categories).find(c=>c.categoryId===Number(categoryId))?.fields||[]).map(rule=>({...schema.attributes.find(a=>a.code===rule.code),...rule}));
    }
    // A specific helmet category already defines the common helmet type.
    // Keep that specification in the saved attributes without asking for it twice.
    function categoryValue(category,field) {
        if(field.code!=='helmet_type'||field.type!=='SELECT')return null;
        const normalize=value=>String(value).toLowerCase().replace(/\bhelmets?\b/g,'').replace(/[^a-z0-9]+/g,'');
        const names=(category.selectedOptions[0]?.textContent||'').split(' / ').reverse();
        const seen=new Set();let current=schema.categories.find(c=>c.categoryId===Number(category.value));
        while(current&&!seen.has(current.categoryId)){seen.add(current.categoryId);if(current.categoryName)names.push(current.categoryName);current=schema.categories.find(c=>c.categoryId===current.parentCategoryId);}
        for(const name of names){const matches=(field.options||[]).filter(value=>normalize(value)&&normalize(value)===normalize(name));if(matches.length===1)return matches[0];}
        return null;
    }
    function editor(host,category,initial,legacyCreate,layout={}) {
        let legacy, supplemental, controls=[], extras={}, current=category.value, axisMode=false, disposed=false;
        const stash=new Map(),vehicleStash=new Map();let verifiedFits=[];
        const status=node('p','','small text-muted');status.setAttribute('role','status');
        function snapshot(){
            if(controls.some(c=>c.vehicleDraft?.some(v=>v.value.trim())))throw new Error('Add or clear the unfinished compatible vehicle before saving.');
            if(legacy)return legacy.read();
            const more=supplemental?.read()||{};
            if(Object.keys(more).some(key=>controls.some(c=>c.field.code.toLowerCase()===key.toLowerCase())||['fitment_type','fitment_notes','manufacturer_part_number'].includes(key.toLowerCase())))throw new Error('Use the configured field for this specification instead of adding it again.');
            return {...extras,...more,...Object.fromEntries(controls.filter(c=>c.input.value.trim()).map(c=>[c.field.code,c.input.value.trim()]))};
        }
        function render(values={}) {
            legacy?.destroy();supplemental?.destroy();legacy=null;supplemental=null;controls=[];host.replaceChildren(status);
            layout.compatibility?.replaceChildren();layout.media?.replaceChildren();
            const definition=fields(category.value).filter(field=>!layout.hideSpecificationEntry||field.variantAxis);
            if(!definition.length&&(layout.suppressLegacy||layout.hideSpecificationEntry)){extras={...values};host.dispatchEvent(new CustomEvent('registration-layout-changed',{bubbles:true}));return;}
            if(!definition.length){const inner=node('div');(layout.media || host).append(inner);legacy=legacyCreate(inner,category,values);host.dispatchEvent(new CustomEvent('registration-layout-changed',{bubbles:true}));return;}
            extras=Object.fromEntries(Object.entries(values).filter(([key])=>!definition.some(f=>f.code===key)));
            const row=node('div',null,'row g-3');host.append(row);
            for(const field of definition) {
                const group=node('div',null,'col-md-6 col-lg-4');group.dataset.attributeGroup='';
                const input=document.createElement(['SELECT','BOOLEAN'].includes(field.type)?'select':'input');
                input.id='category-field-'+(++sequence);input.dataset.attributeCode=field.code;input.className=input.tagName==='SELECT'?'form-select':'form-control';
                if(input.tagName==='SELECT') {
                    input.append(new Option('Choose '+field.label,''));
                    (field.type==='BOOLEAN'?['true','false']:field.options).forEach(v=>input.append(new Option(v==='true'?'Yes':v==='false'?'No':v,v)));
                } else {input.type={NUMBER:'number',DATE:'date'}[field.type]||'text';input.maxLength=100;if(field.type==='NUMBER'){input.step='any';if(field.minimum!=null)input.min=field.minimum;if(field.maximum!=null)input.max=field.maximum;}}
                const inheritedValue=categoryValue(category,field);
                input.value=inheritedValue??values[field.code]??field.defaultValue??'';
                if(field.type==='TEXT_LIST'){input.maxLength=1000;input.placeholder='Enter verified values separated by semicolons';}
                const label=node('label',field.label+(field.required?' *':''),'form-label');label.htmlFor=input.id;
                group.append(label,input);
                const fitment=/^(vehicle_compatibility|vehicle_make|compatible_models|model_years|part_position|fitment_notes|universal_fitment)$/.test(field.code);
                if(layout.compatibility && fitment){
                    let fitmentRow=layout.compatibility.querySelector('.row');
                    if(!fitmentRow){fitmentRow=node('div',null,'row g-3');layout.compatibility.append(fitmentRow);}
                    fitmentRow.append(group);
                }else row.append(group);
                controls.push({field,input,group,inheritedValue});
                if(field.type==='TEXT_LIST'&&field.code==='vehicle_compatibility'){
                    group.classList.remove('col-md-6','col-lg-4');group.classList.add('col-12');
                    const typeLabel=node('label','Fitment type','form-label');
                    const type=node('select',null,'form-select mb-3');type.id=input.id+'-fitment-type';typeLabel.htmlFor=type.id;
                    type.append(new Option('Fits specific vehicles','SPECIFIC'),new Option('Universal accessory','UNIVERSAL'),new Option('Not applicable','NONE'));
                    type.value=values.fitment_type||'SPECIFIC';type.querySelector('[value="NONE"]').disabled=!!field.required;
                    group.prepend(typeLabel,type);
                    const detail=node('details',null,'registration-fitment-entry mt-2');detail.open=true;detail.append(node('summary','Add a verified compatible vehicle'));
                    const draft={},entryRow=node('div',null,'row g-3 mt-1');
                    for(const [key,title,max] of [['make','Vehicle make',30],['model','Compatible vehicle model',40],['years','Generation / year range',30],['from','Year from',4],['to','Year to',4],['position','Part position',20]]){
                        const caption=node('label',title,'form-label');const control=node('input',null,'form-control');control.type=['from','to'].includes(key)?'number':'text';control.maxLength=max;
                        if(control.type==='number'){control.min='1900';control.max='2100';control.step='1';}
                        control.setAttribute('aria-label',title+' for compatibility');const cell=node('div',null,'col-md-6 col-lg-4');caption.htmlFor=control.id=input.id+'-vehicle-'+key;cell.append(caption,control);entryRow.append(cell);draft[key]=control;
                    }
                    let chosenVehicle=null,searchVersion=0,searchTimer;
                    const masterLabel=node('label','Find registered vehicle (optional)','form-label'),masterSearch=node('input',null,'form-control mb-2'),masterSelect=node('select',null,'form-select mb-3');masterSearch.type='search';masterSearch.maxLength=100;masterSearch.setAttribute('aria-label','Search registered vehicles');masterSelect.setAttribute('aria-label','Registered vehicle');masterSelect.add(new Option('Search or enter a verified new vehicle',''));masterLabel.append(masterSearch,masterSelect);detail.append(masterLabel);
                    masterSearch.oninput=()=>{clearTimeout(searchTimer);const version=++searchVersion;searchTimer=setTimeout(async()=>{try{const response=await fetch('/api/products/vehicles?'+new URLSearchParams({term:masterSearch.value}),{credentials:'same-origin'});if(!response.ok)return;const data=await response.json();if(disposed||version!==searchVersion)return;masterSelect.replaceChildren(new Option('Choose a registered vehicle',''));for(const vehicle of data.items||[]){const option=new Option([vehicle.manufacturer,vehicle.model,vehicle.variant].filter(Boolean).join(' / '),vehicle.vehicleId);option.vehicle=vehicle;masterSelect.add(option);}}catch{}},250);};
                    masterSelect.onchange=()=>{const vehicle=masterSelect.selectedOptions[0]?.vehicle;if(!vehicle)return;chosenVehicle=vehicle;draft.make.value=vehicle.manufacturer;draft.model.value=vehicle.model;draft.years.value=vehicle.variant||'';};for(const key of ['make','model','years'])draft[key].addEventListener('input',()=>chosenVehicle=null);
                    detail.append(entryRow);controls.at(-1).vehicleDraft=Object.values(draft);
                    const noteLabel=node('label','Fitment notes (optional)','form-label mt-2'),notes=node('input',null,'form-control');notes.maxLength=100;notes.value=values.fitment_notes||'';notes.id=input.id+'-fitment-notes';noteLabel.htmlFor=notes.id;
                    notes.oninput=()=>{if(notes.value.trim())extras.fitment_notes=notes.value.trim();else delete extras.fitment_notes;};
                    const add=node('button','Add compatible vehicle','btn btn-primary btn-sm mt-2');add.type='button';
                    const message=node('p','','small text-muted');message.setAttribute('role','status');
                    const list=node('div',null,'table-responsive mt-3');list.setAttribute('aria-label','Compatible vehicles');
                    function renderList(){
                        list.replaceChildren();const entries=input.value.split(';').map(v=>v.trim()).filter(Boolean);
                        const heading=node('h4','Compatible vehicles ('+entries.length+')','h6');list.append(heading);
                        const table=node('table',null,'table table-sm'),head=node('thead'),header=node('tr');
                        ['Make','Model','Generation / years','Position','Actions'].forEach(t=>header.append(node('th',t)));head.append(header);table.append(head);const body=node('tbody');
                        entries.forEach((entry,index)=>{const tr=node('tr'),parts=entry.split(' / ');for(let i=0;i<4;i++)tr.append(node('td',parts[i]||'?'));const cell=node('td'),remove=node('button','Remove','btn btn-outline-secondary btn-sm');remove.type='button';remove.setAttribute('aria-label','Remove compatible vehicle '+(index+1));remove.onclick=()=>{entries.splice(index,1);verifiedFits=verifiedFits.filter(fitment=>fitment._text!==entry);input.value=entries.join('; ');input.dispatchEvent(new Event('input',{bubbles:true}));};cell.append(remove);tr.append(cell);body.append(tr);});
                        if(!entries.length){const tr=node('tr'),cell=node('td','No compatible vehicles added.');cell.colSpan=5;tr.append(cell);body.append(tr);}table.append(body);list.append(table);
                    }
                    input.addEventListener('input',renderList);
                    add.onclick=()=>{
                        const make=draft.make.value.trim(),model=draft.model.value.trim(),from=draft.from.value,to=draft.to.value;
                        if(!make||!model||Object.values(draft).some(v=>v.value.includes(';')||!v.checkValidity())||from&&to&&Number(from)>Number(to)){message.textContent='Enter make, model and a valid year range.';return;}
                        const years=[draft.years.value.trim(),from||to?[from||'?',to||'?'].join('-'):''].filter(Boolean).join(' ');
                        const entry=[make,model,years||'-',draft.position.value.trim()||'-'].join(' / '),existing=input.value.split(';').map(v=>v.trim()).filter(Boolean);
                        if(entry.length>100||existing.length>=30||[...existing,entry].join('; ').length>1000){message.textContent='Use up to 30 fitments, 100 characters each and 1,000 characters total.';return;}
                        if(existing.some(v=>v.toLowerCase()===entry.toLowerCase())){message.textContent='This vehicle is already listed.';return;}
                        verifiedFits.push({_text:entry,vehicleId:chosenVehicle?.vehicleId||null,manufacturer:make,model,variant:draft.years.value.trim()||null,yearFrom:from?Number(from):null,yearTo:to?Number(to):null,notes:[draft.position.value.trim(),notes.value.trim()].filter(Boolean).join(' / ')||null});chosenVehicle=null;
                        input.value=[...existing,entry].join('; ');input.dispatchEvent(new Event('input',{bubbles:true}));Object.values(draft).forEach(v=>v.value='');message.textContent='Vehicle added.';
                    };
                    let specific=input.value;
                    function fitmentMode(){
                        if(type.value!=='SPECIFIC'){if(!input.disabled)specific=input.value;input.value=type.value==='UNIVERSAL'?'Universal accessory':'Not applicable';Object.values(draft).forEach(v=>v.value='');}
                        else if(input.disabled)input.value=specific;
                        extras.fitment_type=type.value;input.disabled=type.value!=='SPECIFIC';detail.hidden=input.disabled;list.hidden=input.disabled;renderList();
                    }
                    type.onchange=fitmentMode;
                    detail.append(add,message);group.append(detail,noteLabel,notes,list);renderList();fitmentMode();
                }
            }
            if(layout.media&&!layout.suppressLegacy&&!layout.hideSpecificationEntry){
                const custom=Object.fromEntries(Object.entries(extras).filter(([key])=>!['fitment_type','fitment_notes','manufacturer_part_number'].includes(key)));
                for(const key of Object.keys(custom))delete extras[key];
                supplemental=legacyCreate(layout.media,category,custom);supplemental.setTemplate('');
            }else if(Object.keys(extras).length&&!layout.hideSpecificationEntry&&!layout.suppressLegacy){const note=node('p','Additional saved specifications: '+Object.entries(extras).map(([k,v])=>k+': '+v).join('; '),'small text-muted mt-2');host.append(note);}
            setAxisMode(axisMode);
            host.dispatchEvent(new CustomEvent('registration-layout-changed',{bubbles:true}));
        }
        function setAxisMode(enabled){axisMode=enabled;for(const c of controls){const hidden=c.inheritedValue!=null||c.field.visible===false||enabled&&c.field.variantAxis;c.group.hidden=hidden;c.input.required=c.field.required&&!hidden;}}
        function changed(){
            try{stash.set(current,snapshot());}catch{
                category.value=current;status.textContent='Complete or remove the unfinished specification before changing category.';return;
            }
            vehicleStash.set(current,verifiedFits);current=category.value;verifiedFits=vehicleStash.get(current)||[];render(stash.get(current)||{});
            status.textContent='Category changed. Review its fields and variant options. Completed values are retained if you switch back.';
            host.dispatchEvent(new CustomEvent('category-fields-changed',{bubbles:true}));
        }
        render(initial);category.addEventListener('change',changed);
        return {
            read(){if(legacy)return legacy.read();for(const {field,input} of controls){if(axisMode&&field.variantAxis)continue;if(!input.checkValidity())throw new Error('Enter a valid '+field.label+'.');}return snapshot();},
            vehicleFitments(){if(controls.some(c=>c.field.code==='vehicle_compatibility'&&c.input.disabled))return [];return verifiedFits.map(({_text,...fitment})=>fitment);},
            setVehicleFitments(entries){const input=controls.find(c=>c.field.code==='vehicle_compatibility')?.input;const texts=(input?.value||'').split(';').map(value=>value.trim()).filter(Boolean);verifiedFits=(entries||[]).map(entry=>{const generated=[entry.manufacturer,entry.model,[entry.variant,entry.yearFrom||entry.yearTo?[entry.yearFrom||'?',entry.yearTo||'?'].join('-'):''].filter(Boolean).join(' ')||'-','-'].join(' / ');const text=texts.find(text=>text.startsWith(entry.manufacturer+' / '+entry.model+' / ')&&(!entry.yearFrom||text.includes(String(entry.yearFrom))))||generated;if(input&&!texts.includes(text)&&text.length<=100&&[...texts,text].join('; ').length<=1000)texts.push(text);return {...entry,_text:text};});if(input){input.value=texts.join('; ');input.dispatchEvent(new Event('input'));}} ,
            setValues(values){render(values||{});},setTemplate(id){legacy?.setTemplate(id);},setAxisMode,
            destroy(){if(disposed)return;disposed=true;legacy?.destroy();supplemental?.destroy();category.removeEventListener('change',changed);}
        };
    }
    // Preview only: no catalog or stock is created until the registration is saved.
    function combinations(host, category, sku, onMode, defaults = {}) {
        let axes = [], choices = [], rows = [], selections = [], enabled = false, currentCategory = category.value, body;
        const title = node('h4', 'Variant Attributes', 'h6');
        const toggles = node('div', null, 'variant-attribute-choices');
        const options = node('div', null, 'row g-3 variant-option-fields');
        const actions = node('div', null, 'variant-generate-actions');
        const generate = node('button', 'Generate Variants', 'btn btn-primary'); generate.type = 'button';generate.id='variant-generate-'+(++sequence);
        const estimate = node('span', '', 'small text-muted'); estimate.setAttribute('role', 'status'); actions.append(estimate, generate);
        const message = node('p', '', 'small text-danger'); message.setAttribute('role', 'status');
        const resultTitle = node('h4', 'Generated Variants (0)', 'h6 mt-3');const skuHint=node('small','Each variant receives its own unique SKU.','text-muted');
        const preview = node('div', null, 'table-responsive variant-generated-table');
        host.replaceChildren(title, toggles, options, actions, message, resultTitle, skuHint, preview);
        let generatedDefinition='';
        const definition=()=>JSON.stringify(selections.map(item=>[item.axis.code,[...item.select.selectedOptions].map(o=>o.value.toLowerCase()).sort()]).sort((a,b)=>a[0].localeCompare(b[0])));
        const pricing=defaults.manual?node('details',null,'variant-pricing-section'):null,pricingBody=defaults.manual?node('tbody'):null;
        if(pricing){pricing.append(node('summary','Variant Pricing (optional)'));const scroll=node('div',null,'table-responsive');const table=node('table',null,'table table-sm');const heading=node('tr');for(const text of ['Variant','Purchase Cost Override','Selling Price Override','MRP','Reorder Level'])heading.append(node('th',text));const head=node('thead');head.append(heading);table.append(head,pricingBody);scroll.append(table);pricing.append(scroll);pricing.hidden=true;document.getElementById('registration-panel-pricing')?.append(pricing);}
        function combinationCount() {
            const count = selections.reduce((n, item) => n * item.select.selectedOptions.length, axes.length ? 1 : 0);
            estimate.textContent = selections.map(item=>item.select.selectedOptions.length+' '+item.axis.label+' values').join(' \u00d7 ')+' = '+count+' Variants';
            if(defaults.manual){generate.disabled=!axes.length||selections.some(item=>!item.select.selectedOptions.length)||!sku.value.trim()||count>30;message.textContent=!sku.value.trim()?'Enter a Product Code.':!axes.length?'Add an attribute to start.':selections.some(item=>!item.select.selectedOptions.length)?'Add at least one value for each attribute.':count>30?'Use at most 30 variant combinations.':rows.length&&generatedDefinition!==definition()?'Options changed. Generate Variants to update the preview.':'';}
        }
        function count() {
            resultTitle.textContent = 'Generated Variants (' + rows.length + ')';
            const all=preview.querySelector('thead input');
            if(all){const selected=rows.filter(row=>row.check.checked).length;all.checked=rows.length>0&&selected===rows.length;all.indeterminate=selected>0&&selected<rows.length;}
        }
        function picker(axis) {
            if(defaults.manual){chipPicker(axis);return;}
            const cell = node('div', null, 'col-md-6');
            const label = node('label', axis.label + ' options', 'form-label');
            const detail = node('details', null, 'variant-option-picker');
            const summary = node('summary', null, 'form-select');
            const chips = node('span', null, 'variant-selected-chips'); summary.append(chips);
            const menu = node('div', null, 'variant-option-menu');
            const select = node('select', null, 'form-select'); select.multiple = true; select.size = 5;
            select.id = 'axis-options-' + (++sequence); label.htmlFor = select.id; select.setAttribute('aria-label', axis.label + ' options');
            axis.options.forEach(value => select.append(new Option(value, value)));
            function refresh() {
                chips.replaceChildren();
                if (!select.selectedOptions.length) chips.append(node('span', 'Choose ' + axis.label.toLowerCase(), 'text-muted'));
                for (const option of select.selectedOptions) {
                    const chip = node('span', null, 'variant-option-chip'); chip.append(node('span', option.value));
                    const remove = node('button', '×'); remove.type = 'button'; remove.setAttribute('aria-label', 'Remove ' + axis.label + ' option ' + option.value);
                    remove.onclick = event => { event.preventDefault(); event.stopPropagation(); option.selected = false; refresh(); close(); };
                    chip.append(remove); chips.append(chip);
                }
                combinationCount();
            }
            function close() { if(detail.open){detail.open=false;summary.focus();} }
            select.onchange = () => { refresh(); close(); }; menu.append(select);
            select.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();close();}});
            detail.addEventListener('focusout',event=>{if(!detail.contains(event.relatedTarget))detail.open=false;});
            // Keep existing chips when choosing another item with a mouse or touch.
            select.addEventListener('pointerdown', event => {
                const option=event.target.closest('option');
                if(!option||event.button!==0||event.pointerType==='touch')return;
                event.preventDefault();option.selected=!option.selected;
                select.dispatchEvent(new Event('change',{bubbles:true}));
            });
            if (!axis.fixed) {
                const entry = node('div', null, 'registration-inline mt-2'), input = node('input', null, 'form-control'); input.maxLength = 100;
                input.setAttribute('aria-label', 'New ' + axis.label.toLowerCase() + ' option'); input.placeholder = 'Add ' + axis.label.toLowerCase();
                const add = node('button', 'Add', 'btn btn-outline-primary'); add.type = 'button';
                add.onclick = () => {
                    const value = input.value.trim(); if (!value) return;
                    if (select.options.length >= 50 || /[;\x00-\x1f]/.test(value)) { message.textContent = 'Use up to 50 valid options per attribute.'; return; }
                    let option = [...select.options].find(option => option.value.toLowerCase() === value.toLowerCase());
                    if (!option) { option = new Option(value, value); select.append(option); }
                    option.selected = true; input.value = ''; refresh(); close();
                };
                input.onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); add.click(); } };
                entry.append(input, add); menu.append(entry);
            }
            detail.append(summary, menu); cell.append(label, detail); options.append(cell); selections.push({axis, select}); refresh();
        }
        function chipPicker(axis){
            const cell=node('section',null,'col-md-6 variant-chip-section');cell.dataset.axis=axis.code;const heading=node('div',null,'variant-chip-heading');heading.append(node('strong',axis.label));
            const remove=node('button',null,'btn btn-sm variant-icon-button');remove.type='button';remove.textContent='\u00d7';remove.setAttribute('aria-label','Remove Attribute '+axis.label);remove.title=axis.fixed?'Required by this category':'Remove Attribute';remove.disabled=axis.fixed;remove.onclick=async()=>{if(rows.length&&!await StockFlowDialog.confirm('Removing this attribute changes the generated combinations. Review and regenerate variants before saving. Continue?'))return;choices=choices.filter(c=>c.axis.code!==axis.code);renderOptions();};heading.append(remove);
            const chips=node('div',null,'variant-chip-values'),empty=node('p','No values added yet. Enter a value to create variant options.','small text-muted mb-0');
            const select=node('select');select.multiple=true;select.hidden=true;select.setAttribute('aria-label',axis.label+' options');axis.options.forEach(value=>select.add(new Option(value,value)));const entry=node('div',null,'registration-inline');const input=node('input',null,'form-control');input.maxLength=100;input.placeholder='Enter '+axis.label.toLowerCase()+' value';input.setAttribute('aria-label','New '+axis.label.toLowerCase()+' option');const add=node('button','Add','btn btn-sm');add.type='button';
            const suggestions=node('div',null,'variant-value-suggestions');
            const refresh=()=>{chips.replaceChildren();for(const option of select.selectedOptions){const chip=node('span',null,'variant-option-chip');chip.append(node('span',option.value));const x=node('button','\u00d7');x.type='button';x.setAttribute('aria-label','Remove '+axis.label+' option '+option.value);x.title='Remove value';x.onclick=async()=>{if(rows.some(row=>row.attributes[axis.code]?.toLowerCase()===option.value.toLowerCase())&&!await StockFlowDialog.confirm('Removing this value changes generated variants. Review and regenerate before saving. Continue?'))return;option.selected=false;refresh();};chip.append(x);chips.append(chip);}empty.hidden=select.selectedOptions.length>0;suggestions.replaceChildren();for(const option of [...select.options].filter(o=>!o.selected).slice(0,12)){const button=node('button',option.value,'variant-value-suggestion');button.type='button';button.setAttribute('aria-label','Add '+axis.label+' value '+option.value);button.onclick=()=>{option.selected=true;refresh();};suggestions.append(button);}combinationCount();host.dispatchEvent(new Event('registration-layout-changed',{bubbles:true}));};
            select.onchange=refresh;add.onclick=()=>{const value=input.value.normalize('NFKC').trim().replace(/\s+/g,' ');if(!value){message.textContent='Enter a value for '+axis.label+'.';input.focus();return;}if(/[;\x00-\x1f]/.test(value)){message.textContent='Enter a valid attribute value.';return;}let option=[...select.options].find(o=>o.value.toLowerCase()===value.toLowerCase());if(option?.selected){message.textContent='This value is already added to '+axis.label+'.';return;}if(!option&&axis.fixed){message.textContent='Choose a configured '+axis.label+' value.';return;}if(!option){if(select.options.length>=50){message.textContent='Use up to 50 values per attribute.';return;}option=new Option(value,value);select.add(option);}option.selected=true;input.value='';refresh();input.focus();};input.onkeydown=event=>{if(event.key==='Enter'){event.preventDefault();add.click();}};entry.append(input,add);cell.append(heading,select,chips,empty,entry,suggestions);options.append(cell);selections.push({axis,select});refresh();
        }
        function resetManual(){
            host.classList.add('variant-chip-mode');
            for(const row of rows)row.supplierEditor?.destroy();currentCategory=category.value;rows=[];body=null;selections=[];choices=[];generatedDefinition='';toggles.replaceChildren();preview.replaceChildren();pricingBody?.replaceChildren();if(pricing)pricing.hidden=true;count();
            const configured=fields(category.value),required=configured.filter(f=>f.variantAxis),common=new Set(configured.filter(f=>!f.variantAxis).map(f=>f.code));
            for(const axis of required)choices.push({axis:{...axis,fixed:true},check:{checked:true}});
            const candidates=new Map();const suggest=(code,label,values=[])=>{if(!common.has(code)&&!required.some(a=>a.code===code))candidates.set(code,{code,label,options:values,fixed:false});};
            const name=category.selectedOptions[0]?.textContent.toLowerCase()||'';
            if(/mirror/.test(name)){suggest('side','Side',['Left','Right','Pair']);suggest('finish','Finish',['Black','Chrome']);}
            if(/light/.test(name)){suggest('wattage','Wattage',['35W','55W']);suggest('light_color','Light Color',['White','Yellow']);suggest('connector_type','Connector Type');}
            if(/brake/.test(name)){suggest('position','Position',['Front','Rear']);suggest('fitment_version','Fitment Version');}
            for(const [code,label,values] of [['size','Size',['S','M','L','XL']],['color','Color',['Black','Red','White']],['wattage','Wattage',[]],['material','Material',[]],['model','Model',[]],['style','Style',[]]])if(!candidates.has(code))suggest(code,label,values);
            const picker=node('details',null,'variant-attribute-selector'),summary=node('summary','Select attribute','form-select');summary.setAttribute('aria-label','Select variant attribute');const menu=node('div',null,'variant-attribute-menu'),search=node('input',null,'form-control');search.type='search';search.placeholder='Search attributes';search.setAttribute('aria-label','Search variant attributes');const results=node('div',null,'variant-attribute-results');let selected=null;const add=node('button','+ Add Attribute','btn btn-sm');add.type='button';
            const custom=node('input',null,'form-control');custom.hidden=true;custom.maxLength=40;custom.placeholder='Custom attribute name';custom.setAttribute('aria-label','Custom attribute name');
            const render=()=>{results.replaceChildren();for(const axis of candidates.values()){if(!axis.label.toLowerCase().includes(search.value.trim().toLowerCase())||choices.some(c=>c.axis.code===axis.code))continue;const option=node('button',axis.label,'variant-attribute-option');option.type='button';option.onclick=()=>{selected=axis;summary.textContent=axis.label;custom.hidden=true;picker.open=false;summary.focus();};results.append(option);}const option=node('button','Add Custom Attribute','variant-attribute-option');option.type='button';option.onclick=()=>{selected='custom';custom.hidden=false;summary.textContent='Custom Attribute';picker.open=false;custom.focus();};results.append(option);};
            search.oninput=render;picker.addEventListener('toggle',()=>{if(picker.open){render();search.focus();}});picker.onkeydown=event=>{const buttons=[...results.querySelectorAll('button')],index=buttons.indexOf(document.activeElement);if(event.key==='Escape'){event.preventDefault();picker.open=false;summary.focus();}else if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();buttons[index<0?0:(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();}else if(event.target===search&&event.key==='Enter'){event.preventDefault();buttons[0]?.click();}};
            add.onclick=()=>{let axis=selected;if(selected==='custom'){const label=custom.value.normalize('NFKC').trim().replace(/\s+/g,' '),code=label.toLowerCase().replace(/[^a-z0-9]+/g,'_').replace(/^_|_$/g,'');if(!/^[a-z][a-z0-9_]{0,39}$/.test(code)){message.textContent='Enter a valid custom attribute name.';return;}axis={code,label,options:[],fixed:false};}if(!axis){message.textContent='Select an attribute first.';return;}if(choices.length>=3){message.textContent='Choose at most three variant attributes.';return;}if(common.has(axis.code)||choices.some(c=>c.axis.code===axis.code||c.axis.label.toLowerCase()===axis.label.toLowerCase())){message.textContent='Use a distinct attribute name. Category specifications stay common to all variants.';return;}choices.push({axis:{...axis},check:{checked:true}});selected=null;summary.textContent='Select attribute';custom.value='';custom.hidden=true;renderOptions();};custom.onkeydown=event=>{if(event.key==='Enter'){event.preventDefault();add.click();}};
            menu.append(search,results);picker.append(summary,menu);toggles.append(picker,custom,add);renderOptions();
        }
        function renderOptions() {
            const previous = new Map(selections.map(item => [item.axis.code, [...item.select.options].map(option => ({value: option.value, selected: option.selected}))]));
            axes = choices.filter(choice => choice.check.checked).map(choice => choice.axis);
            selections = []; options.replaceChildren();
            for (const axis of axes) {
                const old = previous.get(axis.code); if (old) axis.options = old.map(option => option.value);
                picker(axis); const item = selections.at(-1);
                if (old) { for (const option of item.select.options) option.selected = old.some(saved => saved.value === option.value && saved.selected); item.select.dispatchEvent(new Event('change')); }
            }
            combinationCount();
        }
        function reset() {
            if(defaults.manual){resetManual();return;}
            currentCategory = category.value; rows = []; body = null; selections = []; choices = []; toggles.replaceChildren(); preview.replaceChildren(); message.textContent = ''; count();
            const configured = fields(category.value), required = configured.filter(field => field.variantAxis);
            const candidates = required.map(field=>({...field,fixed:true}));
            // Configured axes are required product identities. Other choices use existing flexible attributes.
            function addAxis(axis) {
                const label = node('label', null, 'variant-attribute-choice'), check = node('input'); check.type = 'checkbox';
                check.checked = axis.fixed;
                check.disabled = axis.fixed || axis.blocked; check.setAttribute('aria-label', 'Use ' + axis.label + ' variants');
                if (axis.fixed) label.title = 'Required by this category';
                if (axis.blocked) label.title = axis.common ? 'This category uses ' + axis.label.toLowerCase() + ' as a common product attribute' : 'Configure this attribute under Category Fields & Variants';
                check.onchange = () => {
                    if (choices.filter(choice => choice.check.checked).length > 3) { check.checked = false; message.textContent = 'Choose at most three variant attributes.'; return; }
                    message.textContent = ''; renderOptions();
                };
                label.append(check, document.createTextNode(axis.label)); toggles.append(label); choices.push({axis, check});
            }
            for(const axis of candidates)addAxis(axis);
            if(!required.length){const name=category.selectedOptions[0]?.textContent.toLowerCase()||'';let suggestions=[];if(/helmet|cover/.test(name))suggestions=[{code:'size',label:'Size',options:['S','M','L','XL']},{code:'color',label:'Color',options:['Black','Red','White']}];else if(/light/.test(name))suggestions=[{code:'wattage',label:'Wattage',options:['35W','55W']},{code:'color_temperature',label:'Color Temperature',options:['3000K','6000K']},{code:'connector_type',label:'Connector Type',options:[]}];else if(/mirror/.test(name))suggestions=[{code:'side',label:'Side',options:['Left','Right','Pair']},{code:'finish',label:'Finish',options:['Black','Chrome']}];else if(/brake/.test(name))suggestions=[{code:'position',label:'Position',options:['Front','Rear']},{code:'fitment_version',label:'Fitment Version',options:[]}];for(const axis of suggestions.filter(a=>!configured.some(f=>f.code===a.code)))addAxis({...axis,fixed:false});}

            if(defaults.manual===true){
                const row=node('div',null,'registration-inline my-2'),name=node('input',null,'form-control');name.placeholder='Attribute name, e.g. Wattage';name.maxLength=40;name.setAttribute('aria-label','New variant attribute');const add=node('button','Add attribute','btn btn-outline-secondary');add.type='button';
                add.onclick=()=>{const label=name.value.trim(),code=label.toLowerCase().replace(/[^a-z0-9]+/g,'_').replace(/^_|_$/g,'');if(!/^[a-z][a-z0-9_]{0,39}$/.test(code)||choices.some(c=>c.axis.code===code)||configured.some(c=>c.code===code)||choices.filter(choice=>choice.check.checked).length>=3){message.textContent='Use a distinct attribute name, up to three attributes. Category specification fields stay common to all variants.';return;}addAxis({code,label,options:[],fixed:false});choices.at(-1).check.checked=true;name.value='';renderOptions();};row.append(name,add);toggles.append(row);
            }
            renderOptions();
        }
        function table() {
            if (body) return;
            const table = node('table', null, 'table table-sm'); const head = node('thead'), line = node('tr');
            const all = node('input'); all.type = 'checkbox'; all.checked = true; all.setAttribute('aria-label', 'Select all generated variants');
            all.onchange = () => { const selected=all.checked;for (const row of rows) { row.check.checked = selected; row.check.onchange(); } };
            const first = node('th'); first.append(all); if(!defaults.manual)line.append(first);
            (defaults.manual?['Variant','SKU','Barcode','Status','Actions']:['Variant','SKU','Barcode','Purchase Cost Override','Selling Price Override','Status','Actions']).forEach(text => line.append(node('th', text))); head.append(line); body = node('tbody'); table.append(head, body); preview.append(table);
        }
        function append(values, include) {
            table(); const name = values.join(' / '), line = node('tr',null,'variant-preview-row'), check = node('input'); check.type = 'checkbox'; check.checked = include; check.setAttribute('aria-label', 'Include ' + name);
            const identity = node('div', null, 'variant-row-identity');
            const symbol = document.createElementNS('http://www.w3.org/2000/svg','svg'); symbol.setAttribute('class','sf-icon'); symbol.setAttribute('aria-hidden','true');
            const use = document.createElementNS('http://www.w3.org/2000/svg','use'); use.setAttribute('href','/images/icons.svg#box'); symbol.append(use); identity.append(symbol,node('span', name));
            const code = node('input', null, 'form-control variant-identifier'); const identityKey=JSON.stringify(axes.map((axis,i)=>[axis.code,values[i].trim().toLowerCase()]).sort((a,b)=>a[0].localeCompare(b[0])));let hash=2166136261;for(const ch of identityKey){hash^=ch.charCodeAt(0);hash=Math.imul(hash,16777619);}const suffix=values.map(v=>v.toUpperCase().replace(/[^A-Z0-9]+/g,'').slice(0,6)).join('-');code.value=sku.value.trim().slice(0,Math.max(3,31-suffix.length))+'-'+suffix+'-'+(hash>>>0).toString(36).slice(0,6); if(defaults.reserveCodes)code.value=pendingCodes.shift()||''; code.maxLength = 40; code.minLength = 3; code.required = true; code.setAttribute('aria-label', name + ' SKU');
            const purchase = node('input', null, 'form-control'), selling = node('input', null, 'form-control');
            const editor = node('div', null, 'variant-price-editor'); editor.hidden = true;
            for (const [input,key,caption] of [[purchase,'purchase','Purchase cost'],[selling,'selling','Selling price']]) {
                input.type = 'number'; input.required = false; input.min = '0'; input.max = '9999999999.99'; input.step = '0.01'; input.value = '';input.placeholder='Default: '+(defaults[key]?.value||'0'); input.setAttribute('aria-label',name + ' ' + (key === 'purchase' ? 'purchase price' : 'selling price'));
                input.oninput = () => input.dataset.priceEdited = 'true';
                const label = node('label', caption, 'form-label'); label.append(input); editor.append(label);
            }
            const edit = node('button', '✎', 'btn btn-outline-secondary btn-sm'), remove = node('button', '×', 'btn btn-outline-danger btn-sm'); edit.type = remove.type = 'button';
            for(const [button,path] of [[edit,'M4 16 16 4l4 4L8 20H4v-4Z M14 6l4 4'],[remove,'M4 6h16 M9 6V3h6v3 M6 6l1 15h10l1-15 M10 10v7 M14 10v7']]){
                const icon=document.createElementNS('http://www.w3.org/2000/svg','svg');icon.setAttribute('viewBox','0 0 24 24');icon.setAttribute('class','sf-icon');icon.setAttribute('aria-hidden','true');const drawing=document.createElementNS('http://www.w3.org/2000/svg','path');drawing.setAttribute('d',path);icon.append(drawing);button.replaceChildren(icon);
            }
            edit.setAttribute('aria-label','Edit variant ' + name); edit.title = 'Edit variant'; remove.setAttribute('aria-label','Remove variant ' + name); remove.title = 'Remove unsaved variant';
            edit.onclick = () => { editor.hidden = !editor.hidden; line.classList.toggle('variant-row-editing',!editor.hidden); if (!editor.hidden) code.focus(); };
            const action = node('div', null, 'variant-row-actions'); action.append(edit,remove);
            const extra={};
            for(const [key,caption,defaultKey] of [['mrp','MRP','mrp'],['minimumStockLevel','Reorder level','reorder'],['warrantyMonths','Warranty months','warranty']]){const label=node('label',caption,'form-label'),input=node('input',null,'form-control');input.type='number';input.min=0;input.step=key==='mrp'?'.01':'1';input.max=key==='warrantyMonths'?'240':'999999999';input.value=defaults[defaultKey]?.value||'0';input.setAttribute('aria-label',name+' '+caption.toLowerCase());label.append(input);editor.append(label);extra[key]=input;input.oninput=()=>input.dataset.priceEdited='true';}
            const part=node('input',null,'form-control');part.maxLength=100;part.placeholder='Manufacturer part number';part.setAttribute('aria-label',name+' manufacturer part number');{const label=node('label','Manufacturer part number','form-label');label.append(part);editor.append(label);}extra.part=part;const supplier=node('input',null,'form-control');supplier.maxLength=100;supplier.placeholder='Supplier item code (reference)';supplier.setAttribute('aria-label',name+' supplier item code');{const label=node('label','Supplier item code (reference)','form-label');label.append(supplier);editor.append(label);}extra.supplierProductCode=supplier;
            const mode=node('select',null,'form-select');mode.setAttribute('aria-label',name+' barcode setup');mode.add(new Option('Assign barcode later',''));mode.add(new Option('Existing manufacturer barcode','MANUFACTURER'));if(document.getElementById('purchase-registration')?.dataset.canGenerate==='true')mode.add(new Option('Generate internal model barcode','INTERNAL'));const barcode=node('input',null,'form-control');barcode.maxLength=150;barcode.setAttribute('aria-label',name+' manufacturer barcode');barcode.placeholder='Scan or enter manufacturer barcode';{const label=node('label','Barcode setup','form-label');label.append(mode);editor.append(label);const codeLabel=node('label','Manufacturer barcode','form-label');codeLabel.append(barcode);editor.append(codeLabel);}extra.mode=mode;extra.barcode=barcode;
            const status=node('select',null,'form-select');status.setAttribute('aria-label',name+' status');status.add(new Option('Active','ACTIVE'));status.add(new Option('Inactive','INACTIVE'));{const label=node('label','Status','form-label');label.append(status);editor.append(label);}extra.status=status;
            const inputs = [code,purchase,selling,...Object.values(extra)];
            let supplierEditor;if(document.getElementById('quick-supplier-identifiers')){supplier.closest('label').hidden=true;const supplierHost=node('section',null,'variant-supplier-editor');supplierHost.id='variant-suppliers-'+(++sequence);editor.append(supplierHost);supplierEditor=SupplierIdentifiers.create(supplierHost,[],{integratedPicker:defaults.manual===true});}
            const entry = {supplierHostId:supplierEditor?.hostId,supplierEditor,key:identityKey,name,check,code,purchase,selling,extra,inputs,attributes:Object.fromEntries(axes.map((axis,index)=>[axis.code,values[index]]))};
            check.onchange = () => {inputs.forEach(input=>input.disabled=!enabled || !check.checked);count();};
            remove.onclick = () => { rows = rows.filter(row=>row!==entry); line.remove(); count(); };
            for (const input of [check,...inputs]) input.id = 'variant-preview-' + (++sequence);
            const barcodeCell=node('div'),internalBarcodeNote=node('small','Internal barcode will be generated when saved.','text-muted');
            barcodeCell.append(barcode,internalBarcodeNote);
            const syncBarcode=()=>{const internal=mode.value==='INTERNAL';barcode.hidden=internal;internalBarcodeNote.hidden=!internal;};
            mode.onchange=()=>{if(!mode.value)barcode.value='';syncBarcode();};
            barcode.oninput=()=>{mode.value=barcode.value.trim()?'MANUFACTURER':'';syncBarcode();};
            entry.syncBarcode=syncBarcode;syncBarcode();if(defaults.manual)identity.prepend(check);const pricingLine=defaults.manual?node('tr'):null;if(pricingLine){for(const content of [node('span',name),purchase,selling,extra.mrp,extra.minimumStockLevel]){const cell=node('td');cell.append(content);pricingLine.append(cell);}pricingBody.append(pricingLine);pricing.hidden=!enabled;}for (const content of (defaults.manual?[identity,code,barcodeCell,status,action]:[check,identity,code,barcodeCell,purchase,selling,status,action])) { const cell = node('td'); cell.append(content); line.append(cell); }
            for(const label of editor.querySelectorAll('label'))if(!label.querySelector('input,select,textarea'))label.remove();
            const detail=node('tr',null,'variant-detail-row'),detailCell=node('td');detailCell.colSpan=defaults.manual?5:8;detailCell.append(editor);detail.append(detailCell);detail.hidden=true;
            const previousEdit=edit.onclick;edit.setAttribute('aria-expanded','false');edit.onclick=()=>{previousEdit();detail.hidden=editor.hidden;edit.setAttribute('aria-expanded',String(!editor.hidden));};const previousRemove=remove.onclick;remove.onclick=async()=>{if((code.value||purchase.value||selling.value||barcode.value)&&!await StockFlowDialog.confirm('Remove this unsaved variant and its entered details?'))return;previousRemove();detail.remove();pricingLine?.remove();entry.supplierEditor?.destroy();};
            body.append(line,detail); rows.push(entry); check.onchange();
        }
        let pendingCodes=[];
        generate.onclick = async () => {
            message.textContent = '';
            const selected = selections.map(item => [...item.select.selectedOptions].map(option=>option.value));
            if (!selected.length || selected.some(values=>!values.length)) { message.textContent = 'Choose an attribute and at least one option for each selected attribute.'; return; }
            if (selected.reduce((n,values)=>n*values.length,1)>(defaults.manual?30:200)) { message.textContent = defaults.manual?'Use at most 30 variant combinations.':'Preview up to 200 combinations at once.'; return; }
            const combos = selected.reduce((all,values)=>all.flatMap(row=>values.map(value=>[...row,value])),[[]]);
            let previous;try{previous = new Map(rows.map(row=>[row.key,{code:row.code.value,purchase:row.purchase.value,selling:row.selling.value,include:row.check.checked,purchaseEdited:row.purchase.dataset.priceEdited,sellingEdited:row.selling.dataset.priceEdited,suppliers:row.supplierEditor?.read()||null,extraEdited:Object.fromEntries(Object.entries(row.extra).map(([key,input])=>[key,input.dataset.priceEdited||''])),extra:Object.fromEntries(Object.entries(row.extra).map(([key,input])=>[key,input.value]))}]));}catch(error){message.textContent=error.message;return;}
            const keyFor=values=>JSON.stringify(axes.map((axis,i)=>[axis.code,values[i].trim().toLowerCase()]).sort((a,b)=>a[0].localeCompare(b[0])));
            const generationCategory=category.value,generationDefinition=definition(),generationParent=sku.value;
            const fresh=combos.filter(values=>!previous.has(keyFor(values)));
            if(defaults.reserveCodes&&fresh.length){generate.disabled=true;try{pendingCodes=await defaults.reserveCodes(fresh.length,JSON.stringify([definition(),[...previous.keys()],fresh]));if(category.value!==generationCategory||definition()!==generationDefinition||sku.value!==generationParent)throw new Error('Variant options changed while reserving SKUs. Generate Variants again.');if(pendingCodes.length!==fresh.length)throw new Error('Unable to reserve all variant SKUs. Retry Generate Variants.');}catch(error){message.textContent=error.message;return;}finally{generate.disabled=false;}}
            const futureKeys=new Set(combos.map(keyFor));if(rows.some(row=>!futureKeys.has(row.key))&&!await StockFlowDialog.confirm('Regenerating removes combinations and their entered SKU, barcode and price details. Continue?'))return;
            for(const row of rows)row.supplierEditor?.destroy();rows=[]; body=null; preview.replaceChildren();pricingBody?.replaceChildren();
            for (const values of combos) { const saved=previous.get(keyFor(values)); if(saved)pendingCodes.unshift(saved.code); append(values,saved?saved.include:combos.length<=30); if(saved){const row=rows.at(-1);for(const key of ['code','purchase','selling'])row[key].value=saved[key];row.purchase.dataset.priceEdited=saved.purchaseEdited||'';row.selling.dataset.priceEdited=saved.sellingEdited||'';for(const [key,value] of Object.entries(saved.extra||{})){row.extra[key].value=value;row.extra[key].dataset.priceEdited=saved.extraEdited?.[key]||'';}row.syncBarcode();if(saved.suppliers?.length&&row.supplierEditor){row.supplierEditor.destroy();row.supplierEditor=SupplierIdentifiers.create(document.getElementById(row.supplierEditor.hostId),saved.suppliers,{integratedPicker:defaults.manual===true});}} }
            generatedDefinition=definition();count();combinationCount();
        };
        reset();sku.addEventListener('input',combinationCount);
        const closePickers=event=>{for(const detail of host.querySelectorAll('.variant-option-picker[open],.variant-attribute-selector[open]'))if(!detail.contains(event.target))detail.open=false;};
        document.addEventListener('click',closePickers);
        const pickerEscape=event=>{if(event.key==='Escape'){const detail=event.target.closest('.variant-option-picker[open]');if(detail){event.preventDefault();event.stopPropagation();detail.open=false;detail.querySelector('summary').focus();}}};
        host.addEventListener('keydown',pickerEscape);
        const defaultHandlers=['purchase','selling','mrp','reorder','warranty'].map(key=>{const handler=()=>{for(const row of rows){const input=key==='purchase'||key==='selling'?row[key]:row.extra[{mrp:'mrp',reorder:'minimumStockLevel',warranty:'warrantyMonths'}[key]];if(key==='purchase'||key==='selling'){input.placeholder='Default: '+defaults[key].value;}else if(input.dataset.priceEdited!=='true')input.value=defaults[key].value;}};defaults[key]?.addEventListener('input',handler);return [key,handler];});
        return {
            destroy(){pricing?.remove();sku.removeEventListener('input',combinationCount);for(const row of rows)row.supplierEditor?.destroy();for(const [key,handler] of defaultHandlers)defaults[key]?.removeEventListener('input',handler);document.removeEventListener('click',closePickers);host.removeEventListener('keydown',pickerEscape);},
            refresh(value){if(currentCategory!==category.value)reset();enabled=value&&(fields(category.value).some(field=>field.variantAxis)||defaults.manual===true);host.hidden=!enabled;if(pricing)pricing.hidden=!enabled||!rows.length;combinationCount();for(const row of rows){row.check.disabled=!enabled;row.inputs.forEach(input=>input.disabled=!enabled||!row.check.checked);}onMode(enabled);},
            validation(step='variants'){
                if(!enabled)return [];
                const errors=[],issue=(input,message)=>errors.push({input,message});
                if(step==='variants'){
                    if(!axes.length)issue(toggles.querySelector('summary')||generate,'Add at least one variant attribute.');
                    for(const item of selections)if(!item.select.selectedOptions.length)issue(item.select.parentElement.querySelector('input')||item.select,'Add at least one value to '+item.axis.label+'.');
                    const chosen=rows.filter(row=>row.check.checked);
                    const total=selections.reduce((count,item)=>count*item.select.selectedOptions.length,axes.length?1:0);
                    if(total>30)issue(generate,'Use at most 30 variant combinations.');
                    else if(axes.length&&selections.every(item=>item.select.selectedOptions.length)&&(!chosen.length||chosen.length>30||generatedDefinition!==definition()))issue(generate,!chosen.length?'Generate and select between 1 and 30 variants.':'Options changed. Generate Variants to update the preview.');
                    errors.push(...identityIssues(chosen.map(row=>({code:row.code,attributes:row.attributes,barcode:row.extra.mode.value==='MANUFACTURER'?row.extra.barcode:null})),axes.map(axis=>axis.code)));
                    for(const row of chosen){
                        if(row.extra.mode.value==='MANUFACTURER'){
                            const barcode=row.extra.barcode.value.trim();
                            if(!barcode)issue(row.extra.barcode,'Enter the manufacturer barcode or choose Assign barcode later.');
                        }
                    }
                }else if(step==='pricing')for(const row of rows.filter(row=>row.check.checked)){
                    const mrp=Number(row.extra.mrp.value),selling=Number(row.selling.value||defaults.selling?.value||0);
                    if(mrp>0&&selling>mrp)issue(row.selling,'Selling price cannot exceed MRP.');
                    errors.push(...(row.supplierEditor?.issues?.()||[]));
                }
                return errors;
            },
            active(){return enabled;},
            hasDraft(){return rows.length>0||selections.some(item=>item.select.selectedOptions.length>0);},
            axes(){return axes.map(axis=>axis.code);},
            read(){
                if(defaults.manual&&rows.length&&generatedDefinition!==definition())throw new Error('Options changed. Generate Variants to update the preview.');
                const chosen=rows.filter(row=>row.check.checked);if(chosen.length<1||chosen.length>30)throw new Error('Generate and select between 1 and 30 variants.');
                const skus=new Set();
                return chosen.map(row=>{if(row.extra.mode.value==='MANUFACTURER'&&!row.extra.barcode.value.trim())throw new Error('Enter a manufacturer barcode for '+row.name+' or choose Assign barcode later.');const code=row.code.value.trim();if(!(/^[A-Za-z0-9][A-Za-z0-9_-]{2,39}$/).test(code)||skus.has(code.toUpperCase()))throw new Error('Enter a unique SKU of 3 to 40 characters for each selected variant.');skus.add(code.toUpperCase());return {supplierIdentifiers:row.supplierEditor?.read()||null,variantName:row.name,sku:code,primaryBarcode:row.extra.mode.value==='MANUFACTURER'?row.extra.barcode.value.trim():null,barcodes:[],barcodeSource:row.extra.mode.value||null,supplierProductCode:row.extra.supplierProductCode.value.trim()||null,mrp:Number(row.extra.mrp.value),minimumStockLevel:Number(row.extra.minimumStockLevel.value),warrantyMonths:Number(row.extra.warrantyMonths.value),isActive:row.extra.status.value==='ACTIVE',useDefaultPurchasePrice:row.purchase.value==='',useDefaultSellingPrice:row.selling.value==='',purchasePrice:Number(row.purchase.value||defaults.purchase?.value||0),sellingPrice:Number(row.selling.value||defaults.selling?.value||0),attributes:{...row.attributes,...(row.extra.part.value.trim()?{manufacturer_part_number:row.extra.part.value.trim()}:{})}};});
            }
        };
    }
    function identityIssues(entries,axes=[]){
        const issues=[],skus=new Map(),barcodes=new Map(),combos=new Map();
        const duplicate=(map,key,input,message)=>{if(map.has(key)){issues.push({input:map.get(key),message},{input,message});}map.set(key,input);};
        for(const entry of entries){
            const code=entry.code.value.trim(),maximum=entry.saved?100:40;
            if(!entry.saved&&!new RegExp('^[A-Za-z0-9][A-Za-z0-9_-]{2,'+(maximum-1)+'}$').test(code))issues.push({input:entry.code,message:'Enter a SKU of 3 to '+maximum+' letters, numbers, hyphens or underscores.'});
            duplicate(skus,code.toUpperCase(),entry.code,'Each sellable variant must have a unique SKU.');
            const barcode=entry.barcode?.value.trim().toLowerCase();if(barcode)duplicate(barcodes,barcode,entry.barcode,'Each barcode must identify one SKU.');
            if(axes.length&&axes.every(axis=>String(entry.attributes?.[axis]||'').trim()))duplicate(combos,JSON.stringify(axes.map(axis=>String(entry.attributes[axis]).trim().toLowerCase())),entry.identity||entry.code,'Duplicate variant option combination. Add a different combination.');
        }return issues;
    }
    return {load,fields,editor,combinations,identityIssues};
})();
