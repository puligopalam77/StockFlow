window.ProductSpecifications = (() => {
    'use strict';

    // One catalog shared by full registration and registration during receiving.
    // Values include their units and are saved in the existing variant attributes.
    const templates = [
        {
            id: 'battery',
            name: 'Battery',
            match: /\bbatter(?:y|ies)\b/i,
            fields: [
                ['Capacity', 'Capacity (Ah)', '150 Ah'],
                ['Voltage', 'Voltage (V)', '12 V'],
                ['Battery type', 'Battery type', 'Tubular / lithium / lead acid'],
                ['Application', 'Used for', 'Car / bike / home backup / solar'],
                ['Free replacement warranty', 'Free replacement period (months)', 'For example: 24 months'],
                ['Pro-rata warranty', 'Part-payment warranty period (months)', 'Enter only if offered by the brand']
            ]
        },
        {
            id: 'inverter',
            name: 'Inverter / UPS',
            match: /\binverters?\b|\bups\b/i,
            fields: [
                ['Inverter rating', 'Inverter rating (VA)', '1100 VA'],
                ['Output power', 'Output power (W)', '900 W'],
                ['Technology', 'Technology', 'Pure sine wave'],
                ['Battery voltage', 'Battery voltage (V)', '12 V']
            ]
        },
        {
            id: 'geyser',
            name: 'Geyser / water heater',
            match: /\bgeysers?\b|\bwater heaters?\b/i,
            fields: [
                ['Tank capacity', 'Tank capacity (L)', '25 L'],
                ['Power', 'Power (W)', '2000 W'],
                ['Heater type', 'Heater type', 'Storage / instant'],
                ['Mounting', 'Fitting position', 'Vertical / horizontal'],
                ['Pressure rating', 'Water pressure rating (bar)', 'For example: 8 bar'],
                ['Tank warranty', 'Tank warranty (months)', 'As shown on the warranty card'],
                ['Heating element warranty', 'Heating element warranty (months)', 'As shown on the warranty card']
            ]
        },
        {
            id: 'tv',
            name: 'Television',
            match: /\btelevisions?\b|\btvs?\b/i,
            fields: [
                ['Screen size', 'Screen size (inches)', '55 inches'],
                ['Resolution', 'Resolution', '4K'],
                ['Display type', 'Display type', 'LED / OLED'],
                ['Smart TV', 'Smart TV', 'Yes / no']
            ]
        },
        {
            id: 'ac',
            name: 'Air conditioner',
            match: /\bair condition(?:er|ers|ing)\b|\bac\b/i,
            fields: [
                ['Cooling capacity', 'Cooling capacity (ton)', '1.5 ton'],
                ['Energy rating', 'Energy rating', '5 star'],
                ['Compressor type', 'Compressor type', 'Inverter'],
                ['AC type', 'AC type', 'Split / window']
            ]
        },
        {
            id: 'fridge',
            name: 'Refrigerator',
            match: /\brefrigerators?\b|\bfridges?\b/i,
            fields: [
                ['Capacity', 'Capacity (L)', '250 L'],
                ['Door type', 'Door type', 'Single / double door'],
                ['Energy rating', 'Energy rating', '3 star'],
                ['Defrost type', 'Defrost type', 'Frost free']
            ]
        },
        {
            id: 'washer',
            name: 'Washing machine',
            match: /\bwashing machines?\b/i,
            fields: [
                ['Load capacity', 'Load capacity (kg)', '7 kg'],
                ['Loading type', 'Loading type', 'Front / top load'],
                ['Automation', 'Automation', 'Fully automatic'],
                ['Spin speed', 'Spin speed (rpm)', '1200 rpm']
            ]
        },
        {
            id: 'laptop',
            name: 'Laptop / computer',
            match: /\blaptops?\b|\bcomputers?\b|\bdesktops?\b/i,
            fields: [
                ['Processor', 'Processor', 'Intel Core i5'],
                ['RAM', 'RAM (GB)', '16 GB'],
                ['Storage', 'Storage', '512 GB SSD'],
                ['Screen size', 'Screen size (inches)', '15.6 inches']
            ]
        }
    ];
    const retailShop = document.body.dataset.shopType === 'battery-geyser' || !!document.getElementById('quick-kind');
    const profile = document.body.dataset.businessProfile || 'ELECTRICAL';
    const offeredTemplates = profile === 'ELECTRICAL' ? templates : [];
    let sequence = 0;
    function infer(category) {
        // Match the closest category first, then walk its displayed parent path.
        const parts = (category?.selectedOptions[0]?.textContent || '')
            .split(/\s*(?:>|\/|→)\s*/)
            .reverse();
        for (const part of parts) {
            const found = offeredTemplates.find((t) => {
                return t.match.test(part);
            });
            if (found) {
                return found.id;
            }
        }
        return '';
    }
    function legacyCreate(host, category, initial = {}) {
        const prefix = 'specification-' + ++sequence;
        const rows = [];
        let customSequence = 0;
        const label = document.createElement('label');
        label.className = 'form-label';
        label.htmlFor = prefix + '-template';
        label.textContent = 'Specification template';
        const select = document.createElement('select');
        select.id = label.htmlFor;
        select.className = 'form-select mb-2';
        select.append(
            new Option('General / custom specifications', ''),
            ...offeredTemplates.map((t) => {
                return new Option(t.name, t.id);
            })
        );
        const help = document.createElement('p');
        help.className = 'small text-muted';
        help.textContent =
            profile === 'ELECTRICAL' ? 'Copy these details from the product label or warranty card. Leave anything you do not know blank. Include units, such as 150 Ah or 25 L.' : 'Enter specifications relevant to this product. Leave optional details blank when they are not applicable.';
        const predefined = document.createElement('div');
        predefined.className = 'row g-3 mb-3';
        const additional=document.createElement('details'); additional.className='product-extra-specifications';
        const additionalTitle=document.createElement('summary'); additionalTitle.textContent='More product details (optional)';
        const supplementary=document.createElement('div'); supplementary.className='row g-3 mt-1'; additional.append(additionalTitle,supplementary);
        const extra = document.createElement('div');
        const add = document.createElement('button');
        add.type = 'button';
        add.className = 'btn btn-outline-secondary';
        add.textContent = 'Add specification';
        host.append(label, select, help, predefined, additional, extra, add);
        function custom(name = '', value = '') {
            const line = document.createElement('div');
            line.className = 'd-flex flex-wrap gap-2 mb-2';
            const key = document.createElement('input');
            const input = document.createElement('input');
            key.id = prefix + '-custom-' + ++customSequence + '-name';
            input.id = prefix + '-custom-' + customSequence + '-value';
            for (const control of [key, input]) {
                control.className = 'form-control';
                control.maxLength = 100;
                control.style.flex = '1 1 160px';
                control.style.minWidth = '0';
            }
            key.value = name;
            key.placeholder = 'Specification';
            key.setAttribute('aria-label', 'Specification name');
            input.value = value;
            input.placeholder = 'Value / unit';
            input.setAttribute('aria-label', 'Specification value');
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-outline-secondary';
            remove.textContent = 'Remove';
            const row = {
                key,
                input,
                line
            };
            remove.onclick = () => {
                rows.splice(rows.indexOf(row), 1);
                line.remove();
            };
            line.append(key, input, remove);
            extra.append(line);
            rows.push(row);
        }
        function snapshot() {
            return [...predefined.querySelectorAll('input'),...supplementary.querySelectorAll('input')]
                .map((input) => {
                    return [input.dataset.specification, input.value, true];
                })
                .concat(
                    rows.map((row) => {
                        return [row.key.value, row.input.value, false];
                    })
                );
        }
        function render(entries) {
            rows.length = 0;
            predefined.replaceChildren();
            supplementary.replaceChildren();
            extra.replaceChildren();
            const remaining = entries.map((pair) => {
                return [...pair];
            });
            const template = templates.find((t) => {
                return t.id === select.value;
            });
            additionalTitle.textContent=template?.id==='battery'?'More battery details and warranty card terms (optional)':template?.id==='geyser'?'More geyser details and warranty card terms (optional)':'More product details (optional)';
            additional.hidden=!retailShop || !template || template.fields.length<=3;
            let fieldIndex=0;
            for (const [name, caption, placeholder] of template?.fields || []) {
                const index = remaining.findIndex(([key]) => {
                    return key.trim().toLowerCase() === name.toLowerCase();
                });
                const value = index < 0 ? '' : remaining.splice(index, 1)[0][1];
                const group = document.createElement('div');
                group.className = 'col-md-6 col-lg-4';
                const input = document.createElement('input');
                input.id = prefix + '-' + name.toLowerCase().replace(/[^a-z0-9]/g, '-');
                input.className = 'form-control';
                input.maxLength = 100;
                input.value = value;
                input.placeholder = placeholder;
                input.dataset.specification = name;
                const captionElement = document.createElement('label');
                captionElement.className = 'form-label';
                captionElement.htmlFor = input.id;
                captionElement.textContent = caption;
                group.append(captionElement, input);
                (retailShop && fieldIndex++>=3?supplementary:predefined).append(group);
            }
            remaining
                .filter(([name, value, suggested]) => {
                    if (suggested) {
                        return value.trim();
                    } else {
                        return name.trim() || value.trim();
                    }
                })
                .forEach(([name, value]) => {
                    return custom(name, value);
                });
        }
        function categoryChanged() {
            const entries = snapshot();
            select.value = infer(category);
            render(entries);
        }
        select.value = infer(category);
        render(Object.entries(initial));
        select.addEventListener('change', () => {
            return render(snapshot());
        });
        category.addEventListener('change', categoryChanged);
        add.onclick = () => {
            return custom();
        };
        return {
            setTemplate(id) { select.value = id; render(snapshot()); },
            read() {
                const result = Object.create(null);
                for (const [rawName, rawValue, suggested] of snapshot()) {
                    const name = rawName.trim();
                    const value = rawValue.trim(); // Empty suggested fields are optional. A partially filled custom row must be corrected.
                    if (!value && suggested) {
                        continue;
                    }
                    if (!name && !value) {
                        continue;
                    }
                    if (!name || !value) {
                        throw new Error(
                            'Enter a specification name and value, or remove the empty specification.'
                        );
                    }
                    if (
                        name.length > 100 ||
                        value.length > 100 ||
                        Object.keys(result).some((k) => {
                            return k.toLowerCase() === name.toLowerCase();
                        })
                    ) {
                        throw new Error(
                            'Use distinct specification names and values of at most 100 characters.'
                        );
                    }
                    result[name] = value;
                }
                if (Object.keys(result).length > 30) {
                    throw new Error('Use at most 30 specifications per item configuration.');
                }
                return result;
            },
            setValues(values) {
                select.value = infer(category);
                render(Object.entries(values || {}));
            },
            destroy() {
                category.removeEventListener('change', categoryChanged);
            }
        };
    }
    // Descriptive rows reuse attribute storage but never participate in axes,
    // SKU allocation, prices, barcode setup or inventory posting.
    const normalizeDetail=value=>String(value||'').normalize('NFKC').trim().replace(/[^\p{L}\p{N}]+/gu,'').toLowerCase();
    const identityNames=['product name','product model','brand','category','subcategory','parent product code','SKU','barcode','stock unit','tracking method','manufacturer part number','fitment type','fitment notes','vehicle compatibility','vehicle make','compatible models','model years','part position','universal fitment'];
    function details(host,initial={},options={}){
        const make=(tag,text,cls)=>{const element=document.createElement(tag);if(text)element.textContent=text;if(cls)element.className=cls;return element;};
        const section=make('details',null,'product-descriptive-details'),summary=make('summary'),heading=make('span'),title=make('strong',options.title||'Product Specifications (Optional)'),count=make('small'),action=make('span','Add Details','product-details-toggle');heading.append(title,count);summary.append(heading,action);
        const content=make('div',null,'product-details-content'),help=make('p','Add technical details about this product. These details do not create variants or affect stock.','small text-muted'),hint=make('p','Different physical versions, such as 35W and 55W, belong in Variants & Attributes.','small text-muted'),rowsHost=make('div',null,'product-detail-rows'),empty=make('p','No details added. Add a detail when it is useful.','small text-muted'),add=make('button','Add Detail','btn btn-sm');add.type='button';content.append(help,hint,rowsHost,empty,add);section.append(summary,content);host.replaceChildren(section);
        const originalKeys=Object.keys(initial),rows=[];
        const definitions=()=>options.definitions?.()||[];
        const blocked=()=>new Set([...identityNames,...(options.blocked?.()||[])].map(normalizeDetail));
        function feedback(input,message){input.setCustomValidity(message);input.classList.toggle('is-invalid',!!message);input.setAttribute('aria-invalid',String(!!message));const error=document.getElementById(input.id+'-error');error.textContent=message;error.hidden=!message;}
        function validate(show=false){const names=new Set(),result=Object.create(null);let valid=true;
            for(const row of rows){const label=row.name.value.trim(),value=row.value.value.trim(),key=row.fixedKey||label,normalized=normalizeDetail(label);let nameError='',valueError='';
                if(!label&&!value){feedback(row.name,'');feedback(row.value,'');continue;}
                if(!label)nameError='Enter a Detail Name.';else if(label.length>100)nameError='Use at most 100 characters for Detail Name.';else if(blocked().has(normalizeDetail(key))||blocked().has(normalized))nameError='Use the existing field in Basic Details or Variants & Attributes for this detail.';else if(names.has(normalized))nameError='This Detail Name is already added.';
                if(!value)valueError='Enter a Value for this detail.';else if(value.length>row.value.maxLength)valueError='Value is too long.';
                names.add(normalized);if(nameError||valueError)valid=false;else if(Object.hasOwn(initial,row.originalKey)&&row.name.value===row.originalName&&row.value.value===row.originalValue)result[row.originalKey]=row.originalValue;else result[key]=value;
                if(show||row.name.classList.contains('is-invalid')||row.value.classList.contains('is-invalid')){feedback(row.name,nameError);feedback(row.value,valueError);}else{row.name.setCustomValidity(nameError);row.value.setCustomValidity(valueError);}
            }
            const total=rows.filter(row=>row.name.value.trim()&&row.value.value.trim()).length;count.textContent=total?' — '+total+' detail'+(total===1?'':'s')+' added':'';empty.hidden=rows.length>0;add.textContent=rows.length?'+ Add Another Detail':'Add Detail';add.disabled=rows.length>=30;
            return {valid,result};
        }
        function row(name='',value=''){
            if(rows.length>=30)return;const definition=definitions().find(field=>field.code===name),wrap=make('div',null,'product-detail-row'),entry={wrap,fixedKey:definition?.code,originalKey:name,originalName:definition?.label||name,originalValue:value};
            for(const [key,caption,placeholder,limit,initialValue] of [['name','Detail Name','e.g. Material',100,definition?.label||name],['value','Value','e.g. Ceramic',definition?.type==='TEXT_LIST'?1000:100,value]]){const group=make('div'),input=make('input',null,'form-control'),label=make('label',caption,'form-label'),error=make('div',null,'invalid-feedback');input.id='product-detail-'+(++sequence);input.type='text';input.maxLength=limit;input.placeholder=placeholder;input.value=initialValue;label.htmlFor=input.id;error.id=input.id+'-error';error.hidden=true;error.setAttribute('role','alert');input.setAttribute('aria-describedby',error.id);if(key==='name'&&definition){input.readOnly=true;input.title='Configured detail names retain their existing storage key.';}entry[key]=input;input.addEventListener('input',()=>{validate(true);options.onChange?.();});group.append(label,input,error);wrap.append(group);}
            const remove=make('button',null,'btn btn-sm product-detail-remove');remove.type='button';remove.title=definition?.required?'Required category detail':'Remove Detail';remove.disabled=definition?.required===true;remove.setAttribute('aria-label','Remove Detail');const icon=document.createElementNS('http://www.w3.org/2000/svg','svg'),drawing=document.createElementNS('http://www.w3.org/2000/svg','path');icon.setAttribute('viewBox','0 0 24 24');icon.setAttribute('class','sf-icon');icon.setAttribute('aria-hidden','true');drawing.setAttribute('d','M4 6h16 M9 6V3h6v3 M6 6l1 15h10l1-15 M10 10v7 M14 10v7');icon.append(drawing);remove.append(icon);remove.onclick=()=>{rows.splice(rows.indexOf(entry),1);wrap.remove();validate();options.onChange?.();};wrap.append(remove);rows.push(entry);rowsHost.append(wrap);return entry;
        }
        for(const [name,value] of Object.entries(initial))row(name,value);
        add.onclick=()=>{const entry=row();validate();entry?.name.focus();options.onChange?.();};
        section.addEventListener('invalid',()=>section.open=true,true);section.addEventListener('toggle',()=>action.textContent=section.open?'Hide Details':'Add Details');validate();
        return {section,keys:originalKeys,count:()=>Object.keys(validate().result).length,
            check(){return validate(false).valid;},validate(){return validate(true).valid;},read(){const value=validate(true);if(!value.valid){section.open=true;rows.flatMap(row=>[row.name,row.value]).find(input=>!input.validity.valid)?.focus();throw new Error('Complete the entered product details and use distinct Detail Names.');}return value.result;},
            apply(attributes){const result=Object.assign(Object.create(null),attributes);for(const key of originalKeys)delete result[key];for(const [key,value] of Object.entries(this.read())){if(Object.keys(result).some(existing=>normalizeDetail(existing)===normalizeDetail(key)))throw new Error('This detail is already maintained in Variants & Attributes.');result[key]=value;}if(Object.keys(result).length>30){section.open=true;throw new Error('Use at most 30 attributes and descriptive details combined per item.');}return result;},destroy(){section.remove();}};
    }
    return {
        details,normalizeDetail,identityNames,
        create(host,category,initial={},layout={}) {
            if(!window.CatalogAttributes&&(layout.suppressLegacy||layout.hideSpecificationEntry)){host.replaceChildren();let values={...initial};return {read:()=>({...values}),setValues:next=>{values={...(next||{})};},setAxisMode(){},setTemplate(){},destroy(){},vehicleFitments:()=>null};}
            return window.CatalogAttributes ? CatalogAttributes.editor(host,category,initial,legacyCreate,layout) : legacyCreate(layout.media || host,category,initial);
        }
    };
})();
