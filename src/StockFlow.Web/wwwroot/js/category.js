let categoryListState=null,categoryPage=0,categoryGroupFilter='',categorySearchTerm='';
let categorySort='name',categorySortAscending=true,categorySortClicks=0;
const categoryNameOrder=new Intl.Collator(undefined,{sensitivity:'base',numeric:true});
const categoryGroupsExpanded=new Set();let categoryPageSize=StockFlowPagination.defaultSize,categoryPager;
// Lucide outlines, local to the category list (no additional icon dependency).
const categoryIcons={PackageSearch:'<path d="m3 7 9-5 9 5-9 5-9-5Zm0 0v10l9 5 4-2M12 12v10M7.5 4.5l9 5"/><circle cx="19" cy="16" r="3"/><path d="m21 18 2 2"/>',Pencil:'<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.5z"/><path d="m15 5 4 4"/>',FolderPlus:'<path d="M12 10v6m-3-3h6"/><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',CircleOff:'<path d="m2 2 20 20M8.35 2.69a10 10 0 0 1 12.96 12.96M19.08 19.08A10 10 0 0 1 4.92 4.92"/>',CircleCheck:'<circle cx="12" cy="12" r="10"/><path d="m9 12 2 2 4-4"/>'};
let categoryTooltip;
function categoryIconAction(icon,label,handler){
 const button=categoryElement('button',null,'category-icon-action sf-action-icon');button.type='button';button.setAttribute('aria-label',label);button.dataset.icon=icon;
 button.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'+categoryIcons[icon]+'</svg>';
 const hide=()=>{categoryTooltip?.remove();categoryTooltip=null;button.removeAttribute('aria-describedby');window.removeEventListener('scroll',hide,true);window.removeEventListener('resize',hide);};
 const show=()=>{hide();categoryTooltip=categoryElement('span',label,'sf-caption-tooltip category-action-tooltip');categoryTooltip.id='category-action-tooltip';categoryTooltip.setAttribute('role','tooltip');categoryTooltip.setAttribute('popover','manual');document.body.append(categoryTooltip);categoryTooltip.showPopover();button.setAttribute('aria-describedby',categoryTooltip.id);const r=button.getBoundingClientRect(),t=categoryTooltip.getBoundingClientRect();categoryTooltip.style.left=Math.max(8,Math.min(innerWidth-t.width-8,r.left+(r.width-t.width)/2))+'px';categoryTooltip.style.top=(r.top-t.height-8>=8?r.top-t.height-8:Math.min(innerHeight-t.height-8,r.bottom+8))+'px';window.addEventListener('scroll',hide,true);window.addEventListener('resize',hide);};
 button.onpointerenter=show;button.onpointerleave=hide;button.onfocus=show;button.onblur=hide;button.onkeydown=e=>{if(e.key==='Escape')hide();};button.onclick=()=>{hide();handler();};return button;
}
function categoryParentFilter(group){
 const label=categoryElement('label','Category Type','form-label'),select=categoryElement('select');select.id='category-main-filter';select.hidden=true;
 const segments=categoryElement('div',null,'category-type-filter');segments.id='category-type-filter';segments.setAttribute('role','group');label.id='category-type-label';segments.setAttribute('aria-labelledby',label.id);
 for(const [text,value] of [['All Categories',''],['Main Categories','main'],['Subcategories','sub']]){select.add(new Option(text,value));const button=categoryElement('button',text);button.type='button';button.dataset.value=value;button.setAttribute('aria-pressed',String(value===''));button.onclick=()=>{select.value=value;select.dispatchEvent(new Event('change'));};segments.append(button);}
 function refresh(){for(const button of segments.children)button.setAttribute('aria-pressed',String(button.dataset.value===select.value));}
 select.onchange=()=>{refresh();renderCategoryRows();};segments.onkeydown=event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();const buttons=[...segments.children],at=buttons.indexOf(document.activeElement),next=event.key==='Home'?0:event.key==='End'?2:(at+(event.key==='ArrowRight'?1:2))%3;buttons[next].focus();buttons[next].click();};
 group.append(label,select,segments);group.refreshCategoryFilter=refresh;
}
function categoryAddAction(record){if(!document.getElementById('add-record')||record.parentCategoryId||!record.isActive)return null;const button=categoryIconAction('FolderPlus','Add Subcategory',()=>CategoryCascades.createUnder(record.categoryId));button.dataset.addUnder=record.categoryId;return button;}
function categoryProducts(record){
 location.assign('/Products?'+new URLSearchParams({categoryId:record.categoryId,includeDescendants:!record.parentCategoryId,categoryName:record.categoryName}));
}
async function categoryStatus(record){
 if(!record.isActive&&record.parentCategoryId&&!categoryListState.records.find(c=>c.categoryId===record.parentCategoryId)?.isActive){categoryListState.show('Activate the parent category first before activating this subcategory.',true);return;}
 const dialog=document.getElementById('deactivate-dialog'),title=document.getElementById('deactivate-title'),confirm=document.getElementById('confirm-deactivate'),cancel=document.getElementById('cancel-deactivate'),close=document.getElementById('close-category-status'),active=!record.isActive;
 title.textContent=active?'Activate Category?':'Deactivate Category?';confirm.textContent=active?'Activate Category':'Deactivate Category';confirm.className='btn btn-primary';
 const symbol=document.getElementById('category-status-symbol');symbol.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8">'+categoryIcons[active?'CircleCheck':'CircleOff']+'</svg>';dialog.dataset.statusAction=active?'activate':'deactivate';
 let info=document.getElementById('category-status-context');if(!info){info=categoryElement('div');info.id='category-status-context';title.closest('.category-status-header').after(info);}
 const children=categoryListState.records.filter(c=>c.parentCategoryId===record.categoryId),direct=Number(record.productCount)||0,total=record.subtreeProductCount??direct+children.reduce((sum,c)=>sum+(Number(c.productCount)||0),0);
 const metadata=categoryElement('div',null,'category-status-metadata');metadata.append(categoryElement('span',record.categoryCode),categoryElement('span',children.length+' subcategories'),categoryElement('span',total+' products'+(children.length?' across hierarchy':'')));
 metadata.title=direct+' direct products; '+(total-direct)+' in subcategories. Includes inactive products; excludes variants and stock units.';
 info.replaceChildren(categoryElement('strong',record.categoryName),metadata);
 const description=dialog.querySelector(':scope > p');description.id='category-status-description';description.replaceChildren(categoryElement('span',active?'This category will be available for new product registration. Subcategories keep their individual statuses.':"This category and its subcategories won't be available for new product registration."),categoryElement('span','Existing products, stock and transaction history will remain unchanged. '+(active?'You can deactivate this category anytime.':'You can reactivate this category anytime.')));
 dialog.setAttribute('aria-describedby',description.id);let busy=false;const dismiss=()=>{if(!busy)dialog.close('cancel');};cancel.onclick=close.onclick=dismiss;dialog.oncancel=event=>{if(busy)event.preventDefault();};
 confirm.onclick=async()=>{if(busy)return;busy=true;confirm.disabled=cancel.disabled=close.disabled=true;confirm.setAttribute('aria-busy','true');confirm.textContent=active?'Activating...':'Deactivating...';try{await categoryListState.requestJson('/api/categories/'+record.categoryId+'/status',{method:'POST',body:JSON.stringify({isActive:active})});dialog.close();await categoryListState.reload();categoryListState.show(active?'Category activated.':'Category deactivated.');}catch(error){categoryListState.show(error.message,true);}finally{busy=false;confirm.disabled=cancel.disabled=close.disabled=false;confirm.removeAttribute('aria-busy');confirm.textContent=active?'Activate Category':'Deactivate Category';}};dialog.showModal();title.tabIndex=-1;title.focus();
}

