// Flag-style operation feedback, following SchoolERP's toast presentation.
window.StockFlowNotify = (() => {
    const entries=new Map();let host,serial=0;
    const supportsPopover=typeof HTMLElement.prototype.showPopover==='function';
    const titles={success:'Success',error:'Error',warning:'Warning',info:'Information'};
    const paths={success:'m5 12 4 4L19 6',error:'M9 9l6 6m0-6-6 6M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0',warning:'M12 3 2 21h20L12 3ZM12 9v5m0 3v1',info:'M12 11v6m0-10v1M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0'};
    function place(){
        if(!host){host=document.createElement('section');host.id='sf-notifications';host.className='sf-notifications';host.setAttribute('aria-label','Notifications');if(supportsPopover)host.setAttribute('popover','manual');}
        const modal=document.activeElement?.closest('dialog:modal')||[...document.querySelectorAll('dialog[open]')].filter(d=>d.matches(':modal')).at(-1);
        const parent=modal||document.body;
        if(host.parentElement!==parent){if(supportsPopover&&host.matches(':popover-open'))host.hidePopover();parent.append(host);}
        if(entries.size&&host.showPopover&&!host.matches(':popover-open'))host.showPopover();
    }
    function dismiss(key){const entry=entries.get(key);if(!entry)return;clearTimeout(entry.timer);entry.card.remove();entries.delete(key);if(!entries.size&&supportsPopover&&host?.matches(':popover-open'))host.hidePopover();}
    function schedule(entry){clearTimeout(entry.timer);if(entry.duration&&!entry.pinned&&!entry.paused)entry.timer=setTimeout(()=>dismiss(entry.key),entry.duration);}
    function show(type,message,options={}){
        if(!titles[type])type='info';message=String(message||'').trim();const key=options.key||'notification-'+(++serial);
        if(!message){dismiss(key);return null;}
        const old=entries.get(key);if(old&&old.message===message&&old.type===type){schedule(old);return old.card;}
        dismiss(key);
        const card=document.createElement('article');card.className='sf-toast sf-toast-'+type;card.dataset.notificationKey=key;card.setAttribute('role',type==='error'||type==='warning'?'alert':'status');card.setAttribute('aria-atomic','true');
        const flag=document.createElement('span');flag.className='sf-toast-flag';flag.setAttribute('aria-hidden','true');const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'),path=document.createElementNS(svg.namespaceURI,'path');svg.setAttribute('viewBox','0 0 24 24');path.setAttribute('d',paths[type]);svg.append(path);flag.append(svg);
        const copy=document.createElement('div');copy.className='sf-toast-copy';const title=document.createElement('strong');title.textContent=options.title||titles[type];const text=document.createElement('p');copy.append(title,text);
        if(message.length>180){text.textContent=message.slice(0,177)+'\u2026';const details=document.createElement('details'),summary=document.createElement('summary'),full=document.createElement('p');summary.textContent='Show details';full.textContent=message;details.append(summary,full);copy.append(details);details.addEventListener('toggle',()=>{entry.paused=details.open;schedule(entry);});}else text.textContent=message;
        const close=document.createElement('button');close.type='button';close.className='sf-toast-close';close.setAttribute('aria-label','Dismiss '+titles[type].toLowerCase()+' notification');close.textContent='\u00d7';close.onclick=()=>dismiss(key);card.append(flag,copy,close);
        const entry={key,card,copy,message,type,duration:options.duration??(type==='error'?0:type==='warning'?8000:5000),pinned:false,paused:false};
        card.addEventListener('mouseenter',()=>{entry.paused=true;clearTimeout(entry.timer);});card.addEventListener('mouseleave',()=>{entry.paused=card.matches(':focus-within')||!!card.querySelector('details[open]');schedule(entry);});card.addEventListener('focusin',()=>{entry.paused=true;clearTimeout(entry.timer);});card.addEventListener('focusout',()=>{entry.paused=!!card.querySelector('details[open]');schedule(entry);});
        entries.set(key,entry);place();host.append(card);while(entries.size>3)dismiss(entries.keys().next().value);schedule(entry);return card;
    }
    function action(key,button){const entry=entries.get(key);if(!entry)return false;entry.pinned=true;clearTimeout(entry.timer);entry.copy.append(button);return true;}
    // Compatibility for existing passive result banners. Validation, actionable
    // warnings and result panels remain at their source; no business state changes.
    const seen=new WeakMap(),tracked=new Set();let scheduled=false;
    const sources='.alert[id$="-message"],.alert[id$="-error"],[data-flash-notification],.alert-success:not([id]),.alert-danger:not([id])';
    function migrate(){
        scheduled=false;
        for(const source of new Set([...document.querySelectorAll(sources),...tracked])){
            if(!source.isConnected||!source.matches(sources)||source.closest('#sf-notifications')||source.matches('#editor-message,.registration-alert,.sf-validation-summary,[data-valmsg-summary],.validation-summary-errors,.validation-summary-valid,[data-keep-banner]')||source.querySelector('input,select,textarea,button,a,table,details')){
                if(tracked.has(source)){dismiss(source.id||source.dataset.toastKey);source.classList.remove('sf-toast-source');tracked.delete(source);seen.delete(source);}continue;
            }
            const message=source.textContent.trim(),key=source.id||source.dataset.toastKey||(source.dataset.toastKey='flash-'+(++serial));
            if(!message){dismiss(key);seen.delete(source);continue;}
            const dialog=source.closest('dialog');if(source.closest('[hidden],.d-none')||source.style.display==='none'||dialog&&!dialog.open){dismiss(key);seen.delete(source);continue;}
            const type=source.classList.contains('alert-danger')?'error':source.classList.contains('alert-warning')?'warning':source.classList.contains('alert-success')?'success':'info';const signature=type+'|'+message;
            if(seen.get(source)!==signature){seen.set(source,signature);tracked.add(source);show(type,message,{key});}
            if(!source.classList.contains('sf-toast-source'))source.classList.add('sf-toast-source');
        }
        if(entries.size)place();
    }
    function start(){migrate();new MutationObserver(records=>{for(const record of records){if(record.type!=='attributes'){const element=record.target.nodeType===1?record.target:record.target.parentElement;const source=element?.closest(sources);if(source&&!source.closest('#sf-notifications'))seen.delete(source);}}if(!scheduled){scheduled=true;queueMicrotask(migrate);}}).observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class','hidden','open','style']});}
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
    return {show,dismiss,action};
})();
