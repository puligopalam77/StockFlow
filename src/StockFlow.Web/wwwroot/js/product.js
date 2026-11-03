(() => {
    const electrical=!document.body.dataset.businessProfile||document.body.dataset.businessProfile==='ELECTRICAL';
    const bike=document.body.dataset.businessProfile==='BIKE_ACCESSORIES';
    let variantsTooltip;
    let registrationReady = false;
    let imagePreview;
    const listColumns=['productName','parentProductCode','skuSummary','categoryName','brandName','configuredVariantCount'];
    const skuStates=new Map();
    let skuGeneration=0;
    const skuScope=()=>[document.body.dataset.businessProfile,document.querySelector('meta[name="stockflow-location"]')?.content,document.getElementById('product-registration-access')?.dataset.profileVersion].join('|');
    let activeSkuScope=skuScope();
    function clearSkuState(clearCells=false){
        ++skuGeneration;
        for(const state of skuStates.values()){
            state.controller?.abort();
            if(clearCells&&state.cell.isConnected){state.cell.removeAttribute('aria-busy');state.cell.textContent='Reload products for the selected shop.';}
        }
        skuStates.clear();activeSkuScope=skuScope();
    }
    // Shop selection normally reloads the page. Also reject in-flight responses
    // if the current page's scope changes before that navigation completes.
    const scopeObserver=new MutationObserver(()=>{if(activeSkuScope!==skuScope())clearSkuState(true);});
    scopeObserver.observe(document.body,{attributes:true,attributeFilter:['data-business-profile']});
    const locationMeta=document.querySelector('meta[name="stockflow-location"]');
    if(locationMeta)scopeObserver.observe(locationMeta,{attributes:true,attributeFilter:['content']});
    const scopeMetadata=document.getElementById('product-registration-access');
    if(scopeMetadata)scopeObserver.observe(scopeMetadata,{attributes:true,attributeFilter:['data-profile-version']});
    addEventListener('pagehide',()=>clearSkuState());
    addEventListener('pageshow',event=>{
        // Refresh a restored list after visiting Edit Product, retaining the
        // current search/filter controls rather than reusing old SKU caches.
        if(event.persisted)document.getElementById('master-search')?.dispatchEvent(new Event('input',{bubbles:true}));
    });
    function skuRows({records,rows}) {
        clearSkuState();
        const search=document.getElementById('master-search');
        search.placeholder='Search by product name, product code or SKU...';
        const term=search.value.trim().toLowerCase();
        for(let index=0;index<records.length;index++){
            const record=records[index],row=rows[index],cell=row.children[listColumns.indexOf('skuSummary')];
            const configured=record.configuredVariantCount===undefined?(record.hasVariants?null:0):record.configuredVariantCount;
            const variants=row.children[listColumns.indexOf('configuredVariantCount')];
            variants.classList.add('product-list-variant-count');
            variants.textContent=configured===null?'Review variants':configured===0?'No variants':configured+' '+(configured===1?'variant':'variants');
            if(configured===null)variants.title='Saved variant configuration needs review. Open Edit Product to inspect the registered variants.';
            cell.classList.add('product-list-sku');cell.replaceChildren();
            if(configured===0){cell.textContent=typeof record.defaultVariantSku==='string'&&record.defaultVariantSku.trim()?record.defaultVariantSku:'Not assigned';continue;}
            const previews=(record.skuPreviews||[]).filter(item=>typeof item.sku==='string'&&item.sku.trim()).slice(0,1);
            if(!previews.length){cell.textContent='Not assigned';continue;}
            function entry(preview,showName=true){
                const entry=document.createElement('div');entry.className='product-sku-preview';
                const sku=document.createElement('span');sku.className='product-sku-code';sku.textContent=preview.sku;
                if(term&&preview.sku.toLowerCase().includes(term))sku.classList.add('product-sku-match');
                entry.append(sku);
                if(showName&&preview.variantName){const name=document.createElement('small');name.className='product-sku-variant';name.textContent=preview.variantName;entry.append(name);}
                return entry;
            }
            const first=previews[0];cell.append(entry(first,false));
            let remaining=Math.max(0,record.skuCount-1);
            if(!remaining)continue;
            const details=document.createElement('div');details.className='product-sku-expanded';details.id='product-skus-'+record.productId+'-'+skuGeneration;details.hidden=true;
            const status=document.createElement('small');status.className='product-sku-loading';status.setAttribute('role','status');
            const error=document.createElement('small');error.className='product-sku-error';error.setAttribute('role','alert');
            const more=document.createElement('button');more.type='button';more.className='product-sku-more';more.setAttribute('aria-controls',details.id);
            const next=document.createElement('button');next.type='button';next.className='product-sku-more';next.textContent='Load more SKUs';next.setAttribute('aria-label','Load more SKUs for '+record.productName);
            const retry=document.createElement('button');retry.type='button';retry.className='product-sku-more';retry.textContent='Try again';retry.setAttribute('aria-label','Retry loading SKUs for '+record.productName);
            const state={cell,items:[],offset:0,hasMore:false,fetched:false,loading:false,expanded:false,generation:skuGeneration,scope:activeSkuScope};skuStates.set(record.productId,state);
            const key=item=>item.variantId==null?'sku:'+item.sku:'id:'+item.variantId;
            const valid=()=>state.generation===skuGeneration&&state.scope===skuScope()&&cell.isConnected&&search.value.trim().toLowerCase()===term;
            function update(){
                details.hidden=!state.expanded;
                more.textContent=state.expanded?'Show less':'+'+remaining+' more '+(remaining===1?'SKU':'SKUs');
                more.setAttribute('aria-expanded',String(state.expanded));more.setAttribute('aria-label',(state.expanded?'Show fewer SKUs for ':'Show '+remaining+' more SKUs for ')+record.productName);
                more.hidden=!remaining&&!state.expanded;
                next.hidden=!state.expanded||!state.hasMore||!!error.textContent;next.disabled=state.loading;
                retry.hidden=!state.expanded||!error.textContent;retry.disabled=state.loading;
                status.textContent=state.loading?'Loading SKUs…':'';status.hidden=!state.expanded||!state.loading;
                error.hidden=!state.expanded||!error.textContent;
                cell.setAttribute('aria-busy',String(state.loading));
            }
            async function loadNext(){
                if(state.loading||!valid())return;
                state.loading=true;error.textContent='';state.controller=new AbortController();update();
                try{
                    const query=new URLSearchParams({offset:String(state.offset),term});
                    const response=await fetch('/api/products/'+encodeURIComponent(record.productId)+'/skus?'+query,{signal:state.controller.signal,headers:{Accept:'application/json'}});
                    if(!response.ok)throw new Error('Unable to load SKUs.');
                    const page=await response.json();
                    if(!valid())return;
                    if(!Array.isArray(page.items))throw new Error('Invalid SKU response.');
                    const seen=new Set([key(first),...state.items.map(key)]);
                    for(const item of page.items){
                        if(typeof item.sku!=='string'||!item.sku.trim()||seen.has(key(item)))continue;
                        seen.add(key(item));state.items.push(item);details.append(entry(item));
                    }
                    state.offset+=page.items.length;state.hasMore=page.hasMore===true&&page.items.length>0;state.fetched=true;
                    if(!state.hasMore)remaining=state.items.length;
                }catch(e){
                    if(e.name!=='AbortError'&&valid())error.textContent='Could not load additional SKUs. Try again.';
                }finally{state.loading=false;if(valid())update();}
            }
            more.onclick=()=>{state.expanded=!state.expanded;update();if(state.expanded&&!state.fetched)loadNext();};
            next.onclick=loadNext;
            retry.onclick=loadNext;
            cell.append(details,status,error,retry,next,more);update();
        }
    }
    function productStatus(controls){
        if(!controls.productStatus)return;
        const active=controls.productStatus.value==='ACTIVE';
        document.querySelectorAll('#product-configurations fieldset').forEach(field=>{
            if(field.dataset.statusWritable===undefined)field.dataset.statusWritable=String(!field.disabled);
            field.disabled=!active||field.dataset.statusWritable!=='true';
        });
        let note=document.getElementById('product-status-help');
        if(!note){note=document.createElement('small');note.id='product-status-help';note.className='text-muted d-block';controls.productStatus.after(note);}
        note.textContent=active?'':'Saved item configurations and stock history are retained. Reactivate the product to edit item configurations.';
        ProductRegistration.status(active);
    }
    function variantsHelp(controls) {
        window.ProductRegistration?.refresh(controls);
        document.getElementById('has-variants-next-step').hidden = !controls.hasVariants.checked;
    }
    function warranty(controls) {
        const enabled = Number(controls.warrantyMonths.value) > 0;
        controls.warrantyProvider.closest('.col-md-6').hidden = !enabled;
        controls.warrantyProvider.required = enabled;
        document.querySelector('label[for="warrantyProvider"]').textContent = 'Warranty Provider' + (enabled ? ' *' : '');
        controls.warrantyProvider.value = enabled
            ? controls.warrantyProvider.value || 'MANUFACTURER'
            : '';
    }
    StockFlowMaster.start({
        singular: 'Product',
        id: 'productId',
        submitText: 'Save product',
        statusLabel: 'Model is active',
        activeStatus: 'Active',
        inactiveStatus: 'Discontinued',
        fields: [
            {name:'parentProductCode',label:'Product Code',type:'text',maxLength:40,nullable:true},
            {name:'detailedDescription',label:'Detailed description',type:'textarea',maxLength:6000,nullable:true},
            ...(document.body.dataset.catalogRegistrationEnhancements==='true'?[{name:'productStatus',label:'Product status',type:'select',valueType:'string',options:[['ACTIVE','Active'],['INACTIVE','Inactive'],['DISCONTINUED','Discontinued']],default:'ACTIVE'}]:[]),
            {
                name: 'productName',
                label: 'Product name',
                type: 'text',
                required: true,
                minLength: 2,
                maxLength: 150,
                section: 'Product details'
            },
            {
                name: 'categoryId',
                label: 'Product group',
                type: 'select',
                required: true,
                lookup: 'categories',
                value: 'categoryId',
                text: 'categoryPath'
            },
            {
                name: 'brandId',
                label: 'Brand',
                type: 'select',
                lookup: 'brands',
                value: 'brandId',
                text: 'brandName',
                nullable: true
            },
            {
                name: 'model',
                label: bike?'Part / model name':'Model',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'description',
                label: 'Description',
                type: 'textarea',
                maxLength: 2000
            },
            {
                name: 'hsnCode',
                label: 'HSN code (from supplier bill)',
                type: 'text',
                maxLength: 8,
                pattern: '[0-9]{4,8}',
                title: 'Enter 4 to 8 digits for the HSN code.'
            },
            {
                name: 'unitOfMeasure',
                label: 'Sold as',
                type: 'select',
                valueType: 'string',
                required: true,
                default: 'PIECE',
                options: [
                    ['PIECE', 'Piece'],
                    ['BOX', 'Box'],
                    ['PAIR', 'Pair'],
                    ['SET', 'Set']
                ]
            },
            {
                name: 'productImage',
                label: 'Product image',
                type: 'file',
                accept: 'image/jpeg,image/png,image/webp',
                transient: true,
                help: 'JPG, PNG or WebP up to 3 MB. Leave empty while editing to retain the saved image.'
            },
            {
                name: 'productImageUrl',
                label: 'Product image web address (optional)',
                type: 'url',
                maxLength: 500,
                placeholder: 'https://...'
            },
            {
                name: 'trackingType',
                label: 'How do you count this stock?',
                type: 'select',
                valueType: 'string',
                required: true,
                default: 'SERIAL',
                options: [
                    ['QUANTITY', 'By quantity (count the items)'],
                    ['SERIAL', 'By serial number (one number for each item)']
                ],
                help: 'Use serial numbers for batteries and geysers. Use quantity for spare parts without a serial number.',
                section: 'Serial numbers and sizes'
            },
            {
                name: 'hasVariants',
                label: 'Available in different sizes or models',
                type: 'checkbox'
            },
            {
                name: 'taxPercentage',
                label: 'Default GST rate (%)',
                type: 'number',
                required: true,
                min: 0,
                max: 100,
                step: '0.01',
                section: 'GST and warranty'
            },
            {
                name: 'warrantyMonths',
                label: 'Warranty (months)',
                type: 'number',
                required: true,
                min: 0,
                max: 240,
                step: '1'
            },
            {
                name: 'warrantyProvider',
                label: 'Who gives the warranty?',
                type: 'select',
                valueType: 'string',
                options: [
                    ['', 'Select provider'],
                    ['MANUFACTURER', 'Manufacturer'],
                    ['DEALER', 'Dealer'],
                    ['BOTH', 'Both']
                ]
            }
        ],
        onLoaded: skuRows,
        columnLabels: {productName:'Product Name',parentProductCode:'Product Code',skuSummary:'SKU',categoryName:'Category',brandName:'Brand',configuredVariantCount:'Variants'},
        columns: listColumns,
        async onCreate({ api: requestJson, onSaved }) {
            if (document.getElementById('product-registration-access').dataset.create !== 'true') {
                throw new Error(
                    'Product registration requires permission to create product variants.'
                );
            }
            if (!registrationReady) {
                ProductRegistrationForm.init(onSaved, {
                    api: requestJson,
                    catalogOnly: true
                });
                registrationReady = true;
            }
            await ProductRegistrationForm.open();
        },
        onReady(controls) {
            document.getElementById('master-form').dataset.productValidation='true';
            if (document.body.dataset.businessProfile && document.body.dataset.businessProfile !== 'ELECTRICAL') {
                for (const name of ['trackingType','warrantyMonths','warrantyProvider']) if (controls[name]) {
                    controls[name].closest('.col-md-6').hidden = true;
                }
                if (controls.trackingType) controls.trackingType.value = 'QUANTITY';
            }
            controls.productImage.form.setAttribute('data-inline-validation', '');
            imagePreview = ProductImagePreview.bind(controls.productImage, controls.productImageUrl);
            controls.warrantyMonths.addEventListener('input', () => {
                return warranty(controls);
            });
            const checkbox = controls.hasVariants;
            const label = document.querySelector('label[for="hasVariants"]');
            const info = document.createElement('button');
            info.type = 'button';
            info.className = 'registration-info';
            info.textContent = 'i';
            info.setAttribute('aria-label', 'About different sizes or models');
            label.after(info);
            variantsTooltip = new bootstrap.Tooltip(info, {
                title: 'Enable for products available in different sizes, colors, capacities, or other variations.',
                container: checkbox.parentElement,
                trigger: 'hover focus',
                placement: 'top'
            });
            info.addEventListener('keydown', (event) => {
                if (event.key === 'Escape') {
                    variantsTooltip.hide();
                    event.preventDefault();
                    event.stopPropagation();
                }
            });
            const notice = document.createElement('small');
            notice.id = 'has-variants-next-step';
            notice.className = 'text-muted d-block mt-2';
            notice.textContent =
                bike?'Enter each size, color or fitting below. Each variant keeps its own part number / SKU, barcode and prices.':'Enter each capacity, rating or other variation below. Each variation keeps its own SKU, barcode and prices.';
            notice.hidden = true;
            notice.setAttribute('aria-live', 'polite');
            checkbox.after(notice);
            checkbox.setAttribute('aria-describedby', notice.id);
            checkbox.addEventListener('change', () => {
                return variantsHelp(controls);
            });
            window.ProductRegistration.init(controls);
            controls.productStatus?.addEventListener('change',()=>productStatus(controls));
            controls.isActive?.addEventListener('change',()=>ProductRegistration.status(controls.isActive.checked));
        },
        async onOpen(controls, record) {
            const submit=document.querySelector('#master-form [type=submit]');submit.disabled=true;
            if(record&&!controls.categoryId.stockFlowLookupOptions.some(item=>item.categoryId===record.categoryId)){
                const response=await fetch('/api/categories?activeOnly=false',{credentials:'same-origin'});if(!response.ok)throw new Error('Unable to load the saved category. Reload before editing.');const all=await response.json(),saved=all.find(item=>item.categoryId===record.categoryId);if(!saved)throw new Error('The saved category is unavailable in this shop.');for(const category of all.filter(item=>item.categoryId===saved.categoryId||item.categoryId===saved.parentCategoryId))if(!controls.categoryId.stockFlowLookupOptions.some(item=>item.categoryId===category.categoryId))controls.categoryId.stockFlowLookupOptions.push(category);
            }
            if(document.body.dataset.catalogRegistrationEnhancements==='true' && controls.isActive?.closest('.col-md-6')) controls.isActive.closest('.col-md-6').hidden=true;
            imagePreview.setSaved(record?.productImageName ? '/api/products/' + record.productId + '/image' : record?.productImageUrl, record?.productImageName, record?.productImages||[]);
            if(record?.brandId&&!controls.brandId.stockFlowLookupOptions.some(item=>item.brandId===record.brandId))controls.brandId.stockFlowLookupOptions.push({brandId:record.brandId,brandName:record.brandName||'Saved inactive brand',isActive:false});
            BrandCategories.bind(
                controls.categoryId,
                controls.brandId,
                controls.brandId.stockFlowLookupOptions || []
            );
            warranty(controls);
            variantsTooltip.hide();
            await window.ProductRegistration.open(controls, record);
            productStatus(controls);
            variantsHelp(controls);
            if(record&&window.StockFlowMasterScreens?.mode==='edit'){
                if(record.editVersion){const response=await fetch('/api/products/'+record.productId,{credentials:'same-origin'});if(!response.ok)throw new Error('Unable to load the latest product. Reload before editing.');const latest=await response.json();if(latest.editVersion!==record.editVersion)throw new Error('This product changed while loading. Reload it before editing.');for(const key of ['hasInventoryHistory','historyVariantIds','categoryChangeLocked'])if(latest[key]!==undefined)record[key]=latest[key];}
                await ProductRegistration.wizard(record,imagePreview);
            }
            submit.disabled=false;
        },
        onSaved(){ProductRegistration.saved();},
        onSaveError(error){ProductRegistration.saveError(error);},
        async beforeSave(body) {
            if(!await ProductRegistration.confirmEdit(body))return false;
            if(imagePreview.removed)body.removeProductImage=imagePreview.removed();
            if(imagePreview.read)body.productImages=await imagePreview.read();
            if(document.body.dataset.catalogRegistrationEnhancements==='true' && body.productStatus) body.isActive=body.productStatus==='ACTIVE';
            const file = document.getElementById('productImage').files[0];
            if (file) {
                if (file.size > 3000000) {
                    throw new Error('Product image must be 3 MB or smaller.');
                }
                if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
                    throw new Error('Product image must be JPG, PNG or WebP.');
                }
                body.productImageName = file.name;
                body.productImageContentType = file.type;
                body.productImageData = await new Promise((resolve, reject) => {
                    const reader = new FileReader();
                    reader.onload = () => {
                        return resolve(String(reader.result).split(',')[1]);
                    };
                    reader.onerror = () => {
                        return reject(new Error('Unable to read the product image.'));
                    };
                    reader.readAsDataURL(file);
                });
            }
            if(body.isActive===false||body.productStatus&&body.productStatus!=='ACTIVE'){delete body.variants;return true;}
            return window.ProductRegistration.beforeSave(body);
        }
    });
})();
