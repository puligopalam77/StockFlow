const enhancedBrandRegistration=document.body.dataset.catalogRegistrationEnhancements==='true';
const brandElement=(tag,text,cls)=>{const node=document.createElement(tag);if(text)node.textContent=text;if(cls)node.className=cls;return node;};
let brandState={categories:[],roots:new Set(),specific:new Set(),record:null,details:false,logo:null,removeLogo:false,logoBusy:false,viewOnly:false};
async function brandApi(url,options={}){const response=await fetch(url,{credentials:'same-origin',cache:'no-store',...options});const body=await response.json().catch(()=>null);if(!response.ok)throw new Error(body?.message||'Unable to load brand details. Please try again.');return body;}
function brandRoot(category){const byId=new Map(brandState.categories.map(c=>[c.categoryId,c])),seen=new Set();let value=category;while(value?.parentCategoryId&&byId.has(value.parentCategoryId)&&!seen.has(value.categoryId)){seen.add(value.categoryId);value=byId.get(value.parentCategoryId);}return value;}
function brandCategoryIds(){
    if(document.getElementById('brand-all-categories').checked)return [];
    const ids=[];for(const rootId of brandState.roots){const specific=[...brandState.specific].filter(id=>brandRoot(brandState.categories.find(c=>c.categoryId===id))?.categoryId===rootId);if(!specific.length){ids.push(rootId);continue;}
        for(const id of specific){let value=brandState.categories.find(c=>c.categoryId===id),covered=false;const seen=new Set();while(value?.parentCategoryId&&!seen.has(value.categoryId)){seen.add(value.categoryId);if(specific.includes(value.parentCategoryId)){covered=true;break;}value=brandState.categories.find(c=>c.categoryId===value.parentCategoryId);}if(!covered)ids.push(id);}
    }return ids;
}
function renderBrandCategories(){
    const host=document.getElementById('brand-category-choices');host.replaceChildren();document.getElementById('brand-category-selection').hidden=document.getElementById('brand-all-categories').checked;
    const term=document.getElementById('brand-category-search').value.trim().toLowerCase(),roots=brandState.categories.filter(c=>!c.parentCategoryId||!brandState.categories.some(parent=>parent.categoryId===c.parentCategoryId));
    for(const root of roots){const descendants=brandState.categories.filter(c=>c.categoryId!==root.categoryId&&brandRoot(c)?.categoryId===root.categoryId),group=brandElement('section',null,'brand-category-group');if(term&&![root,...descendants].some(c=>(c.categoryPath||c.categoryName).toLowerCase().includes(term)))continue;
        const label=brandElement('label',null,'brand-category-main'),checkbox=brandElement('input');checkbox.type='checkbox';checkbox.checked=brandState.roots.has(root.categoryId);checkbox.dataset.brandRoot=root.categoryId;checkbox.disabled=brandState.viewOnly||!root.isActive&&!checkbox.checked;label.append(checkbox,document.createTextNode(root.categoryName+(root.isActive?'':' (Inactive)')));group.append(label);
        checkbox.onchange=()=>{if(checkbox.checked)brandState.roots.add(root.categoryId);else{brandState.roots.delete(root.categoryId);for(const child of descendants)brandState.specific.delete(child.categoryId);}renderBrandCategories();};
        if(checkbox.checked&&descendants.length){const choices=brandElement('div',null,'brand-subcategories');choices.append(brandElement('small','Specific subcategories (optional)','text-muted'));for(const child of descendants){const item=brandElement('label'),input=brandElement('input');input.type='checkbox';input.checked=brandState.specific.has(child.categoryId);input.dataset.brandSub=child.categoryId;input.disabled=brandState.viewOnly||!child.isActive&&!input.checked;input.onchange=()=>{if(input.checked)brandState.specific.add(child.categoryId);else brandState.specific.delete(child.categoryId);};item.style.paddingLeft=((child.categoryLevel||2)-2)*16+'px';item.append(input,document.createTextNode(child.categoryName+(child.isActive?'':' (Inactive)')));choices.append(item);}group.append(choices);}host.append(group);
    }
    if(!host.children.length)host.append(brandElement('p',brandState.categories.length?'No matching categories.':'No categories registered yet. You can keep this brand applicable to all categories.','small text-muted'));
}
function brandLogoPreview(url){const host=document.getElementById('brand-logo-preview');host.hidden=!url;const image=document.getElementById('brand-logo-image');if(url)image.src=url;else image.removeAttribute('src');}
function renderBrandList(records,rows,openEditor){
    const term=document.getElementById('master-search').value.trim().toLowerCase(),status=document.getElementById('status-filter').value,body=document.getElementById('master-rows');let shown=0;
    const header=document.querySelector('#master-head tr');if(header.children.length<5)header.append(brandElement('th','Actions'));
    for(const record of records){const row=rows.find(r=>Number(r.dataset.recordId)===record.brandId);if(!row)continue;
        const labels=(record.categoryIds||[]).map(id=>brandState.categories.find(c=>c.categoryId===id)?.categoryName||'Saved category'),categories=labels.length?labels.join(', '):'All Categories';
        row.hidden=!(status==='false'||(status==='true'?record.isActive:!record.isActive))||!!term&&![record.brandName,record.brandCode,record.description,...(record.aliases||[]),categories].filter(Boolean).join(' ').toLowerCase().includes(term);if(row.hidden)continue;shown++;
        const name=row.children[0];name.replaceChildren();const info=brandElement('div'),title=brandElement('strong',record.brandName),code=brandElement('small',record.brandCode);info.append(title,code);if(record.logoUrl){const logo=brandElement('img');logo.src=record.logoUrl;logo.alt=record.brandName+' logo';logo.loading='lazy';name.append(logo);}name.append(info);name.classList.add('brand-list-name');
        row.children[1].textContent=categories;row.children[1].title=categories;
        row.children[3].replaceChildren(brandElement('span',record.isActive?'Active':'Inactive',record.isActive?'category-status-active':'category-status-inactive'));
        const actions=row.children[4]||brandElement('td');if(!actions.parentElement)row.append(actions);
    }
    for(const row of rows)if(!row.dataset.recordId)row.hidden=true;
    if(!shown){const row=brandElement('tr'),cell=brandElement('td','No matching brands.');cell.colSpan=5;row.append(cell);body.append(row);}
    document.getElementById('brand-total').textContent=records.length;document.getElementById('brand-active-count').textContent=records.filter(b=>b.isActive).length;document.getElementById('brand-product-count').textContent=records.reduce((sum,b)=>sum+(b.productCount||0),0);document.getElementById('brand-list-count').textContent='Showing '+shown+' of '+records.length+' brands';
}
StockFlowMaster.start({singular:'Brand',id:'brandId',listQuery:()=>({activeOnly:'false'}),fields:[
    {name:'brandName',label:'Brand Name',type:'text',minLength:2,maxLength:100,required:true},
    {name:'brandCode',label:'Brand Code',type:'text',readOnly:true,default:'Generated on save'},
    {name:'description',label:'Description (optional)',type:'textarea',maxLength:500},
    ...(enhancedBrandRegistration?[{name:'aliases',label:'Other names / aliases (optional, comma separated)',type:'text',maxLength:2000}]:[])
],columns:['brandName','categorySummary','productCount'],columnLabels:{brandName:'Brand',categorySummary:'Applicable Categories',productCount:'Products'},descriptionTooltip:true,
    onReady(controls){
        brandState.controls=controls;document.querySelector('.configuration-heading h2').textContent='Brand Management';document.querySelector('#master-list>h3').hidden=true;document.querySelector('#master-list .table-responsive').classList.add('brand-list-table');document.getElementById('master-search').placeholder='Search brand, code or category';const status=document.getElementById('status-filter');status.options[0].textContent='All Status';status.add(new Option('Inactive','inactive'));
        document.getElementById('master-form').addEventListener('input',()=>document.getElementById('editor-message').classList.add('d-none'));
        const stats=brandElement('div',null,'brand-stats');for(const [id,label] of [['brand-total','Total Brands'],['brand-active-count','Active Brands'],['brand-product-count','Linked Products']]){const card=brandElement('div'),value=brandElement('strong','0');value.id=id;card.append(value,brandElement('span',label));stats.append(card);}document.getElementById('master-list').before(stats);const count=brandElement('p',null,'small text-muted mt-3');count.id='brand-list-count';document.getElementById('master-list').append(count);
        if(controls.isActive)controls.isActive.closest('.col-md-6').hidden=true;controls.brandCode.maxLength=20;controls.brandCode.pattern='[A-Za-z0-9][A-Za-z0-9-]{1,19}';controls.brandCode.oninput=()=>controls.brandCode.dataset.edited='true';controls.brandName.addEventListener('input',()=>{if(brandState.details&&!brandState.record&&controls.brandCode.dataset.edited!=='true')controls.brandCode.value=controls.brandName.value.trim().toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-|-$/g,'').slice(0,20);});
        document.getElementById('brand-all-categories').onchange=renderBrandCategories;document.getElementById('brand-category-search').oninput=renderBrandCategories;
        document.getElementById('brand-logo').onchange=async event=>{const file=event.target.files[0];if(!file)return;const error=document.getElementById('brand-logo-error');error.textContent='';if(file.size>2000000||!['image/jpeg','image/png','image/webp'].includes(file.type)){error.textContent='Choose a JPG, PNG or WebP logo up to 2 MB.';event.target.value='';return;}brandState.logoBusy=true;try{const data=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result.split(',')[1]);reader.onerror=reject;reader.readAsDataURL(file);});brandState.logo={fileName:file.name,contentType:file.type,data};brandState.removeLogo=false;brandLogoPreview('data:'+file.type+';base64,'+data);}catch{error.textContent='Unable to read this logo. Please choose it again.';}finally{brandState.logoBusy=false;}};
        document.getElementById('brand-remove-logo').onclick=()=>{brandState.logo=null;brandState.removeLogo=!!brandState.record?.logoUrl;document.getElementById('brand-logo').value='';brandLogoPreview(null);};
    },
    async onLoaded({records,rows,openEditor,isCurrent}){const categories=await brandApi('/api/brands/categories');if(isCurrent&&!isCurrent())return;brandState.categories=categories;renderBrandList(records,rows,openEditor);},
    async onOpen(controls,record){
        const [categories,options]=await Promise.all([brandApi('/api/brands/categories'),brandApi('/api/brands/registration-options')]);
        for(const input of document.querySelectorAll('#master-form input,#master-form textarea,#master-form select,#master-form button'))input.disabled=false;
        brandState={...brandState,controls,categories,record,details:!!options.detailsAvailable,roots:new Set(),specific:new Set(),logo:null,removeLogo:false,logoBusy:false,viewOnly:false};
        for(const id of record?.categoryIds||[]){const category=categories.find(c=>c.categoryId===id),root=brandRoot(category);if(root){brandState.roots.add(root.categoryId);if(id!==root.categoryId&&!(record.categoryIds||[]).includes(root.categoryId))brandState.specific.add(id);}}
        controls.brandCode.closest('.col-md-6').querySelector('label').textContent=brandState.details?'Brand Code *':'Brand Code';controls.brandCode.required=brandState.details;controls.brandCode.readOnly=!brandState.details;controls.brandCode.value=record?.brandCode||(brandState.details?'':'Generated on save');controls.brandCode.dataset.edited=record?'true':'false';
        document.getElementById('brand-website').value=record?.officialWebsite||'';document.getElementById('brand-website').disabled=!brandState.details;document.getElementById('brand-logo').disabled=!brandState.details;document.getElementById('brand-logo').value='';document.getElementById('brand-remove-logo').disabled=!brandState.details;document.getElementById('brand-logo-error').textContent='';brandLogoPreview(record?.logoUrl);
        document.getElementById('brand-active').checked=record?.isActive!==false;document.getElementById('brand-active').disabled=document.getElementById('master-data').dataset.canDisable!=='true';document.getElementById('brand-all-categories').checked=!(record?.categoryIds||[]).length;document.getElementById('brand-category-search').value='';renderBrandCategories();
    },
    async beforeSave(body){
        if(brandState.viewOnly)return false;if(brandState.logoBusy)throw new Error('Wait for the logo upload to finish.');
        body.brandName=body.brandName.trim().replace(/\s+/g,' ');body.categoryIds=brandCategoryIds();if(!document.getElementById('brand-all-categories').checked&&!body.categoryIds.length)throw new Error('Select at least one category, or choose Applicable to all categories.');if(!enhancedBrandRegistration&&!body.categoryIds.length)throw new Error('Select at least one category for this brand.');
        if(enhancedBrandRegistration)body.aliases=String(body.aliases||'').split(',').map(v=>v.trim().replace(/\s+/g,' ')).filter(Boolean);
        body.isActive=document.getElementById('brand-active').checked;
        if(brandState.details){if(!brandState.controls.brandCode.checkValidity())return false;body.brandCode=brandState.controls.brandCode.value.trim()||null;body.officialWebsite=document.getElementById('brand-website').value.trim();if(brandState.logo)body.logo=brandState.logo;if(brandState.removeLogo)body.removeLogo=true;}
        const existing=(await brandApi('/api/brands')).filter(b=>b.brandId!==brandState.record?.brandId),normalize=s=>String(s||'').trim().replace(/\s+/g,' ').toLowerCase();
        if(existing.some(b=>[b.brandName,...(b.aliases||[])].some(name=>[body.brandName,...(body.aliases||[])].some(value=>normalize(value)===normalize(name)))))throw new Error('This brand name or alias is already registered. Edit the existing brand instead.');
        if(body.brandCode&&existing.some(b=>normalize(b.brandCode)===normalize(body.brandCode)))throw new Error('This brand code is already registered. Choose another code.');
        return true;
    }
});
