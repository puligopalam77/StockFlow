// Enhance existing elements in place. No replacement, click handler, route,
// permission, loading, disabled or confirmation logic is changed.
window.StockFlowActionIcons = (() => {
    const dense = '.category-icon-action,.configuration-row-action,.admin-row-action,.sales-action,.product-image-action,.goods-icon-button,.category-field-action,.variant-icon-button,.product-detail-remove';
    const contexts = 'table,.variant-row-actions,.variant-option-chip,.product-image-actions,.product-gallery-actions,.goods-item-actions,.billing-item-actions';
    const close = '.sf-dialog-close,.sf-confirm-close,.configuration-close,.goods-close,.bc-close,.billing-customer-x,.sf-toast-close';
    const selector = 'button,a,.sales-more > summary';
    const excluded = '#password-toggle,.sf-menu-toggle,.sf-sidebar-close,.sf-help-open,.sf-signin,[role="tab"],.product-gallery-thumbnail,.product-gallery-stage,.product-gallery-empty';
    const enhanced = new WeakSet();
    let tooltip, owner, timer, pendingOwner, touchUntil = 0;
    const name = node => node.getAttribute('aria-label') || node.getAttribute('title') || node.dataset.bsOriginalTitle || node.querySelector('.visually-hidden,.sr-only')?.textContent.trim();
    function text(node) {
        const walker=document.createTreeWalker(node,NodeFilter.SHOW_TEXT);let value='';
        while(walker.nextNode()) {
            const current=walker.currentNode;
            if(!current.parentElement.closest('svg,[hidden],.visually-hidden,.sr-only')) value+=current.textContent;
        }
        return value.trim();
    }
    function hide() {
        clearTimeout(timer);pendingOwner=null;
        tooltip?.remove();tooltip=null;
        if(owner){
            const ids=(owner.getAttribute('aria-describedby')||'').split(/\s+/).filter(id=>id&&id!=='sf-action-tooltip');
            if(ids.length)owner.setAttribute('aria-describedby',ids.join(' '));else owner.removeAttribute('aria-describedby');
        }
        owner=null;
    }
    function show(node) {
        hide();const label=name(node);if(!label||node.disabled||!node.isConnected||!node.getClientRects().length)return;
        owner=node;
        tooltip=document.createElement('span');tooltip.className='sf-action-tooltip';tooltip.id='sf-action-tooltip';
        tooltip.setAttribute('role','tooltip');tooltip.setAttribute('popover','manual');tooltip.textContent=label;
        document.body.append(tooltip);tooltip.showPopover?.();
        node.setAttribute('aria-describedby',[(node.getAttribute('aria-describedby')||''),tooltip.id].filter(Boolean).join(' '));
        const r=node.getBoundingClientRect(),t=tooltip.getBoundingClientRect();
        tooltip.style.left=Math.max(8,Math.min(innerWidth-t.width-8,r.left+(r.width-t.width)/2))+'px';
        tooltip.style.top=Math.max(8,r.top-t.height-8>=8?r.top-t.height-8:Math.min(innerHeight-t.height-8,r.bottom+8))+'px';
    }
    function enhance(node,{size='compact',label}={}) {
        if(label&&!node.hasAttribute('aria-label'))node.setAttribute('aria-label',label);
        if(!name(node))return node;
        node.classList.add('sf-action-icon');
        if(!node.dataset.actionSize)node.dataset.actionSize=size;
        if(!node.hasAttribute('aria-label'))node.setAttribute('aria-label',name(node));
        const sharedTooltip=!node.matches('.category-icon-action')&&!node.onpointerenter&&!window.bootstrap?.Tooltip?.getInstance(node);
        // Only shared-owned icon actions relinquish their redundant native title.
        // The accessible name is retained before removing it.
        if(sharedTooltip)node.removeAttribute('title');
        if(!enhanced.has(node)){
            enhanced.add(node);
            // Existing Bootstrap tooltips may sit over a neighboring compact
            // action. Keep their positioning/content, but never intercept taps.
            node.addEventListener('inserted.bs.tooltip',()=>{
                for(const id of (node.getAttribute('aria-describedby')||'').split(/\s+/)){
                    const tip=document.getElementById(id);
                    if(tip?.getAttribute('role')==='tooltip')tip.classList.add('sf-action-tooltip-passive');
                }
            });
            // Keep existing category/popover/Bootstrap tooltips; do not duplicate them.
            if(sharedTooltip) {
                node.addEventListener('pointerenter',event=>{
                    hide();if(event.pointerType==='touch'||Date.now()<touchUntil||node.disabled)return;
                    pendingOwner=node;timer=setTimeout(()=>show(node),180);
                });
                node.addEventListener('focus',()=>{if(Date.now()>=touchUntil)show(node);});
                const hideOwn=()=>{if(owner===node||pendingOwner===node)hide();};
                node.addEventListener('pointerleave',hideOwn);node.addEventListener('blur',hideOwn);
                node.addEventListener('pointerdown',hide);node.addEventListener('click',hide);
                node.addEventListener('keydown',event=>{if(event.key==='Escape')hide();});
            }
        }
        return node;
    }
    function scan(root=document) {
        const nodes=[...(root.matches?.(selector)?[root]:[]),...root.querySelectorAll(selector)];
        const parents=new Set();
        for(const node of nodes){
            if(node.matches(excluded)||node.closest('.sf-sidebar,.sf-topbar,.sf-help-open,.sf-help-panel'))continue;
            const visibleText=text(node),glyph=/^[×✕✖✎✏⋮]+$/.test(visibleText),icon=node.querySelector('svg');
            if(visibleText&&!glyph)continue;
            if(!icon&&!glyph)continue;
            const modalClose=node.matches(close)||node.closest('dialog')&&/^close\b/i.test(name(node)||'');
            if(!modalClose&&!node.matches(dense)&&!node.closest(contexts))continue;
            enhance(node,{size:modalClose?'standard':'compact'});parents.add(node.parentElement);
        }
        for(const parent of parents){
            if(parent.matches('td,th')){
                parent.classList.add('sf-action-cell');
                // Keep table-cell semantics. Group only icon-only cells, leaving
                // text links/other controls in their existing layout and order.
                const onlyActions=[...parent.childNodes].every(n=>n.nodeType===Node.TEXT_NODE?!n.textContent.trim():n instanceof Element&&(n.matches('.sf-action-icon,.sales-more,.sf-action-group')||n.hidden));
                if(onlyActions){
                    const nodes=[...parent.childNodes].flatMap(n=>n instanceof Element&&n.matches('.sf-action-group')?[...n.childNodes]:[n]);
                    const group=parent.querySelector(':scope > .sf-action-group')||document.createElement('span');group.className='sf-action-group';
                    group.append(...nodes);parent.replaceChildren(group);
                }
            }
            else if(parent.children.length>1&&[...parent.children].every(n=>n.matches('.sf-action-icon,.sales-more')||n.hidden))parent.classList.add('sf-action-group');
        }
    }
    let frame;const pending=new Set();
    new MutationObserver(changes=>{
        for(const change of changes){
            if(change.type==='attributes')pending.add(change.target);
            else for(const node of change.addedNodes)if(node instanceof Element&&!node.matches('.sf-action-tooltip'))pending.add(node);
        }
        if((owner&&(!owner.isConnected||owner.disabled))||(pendingOwner&&!pendingOwner.isConnected))hide();
        if(changes.some(change=>change.type==='attributes'&&change.attributeName==='open'))hide();
        if(!frame&&pending.size)frame=requestAnimationFrame(()=>{frame=null;const roots=[...pending];pending.clear();roots.forEach(node=>scan(node));});
    }).observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['aria-label','title','open','disabled']});
    document.addEventListener('pointerdown',event=>{if(event.pointerType==='touch')touchUntil=Date.now()+1000;hide();},true);
    document.addEventListener('keydown',event=>{if(event.key==='Escape')hide();},true);
    document.addEventListener('beforetoggle',event=>{if(event.target!==tooltip)hide();},true);
    addEventListener('pagehide',hide);addEventListener('popstate',hide);
    addEventListener('scroll',hide,true);addEventListener('resize',hide);
    scan();return {enhance,scan};
})();
