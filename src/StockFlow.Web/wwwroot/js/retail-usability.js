// Small form and navigation aids. Language preferences and saved values are never changed here.
(() => {
    'use strict';
    function start() {
        const guide=document.getElementById('retail-screen-guide');
        const main=document.getElementById('main-content');
        if (guide && main) {
            const configuration=main.querySelector('.configuration-content');
            let anchor=configuration?.querySelector('.configuration-heading') || main.querySelector('.retail-daily-actions') || main.querySelector('.sf-page-heading') || main.querySelector('h1');
            if (anchor) {
                if (anchor.tagName==='H1' && anchor.parentElement.tagName==='HEADER') anchor=anchor.parentElement;
                while (anchor.nextElementSibling?.matches('.sf-breadcrumb,p:not([role])')) anchor=anchor.nextElementSibling;
                anchor.after(guide);
            }
        }
        function prepare(root) {
            const inputs=root.matches?.('input[type="number"]') ? [root] : root.querySelectorAll?.('input[type="number"]') || [];
            inputs.forEach(input=>{ if (!input.hasAttribute('inputmode')) input.inputMode=(input.step==='any'||input.step.includes('.'))?'decimal':'numeric'; });
        }
        prepare(document);
        const observer=new MutationObserver(changes=>changes.forEach(change=>change.addedNodes.forEach(node=>{ if (node.nodeType===1) prepare(node); })));
        observer.observe(document.body,{childList:true,subtree:true});
        document.addEventListener('invalid',event=>{
            let detail=event.target.closest('details');
            while(detail) { detail.open=true; detail=detail.parentElement.closest('details'); }
        },true);
    }
    if (document.readyState==='loading') document.addEventListener('DOMContentLoaded',start,{once:true}); else start();
})();
