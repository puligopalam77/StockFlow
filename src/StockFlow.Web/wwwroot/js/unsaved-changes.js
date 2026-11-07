// Derived from SchoolERP's form snapshots and replayed in-app navigation.
window.StockFlowUnsaved = (() => {
    const states=new Map();let replaying=false,approved=false;
    const snapshot=root=>JSON.stringify([...root.querySelectorAll('input,select,textarea')]
        .filter(field=>!field.dataset.sfAddressTransient&&!['hidden','submit','button','reset','password'].includes(field.type))
        .map((field,index)=>[field.id||field.name||index,field.type==='file'?[...field.files].map(file=>[file.name,file.size,file.lastModified]):['checkbox','radio'].includes(field.type)?field.checked:field.value]));
    const eligible=form=>!form.matches('[data-skip-unsaved-warning], [role="search"], [id$="-search"], [id$="-filter"], #global-search, #lookup-form, #comparison-form')&&form.querySelector('button[type="submit"],input[type="submit"]');
    function remember(root){if(root)states.set(root,{...states.get(root),initial:snapshot(root),changed:false});}
    function dirty(root){const state=states.get(root),dialog=root.closest('dialog');return root.isConnected&&(!dialog||dialog.open)&&(state?.getDirty?state.getDirty():state?.changed&&state.initial!==snapshot(root));}
    function discard(root){states.get(root)?.onDiscard?.();remember(root);}
    const changed=()=>[...states.keys()].filter(dirty);
    // Only the copy varies by feature; snapshots and navigation replay stay shared.
    function discardMessage(roots){
        const contexts=[...new Set(roots.map(root=>{
            const labelledBy=root.getAttribute('aria-labelledby');
            return (root.dataset.unsavedContext||root.getAttribute('aria-label')||
                (labelledBy&&document.getElementById(labelledBy)?.textContent)||
                root.closest('dialog')?.querySelector('h1,h2,h3')?.textContent||
                document.getElementById('master-page-title')?.textContent||document.querySelector('main h1,h1')?.textContent||'')
                .trim().replace(/\s+/g,' ').slice(0,100);
        }).filter(Boolean))];
        return contexts.length===1?`You have unsaved changes in ${contexts[0]}. Leaving now will discard them.`:'You have unsaved changes. Leaving now will discard them.';
    }
    function discover(){document.querySelectorAll('form').forEach(form=>{if(!states.has(form)&&eligible(form))remember(form);});}
    document.addEventListener('focusin',event=>{const form=event.target.closest('form');if(!form||!eligible(form))return;const state=states.get(form);if(!state?.changed)remember(form);});
    for(const type of ['input','change'])document.addEventListener(type,event=>{const form=event.target.closest('form');if(!form||!eligible(form)||event.target.dataset.sfAddressTransient||event.target.disabled||event.target.readOnly||event.target.type==='hidden')return;if(!states.has(form))remember(form);states.get(form).changed=true;});
    document.addEventListener('reset',event=>queueMicrotask(()=>remember(event.target)));
    document.addEventListener('click',async event=>{
        if(replaying||event.defaultPrevented||event.target.closest('#sf-confirm-dialog'))return;
        const control=event.target.closest('a[href],button');if(!control||control.disabled||control.hasAttribute('data-local-cancel'))return;
        let leaving=false;
        if(control.matches('a[href]')){
            const href=control.getAttribute('href');if(!href||href.startsWith('#')||/^(javascript:|mailto:|tel:)/i.test(href)||control.target==='_blank'||control.hasAttribute('download')||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;
            const url=new URL(control.href,location.href);leaving=url.pathname!==location.pathname||url.search!==location.search||url.origin!==location.origin;
        }else leaving=control.type!=='submit'&&(control.matches('#cancel-edit,#close-editor,#purchase-registration-cancel,[data-registration-close],[data-smart-back],[data-bs-dismiss="modal"]')||/^(back|cancel|close)$/i.test(control.textContent.trim()));
        if(!leaving)return;
        const roots=changed().filter(root=>control.matches('a[href]')||root===control.closest('form')||control.closest('dialog')?.contains(root));
        if(!roots.length)return;
        event.preventDefault();event.stopImmediatePropagation();
        if(roots.some(root=>root.dataset.saving==='true'))return;
        if(!await StockFlowDialog.discard(discardMessage(roots)))return;
        roots.forEach(discard);replaying=true;try{control.click();}finally{replaying=false;}
    },true);
    document.addEventListener('cancel',async event=>{
        const roots=changed().filter(root=>event.target.contains(root));if(!roots.length||event.target.id==='sf-confirm-dialog')return;
        event.preventDefault();event.stopImmediatePropagation();if(roots.some(root=>root.dataset.saving==='true'))return;if(await StockFlowDialog.discard(discardMessage(roots))){roots.forEach(discard);event.target.close();}
    },true);
    document.addEventListener('submit',event=>queueMicrotask(()=>{if(!event.defaultPrevented&&event.target.method.toLowerCase()==='post')approved=true;}));
    window.addEventListener('beforeunload',event=>{if(!approved&&changed().length){event.preventDefault();event.returnValue='';}});
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',discover);else discover();
    return {markClean:remember,markSaved:remember,register(root,getDirty,onDiscard){remember(root);Object.assign(states.get(root),{getDirty,onDiscard});},allowNavigation(){approved=true;},hasChanges:root=>dirty(root),async confirmNavigation(){const roots=changed();if(roots.some(root=>root.dataset.saving==='true'))return false;return !roots.length||await StockFlowDialog.discard(discardMessage(roots));}};
})();
