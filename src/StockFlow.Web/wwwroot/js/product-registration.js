window.ProductRegistration = (() => {
    'use strict';

    let panel;
    let list;
    let add;
    let controls;
    let product;
    let saved = [];
    let access;
    let defaultFields={};
    let descriptiveDetails,commonDetailKeys=[];
    const detailBlocked=()=>[...(product?.registrationDefaults?.variantAxes||[]),...(window.CatalogAttributes?.fields(controls.categoryId.value)||[]).filter(field=>field.variantAxis).flatMap(field=>[field.code,field.label]),...ProductSpecifications.identityNames];
    const detailValues=attributes=>Object.fromEntries(Object.entries(attributes||{}).filter(([key])=>!detailBlocked().some(name=>ProductSpecifications.normalizeDetail(name)===ProductSpecifications.normalizeDetail(key))));
    let fieldId = 0;
    let editWizard;
    let preferredSupplierEditor;
    let pendingVariantReservation;
    const cards = [];
    function field(parent, key, label, value, type = 'text', options = {}) {
        const group = document.createElement('div');
        group.className = 'col-md-6 col-lg-4';
        const input = document.createElement('input');
        input.type = type;
        input.className = 'form-control';
        input.id = `registration-${++fieldId}-${key}`;
        input.value = value ?? '';
        Object.assign(input, options);
        const caption = document.createElement('label');
        caption.className = 'form-label';
        caption.htmlFor = input.id;
        caption.textContent = label;
        group.append(caption, input);
        parent.append(group);
        return input;
    }
    function card(record = {}) {
        const wrap = document.createElement('fieldset');
        wrap.className = 'card card-body mb-3';
        const legend = document.createElement('legend');
        legend.className = 'h6';
        legend.textContent = record.variantId ? record.variantName : 'Item configuration';
        wrap.append(legend);
        const row = document.createElement('div');
        row.className = 'row g-3';
        wrap.append(row);
        const entry = {
            wrap,
            record,
            fields: {}
        };
        const f = entry.fields;
        f.variantName = field(
            row,
            'name',
            'Size / model name *',
            record.variantName ?? 'DEFAULT',
            'text',
            {
                required: true,
                maxLength: 150
            }
        );
        f.sku = field(row, 'sku', 'Item code / SKU *', record.sku || '', 'text', {
            required: true,
            minLength: 3,
            maxLength: 100,
            placeholder: 'BAT-150AH / GYS-25L'
        });
        if(record.variantId){f.sku.readOnly=true;f.sku.title='Saved SKUs are permanent. Add a new variant for a different stock identity.';}
        f.primaryBarcode = field(
            row,
            'barcode',
            'Existing model barcode (optional)',
            record.primaryBarcode || record.barcode,
            'text',
            {
                maxLength: 150
            }
        );
        f.barcodeAliases = field(
            row,
            'aliases',
            'Other barcodes (separate with commas)',
            (record.barcodes || [])
                .filter((value) => {
                    return value !== (record.primaryBarcode || record.barcode);
                })
                .join(', '),
            'text',
            {
                maxLength: 1000,
                placeholder: 'Outer box barcode, older model barcode'
            }
        );
        const hint = document.createElement('p');
        hint.className = 'small text-muted col-12 mb-0';
        hint.textContent =
            'Saved barcode mappings remain valid. Leave blank to retain the saved barcode. New manufacturer barcodes can be added here; physical serial numbers are managed when stock arrives.';
        row.append(hint);
        for (const [key, label, fallback, max, step] of [
            ['purchasePrice', 'Buying price per item *', 0, '9999999999.99', '0.01'],
            ['sellingPrice', 'Selling price per item *', 0, '9999999999.99', '0.01'],
            ['mrp', 'MRP', 0, '9999999999.99', '0.01'],
            ['packQuantity', 'Pack quantity *', 1, '10000', '1'],
            [
                'taxPercentage',
                'GST rate (%) *',
                Number(controls.taxPercentage.value),
                '100',
                '0.01'
            ],
            [
                'warrantyMonths',
                'Warranty (months) *',
                Number(controls.warrantyMonths.value),
                '240',
                '1'
            ],
            ['minimumStockLevel', 'Low-stock alert quantity *', 0, '999999999', '1']
        ]) {
            f[key] = field(row, key, label, record[key] ?? (!record.variantId?defaultFields[key]?.value??product?.registrationDefaults?.[key==='minimumStockLevel'?'reorderLevel':key]:null) ?? fallback, 'number', {
                required: true,
                min: '0',
                max,
                step
            });
        }
        if(document.body.dataset.catalogRegistrationEnhancements==='true'){
            entry.partNumber=field(row,'manufacturerPartNumber','Manufacturer part number',record.attributes?.manufacturer_part_number,'text',{maxLength:100});
            entry.localReorder=field(row,'localReorder','Reorder quantity for the current shop',record.locationMinimumStockLevel??record.minimumStockLevel??0,'number',{min:0,max:999999999,step:1});
            const label=document.createElement('label');label.className='small col-12';entry.applyLocal=document.createElement('input');entry.applyLocal.type='checkbox';label.append(entry.applyLocal,document.createTextNode(' Save this reorder quantity for the selected shop'));row.append(label);
        }
        f.packQuantity.min = '1';
        for(const [key,flag] of [['purchasePrice','useDefaultPurchasePrice'],['sellingPrice','useDefaultSellingPrice']]){
            const label=document.createElement('label');label.className='small mt-1';label.style.display=product?.registrationDefaults?'block':'none';const input=document.createElement('input');input.type='checkbox';input.checked=!!product?.registrationDefaults&&(record.variantId?record[flag]===true:record[flag]!==false);input.disabled=!product?.registrationDefaults;label.append(input,document.createTextNode(' Use product default'));f[key].after(label);entry[flag]=input;
            const refresh=()=>{f[key].readOnly=input.checked;if(input.checked&&defaultFields[key])f[key].value=defaultFields[key].value;};input.onchange=refresh;refresh();
        }
        if (document.body.dataset.businessProfile && document.body.dataset.businessProfile !== 'ELECTRICAL') {
            f.sku.placeholder = 'Enter a unique item code';
            f.warrantyMonths.closest('.col-md-6').hidden = true;
        }
        f.supplierProductCode = field(
            row,
            'supplier-code',
            'Supplier product code',
            record.supplierProductCode,
            'text',
            {
                maxLength: 100
            }
        );
        if(document.body.dataset.catalogRegistrationEnhancements==='true'&&access?.suppliers==='true'){
            const group=f.supplierProductCode.closest('.col-md-6');group.hidden=!f.supplierProductCode.value;group.querySelector('label').textContent='Legacy supplier item reference';f.supplierProductCode.readOnly=true;
        }
        const specs = document.createElement('div');
        specs.className = 'col-12';
        const title = document.createElement('p');
        title.className = 'fw-bold mb-1';
        title.textContent = 'Variant attributes';
        specs.hidden=!(window.CatalogAttributes?.fields(controls.categoryId.value)||[]).some(field=>field.variantAxis);
        const attributes = document.createElement('div');
        specs.append(title, attributes);
        row.append(specs);
        entry.specifications = ProductSpecifications.create(
            attributes,
            controls.categoryId,
            record.variantId?record.attributes||{}:{...Object.fromEntries(Object.entries(saved[0]?.attributes||{}).filter(([key])=>!(window.CatalogAttributes?.fields(controls.categoryId.value)||[]).some(field=>field.variantAxis&&field.code===key))),...(record.attributes||{})},
            {hideSpecificationEntry:true}
        );
        entry.specifications.setVehicleFitments?.(record.vehicleFitments||[]);
        entry.extraAxes={};
        for(const axis of product?.registrationDefaults?.variantAxes||[]){
            if(attributes.querySelector('[data-attribute-code="'+axis+'"]'))continue;
            specs.hidden=false;const input=field(attributes,axis,axis.replace(/_/g,' ')+' *',record.attributes?.[axis]||'','text',{required:true,maxLength:100,readOnly:!!record.variantId});entry.extraAxes[axis]=input;
        }
        if(record.variantId&&window.CatalogAttributes){
            for(const f of CatalogAttributes.fields(controls.categoryId.value).filter(f=>f.variantAxis)) {
                const input=attributes.querySelector('[data-attribute-code="'+f.code+'"]');
                if(input){input.disabled=true;input.title='Saved variant options cannot change. Add a new variant instead.';}
            }
        }
        entry.remove = document.createElement('button');
        entry.remove.type = 'button';
        entry.remove.className = 'btn btn-outline-secondary mt-3 align-self-start';
        entry.remove.textContent = 'Remove unsaved size / model';
        entry.remove.onclick = () => {
            entry.specifications.destroy();
            cards.splice(cards.indexOf(entry), 1);
            wrap.remove();
            refresh(controls);
        };
        wrap.append(entry.remove);
        const editable = record.variantId ? access.edit === 'true' : access.create === 'true';
        if (!editable || record.isActive === false) {
            wrap.disabled = true;
        }
        if(document.body.dataset.catalogRegistrationEnhancements==='true' && access?.suppliers==='true'){const host=document.createElement('section');host.className='col-12';row.append(host);entry.supplierHost=host;entry.supplierIdentifiers=SupplierIdentifiers.create(host,record.supplierIdentifiers||[],{integratedPicker:true});}
        const extraDetails=Object.fromEntries(Object.entries(detailValues(record.attributes)).filter(([key])=>!commonDetailKeys.includes(key)));
        if(Object.keys(extraDetails).length){const host=document.createElement('section');host.className='col-12';row.append(host);entry.descriptiveDetails=ProductSpecifications.details(host,extraDetails,{title:'Saved Details for '+(record.variantName||'this variant'),blocked:detailBlocked,definitions:()=>window.CatalogAttributes?.fields(controls.categoryId.value)||[]});}
        cards.push(entry);
        list.append(wrap);
        refresh(controls);
        editWizard?.mount(entry);
    }
    function refresh(value) {
        if (!panel) {
            return;
        }
        const multiple = value.hasVariants.checked;
        if (!multiple && cards.length > 1) {
            value.hasVariants.checked = true;
            document.getElementById('has-variants-next-step').textContent =
                'Keep the different sizes or models option selected while more than one size or model exists. Use Remove to delete an unsaved size or model.';
            refresh(value);
            return;
        }
        panel.querySelector('h3').textContent = multiple
            ? 'Size / model details'
            : 'Pricing & barcode';
        add.hidden = !multiple || access.create !== 'true';
        cards.forEach((entry) => {
            entry.fields.variantName.closest('.col-md-6').hidden = !multiple;
            entry.remove.hidden = !!entry.record.variantId || cards.length === 1 || !multiple;
        });
    }
    async function open(value, record) {
        if(window.CatalogAttributes) await CatalogAttributes.load();
        controls = value;
        product = record;
        pendingVariantReservation = null;
        if(value.parentProductCode){value.parentProductCode.readOnly=!!record;value.parentProductCode.title=record?'Saved product codes are permanent.':'';}
        const configured=window.CatalogAttributes&&CatalogAttributes.fields(value.categoryId.value).length>0;
        value.categoryId.disabled=!!record&&configured;
        value.categoryId.title=record&&configured?'Configured categories cannot change. Register a new product for a different category.':'';
        saved = [];
        cards.forEach((entry) => {
            entry.descriptiveDetails?.destroy();return entry.specifications.destroy();
        });
        descriptiveDetails?.destroy();descriptiveDetails=null;document.getElementById('product-edit-details')?.remove();commonDetailKeys=[];
        cards.length = 0;
        list.replaceChildren();
        panel.querySelector('[data-product-defaults]')?.remove();defaultFields={};
        if(record?.registrationDefaults){const section=document.createElement('fieldset');section.dataset.productDefaults='';section.className='mb-3';const heading=document.createElement('legend');heading.className='h6';heading.textContent='Product pricing defaults';const row=document.createElement('div');row.className='row g-3';section.append(heading,row);for(const [key,label] of [['purchasePrice','Default purchase cost'],['sellingPrice','Default selling price'],['mrp','Default MRP']])defaultFields[key]=field(row,'default-'+key,label,record.registrationDefaults[key],'number',{min:0,max:9999999999.99,step:'.01'});section.addEventListener('input',()=>{for(const entry of cards){if(entry.useDefaultPurchasePrice?.checked)entry.fields.purchasePrice.value=defaultFields.purchasePrice.value;if(entry.useDefaultSellingPrice?.checked)entry.fields.sellingPrice.value=defaultFields.sellingPrice.value;}});list.before(section);}
        if (record && access.view === 'true') {
            const response = await fetch(
                `/api/products/variants?productId=${record.productId}&activeOnly=false`,
                {
                    credentials: 'same-origin'
                }
            );
            if (!response.ok) {
                throw new Error(
                    'Unable to load item configurations. Reopen the product before saving.'
                );
            }
            saved = await response.json();
            const common=Object.fromEntries(Object.entries(detailValues(saved[0]?.attributes)).filter(([key,value])=>saved.every(variant=>variant.attributes?.[key]===value)));commonDetailKeys=Object.keys(common);
            const host=document.createElement('fieldset');host.id='product-edit-details';host.className='col-12';controls.detailedDescription.closest('.col-12,.col-md-6').after(host);
            descriptiveDetails=ProductSpecifications.details(host,common,{blocked:detailBlocked,definitions:()=>window.CatalogAttributes?.fields(controls.categoryId.value)||[]});
            host.disabled=access.edit!=='true'||record.isActive===false||saved.some(variant=>variant.isActive===false);
            if(host.disabled){const note=document.createElement('small');note.className='text-muted';note.textContent='Shared saved details remain read-only when a configuration is inactive or edit permission is unavailable.';host.append(note);}
            saved.forEach(card);
            await Promise.all(cards.map(entry=>entry.supplierIdentifiers?.ready));
            for(const entry of cards)entry.supplierIdentifiers?.read();
        } else if (!record && access.create === 'true') {
            card();
        }
        panel.hidden = !cards.length;
        refresh(value);
    }
    function syncDefaults() {
        cards.forEach((value) => {
                if (!value.record.variantId) {
                    value.fields.taxPercentage.value = controls.taxPercentage.value;
                    value.fields.warrantyMonths.value = controls.warrantyMonths.value;
                }
        });
    }
    function applyDefaults(body){
        if(product?.registrationDefaults)body.registrationDefaults={...product.registrationDefaults,...Object.fromEntries(Object.entries(defaultFields).map(([key,input])=>[key,Number(input.value)]))};
        if(body.registrationDefaults&&preferredSupplierEditor)body.registrationDefaults.preferredSupplierId=preferredSupplierEditor.preferred();
    }
    function beforeSave(body) {
        if(product)body.parentProductCode=product.parentProductCode;
        descriptiveDetails?.read();for(const card of cards)card.descriptiveDetails?.read();
        applyDefaults(body);
        const editable = cards.filter((value) => {
            return !value.wrap.disabled||value.pendingActive!==undefined&&value.pendingActive!==value.record.isActive;
        });
        if (!editable.length) {
            return true;
        }
        body.variants = editable.map((value) => {
            let attrs = value.specifications.read();
            for(const [axis,input] of Object.entries(value.extraAxes||{}))attrs[axis]=input.value.trim();
            if(value.descriptiveDetails)attrs=value.descriptiveDetails.apply(attrs);
            if(descriptiveDetails)attrs=descriptiveDetails.apply(attrs);
            if(value.record.variantId){for(const field of (window.CatalogAttributes?.fields(controls.categoryId.value)||[]).filter(field=>!field.variantAxis&&/^(vehicle_compatibility|vehicle_make|compatible_models|model_years|part_position|fitment_notes|universal_fitment)$/.test(field.code))){const previous=value.record.attributes?.[field.code];if(previous!=null)attrs[field.code]=previous;else delete attrs[field.code];}}

            if(value.partNumber){if(value.partNumber.value.trim())attrs.manufacturer_part_number=value.partNumber.value.trim();else delete attrs.manufacturer_part_number;}
            if(value.productPartNumber!==undefined){if(value.productPartNumber)attrs.manufacturer_part_number=value.productPartNumber;else delete attrs.manufacturer_part_number;}
            const result = {
                ...value.record,
                attributes: attrs,
                vehicleFitments:value.specifications.vehicleFitments?.()||null,
                productId: product?.productId || 1,
                variantId: value.record.variantId || null
            };
            Object.entries(value.fields)
                .filter(([key]) => {
                    return key !== 'barcodeAliases';
                })
                .forEach(([key, input]) => {
                    return (result[key] =
                        input.type === 'number' ? Number(input.value) : input.value.trim());
                });
            if(value.record.variantId)result.sku=value.record.sku;
            if(value.record.variantId)for(const axis of [...(product?.registrationDefaults?.variantAxes||[]),...(window.CatalogAttributes?.fields(controls.categoryId.value)||[]).filter(field=>field.variantAxis).map(field=>field.code)])if(value.record.attributes?.[axis]!==undefined)result.attributes[axis]=value.record.attributes[axis];
            result.barcodes = value.fields.barcodeAliases.value
                .split(',')
                .map((text) => {
                    return text.trim();
                })
                .filter(Boolean);
            if (
                result.primaryBarcode &&
                !result.barcodes.some((text) => {
                    return text.toLowerCase() === result.primaryBarcode.toLowerCase();
                })
            ) {
                result.barcodes.unshift(result.primaryBarcode);
            }
            if (!body.hasVariants) {
                result.variantName = 'DEFAULT';
            }
            result.isSerialTracked = body.trackingType === 'SERIAL';
            if(!result.primaryBarcode&&value.record.variantId)result.primaryBarcode=value.record.primaryBarcode||value.record.barcode||null;
            const unchanged =
                result.primaryBarcode &&
                result.primaryBarcode === (value.record.primaryBarcode || value.record.barcode);
            result.barcodeSource = result.primaryBarcode
                ? unchanged
                    ? value.record.barcodeSource || 'MANUFACTURER'
                    : 'MANUFACTURER'
                : null;
            result.isActive = value.pendingActive ?? value.record.isActive ?? true;
            result.useDefaultPurchasePrice=value.useDefaultPurchasePrice.checked;
            result.useDefaultSellingPrice=value.useDefaultSellingPrice.checked;
            if(value.supplierIdentifiers)result.supplierIdentifiers=value.supplierIdentifiers.read();else delete result.supplierIdentifiers;
            result.applyLocationMinimumStock = !!value.applyLocal?.checked;
            if(result.applyLocationMinimumStock){result.locationMinimumStockLevel=Number(value.localReorder.value);result.locationId=window.StockFlowLocation;}
            return result;
        });
        for(const image of body.productImages||[]){const entry=cards.find(value=>value.record.variantName===image.variantName);if(entry)image.variantName=entry.fields.variantName.value.trim();}
        if (!product && !body.variants.length) {
            throw new Error('Add an item configuration before saving.');
        }
        return true;
    }
    async function wizard(record,imagePreview){
        if(!record||window.StockFlowMasterScreens?.mode!=='edit')return;
        const form=document.getElementById('master-form'),fields=document.getElementById('form-fields');
        if(editWizard)return;
        if(controls.isActive)controls.isActive.checked=record.isActive;
        const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
        const root=node('div',null,'product-edit-wizard registration-tabs-dialog');root.id='product-edit-wizard';fields.before(root);
        const tabs=node('nav',null,'registration-tabs');tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','Edit product steps');root.append(tabs);
        const banner=document.getElementById('editor-message');root.append(banner);banner.className='alert registration-alert';banner.hidden=true;
        const keys=['basic','variants','pricing','media'],titles=['Basic Details','Variants & Attributes','Pricing & Stock','Product Details & Review'],panels={},buttons={},checked=new Set(),originalCaptions={};let active='basic',baseline,committed=false,confirmed='',errorStep=null,marginWarning=false;
        for(const [i,key] of keys.entries()){
            const button=node('button');button.innerHTML='<svg class="sf-icon" aria-hidden="true"><use href="/images/icons.svg#'+['document','grid','tag','eye'][i]+'"/></svg>';button.append(node('span',titles[i]),node('span',null,'edit-step-status'));button.type='button';button.id='edit-tab-'+key;button.setAttribute('role','tab');button.setAttribute('aria-controls','edit-panel-'+key);tabs.append(button);buttons[key]=button;
            const section=node('section');section.id='edit-panel-'+key;section.setAttribute('role','tabpanel');section.setAttribute('aria-labelledby',button.id);section.append(node('h3',titles[i],'h6 registration-section-title'));const grid=node('div',null,'row g-3');section.append(grid);section.grid=grid;panels[key]=section;root.append(section);button.onclick=()=>navigate(key);
        }
        const group=input=>input?.closest('.col-md-6,.col-12');
        const move=(name,key)=>{const n=group(controls[name]);if(n)panels[key].grid.append(n);};
        for(const name of ['productName','categoryId','brandId','model','unitOfMeasure','trackingType','hsnCode','productStatus','description'])move(name,'basic');
        for(const name of ['parentProductCode','hasVariants'])move(name,'variants');
        for(const name of ['taxPercentage','warrantyMonths','warrantyProvider'])move(name,'pricing');
        for(const name of ['productImage','detailedDescription','productImageUrl'])move(name,'media');
        if(controls.isActive)move('isActive','basic');
        document.getElementById('product-edit-details')?.classList.add('product-edit-shared-details');const common=document.getElementById('product-edit-details');if(common)panels.media.grid.append(common);
        fields.hidden=true;
        for(const [name,caption] of [['productName','Product name *'],['brandId','Brand (Optional)'],['model','Product model'],['unitOfMeasure','Stock unit *'],['trackingType','Tracking method *'],['description','Product Description'],['productImage','Product Photos'],['hasVariants','This product has variants']]){const label=document.querySelector('label[for="'+name+'"]');if(label)label.textContent=caption;}
        const variantToggle=group(controls.hasVariants),variantLabel=document.querySelector('label[for="hasVariants"]');variantToggle.className='col-12 registration-variant-toggle';variantLabel.classList.add('registration-variant-switch');variantLabel.prepend(controls.hasVariants);controls.hasVariants.setAttribute('role','switch');
        const categories=[...(controls.categoryId.stockFlowLookupOptions||[])];if(!categories.some(c=>c.categoryId===record.categoryId))categories.push({categoryId:record.categoryId,categoryName:record.categoryName||'Saved category'});
        const categoryLocked=controls.categoryId.disabled||record.categoryChangeLocked===true;controls.categoryId.disabled=categoryLocked;
        const cascade=ProductRegistrationForm.categoryCascade(controls.categoryId,categories,categoryLocked);cascade.classList.add('col-12');
        if(categoryLocked)cascade.append(node('small','This category is protected by saved attributes or the issued Product Code. Use a reviewed classification correction or register a new product.','text-muted'));
        const locked=record.hasInventoryHistory!==false;
        const simpleIdentityLocked=!record.hasVariants&&!!record.parentProductCode&&saved.some(v=>String(v.sku||'').toUpperCase()===record.parentProductCode.toUpperCase());
        if(simpleIdentityLocked&&!locked){
            const note=node('small','You can enable variants before this product has stock or transaction history. Its existing SKU will stay unchanged; additional variants receive separate SKUs.','text-muted d-block');
            note.id='product-simple-identity-help';controls.hasVariants.setAttribute('aria-describedby',note.id);variantToggle.append(note);
        }
        group(controls.trackingType).hidden=false;
        group(controls.warrantyMonths).hidden=false;
        const partGroup=node('div',null,'col-md-6 col-lg-4'),part=field(partGroup,'manufacturer-part','Manufacturer part number',saved.every(v=>v.attributes?.manufacturer_part_number===saved[0]?.attributes?.manufacturer_part_number)?saved[0]?.attributes?.manufacturer_part_number||'':'','text',{maxLength:100});
        partGroup.firstElementChild.className='';panels.basic.grid.insertBefore(partGroup,group(controls.hsnCode));let partChanged=false;part.addEventListener('input',()=>partChanged=true);
        const additional=node('section',null,'registration-additional col-12'),additionalGrid=node('div',null,'row g-3');additional.append(node('h3','Additional Details','h6 registration-section-title'),additionalGrid);additionalGrid.append(partGroup);for(const name of ['hsnCode','productStatus','description'])if(controls[name])additionalGrid.append(group(controls[name]));group(controls.description).className='col-12';panels.basic.grid.append(additional);panels.basic.firstElementChild.textContent='Product Information';
        for(const name of ['unitOfMeasure','trackingType','hasVariants'])if(locked){controls[name].disabled=true;const note=node('small',name==='hasVariants'?'Variant structure is locked because stock or transaction history exists. Add combinations to this product instead.':'Locked after stock or transaction history. Use an authorized inventory correction to change this field.','text-muted d-block');group(controls[name]).append(note);}
        const variantsHost=node('div',null,'col-12 table-responsive'),table=node('table',null,'table table-bordered product-edit-variants'),thead=node('thead'),head=node('tr'),tbody=node('tbody');for(const title of ['Variant','SKU','Barcode Summary','Status','Actions'])head.append(node('th',title));thead.append(head);table.append(thead,tbody);variantsHost.append(table);panels.variants.grid.append(variantsHost);
        panels.variants.grid.append(add);add.textContent='Add Variant';add.id='edit-add-variant';
        const defaults=panel.querySelector('[data-product-defaults]');if(defaults){defaults.disabled=access.edit!=='true';panels.pricing.grid.prepend(defaults);}
        panels.pricing.grid.prepend(node('p','Price, tax and warranty changes apply to future transactions. Existing purchases, invoices and stock balances remain unchanged.','small text-muted col-12'));
        panels.pricing.grid.append(node('small','Purchase costs exclude GST. Selling prices follow the configured shop tax mode.','text-muted col-12'));
        const pricing=node('div',null,'col-12 product-edit-pricing');panels.pricing.grid.append(pricing);
        if(record.registrationDefaults&&access.suppliers==='true'){const host=node('fieldset',null,'col-12');host.disabled=access.edit!=='true';host.id='edit-product-supplier';panels.pricing.grid.append(host);preferredSupplierEditor=SupplierIdentifiers.create(host,[],{showPreferred:true,preferredOnly:true,preferredSupplierId:record.registrationDefaults.preferredSupplierId});await preferredSupplierEditor.ready;preferredSupplierEditor.read();}
        const review=node('details',null,'registration-preview col-12');review.id='edit-review-changes';review.append(node('summary','Review Changes'));const reviewList=node('ul');review.append(reviewList);panels.media.grid.append(review);
        const footer=form.querySelector('.configuration-dialog-actions');footer.classList.add('registration-footer');root.append(footer);const cancel=document.getElementById('cancel-edit');cancel.textContent='Cancel';const previous=node('button','Previous','btn btn-dark'),next=node('button','Next','btn btn-dark'),submit=footer.querySelector('[type=submit]');previous.type=next.type='button';previous.id='edit-previous';next.id='edit-next';submit.textContent='Save Changes';footer.prepend(previous);footer.insertBefore(next,submit);previous.onclick=()=>select(keys[Math.max(0,keys.indexOf(active)-1)]);next.onclick=()=>navigate(keys[keys.indexOf(active)+1]);
        form.noValidate=true;form.dataset.validationOwner='product-edit';
        const snapshot=()=>{const result={};for(const input of form.querySelectorAll('input,select,textarea')){if(input.type==='search'||input.type==='file'||input.closest('.product-category-select-panel')||input.id.startsWith('edit-category-level-'))continue;if(!input.id)input.id='edit-field-'+(++fieldId);result[input.id]=input.type==='checkbox'?input.checked:input.type==='number'&&input.value!==''?Number(input.value):input.value;}for(const entry of cards)result['Variant status: '+(entry.record.variantId||entry.fields.sku.id)]=entry.pendingActive;result.Photos=imagePreview.signature?.()||controls.productImage.value;return result;};
        const changes=()=>{const now=snapshot();return Object.keys({...baseline,...now}).filter(key=>baseline?.[key]!==now[key]).map(key=>({key,old:baseline?.[key],value:now[key]}));};
        function notice(type,title,message){ProductRegistrationForm.banner(banner,title?{type,title,message,dismissible:false}:null);}
        function errors(key,show=false){
            const section=panels[key],issues=[];
            if(show)for(const input of section.querySelectorAll('[data-registration-error]'))ProductRegistrationForm.field(input,'');
            if(key==='media'){descriptiveDetails?.check();for(const entry of cards)entry.descriptiveDetails?.check();}
            for(const input of section.querySelectorAll('input,select,textarea')){const hidden=input.closest('[hidden]'),picker=hidden===input&&input.closest('.product-category-select');if(input.disabled||!input.willValidate||input.type==='search'||hidden&&hidden!==section&&!picker&&!hidden.classList.contains('variant-detail-row')&&!hidden.classList.contains('variant-price-editor'))continue;const message=StockFlowValidation.fieldMessage(input);if(show)ProductRegistrationForm.field(input,message);if(message)issues.push({input,message});}
            if(key==='variants')issues.push(...CatalogAttributes.identityIssues(cards.map(entry=>({code:entry.fields.sku,saved:!!entry.record.variantId,barcode:entry.fields.primaryBarcode,identity:entry.fields.variantName,attributes:{...entry.record.attributes,...Object.fromEntries(Object.entries(entry.extraAxes||{}).map(([axis,input])=>[axis,input.value])),...Object.fromEntries([...entry.wrap.querySelectorAll('[data-attribute-code]')].map(input=>[input.dataset.attributeCode,input.value]))}})),record.registrationDefaults?.variantAxes||[]));
            if(key==='pricing')for(const entry of cards){if(Number(entry.fields.mrp.value)>0&&Number(entry.fields.sellingPrice.value)>Number(entry.fields.mrp.value))issues.push({input:entry.fields.sellingPrice,message:'Selling price cannot exceed MRP.'});issues.push(...(entry.supplierIdentifiers?.issues()||[]));}
            if(show)for(const issue of issues)ProductRegistrationForm.field(issue.input,issue.message);
            return [...new Map(issues.map(issue=>[issue.input,issue])).values()];
        }
        function select(key){active=key;for(const k of keys){panels[k].hidden=k!==key;buttons[k].setAttribute('aria-selected',String(k===key));buttons[k].tabIndex=k===key?0:-1;}previous.hidden=key==='basic';next.hidden=key==='media';submit.hidden=key!=='media';if(errorStep&&errorStep!==key)notice(null,null);renderReview();}
        function validate(key){checked.add(key);const issues=errors(key,true);buttons[key].classList.toggle('has-errors',!!issues.length);buttons[key].querySelector('.edit-step-status').textContent=issues.length?' !':' ✓';if(issues.length){select(key);errorStep=key;const input=issues[0].input;for(let n=input.parentElement;n&&n!==panels[key];n=n.parentElement){if(n.tagName==='DETAILS')n.open=true;if(n.classList.contains('variant-detail-row')||n.classList.contains('variant-price-editor'))n.hidden=false;}input.focus();input.scrollIntoView({block:'center'});return false;}return true;}
        function navigate(key){if(!key)return;if(keys.indexOf(key)>keys.indexOf(active))for(const prior of keys.slice(0,keys.indexOf(key)))if(!validate(prior))return;notice(null,null);select(key);}
        function renderReview(){
            reviewList.replaceChildren();const delta=changes();if(!delta.length){reviewList.append(node('li','No changes made.'));return;}
            for(const item of delta){
                const input=document.getElementById(item.key),control=input?.closest('.product-category-select')?.querySelector('button')||input,statusEntry=cards.find(entry=>item.key==='Variant status: '+(entry.record.variantId||entry.fields.sku.id));
                const label=statusEntry?'Variant status — '+statusEntry.fields.variantName.value:item.key==='Photos'?'Product Photos':(control?.labels?.[0]?.textContent||document.querySelector('label[for="'+control?.id+'"]')?.textContent||input?.getAttribute('aria-label')||'Product detail').trim().replace(/\s*\*$/,'');
                const display=(value,old)=>{if(typeof value==='boolean')return statusEntry||input===controls.isActive?(value?'Active':'Inactive'):(value?'Yes':'No');if(value==null||value==='')return input===controls.brandId?'Unbranded':'Not set';if(input?.tagName==='SELECT')return old&&originalCaptions[item.key]||[...input.options].find(option=>String(option.value)===String(value))?.textContent||value;return value;};
                reviewList.append(node('li',item.key==='Photos'?label+' updated':label+': '+display(item.old,true)+' → '+display(item.value,false)));
            }
        }
        function mount(entry){
            const {wrap,fields:f,record:r}=entry;entry.pendingActive=r.isActive??true;
            const tr=node('tr'),detail=node('tr',null,'variant-detail-row'),cell=node('td');detail.hidden=true;cell.colSpan=5;cell.append(wrap);detail.append(cell);const name=node('td',r.variantName||'New variant'),sku=node('td',r.sku||f.sku.value),barcode=node('td',(r.barcodes||[r.primaryBarcode]).filter(Boolean).join(', ')||'Not assigned'),status=node('td',entry.pendingActive?'Active':'Inactive'),actions=node('td');tr.append(name,sku,barcode,status,actions);tbody.append(tr,detail);
            const edit=node('button',null,'btn btn-dark btn-sm'),toggle=node('button',null,'btn btn-dark btn-sm');edit.type=toggle.type='button';edit.setAttribute('aria-label','Edit variant '+(r.variantName||'new variant'));edit.innerHTML='<svg class="sf-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="m4 16 12-12 4 4L8 20H4z"/></svg>';edit.setAttribute('aria-expanded','false');edit.onclick=()=>{detail.hidden=!detail.hidden;edit.setAttribute('aria-expanded',String(!detail.hidden));};actions.append(edit);
            if(r.variantId&&access.status==='true'){
                const statusAction=()=>{toggle.setAttribute('aria-label',(entry.pendingActive?'Deactivate':'Activate')+' variant '+r.variantName);toggle.innerHTML='<svg class="sf-icon" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="'+(entry.pendingActive?'m5 5 14 14':'m7 12 3 3 7-7')+'"/></svg>';};statusAction();
                toggle.onclick=async()=>{if(entry.pendingActive&&(r.currentStock||0)!==0){notice('warning','This variant still has stock. Resolve inventory before deactivating it.');return;}if(!await StockFlowMaster.confirmChange({title:(entry.pendingActive?'Deactivate':'Activate')+' Variant?',message:'This changes eligibility for future transactions. Saved identifiers and history will be preserved.',action:entry.pendingActive?'Deactivate Variant':'Activate Variant'}))return;entry.pendingActive=!entry.pendingActive;wrap.disabled=!entry.pendingActive||access.edit!=='true';priceSection.disabled=wrap.disabled;status.textContent=entry.pendingActive?'Active':'Inactive';statusAction();};actions.append(toggle);
            }
            const priceSection=node('fieldset',null,'mb-3');priceSection.disabled=wrap.disabled;const priceHead=node('legend',(r.variantName||'New variant')+' · '+f.sku.value,'h6'),mainPrices=node('div',null,'row g-3'),more=node('details'),moreRow=node('div',null,'row g-3');more.append(node('summary','Inventory, tax, warranty & suppliers'),moreRow);priceSection.append(priceHead,mainPrices,more);pricing.append(priceSection);
            for(const key of ['purchasePrice','sellingPrice','mrp'])mainPrices.append(group(f[key]));
            for(const key of ['packQuantity','taxPercentage','warrantyMonths','minimumStockLevel','supplierProductCode'])moreRow.append(group(f[key]));
            group(f.warrantyMonths).hidden=false;
            if(r.variantId&&(record.historyVariantIds?record.historyVariantIds.includes(r.variantId):locked)){f.packQuantity.readOnly=true;group(f.packQuantity).append(node('small','Pack conversion is locked after stock or transaction history.','text-muted'));}
            if(entry.localReorder)moreRow.append(group(entry.localReorder));if(entry.applyLocal)moreRow.append(entry.applyLocal.closest('label'));
            if(entry.supplierHost){moreRow.append(entry.supplierHost);if(r.variantId&&access.supplierCreate==='true'){const create=node('a','+ Add Supplier','btn btn-dark btn-sm mt-2');create.href=StockFlowSupplierNavigation.createUrl(record.productId,r.variantId,StockFlowSupplierNavigation.resolve(record,saved)?.from);entry.supplierHost.append(create);}}
            const extra=wrap.querySelector('.product-descriptive-details');if(extra)panels.media.grid.insertBefore(extra.closest('section')||extra,review);
            let previousName=f.variantName.value;f.variantName.addEventListener('input',()=>{name.textContent=f.variantName.value||'New variant';priceHead.textContent=(f.variantName.value||'New variant')+' · '+f.sku.value;if(f.variantName.value.trim()){imagePreview.renameVariant?.(previousName,f.variantName.value.trim());previousName=f.variantName.value.trim();}imagePreview.setVariantOptions?.(cards.map(value=>value.fields.variantName.value).filter(Boolean));});f.primaryBarcode.addEventListener('input',()=>barcode.textContent=f.primaryBarcode.value||'Not assigned');
            if(!r.variantId){actions.append(entry.remove);const remove=entry.remove.onclick;entry.remove.onclick=async()=>{if(!await StockFlowDialog.confirm('Remove this unsaved variant and its entered details?'))return;remove();tr.remove();detail.remove();priceSection.remove();};}
            entry.priceSection=priceSection;
            imagePreview.setVariantOptions?.(cards.map(value=>value.fields.variantName.value).filter(Boolean));
        }
        let supplierContext=null,supplierTarget=null;const supplierNotice=message=>{const note=node('section',null,'supplier-navigation-hint');note.setAttribute('role','status');note.append(node('h3','Supplier setup unavailable','h6'),node('p',message));panels.basic.grid.prepend(note);};
        editWizard={mount,initialFocus(){if(supplierTarget)supplierTarget.scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'auto':'smooth',block:'center'});return supplierTarget;},validate(){for(const key of keys)if(!validate(key))return false;return true;},saved(){committed=true;const destination=supplierTarget&&window.StockFlowSupplierNavigation?.returnUrl(supplierContext);if(destination&&window.StockFlowMasterScreens)StockFlowMasterScreens.back=()=>location.assign(destination);},conflict(error){if(error.status!==409)return;const reload=node('button','Reload latest product','btn btn-dark btn-sm');reload.type='button';reload.onclick=async()=>{if(changes().length&&!await StockFlowDialog.confirm('Discard unsaved changes and reload the latest product?'))return;committed=true;location.reload();};StockFlowNotify.action('master-result',reload);},review:renderReview,async beforeSave(body){
            body.expectedEditVersion=record.editVersion;body.registrationBusinessProfile=record.registrationBusinessProfile||document.body.dataset.businessProfile;
            if(partChanged)for(const entry of cards){if(entry.partNumber)entry.partNumber.value=part.value;entry.productPartNumber=part.value.trim();}
            if(!record.editVersion)throw new Error('Reload the product before saving so concurrent changes can be checked.');
            const sensitive=changes().filter(change=>['categoryId','brandId','unitOfMeasure','trackingType','hasVariants','productStatus','isActive'].includes(change.key));const signature=JSON.stringify(sensitive);
            if(sensitive.length&&signature!==confirmed){if(!await StockFlowMaster.confirmChange({title:'Review product changes',message:"These changes affect current classification or future purchasing and billing. Existing Product Code, SKUs, barcodes, transaction snapshots and stock quantities will remain unchanged.",action:'Save Changes'}))return false;confirmed=signature;}
            return true;
        }};
        for(const entry of cards)mount(entry);
        StockFlowUnsaved.register(form,()=>!committed&&changes().length>0,()=>{committed=true;});
        form.addEventListener('submit',event=>{if(!editWizard.validate()){event.preventDefault();event.stopImmediatePropagation();}},true);
        form.addEventListener('input',()=>{for(const key of checked){const issues=errors(key,true);buttons[key].classList.toggle('has-errors',!!issues.length);buttons[key].querySelector('.edit-step-status').textContent=issues.length?' !':' ✓';}if(errorStep&&!errors(errorStep).length){notice(null,null);errorStep=null;}if(active==='pricing'&&!errorStep){const below=Number(defaultFields.sellingPrice?.value)<Number(defaultFields.purchasePrice?.value)||cards.some(entry=>Number(entry.fields.sellingPrice.value)<Number(entry.fields.purchasePrice.value));if(below){notice('warning','Current selling price is below the default purchase cost. Please verify the values.');marginWarning=true;}else if(marginWarning){notice(null,null);marginWarning=false;}}if(active==='media')renderReview();});
        form.addEventListener('change',()=>{if(active==='media')renderReview();});
        tabs.onkeydown=event=>{const index=keys.indexOf(active);if(['ArrowRight','ArrowLeft','Home','End'].includes(event.key)){event.preventDefault();const target=event.key==='Home'?keys[0]:event.key==='End'?keys[3]:keys[Math.max(0,Math.min(3,index+(event.key==='ArrowRight'?1:-1)))];navigate(target);buttons[active].focus();}};
        supplierContext=window.StockFlowSupplierNavigation?.resolve(record,saved);
        if(supplierContext){const entry=!supplierContext.invalid&&cards.find(card=>card.record.variantId===supplierContext.variant.variantId);if(!entry||access.edit!=='true'||access.suppliers!=='true'||entry.priceSection?.disabled||!entry.supplierIdentifiers){supplierNotice('The requested variant or supplier control is unavailable in this product and authorized scope. No settings have changed.');supplierContext=null;}else{try{supplierTarget=await entry.supplierIdentifiers.prepareMappingFocus();entry.priceSection.querySelector('details').open=true;const hint=node('p','Configure supplier for this variant: '+entry.record.sku+'. Enter its supplier item code, then review and save Product Edit. Product-level Preferred Supplier does not create this association.','supplier-navigation-hint');hint.setAttribute('role','status');entry.supplierHost.prepend(hint);}catch(error){supplierNotice(error.message);supplierContext=null;}}}
        baseline=snapshot();for(const input of form.querySelectorAll('select'))originalCaptions[input.id]=input.selectedOptions[0]?.textContent;select(supplierTarget?'pricing':'basic');
        if(supplierTarget){const launcher=document.querySelector('.sf-help-open');if(launcher){const dock=node('div',null,'supplier-navigation-dock');form.after(dock);dock.append(launcher);}await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));if(!supplierTarget.matches(':disabled')){supplierTarget.classList.add('supplier-navigation-target');supplierTarget.scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'auto':'smooth',block:'center'});supplierTarget.focus({preventScroll:true});const clear=()=>supplierTarget.classList.remove('supplier-navigation-target');supplierTarget.addEventListener('pointerdown',clear,{once:true});supplierTarget.addEventListener('keydown',clear,{once:true});supplierTarget.addEventListener('blur',clear,{once:true});}}

        return editWizard;
    }
    function init(value) {
        controls = value;
        access = document.getElementById('product-registration-access').dataset;
        value.taxPercentage.addEventListener('input', syncDefaults);
        value.warrantyMonths.addEventListener('input', syncDefaults);
        panel = document.createElement('section');
        panel.id = 'product-configurations';
        panel.className = 'col-12';
        panel.hidden = true;
        const heading = document.createElement('h3');
        heading.className = 'registration-section mb-3';
        list = document.createElement('div');
        add = document.createElement('button');
        add.type = 'button';
        add.className = 'btn btn-outline-primary';
        add.textContent = 'Add size / model';
        add.onclick = async () => {
            if (cards.length >= 30) {
                return;
            }
            add.disabled=true;
            try{
                let sku='';
                if(product?.parentProductCode){
                    // Reuse the operation on retry if allocation succeeded but its response was lost.
                    pendingVariantReservation ??= {businessProfileCode:document.body.dataset.businessProfile,categoryId:product.categoryId,requestId:crypto.randomUUID(),operationId:crypto.randomUUID(),parentCode:product.parentProductCode,productId:product.productId,count:1};
                    const response=await fetch('/api/products/'+product.productId+'/identifier-reservations',{
                        method:'PUT',credentials:'same-origin',
                        headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''},
                        body:JSON.stringify(pendingVariantReservation)
                    });
                    const data=await response.json().catch(()=>null);
                    if(!response.ok)throw new Error(data?.message||data?.title||'Unable to reserve a variant SKU. Please try again.');
                    sku=data?.codes?.[0];
                    if(!sku)throw new Error('No variant SKU was returned. Please try again.');
                }
                card({variantName:'',sku});
                pendingVariantReservation=null;
                StockFlowNotify.dismiss('variant-reservation');
            }catch(error){StockFlowNotify.show('error',error.message,{key:'variant-reservation'});}finally{add.disabled=false;}

        };
        panel.append(heading, list, add);
        document.getElementById('form-fields').append(panel);
    }
    return {
        init,
        open,
        refresh,
        wizard,
        async confirmEdit(body){if(editWizard)applyDefaults(body);return !editWizard||await editWizard.beforeSave(body);},
        initialFocus(){return editWizard?.initialFocus?.();},
        saved(){editWizard?.saved();},
        saveError(error){editWizard?.conflict(error);},
        status(active){for(const entry of cards){entry.wrap.disabled=!active||(entry.record.variantId?access.edit:access.create)!=='true'||(entry.pendingActive??entry.record.isActive)===false;if(entry.priceSection)entry.priceSection.disabled=entry.wrap.disabled;}const defaults=defaultFields.purchasePrice?.closest('fieldset');if(defaults)defaults.disabled=!active||access.edit!=='true';},
        refreshEdit(){editWizard?.review();},
        beforeSave
    };
})();
