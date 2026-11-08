// StockFlow confirmations follow SchoolERP's explicit action + continue-editing pattern.
window.StockFlowDialog = (() => {
    let dialog, pending, returnFocus;
    function finish(accepted){const resolve=pending;pending=null;dialog.close(accepted?'confirm':'cancel');if(returnFocus?.isConnected)returnFocus.focus();resolve?.(accepted);}
    function ensure(){
        if(dialog)return;
        dialog=document.createElement('dialog');dialog.id='sf-confirm-dialog';dialog.dataset.sfDialog='compact';dialog.className='sf-confirm-dialog';
        dialog.setAttribute('aria-labelledby','sf-confirm-title');dialog.setAttribute('aria-describedby','sf-confirm-description');
        dialog.innerHTML='<button type="button" class="sf-confirm-close" aria-label="Close confirmation">&times;</button><div class="sf-confirm-symbol" aria-hidden="true">!</div><h2 id="sf-confirm-title"></h2><p id="sf-confirm-description"></p><div class="sf-confirm-actions"><button type="button" id="sf-confirm-cancel" class="btn btn-dark"></button><button type="button" id="sf-confirm-accept" class="btn btn-primary"></button></div>';
        document.body.append(dialog);
        dialog.querySelector('.sf-confirm-close').onclick=()=>finish(false);
        dialog.querySelector('#sf-confirm-cancel').onclick=()=>finish(false);
        dialog.querySelector('#sf-confirm-accept').onclick=()=>finish(true);
        dialog.addEventListener('cancel',event=>{event.preventDefault();finish(false);});
        dialog.addEventListener('keydown',event=>{
            if(event.key!=='Tab')return;
            const buttons=[...dialog.querySelectorAll('button:not(:disabled):not([hidden])')].filter(button=>button.getClientRects().length);
            const first=buttons[0],last=buttons.at(-1);
            if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
            else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
        });
        dialog.addEventListener('close',()=>{if(pending&&!dialog.open){const resolve=pending;pending=null;returnFocus?.isConnected&&returnFocus.focus();resolve(dialog.returnValue==='confirm');}});
    }
    function confirm(message,options={}){
        if(typeof message==='object'){options=message;message=options.message;}
        const discardAction=options.kind==='discard'||/^Discard unsaved/i.test(message||'');
        const removeAction=/^Remove this/i.test(message||'');
        // Reject duplicate actions while a decision is pending; never approve them implicitly.
        if(pending)return Promise.resolve(false);
        ensure();returnFocus=document.activeElement;dialog.returnValue='';
        dialog.dataset.kind=discardAction?'discard':'confirmation';
        dialog.querySelector('.sf-confirm-symbol').innerHTML=discardAction?'<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 3.9 2.6 17.2A2 2 0 0 0 4.3 20h15.4a2 2 0 0 0 1.7-2.8L13.7 3.9a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4m0 3h.01"/></svg>':'!';
        dialog.querySelector('h2').textContent=options.title||(discardAction?'Discard unsaved changes?':removeAction?'Remove item?':'Confirm action');
        dialog.querySelector('p').textContent=message||'Do you want to continue?';
        dialog.querySelector('#sf-confirm-cancel').textContent=options.cancelText||(discardAction?'Continue Editing':'Cancel');
        dialog.querySelector('#sf-confirm-cancel').hidden=options.informational===true;
        dialog.querySelector('#sf-confirm-accept').textContent=options.confirmText||options.action||(discardAction?'Discard Changes':removeAction?'Remove':'Continue');
        // Presentation only: keep the existing decisions, labels and safe default focus.
        const destructive=removeAction||/^(delete|permanently delete|discard|remove)\b/i.test(options.confirmText||options.action||message||'');
        dialog.dataset.tone=discardAction?'warning':destructive?'danger':'information';
        dialog.querySelector('#sf-confirm-cancel').className=discardAction?'btn btn-dark':'btn btn-outline-secondary';
        dialog.querySelector('#sf-confirm-accept').className=discardAction?'btn btn-outline-danger':destructive?'btn btn-danger':'btn btn-dark';
        return new Promise(resolve=>{pending=resolve;dialog.showModal();dialog.querySelector(options.informational?'#sf-confirm-accept':'#sf-confirm-cancel').focus();});
    }
    const discard=(message='You have unsaved changes. Leaving now will discard them.',options={})=>confirm(message,{kind:'discard',title:'Discard unsaved changes?',cancelText:'Continue Editing',confirmText:'Discard Changes',...options});
    return {confirm,discard,info(message){return confirm(message,{title:'StockFlow',informational:true,confirmText:'OK'});}};
})();
