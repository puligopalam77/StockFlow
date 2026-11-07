// Validate and save product details before adding the product to a receipt.
window.ProductRegistrationForm = (() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const enhanced=document.body.dataset.catalogRegistrationEnhancements==='true';
    let busy = false;
    let saved = null;
    let onAdded;
    let validationShown = false;
    const validatedSteps=new Set(),externalErrors=new Map();let alertState=null,basicChecked="",feedbackSequence=0,renderedAlert="";
    let specifications;
    let descriptiveDetails;
    let supplierIdentifiers;
    let productImages;
    let combinations;
    let categoryDefinitions=[];
    let categoryPicker;
    let requestId;
    let simpleSkuEdited=false;
    let reservedCategory='',reservationPending=null;const variantOperations=new Map();
    function defaultSku(){return getElement('quick-sku').value.trim();}
    function syncDefaultSku(){const input=getElement('quick-default-sku');if(input&&!simpleSkuEdited){input.value=defaultSku();if(input.classList.contains('is-invalid'))validateField(input);}}
    async function ensureProductCode(force=false){
        const category=getElement('quick-category').value,input=getElement('quick-sku');
        if(!category){const error=new Error('Choose a Parent Category or Subcategory before generating a Product Code.');error.prerequisite=true;throw error;}
        if(categoryDefinitions.some(c=>String(c.parentCategoryId)===category)){const error=new Error('Choose a subcategory before generating a Product Code.');error.prerequisite=true;throw error;}
        if(reservedCategory===category&&input.value.trim()&&!force)return;
        if(reservationPending)return reservationPending;
        const originalRequest=requestId,originalValue=input.value;
        const manual=input.dataset.generated!=='true'&&input.value.trim()?input.value.trim():null;
        const operation=(async()=>{const response=await requestJson('/api/products/identifier-reservations',{method:'POST',body:JSON.stringify({businessProfileCode:document.body.dataset.businessProfile,categoryId:Number(category),requestId:originalRequest,reserveParent:true,parentCode:manual})});
            if(requestId!==originalRequest||getElement('quick-category').value!==category||input.value!==originalValue)throw new Error('Registration details changed. Generate the code again.');
            input.value=response.code;input.dataset.generated=String(!manual);reservedCategory=category;if(input.classList.contains('is-invalid'))validateField(input);syncDefaultSku();combinations?.refresh(getElement('quick-has-variants').checked);refreshValidation();
        })();reservationPending=operation;getElement('quick-generate-sku').disabled=true;
        try{await operation;}finally{reservationPending=null;getElement('quick-generate-sku').disabled=false;}
    }
    async function reserveVariantCodes(count,key){
        await ensureProductCode();const parentCode=getElement('quick-sku').value.trim();
        const cacheKey=parentCode+'|'+key;if(!variantOperations.has(cacheKey))variantOperations.set(cacheKey,crypto.randomUUID());
        return (await requestJson('/api/products/identifier-reservations',{method:'POST',body:JSON.stringify({businessProfileCode:document.body.dataset.businessProfile,categoryId:Number(getElement('quick-category').value),requestId,parentCode,operationId:variantOperations.get(cacheKey),count})})).codes;
    }
    function chooseSavedSku(rows){return new Promise(resolve=>{const dialog=document.createElement('dialog');dialog.className='purchase-registration';const form=document.createElement('form');const label=document.createElement('label');label.className='form-label';label.textContent='Choose the exact SKU to add to this receipt';const select=document.createElement('select');select.className='form-select';select.required=true;select.add(new Option('Select variant / SKU',''));for(const row of rows)select.add(new Option(row.variantName+' / '+row.sku,row.variantId));label.append(select);const add=document.createElement('button');add.className='btn btn-primary mt-3';add.textContent='Add selected SKU';const cancel=document.createElement('button');cancel.type='button';cancel.className='btn btn-outline-secondary mt-3 ms-2';cancel.textContent='Cancel';cancel.onclick=()=>dialog.close();form.append(label,add,cancel);dialog.append(form);document.body.append(dialog);let selected=null;form.onsubmit=event=>{event.preventDefault();if(!select.reportValidity())return;selected=rows.find(row=>row.variantId===Number(select.value));dialog.close();};dialog.addEventListener('close',()=>{dialog.remove();resolve(selected);});dialog.showModal();select.focus();});}
    // Use the same selection primitive as addresses, retaining authorized options and IDs.
    function searchable(select,items,label,value,addAction){
        select.productSearchCleanup?.();
        const prompt=select.options[0]?.textContent||'Select '+label;
        const isBrand=label==='Brand (Optional)';
        const api=StockFlowSelect.create(select,{button:true,label,notifyReselect:true,
            hostClass:'product-category-select',triggerClass:'product-category-select-toggle',triggerId:select.id+'-toggle',
            values:()=>[{value:'',label:prompt},...items.map(item=>({value:value(item),label:isBrand?item.brandName:(item.categoryPath||item.categoryName),disabled:item.isActive===false&&String(value(item))!==select.value}))],
            addAction,addLabel:'Add New Brand',addId:'quick-add-brand'});
        select.productSearchCleanup=()=>api.destroy();
    }
    function bindProductBrandPicker(){const brand=getElement('quick-brand');if(!brand||!enhanced||brand.dataset.integratedBrandPicker)return;brand.dataset.integratedBrandPicker='true';brand.addEventListener('brand-options-updated',event=>{const caption=brand.closest('.col-md-6').querySelector('label');caption.textContent='Brand (Optional)';searchable(brand,event.detail,'Brand (Optional)',item=>item.brandId,getElement('purchase-registration').dataset.canBrandCreate==='true'?()=>quickCreate('Brand'):null);});}
    function categoryCascade(select,items,locked){
        const host=document.createElement('div');host.className='row g-3';host.id='edit-category-cascade';select.closest('.col-md-6').before(host);select.closest('.col-md-6').hidden=true;
        const saved=items.find(item=>String(item.categoryId)===select.value);let parent=saved?.parentCategoryId||saved?.categoryId||null,child=saved?.parentCategoryId?saved.categoryId:null;
        function render(){host.replaceChildren();const roots=items.filter(item=>!item.parentCategoryId),children=items.filter(item=>String(item.parentCategoryId)===String(parent));
            for(const [level,label,choices,value,prompt] of [[0,'Parent Category',roots,parent,'Select parent category'],[1,'Subcategory',children,child,parent?(children.length?'Select subcategory':'No subcategories'):'Select parent category first']]){
                const group=document.createElement('div');group.className='col-md-6 product-category-level';const caption=document.createElement('label');caption.className='form-label';caption.textContent=label+(level===0||children.length?' *':'');
                const input=document.createElement('select');input.id='edit-category-level-'+level;input.className='form-select';input.required=level===0||children.length>0;input.disabled=locked||(level===1&&!children.length);input.add(new Option(prompt,''));for(const item of choices)input.add(new Option(item.categoryName,item.categoryId));input.value=String(value||'');group.append(caption,input);host.append(group);searchable(input,choices,label,item=>item.categoryId);
                input.onchange=()=>{if(level===0){parent=Number(input.value)||null;child=null;}else child=Number(input.value)||null;select.value=String(child||parent||'');select.dispatchEvent(new Event('change',{bubbles:true}));render();};
            }
        }render();return host;
    }
    async function refreshCreatedCategory(created){
        if(created.isActive===false){showNotice('warning','Category saved as inactive. Activate it in Category Management before selecting it for a product.',null,activeTab);return false;}
        const oldBrand=getElement('quick-brand')?.value;categoryDefinitions=await requestJson('/api/categories?activeOnly=true');const final=getElement('quick-category');final.replaceChildren(new Option('Choose category',''),...categoryDefinitions.map(c=>new Option(c.categoryPath||c.categoryName,c.categoryId)));final.value=String(created.categoryId);final.dispatchEvent(new Event('change',{bubbles:true}));categoryPicker=cascadingCategories(categoryDefinitions);if(getElement('quick-brand')){const brands=await requestJson('/api/brands?activeOnly=true');const previous=brands.find(brand=>String(brand.brandId)===oldBrand);if(previous){getElement('quick-brand').add(new Option(previous.brandName,previous.brandId));getElement('quick-brand').value=oldBrand;}BrandCategories.bind(final,getElement('quick-brand'),brands);}
    }
    // Embed the existing Category Management page, including its shared form, validation and API.
    function quickCreateCategory(kind,parentId){
        const opener=document.activeElement,dialog=document.createElement('dialog');dialog.className='purchase-registration category-form-popup';dialog.setAttribute('aria-labelledby','category-popup-title');
        const header=document.createElement('header'),title=document.createElement('h2');title.id='category-popup-title';title.className='h5';title.textContent='Add '+kind;
        const close=document.createElement('button');close.type='button';close.className='btn btn-sm';close.textContent='\u00d7';close.setAttribute('aria-label','Close category registration');header.append(title,close);
        const message=document.createElement('p');message.className='text-danger small';message.setAttribute('role','alert');message.hidden=true;
        const retry=document.createElement('button');retry.type='button';retry.className='btn btn-sm';retry.textContent='Retry refreshing categories';retry.hidden=true;
        const frame=document.createElement('iframe');frame.title=title.textContent;frame.src='/Categories/Create?'+new URLSearchParams({kind:kind==='Subcategory'?'subcategory':'parent',categoryPopup:'1',...(parentId?{parentId}:{} )});
        dialog.append(header,message,retry,frame);document.body.append(dialog);let refreshing=false,savedCategory=null;
        const pending=()=>refreshing||!!frame.contentDocument?.querySelector('#category-save:disabled');
        const dismiss=async()=>{if(pending())return;const changed=!savedCategory&&frame.contentWindow?.CategoryRegistration?.hasChanges();if(changed&&!await StockFlowDialog.confirm('Discard unsaved category changes?'))return;dialog.close();};
        const complete=async()=>{if(refreshing)return;refreshing=true;close.disabled=true;retry.disabled=true;try{const applied=await refreshCreatedCategory(savedCategory);dialog.close();if(applied!==false)showNotice('success',kind+' added successfully.',null,activeTab);}catch(error){message.textContent='Category saved, but the product options could not refresh. '+error.message;message.hidden=false;retry.hidden=false;}finally{refreshing=false;close.disabled=false;retry.disabled=false;}};
        const receive=event=>{if(event.origin!==location.origin||event.source!==frame.contentWindow)return;if(event.data?.type==='stockflow-category-cancel')dismiss();if(event.data?.type==='stockflow-category-created'&&Number.isInteger(event.data.category?.categoryId)&&!savedCategory){savedCategory=event.data.category;complete();}};
        window.addEventListener('message',receive);close.onclick=dismiss;retry.onclick=complete;dialog.addEventListener('cancel',event=>{event.preventDefault();dismiss();});dialog.addEventListener('close',()=>{window.removeEventListener('message',receive);dialog.remove();if(opener?.isConnected)opener.focus();});
        frame.addEventListener('load',()=>{const doc=frame.contentDocument;if(!doc?.querySelector('#master-data[data-module="categories"]')){message.textContent='Unable to load category registration. Close this popup and try again.';message.hidden=false;}doc?.addEventListener('keydown',event=>{if(event.key==='Escape'&&!doc.querySelector('details[open]')){event.preventDefault();dismiss();}});});dialog.showModal();close.focus();
    }
    async function quickCreate(kind,parentId){
        if(kind==='Parent Category'||kind==='Subcategory'){quickCreateCategory(kind,parentId);return;}
        const dialog=document.createElement('dialog');dialog.className='purchase-registration';const form=document.createElement('form');form.noValidate=true;const title=document.createElement('h2');title.className='h5';title.textContent='Add '+kind;const label=document.createElement('label');label.className='form-label';label.textContent=kind+' name *';const name=document.createElement('input');name.className='form-control';name.required=true;name.minLength=2;name.maxLength=kind==='Supplier'?150:100;label.append(name);const message=document.createElement('p');message.className='text-danger small';message.setAttribute('role','alert');const footer=document.createElement('div');footer.className='d-flex gap-2 mt-3';const save=document.createElement('button');save.className='btn btn-primary';save.textContent='Save '+kind;const cancel=document.createElement('button');cancel.className='btn btn-outline-secondary';cancel.type='button';cancel.textContent='Cancel';cancel.onclick=()=>dialog.close();footer.append(cancel,save);form.append(title,label,message,footer);dialog.append(form);document.body.append(dialog);dialog.addEventListener('close',()=>dialog.remove());dialog.showModal();name.focus();
        form.onsubmit=async event=>{event.preventDefault();if(!name.reportValidity())return;save.disabled=true;try{let url,body;if(kind==='Brand'){url='/api/brands';body={brandName:name.value.trim(),categoryIds:[],isActive:true};}else if(kind==='Supplier'){url='/api/suppliers';body={supplierName:name.value.trim(),countryCode:'IN'};}else{url='/api/categories';body={categoryName:name.value.trim(),parentCategoryId:parentId||null,isActive:true};}const created=await requestJson(url,{method:'POST',body:JSON.stringify(body)});
                if(kind==='Brand'){const search=getElement('quick-brand-search');if(search)search.value='';const brands=await requestJson('/api/brands?activeOnly=true');const brand=getElement('quick-brand');brand.add(new Option(created.brandName,created.brandId));brand.value=String(created.brandId);BrandCategories.bind(getElement('quick-category'),brand,brands);brand.dispatchEvent(new Event('change',{bubbles:true}));}
                else if(kind==='Supplier'){await supplierIdentifiers?.refresh?.(created.supplierId);}
                
                dialog.close();showNotice('success',kind+' added successfully.',null,activeTab);
            }catch(error){message.textContent=error.message;}finally{save.disabled=false;}};
    }
    const warrantySupported=electricalProfile=>electricalProfile||document.body.dataset.businessProfile==='BIKE_ACCESSORIES';
    function cascadingCategories(records){
        const final=getElement('quick-category'),legacy=getElement('quick-category-dropdown').closest('.col-md-6');legacy.hidden=true;
        let host=getElement('quick-category-cascades');if(!host){host=document.createElement('div');host.id='quick-category-cascades';host.className='col-12 product-category-cascades';legacy.before(host);}
        const byId=new Map(records.filter(c=>c.isActive!==false&&c.isEffectivelyActive!==false).map(c=>[c.categoryId,c])),roots=[...byId.values()].filter(c=>!c.parentCategoryId);
        let parentId=null,childId=null;
        function sync(){const selected=byId.get(Number(final.value));parentId=selected?(selected.parentCategoryId||selected.categoryId):null;childId=selected?.parentCategoryId?selected.categoryId:null;render();}
        function change(){const old=final.value;final.value=String(childId||parentId||'');const proposed=final.value;final.dispatchEvent(new Event('change',{bubbles:true}));if(final.value!==proposed){final.value=old;sync();}else render();}
        function render(){host.replaceChildren();const parent=byId.get(parentId),subs=parent&&!parent.parentCategoryId?[...byId.values()].filter(c=>c.parentCategoryId===parentId):[];
            for(const [index,title,items,value,prompt] of [[0,'Parent Category *',roots,parentId,'Select parent category'],[1,'Subcategory'+(subs.length?' *':''),subs,childId,parentId?(subs.length?'Select subcategory':'No subcategories'):'Select parent category first']]){const box=document.createElement('div');box.className='product-category-level';const label=document.createElement('label');label.className='form-label';label.textContent=title;const select=document.createElement('select');select.className='form-select';select.id='quick-category-level-'+index;label.htmlFor=select.id;select.required=index===0||subs.length>0;select.disabled=index===1&&!subs.length;select.add(new Option(prompt,''));const visible=items.slice(0,50),selected=items.find(item=>item.categoryId===value);if(selected&&!visible.includes(selected))visible.push(selected);for(const c of visible)select.add(new Option(c.categoryName,c.categoryId));select.value=String(value||'');select.onchange=()=>{if(index===0){parentId=Number(select.value)||null;childId=null;}else childId=Number(select.value)||null;change();};box.append(label,select);host.append(box);searchable(select,items,index?'Subcategory':'Parent Category',c=>c.categoryId);
                if(getElement('purchase-registration').dataset.canCategoryCreate==='true'){const add=document.createElement('button');add.type='button';add.className='btn btn-outline-secondary btn-sm mt-2';add.textContent=index?'+ Add Subcategory':'+ Add Parent Category';add.disabled=index===1&&!parentId;add.onclick=()=>quickCreate(index?'Subcategory':'Parent Category',index?parentId:null);box.append(add);}
                if(validatedSteps.has('basic'))validateField(select);
            }

        }
        sync();return {sync,focus(){host.querySelector('select:invalid')?.focus();}};
    }

    const categoryPolicy=()=>categoryDefinitions.find(c=>c.categoryId===Number(getElement('quick-category').value))?.registrationDefaults;
    const autoSkuAllowed=()=>categoryPolicy()?.generateSkuAutomatically!==false;
    function applyCategoryDefaults(){
        const policy=categoryPolicy();getElement('quick-unit').disabled=false;getElement('quick-tracking').disabled=false;getElement('quick-has-variants').disabled=false;
        for(const id of ['quick-warranty','quick-provider'])getElement(id).closest('.col-md-6').hidden=!warrantySupported(electrical)||policy?.enableWarranty===false&&policy?.allowProductOverride===false;
        if(getElement('quick-generate-sku'))getElement('quick-generate-sku').disabled=!autoSkuAllowed();
        if(!policy)return;
        getElement('quick-unit').value=policy.stockUnit;getElement('quick-stock-unit-summary').value=getElement('quick-unit').selectedOptions[0]?.textContent||'';
        getElement('quick-tracking').value=policy.trackingMethod;getElement('quick-reorder').value=policy.reorderLevel||0;
        getElement('quick-unit').disabled=getElement('quick-tracking').disabled=policy.allowProductOverride===false;
        if(policy.enableWarranty===false&&policy.allowProductOverride===false)getElement('quick-warranty').value='0';
        const axes=window.CatalogAttributes?.fields(getElement('quick-category').value).filter(f=>f.variantAxis)||[];
        getElement('quick-has-variants').checked=axes.length>0;getElement('quick-has-variants').disabled=policy.allowProductOverride===false&&axes.length===0;
        if(!autoSkuAllowed()&&getElement('quick-sku').dataset.generated==='true'){getElement('quick-sku').value='';delete getElement('quick-sku').dataset.generated;}
        
        options();
    }
    let requestJson;
    let catalogOnly = false;
    const electrical = !document.body.dataset.businessProfile || document.body.dataset.businessProfile === 'ELECTRICAL';
    let activeTab = 'basic';
    let previousCategory = '';
    let navigating = false,submitting=false;
    const tabButtons = () => [...document.querySelectorAll('[data-registration-tab]')].filter(button => !button.hidden);
    function updateTabs() {
        const compatibility = getElement('quick-compatibility-fields');
        getElement('registration-tab-compatibility').hidden = true;
        getElement('registration-panel-compatibility').hidden = !compatibility.querySelector('[data-attribute-code]');
        if (getElement('registration-tab-' + activeTab).hidden) selectTab('variants');
        for (const button of tabButtons()) {
            const issues=stepIssues(button.dataset.registrationTab);
            const errors=validatedSteps.has(button.dataset.registrationTab)&&issues.length>0;
            button.classList.toggle('has-errors',errors);
            const complete=validatedSteps.has(button.dataset.registrationTab)&&issues.length===0&&!getElement('purchase-registration-fields').disabled;
            button.querySelector('.registration-tab-status').textContent=errors?' !':complete?' \u2713':'';
            button.setAttribute('aria-label', button.querySelector('.registration-tab-label').textContent + (errors ? ' — contains errors' : ''));
        }
        const buttons = tabButtons(), index = buttons.findIndex(button => button.dataset.registrationTab === activeTab);
        getElement('registration-back').hidden = false;
        getElement('registration-back').textContent = index === 0 ? 'Back' : 'Previous';
        getElement('registration-next').hidden = index === buttons.length - 1;
        getElement('registration-next').textContent = 'Next';
        getElement('purchase-registration-save').hidden = index !== buttons.length - 1;
        if (!saved) getElement('purchase-registration-save').textContent = catalogOnly?'Save Product':'Save & Add to Receipt';
    }
    function selectTab(key, focus = false) {
        StockFlowSelect.closeAll();
        activeTab = key;
        for (const button of document.querySelectorAll('[data-registration-tab]')) {
            if(button.dataset.registrationTab==='compatibility')continue;
            const selected = button.dataset.registrationTab === key;
            button.setAttribute('aria-selected', String(selected));button.tabIndex = selected ? 0 : -1;
            getElement(button.getAttribute('aria-controls')).hidden = !selected;
            if (selected && focus) button.focus();
        }
        getElement('quick-category-dropdown').open = false;
        getElement('purchase-registration').querySelectorAll('.variant-option-picker[open]').forEach(picker=>picker.open=false);
        getElement('purchase-registration').querySelector('.registration-content').scrollTop = 0;
        renderAlert();if(key==='media')renderMediaDetails();
    }
    function renderMediaDetails(){
        productImages?.setVariantOptions?.(combinations?.active()?[...getElement('quick-combinations').querySelectorAll('.variant-row-identity')].map(node=>node.textContent):[]);
        let review=getElement('quick-final-review');
        if(!review){review=document.createElement('details');review.id='quick-final-review';review.className='product-final-review';const summary=document.createElement('summary'),label=document.createElement('span'),title=document.createElement('strong'),subtitle=document.createElement('small');title.textContent='Review Product';subtitle.textContent='Check your product details before saving.';label.append(title,subtitle);const action=document.createElement('span');action.className='product-review-toggle';action.textContent='Show Review';summary.append(label,action);const body=document.createElement('div');body.id='product-review-body';review.append(summary,body);review.addEventListener('toggle',()=>{action.textContent=review.open?'Hide Review':'Show Review';if(review.open)renderProductReview();});getElement('registration-panel-media').append(review);}
        if(review.open)renderProductReview();
    }
    function renderProductReview(){
        const body=getElement('product-review-body');if(!body)return;body.replaceChildren();
        const text=id=>getElement(id)?.value?.trim()||'',caption=id=>getElement(id)?.selectedOptions?.[0]?.textContent||'',item=(tag,value,cls)=>{const node=document.createElement(tag);if(value)node.textContent=value;if(cls)node.className=cls;return node;};
        const selected=categoryDefinitions.find(c=>c.categoryId===Number(text('quick-category'))),parent=selected?.parentCategoryId?categoryDefinitions.find(c=>c.categoryId===selected.parentCategoryId):selected;
        const rows=combinations?.active()?[...getElement('quick-combinations').querySelectorAll('.variant-preview-row')].filter(row=>row.querySelector('input[type=checkbox]')?.checked):[];
        const skus=rows.length?rows.map(row=>row.querySelector('.variant-identifier')?.value):[text('quick-default-sku')];
        const barcodes=rows.map(row=>row.querySelector('input[aria-label$="manufacturer barcode"]')?.value).filter(Boolean);if(!rows.length&&text('quick-default-barcode'))barcodes.push(text('quick-default-barcode'));
        const photoCount=getElement('purchase-registration').querySelectorAll('.product-gallery-tile').length||getElement('quick-image').files.length||(text('quick-image-url')?1:0);
        const supplierCodes=[...getElement('quick-supplier-identifiers')?.querySelectorAll('input[aria-label="Supplier item code"]')||[],...getElement('quick-combinations')?.querySelectorAll('input[aria-label="Supplier item code"]')||[]].filter(input=>!input.disabled).map(input=>input.value.trim()).filter(Boolean);
        const overrides=[...getElement('registration-panel-pricing').querySelectorAll('.variant-pricing-section input')].filter(input=>!input.disabled&&(input.dataset.priceEdited==='true'||/ (purchase|selling) price$/.test(input.getAttribute('aria-label')||''))&&input.value!=='').map(input=>input.getAttribute('aria-label')+': '+input.value).join('; ');
        const grid=item('div',null,'product-review-grid');
        const groups=[['Product Information',[['Product Name',text('quick-product-name')],['Parent Category',parent?.categoryName],['Subcategory',selected?.parentCategoryId?selected.categoryName:null],['Brand',text('quick-brand')?caption('quick-brand'):'Unbranded'],['Product Model',text('quick-model')],['Stock Unit',caption('quick-unit')],['Tracking Method',caption('quick-tracking')],['Product Status',caption('quick-new-status')]]],['Variants',[['Product Code',text('quick-sku')],['Number of variants',String(skus.filter(Boolean).length)],['SKUs',skus.filter(Boolean).join(', ')],['Variant attributes',rows.map(row=>row.querySelector('.variant-row-identity')?.textContent.trim()).filter(Boolean).join('; ')],['Barcodes',barcodes.join(', ')]]],['Pricing',[['Default Purchase Cost',text('quick-purchase')],['Selling Price',text('quick-sale')],['MRP',Number(text('quick-mrp'))?text('quick-mrp'):null],['GST',text('quick-tax')+'%'],['Entered prices',getElement('quick-tax-inclusive').checked?'Include GST':'Exclude GST'],['Variant overrides',overrides]]],['Inventory & Supplier',[['Stock units per purchase pack',text('quick-pack')],['Reorder level',text('quick-reorder')],['Preferred supplier',text('quick-supplier-identifiers-preferred')?caption('quick-supplier-identifiers-preferred'):null],['Supplier item codes',supplierCodes.join(', ')]]],['Additional Information',[['Warranty',Number(text('quick-warranty'))?text('quick-warranty')+' months / '+caption('quick-provider'):null],['Product photos',String(photoCount)],['Detailed description',text('quick-media-description')?'Provided':'Not provided'],['Product Specifications',descriptiveDetails?.count()?descriptiveDetails.count()+' details added':'Not added (Optional)']]]];
        for(const [heading,values] of groups){const group=item('section'),title=item('h4',heading,'h6'),list=item('dl');for(const [label,value] of values){if(value===null||value===undefined||value==='')continue;list.append(item('dt',label),item('dd',String(value)));}group.append(title,list);grid.append(group);}body.append(grid);
    }
    function categoryCaption() {
        getElement('quick-category-toggle').textContent = getElement('quick-category').selectedOptions[0]?.textContent || 'Choose category';
    }
    const stepOrder=['basic','variants','pricing','media'];
    function eligible(input){
        if(input.disabled||!input.willValidate||input.type==='search')return false;
        if(input.id==='quick-category'&&getElement('quick-category-cascades'))return false;
        if(input.closest('.col-md-6[hidden]')&&!input.closest('.product-descriptive-details'))return false;
        return true;
    }
    function fieldMessage(input){
        if(!eligible(input))return '';
        if(input.validity.customError)return input.validationMessage;
        return window.StockFlowValidation?.fieldMessage(input)||(!input.validity.valid?input.validationMessage:'');
    }
    function stepIssues(key,show=false){
        if(key==='media')descriptiveDetails?.check?.();
        const panel=getElement('registration-panel-'+key),inputs=[...panel.querySelectorAll('input,select,textarea')];
        const issues=inputs.map(input=>({input,message:fieldMessage(input)})).filter(issue=>issue.message);
        for(const [input,error] of externalErrors){if(!input.isConnected||input.value!==error.value)externalErrors.delete(input);else if(stepFor(input)===key)issues.push({input,message:error.message});}
        issues.push(...(combinations?.validation?.(key)||[]));
        if(key==='pricing'){
            issues.push(...(supplierIdentifiers?.issues?.()||[]));
            if(combinations?.active())for(const input of getElement('quick-supplier-identifiers')?.querySelectorAll('[aria-label="Supplier item code"]')||[])if(input.value.trim())issues.push({input,message:'Set separate supplier codes for each variant in Product edit after creating these variants.'});
            if(Number(getElement('quick-mrp').value)>0&&Number(getElement('quick-sale').value)>Number(getElement('quick-mrp').value))issues.push({input:getElement('quick-sale'),message:'Selling price cannot exceed MRP.'});
        }
        const unique=[...new Map(issues.map(issue=>[issue.input,issue])).values()];
        if(show){
            for(const input of panel.querySelectorAll('input,select,textarea,[data-registration-error]'))showFieldError(input,'');
            for(const {input,message} of unique)showFieldError(input,message);
        }
        return unique;
    }
    function focusIssue(input){
        if(!input)return;
        selectTab(stepFor(input));
        let details=input.closest('details');while(details){details.open=true;details=details.parentElement.closest('details');}
        const editor=input.closest('.variant-price-editor');if(editor){editor.hidden=false;const detail=editor.closest('tr');if(detail)detail.hidden=false;}
        const target=input.closest('.product-category-select')?.querySelector('.product-category-select-toggle')||input;
        target.scrollIntoView({block:'center',behavior:'instant'});target.focus({preventScroll:true});
    }
    function stepFor(input){return input.closest('[role=tabpanel]')?.id.replace('registration-panel-','')==='compatibility'?'variants':input.closest('[role=tabpanel]')?.id.replace('registration-panel-','')||activeTab;}
    function validationAlert(key,issues){
        // Field errors and step markers already explain what needs correcting.
        // Do not dismiss or replace operation notifications when validation runs.
        if(alertState?.fields?.length){alertState=null;renderAlert();}
    }
    function validateStep(key){
        validationShown=true;validatedSteps.add(key);const issues=stepIssues(key,true);updateTabs();
        if(!issues.length)return true;
        selectTab(key);validationAlert(key,issues);focusIssue(issues[0].input);return false;
    }
    async function checkBasic(){
        const inputs=[...getElement('registration-panel-basic').querySelectorAll('input,select,textarea')].filter(eligible);
        const fingerprint=()=>JSON.stringify(inputs.map(input=>[input.id,input.value,input.checked]));
        const original=fingerprint();if(enhanced)await ensureProductCode();
        if(basicChecked===original)return;
        const products=await requestJson('/api/products/search?'+new URLSearchParams({activeOnly:false,term:getElement('quick-product-name').value.trim()}));
        if(original!==fingerprint())throw new Error('Details changed. Click Next to check again.');
        const normalize=value=>(value||'').trim().replace(/\s+/g,' ').toLowerCase(),name=getElement('quick-product-name'),sku=getElement('quick-sku');
        if(products.some(product=>normalize(product.productName)===normalize(name.value)&&Number(product.categoryId)===Number(getElement('quick-category').value)&&(Number(product.brandId)||null)===(Number(getElement('quick-brand')?.value)||null)&&normalize(product.model)===normalize(getElement('quick-model').value)))showNotice('warning','A similar product exists.','Review its brand, model and manufacturer part number before saving.','variants');
        if(products.some(product=>normalize(product.parentProductCode)===normalize(sku.value))){const error=new Error('Product code already exists. Enter a different code.');error.fields=[sku];throw error;}
        basicChecked=original;
    }
    async function navigateTo(key,focus=true){
        if(busy||navigating||getElement('purchase-registration-fields').disabled)return;
        const target=stepOrder.indexOf(key),current=stepOrder.indexOf(activeTab);
        if(target<=current){selectTab(key,focus);updateTabs();return;}
        navigating=true;getElement('registration-next').disabled=true;
        try{
            for(const step of stepOrder.slice(0,target)){
                if(!validateStep(step))return;
                if(step==='basic')await checkBasic();
            }
            if(!getElement('purchase-registration').open)return;
            if(alertState?.type==='error')showMessage(null);
            selectTab(key,focus);updateTabs();
        }catch(error){showMessage(error);if(error.fields?.length){for(const input of error.fields)showFieldError(input,error.message);focusIssue(error.fields[0]);}}
        finally{navigating=false;getElement('registration-next').disabled=false;}
    }
    async function nextTab(){const index=stepOrder.indexOf(activeTab);if(stepOrder[index+1])await navigateTo(stepOrder[index+1]);}

    function validateField(input){const message=fieldMessage(input);showFieldError(input,message);return !message;}
    function showFieldError(input, message) {
        if(!input.id)input.id='registration-field-'+(++feedbackSequence);
        input.dataset.registrationError='true';
        const hint=input.closest('.variant-generate-actions')?.nextElementSibling;if(message&&hint?.tagName==='P'&&hint.textContent===message)hint.textContent='';
        const id = input.id + '-error';
        let feedback = getElement(id);
        const picker=input.closest('.product-category-select');
        const control=picker||(input.id==='quick-category'?getElement('quick-category-dropdown'):input.closest('.registration-inline')||input);
        if (!feedback && message) {
            feedback = document.createElement('div');
            feedback.id = id;
            feedback.className = 'invalid-feedback';
            feedback.setAttribute('aria-live','off');
            control.insertAdjacentElement('afterend',feedback);
            input.setAttribute(
                'aria-describedby',
                [input.getAttribute('aria-describedby'), id].filter(Boolean).join(' ')
            );
        }
        if (feedback) {
            if(control.nextElementSibling!==feedback)control.insertAdjacentElement('afterend',feedback);
            feedback.className='invalid-feedback';
            feedback.textContent = message;
            feedback.hidden = !message;
            feedback.classList.toggle('d-block',!!message);
        }
        input.classList.toggle('is-invalid', !!message);
        const categoryToggle=input.closest('.product-category-select')?.querySelector('.product-category-select-toggle');
        if(categoryToggle){categoryToggle.classList.toggle('is-invalid',!!message);categoryToggle.setAttribute('aria-invalid',String(!!message));if(feedback){categoryToggle.setAttribute('aria-describedby',id);feedback.classList.toggle('d-block',!!message);}}
        if(input.id==='quick-category')getElement('quick-category-toggle').classList.toggle('is-invalid',!!message);
        if (message) {
            input.setAttribute('aria-invalid', 'true');
        } else {
            input.removeAttribute('aria-invalid');
        }
    }
    function validateForm(){
        validationShown=true;let first;
        for(const key of stepOrder){validatedSteps.add(key);const issues=stepIssues(key,true);if(issues.length&&!first)first={key,issues};}
        updateTabs();
        if(first){selectTab(first.key);validationAlert(first.key,first.issues);focusIssue(first.issues[0].input);return false;}
        showMessage(null);return true;
    }
    function renderAlert(){
        const box=getElement('purchase-registration-message');box.hidden=!alertState||alertState.step!==activeTab;
        if(box.hidden){box.replaceChildren();renderedAlert='';return;}
        const signature=JSON.stringify([alertState.type,alertState.title,alertState.message,alertState.step,alertState.prerequisite]);if(signature===renderedAlert)return;renderedAlert=signature;box.replaceChildren();
        renderBanner(box,{...alertState,onPrerequisite:()=>{selectTab('basic');updateTabs();getElement('quick-category-level-0')?.focus();},onDismiss:()=>{showMessage(null);getElement('registration-tab-'+activeTab).focus();}});
    }
    function renderBanner(box,state){
        box.hidden=!state;box.replaceChildren();if(!state)return;
        const {type,title,message,dismissible=true}=state;
        box.className='alert registration-alert alert-'+({error:'danger',success:'success',warning:'warning'}[type]);
        box.setAttribute('role',type==='error'?'alert':'status');box.setAttribute('aria-live',type==='error'?'assertive':'polite');box.setAttribute('aria-atomic','true');
        const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'),path=document.createElementNS(svg.namespaceURI,'path');svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('aria-hidden','true');
        path.setAttribute('d',type==='warning'?'M12 3 2 21h20L12 3ZM12 9v5m0 3v1':type==='success'?'M22 11a10 10 0 1 1-6-9M8 11l4 4L22 4':'M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0ZM8 8l8 8M16 8l-8 8');svg.append(path);
        const copy=document.createElement('div'),heading=document.createElement('strong');copy.className='registration-alert-copy';heading.textContent=title;copy.append(heading);
        if(message){const text=document.createElement('p');text.textContent=message;copy.append(text);}
        if(state.prerequisite){const action=document.createElement('button');action.type='button';action.className='btn btn-sm registration-alert-action';action.textContent='Go to Basic Details';action.onclick=state.onPrerequisite;copy.append(action);}
        box.append(svg,copy);
        if(dismissible){const dismiss=document.createElement('button');dismiss.type='button';dismiss.className='registration-alert-dismiss';dismiss.textContent='\u00d7';dismiss.setAttribute('aria-label','Dismiss notification');dismiss.onclick=state.onDismiss||(()=>renderBanner(box,null));box.append(dismiss);}
    }
    function showNotice(type,title,message=null,step=activeTab,fields=[]){alertState={type,title,message,step,fields};renderAlert();}
    function showMessage(error){
        if(!error){StockFlowNotify.dismiss('product-registration-result');alertState=null;renderAlert();return;}
        if(error.fields?.length){
            for(const input of error.fields){externalErrors.set(input,{value:input.value,message:error.message});showFieldError(input,error.message);}
            const step=stepFor(error.fields[0]);validatedSteps.add(step);validationAlert(step,error.fields.map(input=>({input,message:error.message})));return;
        }
        if(!error.prerequisite){StockFlowNotify.show('error',error.message||'Unable to complete this action.',{key:'product-registration-result'});return;}
        showNotice(error.prerequisite?'warning':'error',error.prerequisite?'Select a product category first.':error.message||'Unable to complete this action.',error.prerequisite?error.message:null,activeTab,error.fields||[]);
        if(error.prerequisite){alertState.prerequisite=true;renderAlert();}
    }
    function refreshValidation(){
        for(const key of validatedSteps)stepIssues(key,true);
        updateTabs();
        if(alertState?.prerequisite&&getElement('quick-category').value&&!categoryDefinitions.some(c=>String(c.parentCategoryId)===getElement('quick-category').value))showMessage(null);
    }
    function options() {
        const multiple=getElement('quick-has-variants').checked;
        getElement('quick-default-identity').hidden=multiple;
        getElement('quick-default-sku').disabled=multiple;
        syncDefaultSku();
        getElement('quick-variant-fields').hidden = !getElement('quick-has-variants').checked;
        getElement('quick-variant-name').required = getElement('quick-has-variants').checked;
        if(combinations){combinations.refresh(getElement('quick-has-variants').checked);}
        getElement('quick-variants-empty').hidden=getElement('quick-has-variants').checked||!!getElement('quick-specifications').querySelector('[data-attribute-code]');
        if (validationShown) {
            validateField(getElement('quick-variant-name'));
        }
    }
    async function open(code = '', draft = null, resetSaved = false) {
        if (busy || getElement('purchase-registration')?.open&&!resetSaved) {
            throw new Error('Finish the current product registration first.');
        }
        saved = null;validatedSteps.clear();externalErrors.clear();basicChecked='';
        descriptiveDetails?.destroy();descriptiveDetails=ProductSpecifications.details(getElement('quick-product-details'),{},{blocked:()=>[...(window.CatalogAttributes?.fields(getElement('quick-category').value)||[]).flatMap(field=>[field.code,field.label]),...(combinations?.axes()||[])],onChange:()=>{if(getElement('quick-final-review')?.open)renderProductReview();}});
        requestId=crypto.randomUUID();simpleSkuEdited=false;reservedCategory='';variantOperations.clear();
        selectTab('basic');
        combinations?.destroy();combinations = null;
        getElement('purchase-registration-form').reset();
        if(getElement('quick-final-review'))getElement('quick-final-review').open=false;
        getElement('quick-stock-unit-summary').value='Piece';
        if(getElement('quick-branch-reorder-fields'))getElement('quick-branch-reorder-fields').hidden=true;
        if (!electrical) {
            getElement('quick-product-name').placeholder = 'Enter product name';
            getElement('quick-variant-name').placeholder = 'Size / color / model';
            getElement('quick-tracking').value = 'QUANTITY';
            getElement('quick-warranty').value = '0';
            for (const id of ['quick-warranty','quick-provider']) getElement(id).closest('.col-md-6').hidden = !warrantySupported(electrical);
            getElement('quick-tracking').querySelector('[value="SERIAL"]').disabled = true;
            if(document.body.dataset.businessProfile==='BIKE_ACCESSORIES') {
                getElement('quick-product-name').placeholder='Helmet, head light, side guard or spare part';
                getElement('quick-variant-name').placeholder='Size / color / left or right';
                getElement('quick-sku').placeholder='Unique parent product code';
                document.querySelector('label[for="quick-model"]').textContent=enhanced?'Product model':'Part / model name';
            }
        }
        supplierIdentifiers?.destroy();supplierIdentifiers=null;
        if(getElement('quick-supplier-identifiers')) supplierIdentifiers=SupplierIdentifiers.create(getElement('quick-supplier-identifiers'),[],{showEmptyRow:true,showPreferred:true,onCreate:getElement('purchase-registration').dataset.canSupplierCreate==='true'?()=>quickCreate('Supplier'):null});
        if(getElement('quick-duplicates-message'))getElement('quick-duplicates-message').textContent='';
        specifications?.destroy();
        getElement('quick-specifications').replaceChildren();
        getElement('quick-compatibility-fields').replaceChildren();
        specifications = ProductSpecifications.create(
            getElement('quick-specifications'),
            getElement('quick-category'), {}, {compatibility:getElement('quick-compatibility-fields'),suppressLegacy:true}
        );
        validationShown = false;
        getElement('purchase-registration-form')
            .querySelectorAll('input,select,textarea')
            .forEach((input) => {
                return showFieldError(input, '');
            });
        if (getElement('quick-category-help')) getElement('quick-category-help').textContent='';
        getElement('quick-sku').value = '';
        getElement('quick-sku').dataset.generated=String(enhanced);
        if(enhanced) document.querySelector('label[for=quick-model]').textContent='Product model';
        getElement('purchase-registration').querySelectorAll('details').forEach(detail=>detail.open=false);
        getElement('quick-catalog-source').hidden = !draft;
        getElement('quick-catalog-source').textContent = draft
            ? `Prefilled from ${draft.sourceName}. Verify category, GST, prices, warranty and tracking before saving.`
            : '';
        getElement('purchase-registration-save').textContent = catalogOnly
            ? 'Save product'
            : enhanced ? 'Save & Add to Receipt' : 'Save + Add';
        showMessage(null);
        options();
        getElement('purchase-registration-fields').disabled = true;
        if(!getElement('purchase-registration').open)getElement('purchase-registration').showModal();
        try {
            if(window.CatalogAttributes) await CatalogAttributes.load();
            const requests = [requestJson('/api/categories?activeOnly=true')];
            if (getElement('quick-brand')) {
                requests.push(requestJson('/api/brands?activeOnly=true'));
            }
            const [categories, brands] = await Promise.all(requests);
            categoryDefinitions=categories;
            getElement('quick-category').replaceChildren(
                new Option('Choose category', ''),
                ...categories.map((value) => {
                    return new Option(value.categoryPath || value.categoryName, value.categoryId);
                })
            );
            if (getElement('quick-brand')) {
                getElement('quick-brand').replaceChildren(
                    new Option('None', ''),
                    ...brands.map((value) => {
                        return new Option(value.brandName, value.brandId);
                    })
                );
            }
            if (draft) {
                const set = (id, value) => {
                    const input = getElement(id);
                    if (input && value !== null && value !== undefined) {
                        input.value = value;
                    }
                };
                set('quick-product-name', draft.productName);
                set('quick-category', draft.categoryId);
                set('quick-brand', draft.brandId);
                set('quick-model', draft.model);
                set('quick-hsn', draft.hsnCode);
                set('quick-unit', draft.unitOfMeasure);
                set('quick-tracking', draft.trackingType);
                set('quick-purchase', draft.purchasePrice);
                set('quick-sale', draft.sellingPrice);
                set('quick-mrp', draft.mrp);
                set('quick-tax', draft.taxPercentage);
                set('quick-warranty', draft.warrantyMonths);
                set('quick-provider', draft.warrantyProvider);
                set('quick-supplier-code', draft.supplierProductCode);
            }
            const legacySupplier=getElement('quick-supplier-code');legacySupplier.closest('.col-md-6').hidden=!!supplierIdentifiers&&!legacySupplier.value;legacySupplier.readOnly=!!supplierIdentifiers;
            document.querySelector('label[for="quick-supplier-code"]').textContent=supplierIdentifiers?'Legacy supplier item reference':'Supplier item reference';
            if (!electrical) { getElement('quick-tracking').value = 'QUANTITY'; getElement('quick-warranty').value = '0'; }
            getElement('quick-stock-unit-summary').value=getElement('quick-unit').selectedOptions[0]?.textContent||'';
            specifications.setValues(draft?.attributes || {});
            if(window.CatalogAttributes){
                let host=getElement('quick-combinations');
                if(!host){host=document.createElement('section');host.id='quick-combinations';host.className='col-12';getElement('quick-specifications').parentElement.after(host);}
                host.replaceChildren();
                combinations=CatalogAttributes.combinations(host,getElement('quick-category'),getElement('quick-sku'),active=>{
                    specifications.setAxisMode(active);
                    getElement('purchase-registration').querySelector('.registration-barcode').hidden=true;
                    document.querySelector('label[for="quick-sku"]').textContent='Product Code *';
                    getElement('quick-variant-fields').hidden=active||!getElement('quick-has-variants').checked;
                    getElement('quick-variant-name').required=!active&&getElement('quick-has-variants').checked;
                },{purchase:getElement('quick-purchase'),selling:getElement('quick-sale'),mrp:getElement('quick-mrp'),reorder:getElement('quick-reorder'),warranty:getElement('quick-warranty'),manual:enhanced,reserveCodes:reserveVariantCodes});
            }
            if (getElement('quick-brand')) {
                bindProductBrandPicker();
                BrandCategories.bind(
                    getElement('quick-category'),
                    getElement('quick-brand'),
                    brands || []
                );
            }
            if(!draft)applyCategoryDefaults();
            options();
            previousCategory = getElement('quick-category').value;
            categoryCaption();
            categoryPicker=cascadingCategories(categories);
            syncDefaultSku();

            getElement('purchase-registration-fields').disabled = false;
            updateTabs();
            getElement('quick-product-name').focus();
        } catch (error) {
            showMessage(error);
        }
    }
    function init(callback, settings = {}) {
        if (!getElement('purchase-registration')) {
            return;
        }
        onAdded = callback;
        requestJson =
            settings.api ||
            ((url, options) => {
                return Purchases.api(url, options);
            });
        catalogOnly = settings.catalogOnly === true;
        getElement('quick-barcode-mode').onchange=()=>{getElement('quick-manufacturer-barcode').required=getElement('quick-barcode-mode').value==='MANUFACTURER';};
        let replayCategoryChange=false;
        getElement('quick-category').addEventListener('change', async event => {
            const category=getElement('quick-category');
            const hasCombinations=getElement('quick-combinations').querySelector('tbody tr');
            if(!replayCategoryChange&&hasCombinations&&category.value!==previousCategory){
                event.stopImmediatePropagation();const proposed=category.value;category.value=previousCategory;
                if(await StockFlowDialog.confirm('Changing category clears the current variant combination preview. Common category values are kept if you switch back. Continue?',{title:'Change product category?',confirmText:'Change Category'})){
                    category.value=proposed;replayCategoryChange=true;category.dispatchEvent(new Event('change',{bubbles:true}));replayCategoryChange=false;
                }
                return;
            }
            previousCategory=category.value;
            reservedCategory='';if(getElement('quick-sku').dataset.generated==='true'){getElement('quick-sku').value='';simpleSkuEdited=false;syncDefaultSku();}
            applyCategoryDefaults();
        },true);
        for (const button of document.querySelectorAll('[data-registration-tab]')) {
            button.onclick=()=>navigateTo(button.dataset.registrationTab);
            button.onkeydown = event => {
                if(busy||navigating)return;
                if (!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
                event.preventDefault();const buttons=tabButtons(), index=buttons.indexOf(button);
                const next=event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowRight'?1:-1)+buttons.length)%buttons.length;
                navigateTo(buttons[next].dataset.registrationTab);
            };
        }
        getElement('registration-next').onclick=nextTab;
        getElement('registration-back').onclick=()=>{
            if(busy||navigating)return;
            const buttons=tabButtons(),index=buttons.findIndex(button=>button.dataset.registrationTab===activeTab);
            if(index===0){getElement('purchase-registration').close();return;}
            selectTab(buttons[index-1].dataset.registrationTab,true);updateTabs();
        };
        getElement('quick-category').addEventListener('change',()=>{
            queueMicrotask(()=>{categoryCaption();getElement('quick-category-dropdown').open=false;});
        });
        getElement('quick-category-toggle').addEventListener('click',()=>{
            if(getElement('quick-category-dropdown').open)return;
            const search=getElement('quick-category-search');
            if(search){search.value='';[...getElement('quick-category').options].forEach(option=>option.hidden=false);}
        });
        getElement('quick-category-dropdown').addEventListener('toggle',()=>{
            if(getElement('quick-category-dropdown').open&&document.activeElement===getElement('quick-category-toggle'))getElement('quick-category-search')?.focus();
        });
        document.addEventListener('click',event=>{if(!getElement('quick-category-dropdown').contains(event.target))getElement('quick-category-dropdown').open=false;});
        getElement('quick-category-dropdown').addEventListener('keydown',event=>{if(event.key==='Escape'&&getElement('quick-category-dropdown').open){event.preventDefault();event.stopPropagation();getElement('quick-category-dropdown').open=false;getElement('quick-category-toggle').focus();}});
        getElement('purchase-registration-form').addEventListener('registration-layout-changed',refreshValidation);
        getElement('purchase-registration-close').onclick=()=>{if(!busy)getElement('purchase-registration').close();};
        productImages=ProductImagePreview.bind(getElement('quick-image'), getElement('quick-image-url'),{autoPreview:true});
        
        
        getElement('quick-unit').addEventListener('change',()=>getElement('quick-stock-unit-summary').value=getElement('quick-unit').selectedOptions[0]?.textContent||'');
        const syncPartNumber=()=>{const value=getElement('quick-part-number')?.value;if(value)for(const input of getElement('purchase-registration-form').querySelectorAll('[data-attribute-code="manufacturer_part_number"]'))input.value=value;};
        getElement('quick-part-number')?.addEventListener('input',syncPartNumber);
        getElement('quick-specifications').addEventListener('category-fields-changed',syncPartNumber);
        getElement('quick-reorder-location')?.addEventListener('change',()=>getElement('quick-branch-reorder-fields').hidden=!getElement('quick-reorder-location').checked);
        for (const eventName of ['input', 'change']) {
            getElement('purchase-registration-form').addEventListener(eventName, (event) => {
                if(event.target.matches('input,select,textarea'))refreshValidation();
                updateTabs();if(activeTab==='media'&&getElement('quick-final-review')?.open)renderProductReview();
            });
        }
        getElement('purchase-registration-form').addEventListener('click',()=>queueMicrotask(refreshValidation));
        getElement('purchase-registration-message').addEventListener('click',event=>event.stopPropagation());
        getElement('purchase-registration-form').addEventListener('product-images-changed',()=>{if(getElement('quick-final-review')?.open)renderProductReview();});
        if(enhanced){
            getElement('quick-generate-sku').onclick=async()=>{try{await ensureProductCode(true);showMessage(null);}catch(error){showMessage(error);}};
            getElement('quick-category-search').oninput=event=>{
                const search=event.target.value.trim().toLowerCase();
                [...getElement('quick-category').options].forEach(option=>option.hidden=!!option.value&&!option.textContent.toLowerCase().includes(search));
            };
        }
        getElement('quick-has-variants').addEventListener('change',async()=>{if(!getElement('quick-has-variants').checked&&combinations?.hasDraft()&&!await StockFlowDialog.confirm('Use one default SKU instead of these variants? Your variant draft is kept while this form remains open.')){getElement('quick-has-variants').checked=true;return;}options();});
        getElement('quick-sku').addEventListener('input',()=>{delete getElement('quick-sku').dataset.generated;reservedCategory='';syncDefaultSku();});
        getElement('quick-default-sku').addEventListener('input',()=>simpleSkuEdited=true);
        getElement('quick-specifications').addEventListener('category-fields-changed',options);
        getElement('purchase-registration-cancel').onclick = () => {
            if (!busy) {
                getElement('purchase-registration').close();
            }
        };
        getElement('purchase-registration').addEventListener('cancel', (event) => {
            if (busy) {
                event.preventDefault();
            }
        });
        getElement('purchase-registration-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            if(activeTab!=='media'){await nextTab();return;}
            if (busy || navigating || submitting || (getElement('purchase-registration-fields').disabled && !saved)) {
                return;
            }
            let reopen=false;
            const addAnother=event.submitter?.id==='purchase-registration-another';
            submitting=true;
            if(!saved&&!validateForm()){submitting=false;return;}
            try{if(enhanced)await ensureProductCode();}catch(error){submitting=false;showMessage(error);return;}
            busy = true;submitting=false;getElement('purchase-registration-form').dataset.saving='true';
            getElement('purchase-registration-fields').disabled = true;
            getElement('purchase-registration-save').disabled = true;
            if(getElement('purchase-registration-another'))getElement('purchase-registration-another').disabled=true;
            getElement('purchase-registration-cancel').disabled = true;
            showMessage(null);
            try {
                let attributes;
                try { attributes = specifications.read(); } catch(error) {
                    selectTab('variants');updateTabs();throw error;
                }
                try { attributes=descriptiveDetails.apply(attributes); } catch(error) { selectTab('media');updateTabs();throw error; }
                if(enhanced&&getElement('quick-part-number').value.trim()) attributes.manufacturer_part_number=getElement('quick-part-number').value.trim();
                if (!saved) {
                    const tracked = getElement('quick-tracking').value === 'SERIAL';
                    const tax = Number(getElement('quick-tax').value);
                    const warranty = Number(getElement('quick-warranty').value);
                    const body = {
                        gstTaxRuleId:Number(document.getElementById('quick-gst-rule')?.value)||null,registrationBusinessProfile:document.body.dataset.businessProfile,registrationRequestId:requestId,
                        vehicleFitments:specifications.vehicleFitments?.()||null,
                        registrationDefaults:{catalogPricesIncludeTax:getElement('purchase-registration').dataset.catalogTaxInclusive==='true',variantAxes:combinations?.active()?combinations.axes():[],purchasePrice:Number(getElement('quick-purchase').value),sellingPrice:Number(getElement('quick-sale').value),mrp:Number(getElement('quick-mrp').value||0),packQuantity:Number(getElement('quick-pack').value),reorderLevel:Number(getElement('quick-reorder').value),pricesIncludeTax:getElement('quick-tax-inclusive').checked,preferredSupplierId:supplierIdentifiers?.preferred?.()||null},
                        productName: getElement('quick-product-name').value.trim(),
                        parentProductCode: getElement('quick-sku').value.trim(),
                        detailedDescription: getElement('quick-media-description').value.trim()||null,
                        productStatus: getElement('quick-new-status').value,
                        allowUnassociatedBrand: enhanced||getElement('quick-brand-all-brands')?.checked===true,
                        categoryId: Number(getElement('quick-category').value),
                        brandId: Number(getElement('quick-brand')?.value) || null,
                        model: getElement('quick-model').value.trim() || null,
                        hsnCode: getElement('quick-hsn').value.trim() || null,
                        unitOfMeasure: getElement('quick-unit').value,
                        description: getElement('quick-description').value.trim() || null,
                        productImageUrl: getElement('quick-image-url').value.trim() || null,
                        trackingType: tracked ? 'SERIAL' : 'QUANTITY',
                        hasVariants: getElement('quick-has-variants').checked,
                        taxPercentage: tax,
                        warrantyMonths: warranty,
                        warrantyProvider: warranty > 0 ? getElement('quick-provider').value : null,
                        variants: [
                            {
                                supplierIdentifiers: supplierIdentifiers?.read() || null,
                                variantName: getElement('quick-has-variants').checked
                                    ? getElement('quick-variant-name').value.trim()
                                    : 'DEFAULT',
                                sku: getElement('quick-default-sku').value.trim(),
                                barcodeSource:getElement('quick-default-barcode').value.trim()?'MANUFACTURER':null,
                                primaryBarcode:getElement('quick-default-barcode').value.trim()||null,
                                useDefaultPurchasePrice:true,useDefaultSellingPrice:true,
                                isActive: getElement('quick-new-status').value==='ACTIVE',
                                purchasePrice: Number(getElement('quick-purchase').value),
                                sellingPrice: Number(getElement('quick-sale').value),
                                mrp: Number(getElement('quick-mrp').value || 0),
                                packQuantity: Number(getElement('quick-pack').value),
                                supplierProductCode:
                                    getElement('quick-supplier-code').value.trim() || null,
                                minimumStockLevel: Number(getElement('quick-reorder').value),
                                locationId: enhanced&&getElement('quick-reorder-location').checked?window.StockFlowLocation:null,
                                applyLocationMinimumStock: enhanced&&getElement('quick-reorder-location').checked,
                                locationMinimumStockLevel: enhanced&&getElement('quick-reorder-location').checked?Number(getElement('quick-branch-reorder').value):null,
                                taxPercentage: tax,
                                warrantyMonths: warranty,
                                isSerialTracked: tracked,

                                barcodes: [],
                                attributes
                            }
                        ]
                    };
                    const image = getElement('quick-image').files[0];
                    if(combinations?.active()) {
                        if(supplierIdentifiers?.read().length)throw new Error('Set separate supplier codes for each variant in Product edit after creating these variants.');
                        const base=body.variants[0];
                        try { body.variants=combinations.read().map(item=>({...base,...item,isActive:body.productStatus==='ACTIVE'&&item.isActive,attributes:{...attributes,...item.attributes}})); }
                        catch(error){selectTab('variants');updateTabs();throw error;}
                    }
                    if(productImages?.removed)body.removeProductImage=productImages.removed();
                    if(productImages?.read)body.productImages=await productImages.read();
                    if (image) {
                        if (image.size > 3000000) {
                            throw new Error('Product image must be 3 MB or smaller.');
                        }
                        if (!['image/jpeg', 'image/png', 'image/webp'].includes(image.type)) {
                            throw new Error('Product image must be JPG, PNG or WebP.');
                        }
                        body.productImageName = image.name;
                        body.productImageContentType = image.type;
                        body.productImageData = await new Promise((resolve, reject) => {
                            const reader = new FileReader();
                            reader.onload = () => {
                                return resolve(String(reader.result).split(',')[1]);
                            };
                            reader.onerror = () => {
                                return reject(new Error('Unable to read the product image.'));
                            };
                            reader.readAsDataURL(image);
                        });
                    }
                    const enteredInclusive=getElement('quick-tax-inclusive').checked,catalogInclusive=body.registrationDefaults.catalogPricesIncludeTax,factor=1+tax/100,round=value=>Math.round((value+Number.EPSILON)*100)/100;
                    const purchase=value=>enteredInclusive?round(value/factor):value;
                    const selling=value=>enteredInclusive===catalogInclusive?value:round(enteredInclusive?value/factor:value*factor);
                    body.registrationDefaults.purchasePrice=purchase(body.registrationDefaults.purchasePrice);body.registrationDefaults.sellingPrice=selling(body.registrationDefaults.sellingPrice);
                    body.variants=body.variants.map(variant=>({...variant,purchasePrice:purchase(variant.purchasePrice),sellingPrice:selling(variant.sellingPrice)}));
                    saved = await requestJson('/api/products', {
                        method: 'POST',
                        body: JSON.stringify(body)
                    });
                }
                window.StockFlowUnsaved?.markSaved(getElement('purchase-registration-form'));
                if (catalogOnly) {
                    if(window.StockFlowMasterScreens?.page){location.assign('/Products');return;}
                    await onAdded(saved);
                    getElement('purchase-registration').close();
                    if(addAnother)reopen=true;else getElement('add-record')?.focus();
                    return;
                }
                const rows = await requestJson(
                    '/api/products/variants?' +
                        new URLSearchParams({
                            productId: saved.productId,
                            activeOnly: true
                        })
                );
                const item = rows.length>1 ? await chooseSavedSku(rows) : rows.find((value) => {
                    return (
                        value.sku.toUpperCase() ===
                        getElement('quick-default-sku').value.trim().toUpperCase()
                    );
                });
                if (!item) {
                    throw new Error(
                        'The product is saved but its item configuration could not be loaded.'
                    );
                }
                await onAdded(item, null);
                getElement('purchase-registration').close();
                getElement('productScanner')?.focus();
            } catch (error) {
                if(!saved&&/sku|barcode/i.test(error.message)){const input=/barcode/i.test(error.message)?getElement('quick-combinations').querySelector('input[aria-label$="manufacturer barcode"]')||getElement('quick-default-barcode'):getElement('quick-combinations').querySelector('.variant-identifier')||getElement('quick-default-sku');error.fields=[input];selectTab('variants');focusIssue(input);}
                if (saved) {
                    error.message =
                        'Product registration is saved. ' +
                        (catalogOnly
                            ? 'Retry refreshing the product list. '
                            : 'Retry adding it to this receipt. ') +
                        error.message;
                    getElement('purchase-registration-save').textContent = catalogOnly
                        ? 'Refresh product list'
                        : 'Retry adding saved item';
                }
                showMessage(error);
            } finally {
                busy = false;
                getElement('purchase-registration-form').dataset.saving='false';
                getElement('purchase-registration-fields').disabled = !!saved;
                getElement('purchase-registration-save').disabled = false;
                if(getElement('purchase-registration-another'))getElement('purchase-registration-another').disabled=false;
                getElement('purchase-registration-cancel').disabled = false;
                options();

                if(reopen)await open();
            }
        });
    }
    return {
        open,
        banner:renderBanner,
        brandPicker:(select,items)=>searchable(select,items,'Brand (Optional)',item=>item.brandId),
        categoryCascade,
        field:showFieldError,
        refreshValidation,
        init
    };
})();
