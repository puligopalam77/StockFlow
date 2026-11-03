(async()=>{
    'use strict';
    const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
    const enhanced=document.body.dataset.catalogRegistrationEnhancements==='true';
    let schema,categories,definitions=[],assignments=[],busy=false;
    const error=e=>{el('attribute-error').textContent=e.message;el('attribute-error').hidden=false;};
    async function api(url,options={}){const r=await fetch(url,{credentials:'same-origin',...options,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''}});const data=await r.json();if(!r.ok)throw new Error(data.message||'Unable to save category settings. Reload and try again.');return data;}
    function input(parent,label,value,type='text') {const wrap=node('div',null,'col-md-6 col-lg-3');const control=node(type==='select'?'select':'input',null,'form-control');if(type!=='select')control.type=type;control.setAttribute('aria-label',label);const caption=node('label',label,'form-label');caption.append(control);wrap.append(caption);parent.append(wrap);control.value=value??'';return control;}
    function checked(parent,label,value){const caption=node('label',null,'me-3');const box=node('input');box.type='checkbox';box.checked=value;caption.append(box,document.createTextNode(' '+label));parent.append(caption);return box;}
    function capture(){
        return {version:schema.version,attributes:definitions.map(d=>({code:d.code.value.trim(),label:d.label.value.trim(),type:d.type.value,options:d.type.value==='SELECT'?d.options.value.split(',').map(v=>v.trim()).filter(Boolean):[],minimum:d.type.value==='NUMBER'&&d.min.value!==''?Number(d.min.value):null,maximum:d.type.value==='NUMBER'&&d.max.value!==''?Number(d.max.value):null})),categories:assignments.map(c=>({categoryId:c.id,fields:c.fields.filter(f=>f.include.checked&&!f.inherited).map(f=>({code:f.code,required:f.required.checked,variantAxis:f.axis.checked,...(enhanced?{searchable:f.searchable.checked,visible:f.visible.checked,defaultValue:f.defaultValue.value||null}:{})}))})).filter(c=>c.fields.length)};
    }
    function definition(a={},locked=false){
        const card=node('fieldset',null,'border rounded p-3 mb-3');card.disabled=locked;const row=node('div',null,'row g-2');card.append(row);
        const d={card};d.code=input(row,'Code *',a.code);d.code.required=true;d.code.pattern='[a-z][a-z0-9_]{0,39}';d.code.maxLength=40;
        d.label=input(row,'Label *',a.label);d.label.required=true;d.label.maxLength=80;
        d.type=input(row,'Type',null,'select');['TEXT','NUMBER','DATE','BOOLEAN','SELECT',...(enhanced?['TEXT_LIST']:[])].forEach(v=>d.type.append(new Option(v,v)));d.type.value=a.type||'TEXT';
        d.options=input(row,'Selection options',(a.options||[]).join(', '));d.options.maxLength=5000;
        d.min=input(row,'Minimum',a.minimum,'number');d.max=input(row,'Maximum',a.maximum,'number');d.min.step=d.max.step='any';
        const sync=()=>{d.options.closest('.col-md-6').hidden=d.type.value!=='SELECT';d.min.closest('.col-md-6').hidden=d.max.closest('.col-md-6').hidden=d.type.value!=='NUMBER';};d.type.onchange=()=>{sync();updateAssignments();};sync();
        d.code.onchange=d.label.onchange=d.options.onchange=updateAssignments;
        const remove=node('button','Remove attribute','btn btn-outline-secondary mt-2');remove.type='button';remove.onclick=()=>{const old=capture();old.attributes=old.attributes.filter((_,i)=>definitions[i]!==d);old.categories.forEach(c=>c.fields=c.fields.filter(f=>f.code!==d.code.value));render(old);};card.append(remove);
        if(locked)card.append(node('p','Used by a registered product. This definition is locked.','small text-muted mt-2'));
        definitions.push(d);el('attribute-definitions').append(card);
    }
    function inheritedRule(doc,category,code){
        if(!schema.inheritanceAvailable)return null;
        const seen=new Set();let current=category;
        while(current&&!seen.has(current.categoryId)){
            seen.add(current.categoryId);
            const rule=doc.categories.find(c=>c.categoryId===current.categoryId)?.fields.find(f=>f.code===code);
            if(rule)return {rule,source:current};
            if(current.inheritsParentFields===false)break;
            current=categories.find(c=>c.categoryId===current.parentCategoryId);
        }
        return null;
    }
    function mappings(doc){
        assignments=[];el('attribute-categories').replaceChildren();
        for(const category of categories){
            const locked=schema.lockedCategoryIds.includes(category.categoryId);const detail=node('details',null,'mb-3');const summary=node('summary',(category.categoryPath||category.categoryName)+(locked?' — locked':''));detail.append(summary);
            const fieldset=node('fieldset',null,'p-2');fieldset.disabled=locked;detail.append(fieldset);const entry={id:category.categoryId,fields:[]};
            for(const a of doc.attributes.filter(a=>a.code&&a.label)){
                const effective=inheritedRule(doc,category,a.code);
                const rule=effective?.rule||doc.categories.find(c=>c.categoryId===category.categoryId)?.fields.find(f=>f.code===a.code);
                const inherited=!!effective&&effective.source.categoryId!==category.categoryId;
                const line=node('div',null,'border-bottom py-2');const include=checked(line,a.label,!!rule),required=checked(line,'Required',rule?.required),axis=checked(line,enhanced?'Creates variants':'Variant axis',rule?.variantAxis);
                const searchable=checked(line,'Searchable',rule?.searchable!==false),visible=checked(line,'Visible',rule?.visible!==false);
                const defaultValue=input(line,'Default value',rule?.defaultValue);defaultValue.maxLength=a.type==='TEXT_LIST'?1000:100;
                if(!enhanced){searchable.closest('label').hidden=visible.closest('label').hidden=defaultValue.closest('.col-md-6').hidden=true;}
                const sync=()=>{required.disabled=inherited||!include.checked;axis.disabled=inherited||!include.checked||a.type!=='SELECT';if(!include.checked||a.type!=='SELECT')axis.checked=false;if(axis.checked)required.checked=true;};
                include.disabled=inherited;searchable.disabled=visible.disabled=defaultValue.disabled=inherited;
                if(inherited)line.append(node('p','Inherited from '+(effective.source.categoryPath||effective.source.categoryName)+'. Edit its parent assignment to change this field.','small text-muted mb-0'));
                else if(rule&&schema.inheritanceAvailable)line.append(node('p','Defined on this category.','small text-muted mb-0'));
                include.onchange=axis.onchange=()=>{sync();if(schema.inheritanceAvailable)updateAssignments();};
                required.onchange=()=>{if(!required.checked)axis.checked=false;if(schema.inheritanceAvailable)updateAssignments();};
                searchable.onchange=visible.onchange=defaultValue.onchange=()=>{if(schema.inheritanceAvailable)updateAssignments();};sync();
                fieldset.append(line);entry.fields.push({code:a.code,include,required,axis,searchable,visible,defaultValue,inherited});
            }
            if(schema.inheritanceAvailable&&category.inheritsParentFields===false)fieldset.prepend(node('p','Existing registered products retain their direct fields. Parent inheritance requires a reviewed migration.','small text-muted'));
            if(!entry.fields.length)fieldset.append(node('p','Add reusable attributes first.','small text-muted'));
            assignments.push(entry);el('attribute-categories').append(detail);
        }
    }
    function updateAssignments(){const doc=capture();mappings(doc);}
    function render(doc){
        const lockedCodes=new Set(doc.categories.filter(c=>schema.lockedCategoryIds.includes(c.categoryId)).flatMap(c=>c.fields.map(f=>f.code)));
        definitions=[];el('attribute-definitions').replaceChildren();doc.attributes.forEach(a=>definition(a,lockedCodes.has(a.code)));mappings(doc);
    }
    try{
        [schema,categories]=await Promise.all([api('/api/categories/schema'),api('/api/categories')]);render(schema);
        el('attribute-fields').disabled=!schema.available||el('attribute-form').dataset.canEdit!=='true';
        el('attribute-status').textContent=!schema.available?'Apply database migration 048 manually to enable category settings.':el('attribute-fields').disabled?'You have view access. Category edit permission is required to save settings.':'Settings apply to this company and shop profile across all branches. Version '+schema.version+'.';
    }catch(e){error(e);return;}
    el('attribute-add').onclick=()=>{if(definitions.length<30)definition();};
    el('attribute-suggestions').onclick=()=>{
        if(definitions.length){error(new Error('Shop suggestions can be loaded into an empty attribute list. Add individual fields to extend an existing setup.'));return;}
        const profile=document.body.dataset.businessProfile;
        const attrs=profile==='CLOTHING'?[['clothing_size','Clothing size',['XS','S','M','L','XL','XXL']],['color','Color',['Black','White','Blue','Red']],['fabric','Fabric',[]]]:
            profile==='FOOTWEAR'?[['footwear_size','Footwear size (UK)',['4','5','6','7','8','9','10','11']],['color','Color',['Black','Brown','White']],['material','Material',[]]]:
            profile==='BIKE_ACCESSORIES'?[['size','Size',['S','M','L','XL']],['color','Color',['Black','White','Red']],['vehicle_fitment','Vehicle fitment',[]]]:[];
        if(!attrs.length){error(new Error('Existing Electrical templates remain available in Product Registration. Add custom category attributes only for new categories when needed.'));return;}
        render({attributes:attrs.map(([code,label,options])=>({code,label,options,type:options.length?'SELECT':'TEXT'})),categories:[]});
    };
    el('attribute-form').onsubmit=async event=>{
        event.preventDefault();if(busy||!event.target.reportValidity())return;busy=true;el('attribute-save').disabled=true;el('attribute-error').hidden=true;
        try{schema=await api('/api/categories/schema',{method:'PUT',body:JSON.stringify(capture())});render(schema);el('attribute-status').textContent='Category settings saved. Version '+schema.version+'. Reopen Product Registration to load the latest fields.';}
        catch(e){error(e);}finally{busy=false;el('attribute-save').disabled=false;}
    };
})();
