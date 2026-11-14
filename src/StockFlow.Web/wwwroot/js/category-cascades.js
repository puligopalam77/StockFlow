// Both category creation actions share the existing parent ID and validation flow.
window.CategoryCascades=(()=>{
    const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
    let openEditor,presetParent=null,controls,rows=[],byId=new Map(),record=null,group,details,summary,search,results,panel,locked=false;
    const params=new URLSearchParams(location.search);
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
        summary.textContent=item?.categoryPath||item?.categoryName||'Main Category (No Parent)';details.open=false;
        if(details.isConnected)summary.focus();
    }
    function render(){
        const term=search.value.trim().toLowerCase();const matches=rows.filter(c=>canParent(c)&&(!term||(c.categoryPath||c.categoryName).toLowerCase().includes(term)));
        search.hidden=rows.filter(canParent).length<=10;results.replaceChildren();{const none=node('button','Main Category (No Parent)','category-parent-option');none.type='button';none.disabled=locked;none.setAttribute('aria-pressed',String(!controls.parentCategoryId.value));none.onclick=()=>choose(null);results.append(none);}
        for(const item of matches){const button=node('button',item.categoryPath||item.categoryName,'category-parent-option');button.type='button';button.disabled=locked;button.setAttribute('aria-pressed',String(Number(controls.parentCategoryId.value)===item.categoryId));button.onclick=()=>choose(item);results.append(button);}
        if(!matches.length)results.append(node('p','No eligible parent categories found.'));
    }
    function bind(value,open){openEditor=open;
        controls=value;group=controls.parentCategoryId.closest('.col-md-6');group.id='category-parent-picker';group.hidden=false;group.querySelector('label').textContent='Under Category (Optional)';
        details=node('details',null,'category-parent-details');summary=node('summary','Select parent category','form-select');summary.setAttribute('aria-label','Under Category (Optional)');details.append(summary);
        panel=node('div',null,'category-parent-panel');search=node('input',null,'form-control');search.type='search';search.placeholder='Search categories';search.setAttribute('aria-label','Find parent category');
        results=node('div',null,'category-parent-results');results.id='category-parent-options';summary.setAttribute('aria-controls',results.id);summary.setAttribute('aria-expanded','false');panel.append(search,results);details.append(panel);group.append(details);
        search.oninput=()=>{render();position();};
        function position(){
            if(!details.open)return;
            const r=summary.getBoundingClientRect(),footer=details.closest('dialog')?.querySelector('footer')?.getBoundingClientRect(),gap=4;
            // Float over form fields without changing grid rows or the dialog height.
            const bottom=Math.min(innerHeight-8,footer?.top??innerHeight-8),below=Math.max(0,bottom-r.bottom-gap),above=Math.max(0,r.top-8-gap);
            const desired=Math.min(240,panel.scrollHeight),up=below<desired&&above>below;
            panel.style.maxHeight=Math.min(240,up?above:below)+'px';
            panel.style.width=Math.min(r.width,innerWidth-24)+'px';
            panel.style.left=Math.max(12,Math.min(r.left,innerWidth-panel.offsetWidth-12))+'px';
            panel.style.top=(up?r.top-panel.offsetHeight-gap:r.bottom+gap)+'px';
            details.dataset.placement=up?'above':'below';
        }
        details.addEventListener('toggle',()=>{summary.setAttribute('aria-expanded',String(details.open));if(details.open){if(locked){details.open=false;return;}render();position();if(!search.hidden)search.focus();else results.querySelector('button')?.focus();}});
        details.addEventListener('keydown',event=>{if(event.key==='Escape'&&details.open){event.preventDefault();event.stopPropagation();details.open=false;summary.focus();}else if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();if(!details.open){details.open=true;return;}const options=[...results.querySelectorAll('button:not(:disabled)')],at=options.indexOf(document.activeElement),index=event.key==='Home'?0:event.key==='End'?options.length-1:event.key==='ArrowDown'?Math.min(at+1,options.length-1):Math.max(0,at-1);options[index]?.focus();}});
        document.addEventListener('pointerdown',event=>{if(!details.contains(event.target))details.open=false;});window.addEventListener('resize',position);document.addEventListener('scroll',event=>{if(details.open&&!panel.contains(event.target))position();},true);
        controls.parentCategoryId.addEventListener('invalid',()=>{if(!group.hidden){details.open=true;summary.focus();}});
        el('category-add-subcategory')?.addEventListener('click',()=>createUnder());
    }
    function load(all,item){
        rows=all;byId=new Map(rows.map(c=>[c.categoryId,c]));record=item;search.value='';locked=!!item;
        group.querySelector('label').textContent='Under Category (Optional)';summary.setAttribute('aria-required','false');group.hidden=false;controls.parentCategoryId.required=false;controls.parentCategoryId.disabled=locked;details.open=false;summary.style.pointerEvents=locked?'none':'';summary.setAttribute('aria-disabled',String(locked));summary.tabIndex=locked?-1:0;
        summary.title=locked?'Existing parent relationships are preserved. Moving categories requires a reviewed migration.':'';
        const requested=item?.parentCategoryId??presetParent??(!item&&mode()==='subcategory'?Number(params.get('parentId'))||null:null),parent=rows.find(c=>c.categoryId===requested);presetParent=null;
        choose(item?(parent||(item.parentCategoryId?{categoryId:item.parentCategoryId,categoryName:item.parentCategoryName||'Existing parent (unavailable)'}:null)):(canParent(parent)?parent:null));render();
        window.CategoryRegistration.markClean();
    }
    async function createUnder(id){
        if(!window.StockFlowMasterScreens?.page){presetParent=id||null;return openEditor();}
        if(window.StockFlowMasterScreens?.page&&window.CategoryRegistration.hasChanges()&&!await StockFlowDialog.confirm('Discard unsaved category changes?'))return;
        window.StockFlowMasterScreens.navigate('Create',null,{kind:'subcategory',parentId:id});
    }
    function validate(){
        if(controls.parentCategoryId.value&&!record&&!canParent(rows.find(c=>c.categoryId===Number(controls.parentCategoryId.value)))){const target=el('editor-message');target.textContent='Select an active parent category with room for another level.';target.classList.remove('d-none');target.classList.add('alert-danger');details.open=true;summary.focus();return false;}return true;
    }
    return {bind,load,createUnder,canParent,validate};
})();
