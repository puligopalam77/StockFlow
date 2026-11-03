window.CategoryRegistration = (() => {
    'use strict';
    const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
    let prefixEdited=false,prefixTimer,prefixPending;let controls,record=null,detailsAvailable=false,cleanState='',viewOnly=false;
    const state=()=>JSON.stringify({name:controls.categoryName.value,parent:controls.parentCategoryId.value,description:controls.description.value,status:el('category-status').value,code:controls.categoryCode.value,order:el('category-display-order').value,prefix:el('category-sku-prefix').value});
    function message(text){const target=el('editor-message');target.textContent=text;target.classList.toggle('d-none',!text);target.classList.toggle('alert-danger',!!text);}
    async function suggestPrefix(){
        if(record||prefixEdited)return;
        const name=controls.categoryName.value;
        const pending=(async()=>{try{const response=await fetch('/api/categories/sku-prefix-suggestion?'+new URLSearchParams({name}),{credentials:'same-origin',cache:'no-store'});if(!response.ok)return;const result=await response.json();if(record||prefixEdited||controls.categoryName.value!==name)return;el('category-sku-prefix').disabled=result.available===false;if(result.available)el('category-sku-prefix').value=name.trim().length>=2?result.prefix:'';}catch{ /* Saving still validates against the durable registry. */ }})();prefixPending=pending;await pending;
    }
    function bind(value){
        controls=value;controls.categoryName.addEventListener('input',()=>controls.categoryName.setCustomValidity(''));controls.parentCategoryId.addEventListener('change',()=>controls.categoryName.setCustomValidity(''));
        const description=controls.description.closest('.col-12');description.querySelector('label').textContent='Description (optional)';
        const counter=node('small',null,'text-end');counter.id='category-description-count';controls.description.after(counter);controls.description.addEventListener('input',()=>counter.textContent=controls.description.value.length+'/500');
        const statusGroup=node('div',null,'col-md-6'),statusLabel=node('label','Status','form-label'),status=node('select',null,'form-select');status.id='category-status';statusLabel.htmlFor=status.id;status.append(new Option('Active','true'),new Option('Inactive','false'));statusGroup.append(statusLabel,status);
        const orderGroup=node('div',null,'col-md-6'),orderLabel=node('label','Display Order (optional)','form-label'),order=node('input',null,'form-control');order.id='category-display-order';order.type='number';order.min='0';order.max='99999';order.step='1';order.value='0';orderLabel.htmlFor=order.id;orderGroup.append(orderLabel,order);
        if(controls.isActive)controls.isActive.closest('.col-md-6').hidden=true;
        const parentGroup=controls.parentCategoryId.closest('.col-md-6');parentGroup.hidden=true;
        const preview=el('category-path-preview').closest('section');preview.classList.add('category-registration-path');el('category-level-preview').hidden=true;
        el('form-fields').append(controls.categoryName.closest('.col-md-6'),statusGroup,controls.categoryCode.closest('.col-md-6'),orderGroup,parentGroup,preview,description);
        controls.categoryCode.maxLength=30;controls.categoryCode.pattern='[A-Za-z0-9][A-Za-z0-9_-]{1,29}';controls.categoryCode.addEventListener('input',()=>controls.categoryCode.dataset.edited='true');
        const prefixGroup=node('div',null,'col-md-6'),prefixLabel=node('label','SKU Prefix (optional)','form-label'),prefix=node('input',null,'form-control');prefix.id='category-sku-prefix';prefix.maxLength=8;prefix.pattern='[A-Za-z][A-Za-z0-9]{2,7}';prefixLabel.htmlFor=prefix.id;prefix.placeholder='e.g. BRK';const prefixHelp=node('small','3 to 8 letters or digits. Kept permanently after issuing a code.','text-muted');prefixGroup.append(prefixLabel,prefix,prefixHelp);description.before(prefixGroup);
        prefix.addEventListener('input',()=>{prefixEdited=true;prefix.value=prefix.value.toUpperCase();prefix.setCustomValidity('');});
        controls.categoryName.addEventListener('input',()=>{clearTimeout(prefixTimer);prefixTimer=setTimeout(suggestPrefix,250);});
        el('category-save').classList.replace('btn-primary','category-primary');
    }
    async function open(item){
        viewOnly=false;record=item;prefixEdited=false;el('category-sku-prefix').value=record?.skuPrefix||'';el('category-sku-prefix').readOnly=record?.skuPrefixUsed===true;
        await suggestPrefix();
        const response=await fetch('/api/categories/schema',{credentials:'same-origin',cache:'no-store'});
        if(!response.ok)throw new Error('Unable to load category settings. Reopen the category.');
        detailsAvailable=!!(await response.json()).categoryDetailsAvailable;
        controls.categoryCode.readOnly=true;controls.categoryCode.value=record?.categoryCode||'';controls.categoryCode.placeholder='Auto-generated';
        el('category-display-order').value=record?.displayOrder||0;el('category-display-order').disabled=!detailsAvailable;
        el('category-status').value=String(record?.isActive!==false);el('category-status').querySelector('[value="false"]').disabled=el('master-data').dataset.canDisable!=='true';
        message('');el('editor').querySelector('.category-registration-content').scrollTop=0;
        const sub=new URLSearchParams(location.search).get('kind')==='subcategory',title=window.StockFlowMasterScreens?.mode==='view'?'View Category':record?'Edit Category':sub?'Add Subcategory':'Add Parent Category';
        const heading=document.querySelector('.master-screen-heading h1');if(heading)heading.textContent=title;document.title=title+' - StockFlow';const crumb=el('breadcrumb-current');if(crumb)crumb.textContent=title;
        el('category-save').textContent=record?'Save Category':sub?'Save Subcategory':'Save Parent Category';
        controls.categoryName.closest('.col-md-6').querySelector('label').textContent=(record?'Category Name':sub?'Subcategory Name':'Parent Category Name')+' *';controls.categoryName.placeholder=sub?'e.g. Head Lights':'e.g. Helmets';
        el('editor-help').textContent=record?'Update category details. Existing product relationships are preserved.':sub?'Create a subcategory under an existing parent category.':'Create a top-level category to organize your products. You can add subcategories later.';
        const prefix=el('category-sku-prefix').closest('.col-md-6');const fields=el('form-fields'),name=controls.categoryName.closest('.col-md-6'),code=controls.categoryCode.closest('.col-md-6'),status=el('category-status').closest('.col-md-6'),order=el('category-display-order').closest('.col-md-6'),parent=el('category-parent-picker'),preview=el('category-path-preview').closest('section'),description=controls.description.closest('.col-12');
        for(const item of record||sub?[parent,name,code,prefix,status,order,preview,description]:[name,code,prefix,status,order,parent,preview,description])fields.append(item);
        preview.hidden=!record&&!sub;controls.description.placeholder='Enter a short description about this category...';controls.description.dispatchEvent(new Event('input'));
    }
    async function beforeSave(body){
        clearTimeout(prefixTimer);if(!record&&!prefixEdited)await suggestPrefix();else await prefixPending;
        if(viewOnly||!window.CategoryCascades.validate())return false;
        for(const input of [el('category-sku-prefix'),controls.categoryName,controls.parentCategoryId,controls.description,controls.categoryCode,el('category-display-order')])if(!input.checkValidity()){input.reportValidity();return false;}
        body.isActive=el('category-status').value==='true';body.skuPrefix=el('category-sku-prefix').value.trim().toUpperCase()||null;
        if(detailsAvailable){body.categoryCode=record?.categoryCode||null;body.displayOrder=Number(el('category-display-order').value||0);}
        message('');
        try{
            const response=await fetch('/api/categories',{credentials:'same-origin',cache:'no-store'});
            if(!response.ok)throw new Error('Unable to check existing categories. Please try saving again.');
            const existing=(await response.json()).filter(category=>category.categoryId!==record?.categoryId);
            if(existing.some(category=>(category.parentCategoryId||null)===(body.parentCategoryId||null)&&category.categoryName.normalize('NFKC').trim().replace(/\s+/g,' ').toLowerCase()===body.categoryName.normalize('NFKC').trim().replace(/\s+/g,' ').toLowerCase())){
                message(body.parentCategoryId?'A subcategory with this name already exists under the selected parent.':'A parent category with this name already exists.');controls.categoryName.setCustomValidity(el('editor-message').textContent);controls.categoryName.reportValidity();controls.categoryName.focus();return false;
            }
            if(body.skuPrefix&&existing.some(category=>category.skuPrefix===body.skuPrefix)){message('SKU prefix already exists. Choose another prefix.');el('category-sku-prefix').focus();return false;}
            if(body.categoryCode&&existing.some(category=>category.categoryCode?.trim().toLowerCase()===body.categoryCode.toLowerCase())){
                message('This category code is already registered. Use a different code.');controls.categoryCode.focus();return false;
            }
        }catch(error){message(error.message||'Unable to check existing categories. Please try saving again.');return false;}
        // Basic category edits never replace saved attribute schemas, axes or stock defaults.
        delete body.configuration;
        return true;
    }
    return {bind,open,beforeSave,setViewOnly:()=>{viewOnly=true;},markClean:()=>cleanState=state(),hasChanges:()=>!!cleanState&&state()!==cleanState};
})();
