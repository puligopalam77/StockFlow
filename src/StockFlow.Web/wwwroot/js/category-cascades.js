// Both category creation actions share the existing parent ID and validation flow.
window.CategoryCascades=(()=>{
    const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
    let controls,rows=[],byId=new Map(),record=null,group,details,summary,search,results,count,previous,next,offset=0,locked=false;
    const limit=20,params=new URLSearchParams(location.search);
    const mode=()=>params.get('kind')==='subcategory'?'subcategory':'parent';
    function canParent(item){
        if(!item||!item.isActive||item.parentCategoryId||item.isEffectivelyActive===false)return false;
        const seen=new Set();let current=item;
        while(current){if(seen.has(current.categoryId)||current.categoryId===record?.categoryId||current.isActive===false)return false;seen.add(current.categoryId);if(seen.size>=3)return false;if(!current.parentCategoryId)break;current=byId.get(current.parentCategoryId);if(!current)return false;}
        return true;
    }
    function choose(item){
        controls.parentCategoryId.replaceChildren(new Option(item?.categoryPath||item?.categoryName||'None',item?.categoryId||''));
        controls.parentCategoryId.value=String(item?.categoryId||'');controls.parentCategoryId.dispatchEvent(new Event('change'));
        summary.textContent=item?.categoryPath||item?.categoryName||'Select parent category';details.open=false;
        if(details.isConnected)summary.focus();
    }
    function render(){
        const term=search.value.trim().toLowerCase();const matches=rows.filter(c=>canParent(c)&&(!term||(c.categoryPath||c.categoryName).toLowerCase().includes(term)));
        results.replaceChildren();if(record){const none=node('button','None (top-level category)','category-parent-option');none.type='button';none.disabled=locked;none.onclick=()=>choose(null);results.append(none);}
        for(const item of matches.slice(offset,offset+limit)){const button=node('button',item.categoryPath||item.categoryName,'category-parent-option');button.type='button';button.disabled=locked;button.setAttribute('aria-pressed',String(Number(controls.parentCategoryId.value)===item.categoryId));button.onclick=()=>choose(item);results.append(button);}
        if(!matches.length)results.append(node('p','No eligible parent categories found.'));
        count.textContent=matches.length?(offset+1)+'-'+Math.min(offset+limit,matches.length)+' of '+matches.length:'0 matches';previous.disabled=offset===0;next.disabled=offset+limit>=matches.length;
    }
    function bind(value){
        controls=value;group=controls.parentCategoryId.closest('.col-md-6');group.id='category-parent-picker';group.hidden=false;group.querySelector('label').textContent='Select Parent Category *';
        details=node('details',null,'category-parent-details');summary=node('summary','Select parent category','form-select');summary.setAttribute('aria-label','Select Parent Category');details.append(summary);
        const panel=node('div',null,'category-parent-panel');search=node('input',null,'form-control');search.type='search';search.placeholder='Search category name or path';search.setAttribute('aria-label','Find parent category');
        results=node('div',null,'category-parent-results');const footer=node('div',null,'category-parent-paging');count=node('span');count.setAttribute('role','status');previous=node('button','Previous','btn btn-outline-secondary btn-sm');next=node('button','Next','btn btn-outline-secondary btn-sm');for(const b of [previous,next])b.type='button';previous.onclick=()=>{offset=Math.max(0,offset-limit);render();};next.onclick=()=>{offset+=limit;render();};footer.append(count,previous,next);panel.append(search,results,footer);details.append(panel);group.append(details);
        search.oninput=()=>{offset=0;render();};details.addEventListener('toggle',()=>{if(details.open){render();search.focus();}});details.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();details.open=false;summary.focus();}});
        controls.parentCategoryId.addEventListener('invalid',()=>{if(!group.hidden){details.open=true;summary.focus();}});
        el('category-add-subcategory')?.addEventListener('click',()=>createUnder());
    }
    function load(all,item){
        rows=all;byId=new Map(rows.map(c=>[c.categoryId,c]));record=item;offset=0;search.value='';locked=!!item;
        group.querySelector('label').textContent=item?'Parent Category':'Select Parent Category *';summary.setAttribute('aria-required',String(!item&&mode()==='subcategory'));group.hidden=item?!item.parentCategoryId:mode()==='parent';controls.parentCategoryId.required=false;controls.parentCategoryId.disabled=locked;if(locked)details.open=false;summary.style.pointerEvents=locked?'none':'';summary.setAttribute('aria-disabled',String(locked));summary.tabIndex=locked?-1:0;
        const requested=item?.parentCategoryId??(!item&&mode()==='subcategory'?Number(params.get('parentId'))||null:null),parent=rows.find(c=>c.categoryId===requested);
        choose(item?parent:(canParent(parent)?parent:null));
        if(group.hidden)choose(null);render();
        if(!item&&mode()==='subcategory'&&!rows.some(canParent)){const text=el('editor-message');text.textContent='Add an active parent category before creating a subcategory.';text.classList.remove('d-none');text.classList.add('alert-danger');}
        window.CategoryRegistration.markClean();
    }
    async function createUnder(id){
        if(window.StockFlowMasterScreens?.page&&window.CategoryRegistration.hasChanges()&&!await StockFlowDialog.confirm('Discard unsaved category changes?'))return;
        window.StockFlowMasterScreens.navigate('Create',null,{kind:'subcategory',parentId:id});
    }
    function validate(){
        if(!group.hidden&&!record&&!canParent(rows.find(c=>c.categoryId===Number(controls.parentCategoryId.value)))){const target=el('editor-message');target.textContent='Select an active parent category with room for another level.';target.classList.remove('d-none');target.classList.add('alert-danger');details.open=true;summary.focus();return false;}return true;
    }
    return {bind,load,createUnder,canParent,validate};
})();