function renderCategoryHeader(){const head=document.createElement('tr');for(const [text,key]of [['Category Name','name'],['Category Code','code'],['Products','products'],['Status','status'],['Actions',null]]){const th=categoryElement('th');th.scope='col';if(key){const current=categorySort===key,button=categoryElement('button',null,'category-sort');button.type='button';button.dataset.sort=key;button.append(categoryElement('span',text));const icon=categoryElement('span',null,'category-sort-icon');icon.setAttribute('aria-hidden','true');icon.innerHTML='<svg viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="1.5"><path d="m4 5 3-3 3 3" class="sort-up"/><path d="m4 9 3 3 3-3" class="sort-down"/></svg>';button.append(icon);button.setAttribute('aria-label',current&&categorySortClicks===2?text+': return to default ordering':text+': sort '+(current&&categorySortClicks===1?'descending':'ascending'));if(current){th.setAttribute('aria-sort',categorySortAscending?'ascending':'descending');button.dataset.direction=categorySortAscending?'ascending':'descending';}button.onclick=()=>{if(categorySort!==key||categorySortClicks===0){categorySort=key;categorySortAscending=true;categorySortClicks=1;}else if(categorySortClicks===1){categorySortAscending=false;categorySortClicks=2;}else{categorySort='name';categorySortAscending=true;categorySortClicks=0;}categoryPage=0;renderCategoryRows();document.querySelector('.category-sort[data-sort="'+key+'"]').focus({preventScroll:true});};th.append(button);}else th.textContent=text;head.append(th);}document.getElementById('master-head').replaceChildren(head);}
function renderCategoryRows(){
 categoryTooltip?.remove();categoryTooltip=null;
 if(!categoryListState)return;const {records,openEditor}=categoryListState,root=document.getElementById('master-data'),canEdit=root.dataset.canEdit==='true',canStatus=root.dataset.canDisable==='true';
 const byId=new Map(records.map(c=>[c.categoryId,c])),children=new Map();for(const c of records)if(c.parentCategoryId){if(!children.has(c.parentCategoryId))children.set(c.parentCategoryId,[]);children.get(c.parentCategoryId).push(c);}
 // Use one numeric projection for both sorting and the Products cells.
 const productTotals=new Map(records.map(c=>[c.categoryId,c.parentCategoryId?(Number(c.productCount)||0):(Number.isFinite(c.subtreeProductCount)?c.subtreeProductCount:(Number(c.productCount)||0)+(children.get(c.categoryId)||[]).reduce((n,child)=>n+(Number(child.productCount)||0),0))]));
 const nameOrder=(a,b)=>categoryNameOrder.compare(a.categoryName||'',b.categoryName||'');
 const compare=(a,b)=>{const value=categorySort==='products'?productTotals.get(a.categoryId)-productTotals.get(b.categoryId):categorySort==='status'?Number(!a.isActive)-Number(!b.isActive):categorySort==='code'?categoryNameOrder.compare(a.categoryCode||'',b.categoryCode||''):nameOrder(a,b);return (categorySortAscending?value:-value)||nameOrder(a,b)||a.categoryId-b.categoryId;};
 for(const kids of children.values())kids.sort(compare);
 const roots=records.filter(c=>!c.parentCategoryId),filter=document.getElementById('category-main-filter'),selected=filter.value;
 const term=document.getElementById('master-search').value.trim().toLowerCase(),status=document.getElementById('status-filter').value,key=[selected,term,status].join('|'),filterChanged=key!==categoryGroupFilter;if(filterChanged){categoryPage=0;categoryGroupFilter=key;if(!term&&categorySearchTerm)categoryGroupsExpanded.clear();categorySearchTerm=term;}
 const matches=c=>(status==='false'||(status==='true'?c.isActive:!c.isActive))&&(!term||[c.categoryName,c.categoryCode].some(value=>value?.toLowerCase().includes(term)));
 const visibleRoots=roots.filter(c=>selected==='main'?matches(c):matches(c)||(children.get(c.categoryId)||[]).some(matches));
 const visible=(selected==='sub'?records.filter(c=>c.parentCategoryId&&matches(c)):visibleRoots).sort(compare);
 const body=document.getElementById('master-rows');body.replaceChildren();renderCategoryHeader();
 if(records.some(c=>c.parentCategoryId&&byId.get(c.parentCategoryId)?.parentCategoryId)){categoryListState.show('Apply manual migration 065 before managing the two-level category hierarchy.',true);return;}
 const pages=Math.max(1,Math.ceil(visible.length/categoryPageSize));categoryPage=Math.min(categoryPage,pages-1);
 function row(record,isParent){const tr=categoryElement('tr',null,isParent?'category-group-row':'category-subcategory-row');tr.dataset.recordId=record.categoryId;const cell=categoryElement('td');if(isParent){const kids=children.get(record.categoryId)||[];if(kids.length&&selected!=='main'){const toggle=categoryElement('button',categoryGroupsExpanded.has(record.categoryId)?'\u2304':'\u203a','category-group-toggle');toggle.type='button';toggle.dataset.categoryToggle=record.categoryId;toggle.setAttribute('aria-expanded',String(categoryGroupsExpanded.has(record.categoryId)));toggle.setAttribute('aria-label',(categoryGroupsExpanded.has(record.categoryId)?'Collapse ':'Expand ')+record.categoryName);toggle.onclick=()=>{if(categoryGroupsExpanded.has(record.categoryId))categoryGroupsExpanded.delete(record.categoryId);else categoryGroupsExpanded.add(record.categoryId);renderCategoryRows();document.querySelector('[data-category-toggle="'+record.categoryId+'"]')?.focus({preventScroll:true});};cell.append(toggle);}cell.append(categoryElement('strong',record.categoryName),categoryElement('small',kids.length?kids.length+' subcategories':'No subcategories added.','category-child-count'));}else{if(selected==='sub'){tr.classList.add('category-flat-child');cell.append(categoryElement('span',record.categoryName),categoryElement('small','Under '+(byId.get(record.parentCategoryId)?.categoryName||'Main category'),'category-child-count'));}else{cell.style.paddingLeft='38px';cell.append(categoryElement('span',record.categoryName));}}
 const direct=Number(record.productCount)||0,total=productTotals.get(record.categoryId);
 const products=categoryElement('td');const breakdown=direct+' direct; '+(total-direct)+' in subcategories. Product records including inactive products; excludes variants and stock units.';const count=categoryElement('span',String(total),'category-product-count');count.tabIndex=0;count.title=breakdown;count.setAttribute('aria-label',total+' products. '+breakdown);products.append(count);tr.append(cell,categoryElement('td',record.categoryCode||'','category-code-cell'),products);
 const state=categoryElement('td');state.append(categoryElement('span',record.isActive?'Active':'Inactive',record.isActive?'category-status-active':'category-status-inactive'));if(record.isActive&&record.isEffectivelyActive===false)state.append(categoryElement('small','Parent inactive','category-child-count'));tr.append(state);
 const actions=categoryElement('td',null,'category-list-actions'),buttons=categoryElement('div',null,'category-row-actions sf-action-group');
 if(canEdit)buttons.append(categoryIconAction('Pencil','Edit Category',()=>openEditor(record.categoryId)));
 if(isParent){const add=categoryAddAction(record);if(add)buttons.append(add);}
 if(root.dataset.canProducts==='true')buttons.append(categoryIconAction('PackageSearch','View Products',()=>categoryProducts(record)));
 if(canStatus){const statusAction=categoryIconAction(record.isActive?'CircleOff':'CircleCheck',record.isActive?'Deactivate Category':'Activate Category',()=>categoryStatus(record));if(record.isActive)statusAction.classList.add('category-action-warning');buttons.append(statusAction);}

actions.append(buttons);tr.append(actions);body.append(tr);}
 for(const parent of visible.slice(categoryPage*categoryPageSize,(categoryPage+1)*categoryPageSize)){if(selected==='sub'){row(parent,false);continue;}const kids=children.get(parent.categoryId)||[];if(filterChanged&&(term||status!=='false'||selected==='sub')&&kids.some(matches))categoryGroupsExpanded.add(parent.categoryId);row(parent,true);if(selected!=='main'&&categoryGroupsExpanded.has(parent.categoryId))for(const child of kids.filter(c=>(status==='false'||(status==='true'?c.isActive:!c.isActive))&&(!term||[parent.categoryName,parent.categoryCode,c.categoryName,c.categoryCode].some(v=>v?.toLowerCase().includes(term)))))row(child,false);}
 if(!visible.length){const empty=categoryElement('tr');empty.id='category-list-empty';const cell=categoryElement('td',records.length?'No matching categories.':'No categories added yet.');cell.colSpan=5;empty.append(cell);body.append(empty);}
 document.getElementById('category-list-count').textContent=visible.length+(selected==='sub'?' subcategories':' main categories');categoryPager?.set({total:visible.length,page:categoryPage+1,size:categoryPageSize});
}
async function openCategoryView(id,openEditor){
    if(window.StockFlowMasterScreens&&!StockFlowMasterScreens.page){StockFlowMasterScreens.navigate('View',id);return;}
    await openEditor(id);
    if(!document.getElementById('editor').open)return;
    window.CategoryRegistration.setViewOnly();
    const editor=document.getElementById('editor');for(const input of editor.querySelectorAll('#form-fields input,#form-fields textarea,#form-fields select,#category-status'))input.disabled=true;
    document.getElementById('category-save').disabled=true;
    document.getElementById('editor-title').textContent='View Category';
}
const categoryElement=(tag,text,cls)=>{const element=document.createElement(tag);if(text)element.textContent=text;if(cls)element.className=cls;return element;};
StockFlowMaster.start({
    singular: 'Category',
    inlineEditor: true,
    id: 'categoryId',
    fields: [
        {
            name: 'categoryName',
            label: 'Category Name',
            type: 'text',
            minLength: 2,
            maxLength: 100,
            required: true
        },
        {
            name: 'categoryCode',
            label: 'Category Code',
            type: 'text',
            readOnly: true,
            default: 'Generated on save'
        },
        {
            name: 'parentCategoryId',
            label: 'Parent Category',
            type: 'select',
            value: 'categoryId',
            text: 'categoryPath',
            nullable: true
        },
        {
            name: 'description',
            label: 'Description',
            type: 'textarea',
            maxLength: 500
        }
    ],
    listQuery:()=>({activeOnly:'false',listSearch:'true'}),
    columns: ['categoryName','categoryCode'],
    columnLabels: {
        categoryName:'Category Name',categoryCode:'Category Code'
    },
    descriptionTooltip: true,
    onReady(controls, editorActions) {
        if(!window.StockFlowMasterScreens?.page)document.body.classList.add('sf-category-list');
        const statusDialog=document.getElementById('deactivate-dialog'),closeStatus=categoryElement('button',null,'sf-dialog-close');closeStatus.type='button';closeStatus.id='close-category-status';closeStatus.setAttribute('aria-label','Close category confirmation');closeStatus.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2"><path d="m6 6 12 12M18 6 6 18"/></svg>';statusDialog.prepend(closeStatus);
        const symbol=categoryElement('div',null,'category-status-symbol');symbol.id='category-status-symbol';symbol.setAttribute('aria-hidden','true');const statusTitle=document.getElementById('deactivate-title'),statusHeader=categoryElement('div',null,'category-status-header');statusTitle.before(statusHeader);statusHeader.append(symbol,statusTitle);
        const table=document.querySelector('#master-list .table-responsive');table.classList.add('category-list-table');renderCategoryHeader();
        document.querySelector('.configuration-heading h2').textContent='Category Management';document.querySelector('#master-list>h3').hidden=true;
        const search=document.getElementById('master-search');search.placeholder='Search by category name or code...';document.querySelector('label[for="master-search"]').textContent='Search Categories';search.closest('.col-md-8').className='col-md-5';
        const status=document.getElementById('status-filter');status.dataset.sfSelect='native';status.sfSelect?.destroy();status.closest('.col-md-4').className='col-md-3';status.options[0].textContent='All Status';status.options[1].textContent='Active';status.add(new Option('Inactive','inactive'));
        const group=categoryElement('div',null,'col-md-4');categoryParentFilter(group);status.closest('.col-md-3').before(group);
        const footer=categoryElement('div',null,'category-list-footer'),count=categoryElement('span');count.id='category-list-count';count.setAttribute('aria-live','polite');
        footer.append(count);table.after(footer);const paging=categoryElement('nav',null,'category-pagination');paging.setAttribute('aria-label','Category pages');for(const [id,text,delta] of [['category-page-prev','Previous',-1],['category-page-next','Next',1]]){const b=categoryElement('button',text,'btn btn-sm');b.id=id;b.type='button';b.onclick=()=>{categoryPage+=delta;renderCategoryRows();};paging.append(b);}const pageCount=categoryElement('span');pageCount.id='category-page-count';paging.append(pageCount);footer.append(paging);
        document.getElementById('master-list').dataset.sfPaginationRemote='true';
        categoryPager=StockFlowPagination.create({host:footer,label:'categories',legacy:{range:'#category-list-count',previous:'#category-page-prev',next:'#category-page-next',position:'#category-page-count'},onChange(state){categoryPage=state.page-1;categoryPageSize=state.size;renderCategoryRows();}});
        const parent=controls.parentCategoryId;parent.hidden=true;parent.tabIndex=-1;parent.add(new Option('None',''));
        window.CategoryCascades.bind(controls,editorActions.openEditor);
        const preview=categoryElement('section',null,'col-12 category-path-preview');
        preview.append(categoryElement('strong','Category Hierarchy'));const path=categoryElement('output');path.id='category-path-preview';path.setAttribute('aria-live','polite');
        const level=categoryElement('p',null,'small text-muted');level.id='category-level-preview';preview.append(path,level);document.getElementById('form-fields').append(preview);
        const update=()=>{path.textContent=[parent.value?parent.selectedOptions[0]?.textContent:'',controls.categoryName.value.trim()].filter(Boolean).join(' / ')||'Enter a category name';level.textContent='Level '+((parent.value?Number(parent.selectedOptions[0]?.dataset.level)||1:0)+1)+(parent.value?' - created under the selected parent.':' - main category.');};
        controls.categoryName.addEventListener('input',update);parent.addEventListener('change',update);
        window.CategoryRegistration?.bind(controls);
    },
    localFilter:renderCategoryRows,
    renderList(state) {
        const {records}=state;
        try{const saved=JSON.parse(sessionStorage.getItem('stockflow.category-saved')||'null');if(saved){sessionStorage.removeItem('stockflow.category-saved');if(saved.parentId)categoryGroupsExpanded.add(saved.parentId);const message=document.getElementById('master-message');message.textContent='Category saved successfully.';message.className='alert alert-success';}}catch{}
        categoryListState=state;document.getElementById('category-main-filter').parentElement.refreshCategoryFilter();renderCategoryRows();
    },
    initialFocus() {
        const title=document.getElementById('editor-title');
        if(title.textContent.toLowerCase().startsWith('edit ')){
            title.tabIndex=-1;
            return title;
        }
    },
    async onOpen(controls, record) {
        for(const input of document.querySelectorAll('#form-fields input,#form-fields textarea,#form-fields select'))input.disabled=false;
        const response = await fetch('/api/categories');
        if (!response.ok) {
            throw new Error('Unable to load parent categories. Try again.');
        }
        const categories = await response.json();
        await window.CategoryRegistration?.open(record,categories);
        window.CategoryCascades.load(categories,record);
    },
    onSaveError(error){window.CategoryRegistration?.saveError(error);},
    onSaved(result,body){if(window.StockFlowMasterScreens?.categoryPopup){StockFlowMasterScreens.complete({...body,...result});return;}if(body.parentCategoryId)categoryGroupsExpanded.add(body.parentCategoryId);},
    beforeSave(body) { return window.CategoryRegistration?.beforeSave(body) ?? true; }
});
