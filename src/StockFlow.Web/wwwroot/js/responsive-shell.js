// Presentation only: New Sale anchors the assistant at the viewport's bottom right.
(() => {
    const launcher = document.querySelector('.sf-help-open');
    if (!launcher) return;
    let scheduled = false;
    const intersects = (a,b) => a.left < b.right + 6 && a.right > b.left - 6 && a.top < b.bottom + 6 && a.bottom > b.top - 6;
    function place() {
        scheduled = false;
        if (document.getElementById('billing-workspace')) {
            launcher.style.removeProperty('right');
            launcher.style.removeProperty('bottom');
            return;
        }
        if (document.querySelector('dialog[open]') || launcher.contains(document.activeElement)) return;
        launcher.style.removeProperty('right');launcher.style.removeProperty('bottom');
        const original = getComputedStyle(launcher), right = parseFloat(original.right) || 12, bottom = parseFloat(original.bottom) || 12;
        const targets = [...document.querySelectorAll('.sf-main :is(button,a.btn,input:not([type=hidden]),select,textarea,summary),.sf-topbar :is(button,a,summary,select)')]
            .filter(n => !n.closest('dialog') && !launcher.contains(n)).map(n => n.getBoundingClientRect())
            .filter(r => r.width && r.height && r.bottom > 0 && r.top < innerHeight);
        for (const table of document.querySelectorAll('.sf-main .table-responsive,.sf-main .bc-table-wrap')) {
            if (table.scrollWidth <= table.clientWidth) continue;
            const r = table.getBoundingClientRect();
            if (r.bottom > 0 && r.bottom < innerHeight) targets.push({left:r.left,right:r.right,top:r.bottom-18,bottom:r.bottom});
        }
        let best, minimum = Infinity;
        const insets=[right,Math.max(12,innerWidth-launcher.getBoundingClientRect().width-12)];
        for (const inset of insets) {
            for (const lift of [bottom, bottom + 72, bottom + 144, Math.max(12, innerHeight - 160)]) {
                launcher.style.right = inset + 'px';launcher.style.bottom = lift + 'px';
                const r = launcher.getBoundingClientRect(), caption = launcher.querySelector('.sf-mascot-caption')?.getBoundingClientRect();
                const bounds = {left:Math.min(r.left,caption?.left ?? r.left),right:Math.max(r.right,caption?.right ?? r.right),top:r.top,bottom:r.bottom};
                if(bounds.left<0 || bounds.right>innerWidth || bounds.top<0 || bounds.bottom>innerHeight) continue;
                const score = targets.filter(t => intersects(bounds,t)).length;
                if(score<minimum){minimum=score;best={inset,lift};}
                if(!score) break;
            }
            if(!minimum) break;
        }
        if(best){launcher.style.right=best.inset+'px';launcher.style.bottom=best.lift+'px';}
    }
    function schedule(){if(!scheduled){scheduled=true;requestAnimationFrame(place);}}
    addEventListener('resize',schedule);addEventListener('scroll',schedule,{passive:true});
    document.addEventListener('focusin',schedule);
    new MutationObserver(schedule).observe(document.querySelector('.sf-main') || document.body,{childList:true,subtree:true});
    schedule();
})();
