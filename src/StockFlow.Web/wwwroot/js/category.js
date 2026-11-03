let categoryListState=null,categoryPage=0,categoryGroupFilter='',categorySearchTerm='';
const categoryGroupsExpanded=new Set();let categoryPageSize=StockFlowPagination.defaultSize,categoryPager;
// Lucide outlines, local to the category list (no additional icon dependency).
const categoryIcons={Pencil:'<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.5z"/><path d="m15 5 4 4"/>',FolderPlus:'<path d="M12 10v6m-3-3h6"/><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',CircleOff:'<path d="m2 2 20 20M8.35 2.69a10 10 0 0 1 12.96 12.96M19.08 19.08A10 10 0 0 1 4.92 4.92"/>',CircleCheck:'<circle cx="12" cy="12" r="10"/><path d="m9 12 2 2 4-4"/>'};
let categoryTooltip;
function categoryIconAction(icon,label,handler){
 const button=categoryElement('button',null,'btn btn-sm category-icon-action');button.type='button';button.setAttribute('aria-label',label);button.dataset.icon=icon;
 button.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'+categoryIcons[icon]+'</svg>';
 const hide=()=>{categoryTooltip?.remove();categoryTooltip=null;button.removeAttribute('aria-describedby');window.removeEventListener('scroll',hide,true);window.removeEventListener('resize',hide);};
 const show=()=>{hide();categoryTooltip=categoryElement('span',label,'sf-caption-tooltip category-action-tooltip');categoryTooltip.id='category-action-tooltip';categoryTooltip.setAttribute('role','tooltip');categoryTooltip.setAttribute('popover','manual');document.body.append(categoryTooltip);categoryTooltip.showPopover();button.setAttribute('aria-describedby',categoryTooltip.id);const r=button.getBoundingClientRect(),t=categoryTooltip.getBoundingClientRect();categoryTooltip.style.left=Math.max(8,Math.min(innerWidth-t.width-8,r.left+(r.width-t.width)/2))+'px';categoryTooltip.style.top=(r.top-t.height-8>=8?r.top-t.height-8:Math.min(innerHeight-t.height-8,r.bottom+8))+'px';window.addEventListener('scroll',hide,true);window.addEventListener('resize',hide);};
 button.onpointerenter=show;button.onpointerleave=hide;button.onfocus=show;button.onblur=hide;button.onkeydown=e=>{if(e.key==='Escape')hide();};button.onclick=()=>{hide();handler();};return button;
}
function categoryParentFilter(group){
 const label=categoryElement('label','Parent Category','form-label'),select=categoryElement('select');select.id='category-main-filter';select.hidden=true;select.add(new Option('All Parent Categories',''));
 const details=categoryElement('details',null,'category-filter-dropdown'),toggle=categoryElement('summary','All Parent Categories','form-select');toggle.id='category-filter-toggle';label.htmlFor=toggle.id;toggle.setAttribute('aria-label','Parent Category');
 const panel=categoryElement('div',null,'category-filter-panel'),search=categoryElement('input',null,'form-control'),results=categoryElement('div',null,'category-filter-results');search.id='category-parent-filter-search';search.type='search';search.placeholder='Search parent categories';search.setAttribute('aria-label','Search parent categories');
 const close=()=>{details.open=false;toggle.focus();};
 const choose=value=>{select.value=value;select.dispatchEvent(new Event('change'));close();};
 function refresh(){const roots=(categoryListState?.records||[]).filter(c=>!c.parentCategoryId),term=search.value.trim().toLowerCase(),matches=roots.filter(c=>c.categoryName.toLowerCase().includes(term));results.replaceChildren();
 const option=(name,value)=>{const b=categoryElement('button',name,'category-parent-option');b.type='button';b.setAttribute('aria-pressed',String(select.value===String(value)));b.onclick=()=>choose(String(value));results.append(b);};option('All Parent Categories','');for(const c of matches.slice(0,50))option(c.categoryName,c.categoryId);if(!matches.length)results.append(categoryElement('p','No matching categories.','text-muted small'));if(matches.length>50)results.append(categoryElement('p','Showing first 50 matches. Type to narrow results.','text-muted small'));
 toggle.textContent=roots.find(c=>String(c.categoryId)===select.value)?.categoryName||'All Parent Categories';}
 search.oninput=refresh;select.onchange=()=>{refresh();renderCategoryRows();};toggle.addEventListener('click',()=>{if(!details.open){search.value='';refresh();}});details.addEventListener('toggle',()=>{if(details.open)search.focus();});
 details.onkeydown=e=>{const buttons=[...results.querySelectorAll('button')],index=buttons.indexOf(document.activeElement);if(e.key==='Escape'){e.preventDefault();close();}else if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();buttons[index<0?(e.key==='ArrowDown'?0:buttons.length-1):(index+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();}else if(e.key==='Enter'&&e.target===search){e.preventDefault();(search.value?buttons[1]:buttons[0])?.click();}};
 document.addEventListener('pointerdown',e=>{if(!details.contains(e.target))details.open=false;});panel.append(search,results);details.append(toggle,panel);group.append(label,select,details);group.refreshCategoryFilter=()=>{const value=select.value;select.replaceChildren(new Option('All Parent Categories',''));for(const c of (categoryListState?.records||[]).filter(c=>!c.parentCategoryId))select.add(new Option(c.categoryName,c.categoryId));select.value=value;refresh();};
}
function categoryAddAction(record){if(!document.getElementById('add-record')||record.parentCategoryId||!record.isActive)return null;const button=categoryIconAction('FolderPlus','Add Subcategory',()=>CategoryCascades.createUnder(record.categoryId));button.dataset.addUnder=record.categoryId;return button;}
async function categoryStatus(record){
 if(!record.isActive&&record.parentCategoryId&&!categoryListState.records.find(c=>c.categoryId===record.parentCategoryId)?.isActive){categoryListState.show('Activate the parent category first before activating this subcategory.',true);return;}
 const dialog=document.getElementById('deactivate-dialog'),title=document.getElementById('deactivate-title'),confirm=document.getElementById('confirm-deactivate'),cancel=document.getElementById('cancel-deactivate'),active=!record.isActive;
 title.textContent=active?'Activate Category?':'Deactivate Category?';confirm.textContent=active?'Activate Category':'Deactivate Category';
 const symbol=document.getElementById('category-status-symbol');if(symbol)symbol.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8">'+categoryIcons[active?'CircleCheck':'CircleOff']+'</svg>';
 let info=document.getElementById('category-status-context');if(!info){info=categoryElement('div');info.id='category-status-context';title.after(info);}info.replaceChildren(categoryElement('strong',record.categoryName),categoryElement('p',record.categoryCode+' / '+record.productCount+' direct products'+(!record.parentCategoryId?' / '+categoryListState.records.filter(c=>c.parentCategoryId===record.categoryId).length+' subcategories':'')));
 const description=dialog.querySelector(':scope > p');description.textContent=active?'This category will become available for new product registration when its parent is also active.':'This category will no longer be available for new product registration.';
 if(!active){if(!record.parentCategoryId)info.append(categoryElement('p','Its subcategories will also become unavailable for new product registration while this parent is inactive.'));info.append(categoryElement('p','Existing products will not be deleted. Stock quantities and historical purchases and sales remain unchanged. You can activate this category again later.'));}
 cancel.onclick=()=>dialog.close();confirm.onclick=async()=>{confirm.disabled=cancel.disabled=true;confirm.setAttribute('aria-busy','true');confirm.textContent=active?'Activating?':'Deactivating?';try{await categoryListState.requestJson('/api/categories/'+record.categoryId+'/status',{method:'POST',body:JSON.stringify({isActive:active})});dialog.close();await categoryListState.reload();categoryListState.show(active?'Category activated.':'Category deactivated.');}catch(error){categoryListState.show(error.message,true);}finally{confirm.disabled=cancel.disabled=false;confirm.removeAttribute('aria-busy');confirm.textContent=active?'Activate Category':'Deactivate Category';}};dialog.showModal();cancel.focus();
}
function renderCategoryRows(){
 categoryTooltip?.remove();categoryTooltip=null;
 if(!categoryListState)return;const {records,openEditor}=categoryListState,root=document.getElementById('master-data'),canEdit=root.dataset.canEdit==='true',canStatus=root.dataset.canDisable==='true';
 const byId=new Map(records.map(c=>[c.categoryId,c])),children=new Map();for(const c of records)if(c.parentCategoryId){if(!children.has(c.parentCategoryId))children.set(c.parentCategoryId,[]);children.get(c.parentCategoryId).push(c);}
 const roots=records.filter(c=>!c.parentCategoryId),filter=document.getElementById('category-main-filter'),selected=filter.value;
 const term=document.getElementById('master-search').value.trim().toLowerCase(),status=document.getElementById('status-filter').value,key=[selected,term,status].join('|'),filterChanged=key!==categoryGroupFilter;if(filterChanged){categoryPage=0;categoryGroupFilter=key;if(!term&&categorySearchTerm)categoryGroupsExpanded.clear();categorySearchTerm=term;}
 const matches=c=>(status==='false'||(status==='true'?c.isActive:!c.isActive))&&(!term||[c.categoryName,c.categoryCode].some(value=>value?.toLowerCase().includes(term)));
 const visibleRoots=roots.filter(c=>(!selected||c.categoryId===Number(selected))&&(matches(c)||(children.get(c.categoryId)||[]).some(matches)));
 const body=document.getElementById('master-rows');body.replaceChildren();const head=document.createElement('tr');for(const text of ['Category / Subcategory','Category Code','Products','Status','Actions'])head.append(categoryElement('th',text));document.getElementById('master-head').replaceChildren(head);
 if(records.some(c=>c.parentCategoryId&&byId.get(c.parentCategoryId)?.parentCategoryId)){categoryListState.show('Apply manual migration 065 before managing the two-level category hierarchy.',true);return;}
 const pages=Math.max(1,Math.ceil(visibleRoots.length/categoryPageSize));categoryPage=Math.min(categoryPage,pages-1);
 function row(record,isParent){const tr=categoryElement('tr',null,isParent?'category-group-row':'category-subcategory-row');tr.dataset.recordId=record.categoryId;const cell=categoryElement('td');if(isParent){const kids=children.get(record.categoryId)||[];if(kids.length){const toggle=categoryElement('button',categoryGroupsExpanded.has(record.categoryId)?'\u2304':'\u203a','category-group-toggle');toggle.type='button';toggle.dataset.categoryToggle=record.categoryId;toggle.setAttribute('aria-expanded',String(categoryGroupsExpanded.has(record.categoryId)));toggle.setAttribute('aria-label',(categoryGroupsExpanded.has(record.categoryId)?'Collapse ':'Expand ')+record.categoryName);toggle.onclick=()=>{if(categoryGroupsExpanded.has(record.categoryId))categoryGroupsExpanded.delete(record.categoryId);else categoryGroupsExpanded.add(record.categoryId);renderCategoryRows();document.querySelector('[data-category-toggle="'+record.categoryId+'"]')?.focus({preventScroll:true});};cell.append(toggle);}cell.append(categoryElement('strong',record.categoryName),categoryElement('small',kids.length?kids.length+' subcategories':'No subcategories added.','category-child-count'));}else{cell.style.paddingLeft='38px';cell.append(categoryElement('span',record.categoryName));}
 tr.append(cell,categoryElement('td',record.categoryCode),categoryElement('td',String(isParent?(Number(record.productCount)||0)+(children.get(record.categoryId)||[]).reduce((n,c)=>n+(Number(c.productCount)||0),0):(Number(record.productCount)||0))));
 const state=categoryElement('td');state.append(categoryElement('span',record.isActive?'Active':'Inactive',record.isActive?'category-status-active':'category-status-inactive'));if(record.isActive&&record.isEffectivelyActive===false)state.append(categoryElement('small','Parent inactive','category-child-count'));tr.append(state);
 const actions=categoryElement('td',null,'category-list-actions'),buttons=categoryElement('div',null,'category-row-actions');if(canEdit)buttons.append(categoryIconAction('Pencil','Edit Category',()=>openEditor(record.categoryId)));if(isParent){const add=categoryAddAction(record);if(add)buttons.append(add);}if(canStatus)buttons.append(categoryIconAction(record.isActive?'CircleOff':'CircleCheck',record.isActive?'Deactivate Category':'Activate Category',()=>categoryStatus(record)));actions.append(buttons);tr.append(actions);body.append(tr);}
 for(const parent of visibleRoots.slice(categoryPage*categoryPageSize,(categoryPage+1)*categoryPageSize)){const kids=children.get(parent.categoryId)||[];if(filterChanged&&(term||status!=='false')&&kids.some(matches))categoryGroupsExpanded.add(parent.categoryId);row(parent,true);if(categoryGroupsExpanded.has(parent.categoryId))for(const child of kids.filter(c=>matches(parent)||matches(c)))row(child,false);}
 if(!visibleRoots.length){const empty=categoryElement('tr');empty.id='category-list-empty';const cell=categoryElement('td',records.length?'No matching categories.':'No categories added yet.');cell.colSpan=5;empty.append(cell);body.append(empty);}
 document.getElementById('category-list-count').textContent=visibleRoots.length+' parent categories';categoryPager?.set({total:visibleRoots.length,page:categoryPage+1,size:categoryPageSize});
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
        categoryName:'Category / Subcategory',categoryCode:'Category Code'
    },
    descriptionTooltip: true,
    onReady(controls, editorActions) {
        if(!window.StockFlowMasterScreens?.page)document.body.classList.add('sf-category-list');
        const symbol=categoryElement('div',null,'category-status-symbol');symbol.id='category-status-symbol';symbol.setAttribute('aria-hidden','true');document.getElementById('deactivate-title').before(symbol);
        const table=document.querySelector('#master-list .table-responsive');table.classList.add('category-list-table');
        document.querySelector('.configuration-heading h2').textContent='Category Management';document.querySelector('#master-list>h3').hidden=true;
        const search=document.getElementById('master-search');search.placeholder='Search by category name or code...';document.querySelector('label[for="master-search"]').textContent='Search Categories';search.closest('.col-md-8').className='col-md-5';
        const status=document.getElementById('status-filter');status.closest('.col-md-4').className='col-md-3';status.options[0].textContent='All Status';status.options[1].textContent='Active';status.add(new Option('Inactive','inactive'));
        const group=categoryElement('div',null,'col-md-4');categoryParentFilter(group);status.closest('.col-md-3').before(group);
        const footer=categoryElement('div',null,'category-list-footer'),count=categoryElement('span');count.id='category-list-count';count.setAttribute('aria-live','polite');
        footer.append(count);table.after(footer);const paging=categoryElement('nav',null,'category-pagination');paging.setAttribute('aria-label','Category pages');for(const [id,text,delta] of [['category-page-prev','Previous',-1],['category-page-next','Next',1]]){const b=categoryElement('button',text,'btn btn-sm');b.id=id;b.type='button';b.onclick=()=>{categoryPage+=delta;renderCategoryRows();};paging.append(b);}const pageCount=categoryElement('span');pageCount.id='category-page-count';paging.append(pageCount);footer.append(paging);
        document.getElementById('master-list').dataset.sfPaginationRemote='true';
        categoryPager=StockFlowPagination.create({host:footer,label:'parent categories',legacy:{range:'#category-list-count',previous:'#category-page-prev',next:'#category-page-next',position:'#category-page-count'},onChange(state){categoryPage=state.page-1;categoryPageSize=state.size;renderCategoryRows();}});
        const parent=controls.parentCategoryId;parent.hidden=true;parent.tabIndex=-1;parent.add(new Option('None',''));
        window.CategoryCascades.bind(controls,editorActions.openEditor);
        const preview=categoryElement('section',null,'col-12 category-path-preview');
        preview.append(categoryElement('strong','Category Hierarchy'));const path=categoryElement('output');path.id='category-path-preview';path.setAttribute('aria-live','polite');
        const level=categoryElement('p',null,'small text-muted');level.id='category-level-preview';preview.append(path,level);document.getElementById('form-fields').append(preview);
        const update=()=>{path.textContent=[parent.value?parent.selectedOptions[0]?.textContent:'',controls.categoryName.value.trim()].filter(Boolean).join(' / ')||'Enter a category name';level.textContent='Level '+((parent.value?Number(parent.selectedOptions[0]?.dataset.level)||1:0)+1)+(parent.value?' - created under the selected parent.':' - main category.');};
        controls.categoryName.addEventListener('input',update);parent.addEventListener('change',update);
        window.CategoryRegistration?.bind(controls);
    },
    renderList(state) {
        const {records}=state;
        try{const saved=JSON.parse(sessionStorage.getItem('stockflow.category-saved')||'null');if(saved){sessionStorage.removeItem('stockflow.category-saved');if(saved.parentId)categoryGroupsExpanded.add(saved.parentId);const message=document.getElementById('master-message');message.textContent='Category saved successfully.';message.className='alert alert-success';}}catch{}
        categoryListState=state;document.getElementById('category-main-filter').parentElement.refreshCategoryFilter();renderCategoryRows();
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
    onSaved(result,body){if(window.StockFlowMasterScreens?.categoryPopup){StockFlowMasterScreens.complete({...body,...result});return;}try{sessionStorage.setItem('stockflow.category-saved',JSON.stringify({parentId:body.parentCategoryId}));}catch{}},
    beforeSave(body) { return window.CategoryRegistration?.beforeSave(body) ?? true; }
});
