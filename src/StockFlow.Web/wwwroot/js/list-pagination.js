// Shared list presentation and read-only paging. No form/business submissions.
window.StockFlowPagination = (() => {
    'use strict';
    const sizes=[10,25,50,100],defaultSize=10,clients=new Map();let serial=0;
    // Use the filtered total, not the current page length or selected page size.
    const shouldShow=total=>Number(total)>10;
    const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
    const element=value=>typeof value==='string'?document.querySelector(value):value;
    const integer=(value,min=0,max=2147483647)=>Number.isSafeInteger(Number(value))&&Number(value)>=min&&Number(value)<=max;
    function validateFilters(root=document.querySelector('.sf-list-screen')){
        if(!root)return true;
        const filters=[...root.querySelectorAll('.sf-list-filter,.sf-filter-bar')].filter(n=>!n.closest('dialog,#editor,#purchase-registration,[hidden]'));
        for(const filter of filters){
            const fields=[...filter.querySelectorAll('input,select')];
            for(const field of fields){if(field.dataset.sfPagingInvalid){field.setCustomValidity('');delete field.dataset.sfPagingInvalid;}}
            const dates=fields.filter(n=>n.type==='date'),from=dates.find(n=>/(from|start)/i.test(n.name||n.id)),to=dates.find(n=>/(to|end)/i.test(n.name||n.id));
            if(from?.value&&to?.value&&from.value>to.value){to.setCustomValidity('From date must be on or before To date.');to.dataset.sfPagingInvalid='true';}
            const invalid=fields.find(n=>n.willValidate&&!n.validity.valid);
            if(invalid){invalid.reportValidity();return false;}
        }
        return true;
    }
    function create(options){
        const host=element(options.host)||node('div');host.classList.add('sf-pagination');
        host.setAttribute('aria-label',options.label?options.label+' pagination':'List pagination');
        const state={page:1,size:sizes.includes(Number(options.size))?Number(options.size):defaultSize,total:0,known:true,busy:false};
        const legacy=options.legacy||{},lookup=key=>element(legacy[key]);
        const previous=lookup('previous')||node('button'),next=lookup('next')||node('button'),range=lookup('range')||node('span');
        const size=lookup('size')||node('select'),sizeLabel=node('label','Show','sf-pagination-size'),nav=node('nav',null,'sf-pagination-nav'),info=node('div',null,'sf-pagination-info');
        size.id||='sf-list-size-'+(++serial);sizeLabel.htmlFor=size.id;size.classList.add('form-select');size.setAttribute('aria-label','Rows per page');size.replaceChildren(...sizes.map(value=>new Option(String(value),String(value))));sizeLabel.append(size,document.createTextNode('entries'));
        range.classList.add('sf-pagination-range');range.setAttribute('role','status');range.setAttribute('aria-live','polite');
        const first=node('button','\u00ab'),last=node('button','\u00bb'),numbers=node('span',null,'sf-pagination-numbers'),pageLabel=lookup('position')||node('span');pageLabel.classList.add('sf-pagination-position');
        previous.textContent='\u2039';next.textContent='\u203a';nav.setAttribute('aria-label','Pagination');
        for(const [button,label]of[[first,'First page'],[previous,'Previous page'],[next,'Next page'],[last,'Last page']]){button.type='button';button.className='sf-page-button sf-page-arrow';button.setAttribute('aria-label',label);button.title=label;}
        const jumpLabel=node('label','Go to page','sf-pagination-jump'),jump=node('input'),go=node('button','Go','sf-page-button');jump.type='number';jump.min='1';jump.step='1';jump.inputMode='numeric';jump.setAttribute('aria-label','Go to page');go.type='button';jumpLabel.append(jump,go);
        const error=node('p',null,'sf-pagination-error');error.setAttribute('role','alert');error.hidden=true;
        const retry=node('button','Try again','sf-page-button');retry.type='button';retry.hidden=true;retry.onclick=()=>change(state.page);
        info.append(sizeLabel,range);nav.append(first,previous,numbers,next,last);host.replaceChildren(info,nav,pageLabel,jumpLabel,error,retry);
        const pages=()=>Math.max(1,Math.ceil(state.total/state.size));
        function visibility(){
            const small=!shouldShow(state.total);
            info.hidden=nav.hidden=small;
            pageLabel.hidden=true;
            jumpLabel.hidden=small||!options.showPageJump;
            // Keep failed-request feedback and retry available even before a count is known.
            host.hidden=small&&error.hidden;
        }
        function message(text=''){error.textContent=text;error.hidden=!text;retry.hidden=!text;visibility();}
        function draw(){
            visibility();
            const count=pages();state.page=Math.max(1,Math.min(state.page,state.known?count:Math.max(count,state.page)));
            size.value=String(state.size);size.disabled=state.busy;host.setAttribute('aria-busy',String(state.busy));
            const start=state.total?((state.page-1)*state.size+1):0,end=state.total?Math.min(state.page*state.size,state.total):0;
            range.textContent=state.busy?'Loading records…':`Showing ${start}–${end} of ${state.total}${state.known?'':'+'} ${options.label||'records'}`;
            pageLabel.textContent=`Page ${state.page} of ${count}${state.known?'':'+'}`;
            first.disabled=previous.disabled=state.busy||state.page<=1;next.disabled=state.busy||(state.known&&state.page>=count);last.disabled=state.busy||!state.known||state.page>=count;
            numbers.replaceChildren();const begin=Math.max(1,Math.min(state.page-2,count-4)),endPage=Math.min(count,begin+4);
            for(let value=begin;value<=endPage;value++){const b=node('button',String(value),'sf-page-button');b.type='button';b.setAttribute('aria-label','Page '+value);if(value===state.page)b.setAttribute('aria-current','page');b.disabled=state.busy;b.onclick=()=>change(value);numbers.append(b);}
            jump.value=String(state.page);jump.max=String(count);jump.disabled=go.disabled=state.busy||!state.known||!state.total;
        }
        async function change(page,newSize=state.size){
            if(state.busy||!validateFilters(document.querySelector('.sf-list-screen')))return;
            if(!sizes.includes(Number(newSize))||!integer(page,1)||state.known&&Number(page)>pages()){message('Choose a valid page and rows-per-page value.');draw();return;}
            message();const old={...state};state.page=Number(page);state.size=Number(newSize);
            try{await options.onChange?.(state);draw();}catch(e){Object.assign(state,old,{busy:false});draw();message(e.message||'Unable to load this page. Try again.');}
        }
        first.onclick=()=>change(1);last.onclick=()=>change(pages());
        for(const [button,direction]of[[previous,-1],[next,1]])button.addEventListener('click',event=>{event.preventDefault();event.stopImmediatePropagation();change(state.page+direction);},true);
        size.addEventListener('change',event=>{event.stopImmediatePropagation();change(1,Number(size.value));},true);go.onclick=()=>{jump.setCustomValidity('');if(!integer(jump.value,1,pages())){jump.setCustomValidity('Enter a whole page number between 1 and '+pages()+'.');jump.reportValidity();return;}change(Number(jump.value));};jump.oninput=()=>jump.setCustomValidity('');jump.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();go.click();}};
        draw();return {host,state,draw,message,change,set(values){if(values.total!=null&&!integer(values.total))throw Error('Invalid record count.');Object.assign(state,values);draw();},dispose(){host.remove();}};
    }
    function remote(options){
        const root=element(options.root)||element(options.host)?.parentElement;root?.setAttribute('data-sf-pagination-remote','true');
        let generation=0,controller,lastGood={page:1,size:defaultSize};
        const scope=()=>[document.body.dataset.businessProfile,document.querySelector('meta[name="stockflow-location"]')?.content].join('|');
        const pager=create({...options,onChange:()=>reload()});lastGood.size=pager.state.size;
        const loadingRoot=element(options.root)||root;
        window.StockFlowLoading?.region(loadingRoot?.querySelector?.('.table-responsive')||loadingRoot,{retry:()=>reload()});
        async function reload({reset=false}={}){
            if(!validateFilters(options.filterRoot?element(options.filterRoot):document.querySelector('.sf-list-screen')))return;
            if(reset)pager.state.page=1;
            const token=++generation;controller?.abort();controller=new AbortController();pager.set({busy:true});pager.message();
            const request={page:pager.state.page,size:pager.state.size,offset:(pager.state.page-1)*pager.state.size,limit:pager.state.size,signal:controller.signal,token,scope:scope()};
            try{
                const result=await options.load(request);if(token!==generation||request.scope!==scope()||!pager.host.isConnected)return;
                const data=options.read?options.read(result):{items:Array.isArray(result)?result:(result.items||result.rows||[]),total:result.total??result.totalCount??result.summary?.count};
                if(!Array.isArray(data.items))throw Error('Invalid list response.');
                const supplied=data.total??data.items[0]?.totalCount,known=integer(supplied)&&supplied!==null&&supplied!==undefined;
                const total=known?Number(supplied):request.offset+data.items.length;
                // A concurrent deletion can invalidate the current page; reload the
                // first valid page rather than presenting a false empty result.
                if(!data.items.length&&request.page>1){pager.state.page=1;return await reload();}
                if(known&&request.offset>=total&&request.page>1){pager.state.page=Math.max(1,Math.ceil(total/request.size));return await reload();}
                options.render?.(data.items.slice(0,request.size),result,request);
                lastGood={page:request.page,size:request.size};pager.set({total,known:known||data.items.length<100,busy:false});
            }catch(e){if(token===generation&&e.name!=='AbortError'){pager.set({...lastGood,busy:false});pager.message(e.message||'Unable to load records. Try again.');options.onError?.(e);}}
            finally{if(token===generation&&pager.state.busy)pager.set({busy:false});}
        }
        return Object.assign(pager,{reload,current:request=>request?.token===generation&&request.scope===scope()});
    }
    // Adapter for existing bounded (up to 100-row) readers. Their query, row
    // rendering, summaries and exports remain in the owning module.
    function adapt(options){
        const pending=new Map();
        const pager=remote({...options,read:data=>data,load:async request=>{
            options.setOffset(request.offset,request);await options.load(request);
            const data=pending.get(request.token);pending.delete(request.token);
            if(!data)throw Error('Unable to load records. Try again.');return data;
        }});
        return Object.assign(pager,{
            load:()=>pager.reload({reset:options.getOffset()===0}),
            capture(result,request){
                if(!pager.current(request))return null;
                const key=Array.isArray(result)?null:(options.itemsKey||'rows'),items=key?result[key]:result;
                if(!Array.isArray(items))throw Error('Invalid list response.');
                pending.set(request.token,{items,total:options.total?options.total(result):(result.total??result.totalCount??result.summary?.count??items[0]?.totalCount)});
                const selected=items.slice(0,request.size);return key?{...result,[key]:selected}:selected;
            }
        });
    }
    function table(body){
        if(clients.has(body))return;
        const table=body.closest('table'),wrapper=table.closest('.table-responsive')||table;
        const host=node('div');wrapper.after(host);
        const pager=create({host,onChange:apply});let rows=[],scheduled=false,criteria=null;
        function apply(){const offset=(pager.state.page-1)*pager.state.size;rows.forEach((row,index)=>row.classList.toggle('sf-page-hidden',index<offset||index>=offset+pager.state.size));}
        function refresh(){
            scheduled=false;
            const root=body.closest('.sf-list-screen,.sf-notification-console');
            const key=JSON.stringify([...(root?.querySelectorAll('.sf-list-filter input,.sf-list-filter select,.sf-filter-bar input,.sf-filter-bar select')||[])].map(field=>[field.id,field.type==='checkbox'||field.type==='radio'?field.checked:field.value]));
            rows=[...body.rows].filter(row=>!row.closest('.sf-skeleton')&&!row.hidden&&!(row.cells.length===1&&row.cells[0].colSpan>1));
            // Filters restart the list. Data refreshes retain a still-valid page;
            // draw() clamps to the nearest valid page after a deletion.
            pager.set({page:key===criteria?pager.state.page:1,total:rows.length});criteria=key;apply();
        }
        const observer=new MutationObserver(changes=>{if(changes.some(c=>c.type==='childList'&&c.target===body||c.type==='attributes'&&c.target.parentElement===body)&&!scheduled){scheduled=true;queueMicrotask(refresh);}});observer.observe(body,{childList:true,subtree:true,attributes:true,attributeFilter:['hidden']});
        refresh();clients.set(body,{dispose(){observer.disconnect();pager.dispose();}});
    }
    function scan(){
        for(const [body,client]of clients)if(!body.isConnected){client.dispose();clients.delete(body);}
        const main=document.querySelector('.sf-list-screen')||document.querySelector('.sf-notification-console');if(!main||document.body.classList.contains('master-form-page'))return;
        for(const body of main.querySelectorAll('table > tbody')){
            if(body.closest('dialog,[role=dialog],form,#editor,#purchase-registration,[data-sf-pagination-remote],.sf-pagination-exclude,#shop-summary')||body.closest('table').querySelector('thead input[type=checkbox]'))continue;
            table(body);
        }
        for(const input of main.querySelectorAll('.sf-list-filter input[type=search],.sf-list-filter input[id$="term"],#master-search'))if(input.maxLength<0||input.maxLength>200)input.maxLength=200;
    }
    document.addEventListener('submit',event=>{if(event.target.matches('.sf-list-filter')&&!validateFilters(event.target.parentElement)){event.preventDefault();event.stopImmediatePropagation();}},true);
    document.addEventListener('input',event=>{if(event.target.dataset?.sfPagingInvalid){event.target.setCustomValidity('');delete event.target.dataset.sfPagingInvalid;}});
    document.addEventListener('DOMContentLoaded',()=>{
        for(const host of document.querySelectorAll('[data-sf-pagination-link]')){
            const pager=create({host,size:host.dataset.size,onChange(state){const url=new URL(location.href);url.searchParams.set('PageNumber',state.page);url.searchParams.set('PageSize',state.size);location.assign(url);}});
            pager.set({page:Number(host.dataset.page),size:Number(host.dataset.size),total:Number(host.dataset.total)});
        }
        scan();let queued=false;new MutationObserver(()=>{if(!queued){queued=true;requestAnimationFrame(()=>{queued=false;scan();});}}).observe(document.querySelector('.sf-main')||document.body,{childList:true,subtree:true});
    });
    return {create,remote,adapt,validateFilters,scan,shouldShow,defaultSize,sizes:[...sizes]};
})();
