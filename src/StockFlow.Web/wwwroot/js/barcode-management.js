(() => {
    'use strict';
    const $ = id => document.getElementById(id), permissions = $('barcode-workspace').dataset;
    const query = new URLSearchParams(location.search), kind = query.has('receiptId') ? 'receipt' : 'intake';
    const lotId = query.get(kind === 'receipt' ? 'receiptId' : 'intakeId');
    const base = '/api/labels/management/lots', url = `${base}/${kind}/${lotId}`;
    let lots = [], workspace = null, selected = new Set(), copies = new Map(), mode = 'PRINT', busy = false;
    let lotPage = 0, piecePage = 0, allHistory = false, previewRows = [], previewAction = 'PRINT', pending = null, previewLayout = null;
    const formats={thermal50x30:{width:50,height:30,name:'Thermal 50 x 30 mm'},thermal40x25:{width:40,height:25,name:'Thermal 40 x 25 mm'},thermal30x20:{width:30,height:20,name:'Thermal 30 x 20 mm'},thermal:{width:63,height:38,name:'Thermal 63 x 38 mm'}};
    const standardSheet=()=>({widthMm:63,heightMm:38,columns:3,rows:7,leftMm:7.5,topMm:15.5,columnGapMm:3,rowGapMm:0});
    function printLayout() {
        const format=$('barcode-layout').value;
        if(format==='sheet') {
            const template=$('barcode-sheet-template').value==='custom'?{widthMm:Number($('barcode-sheet-width').value),heightMm:Number($('barcode-sheet-height').value),columns:Number($('barcode-sheet-columns').value),rows:Number($('barcode-sheet-rows').value),leftMm:Number($('barcode-sheet-left').value),topMm:Number($('barcode-sheet-top').value),columnGapMm:Number($('barcode-sheet-column-gap').value),rowGapMm:Number($('barcode-sheet-row-gap').value)}:standardSheet();
            if(!Object.values(template).every(Number.isFinite)||template.widthMm<20||template.widthMm>100||template.heightMm<15||template.heightMm>100||!Number.isInteger(template.columns)||template.columns<1||template.columns>8||!Number.isInteger(template.rows)||template.rows<1||template.rows>20||template.leftMm<0||template.leftMm>50||template.topMm<0||template.topMm>50||template.columnGapMm<0||template.columnGapMm>20||template.rowGapMm<0||template.rowGapMm>20||template.leftMm+template.columns*template.widthMm+(template.columns-1)*template.columnGapMm>210||template.topMm+template.rows*template.heightMm+(template.rows-1)*template.rowGapMm>297)throw new Error('The label dimensions, rows, columns, margins and gaps must fit an A4 sheet (210 x 297 mm).');
            return {format,sheetTemplate:template,smallLabelScanTested:false,width:template.widthMm,height:template.heightMm,name:`A4 sheet - ${template.widthMm} x ${template.heightMm} mm, ${template.columns} columns x ${template.rows} rows`};
        }
        if(!formats[format])throw new Error('Choose a supported paper / label size.');
        if(format==='thermal30x20'&&!$('barcode-small-tested').checked)throw new Error('Confirm a successful scan test before using 30 x 20 mm labels.');
        return {format,...formats[format],sheetTemplate:null,smallLabelScanTested:format==='thermal30x20'};
    }
    function applyLayout(layout) {
        const root=$('barcode-workspace'),template=layout.sheetTemplate,thermal=!template;
        root.style.setProperty('--bc-label-width',layout.width+'mm');root.style.setProperty('--bc-label-height',layout.height+'mm');
        root.style.setProperty('--bc-bar-height',(layout.height<=20?9:layout.height<=25?11:layout.height<=30?13:16)+'mm');
        root.classList.toggle('bc-label-small',layout.width<=40||layout.height<=25);root.classList.toggle('bc-label-tiny',layout.width<=30||layout.height<=20);
        root.style.setProperty('--bc-sheet-width',template?(template.columns*template.widthMm+(template.columns-1)*template.columnGapMm)+'mm':layout.width+'mm');
        root.style.setProperty('--bc-sheet-columns',template?template.columns:1);root.style.setProperty('--bc-column-gap',template?template.columnGapMm+'mm':'0mm');root.style.setProperty('--bc-row-gap',template?template.rowGapMm+'mm':'0mm');
        let style=$('barcode-page-size');if(!style){style=document.createElement('style');style.id='barcode-page-size';document.head.append(style);}
        if(template) {
            const right=210-template.leftMm-template.columns*template.widthMm-(template.columns-1)*template.columnGapMm;
            const bottom=297-template.topMm-template.rows*template.heightMm-(template.rows-1)*template.rowGapMm;
            style.textContent=`@page{size:A4;margin:${template.topMm}mm ${Math.max(0,right).toFixed(3)}mm ${Math.max(0,bottom).toFixed(3)}mm ${template.leftMm}mm}`;
        } else style.textContent=`@page{size:${layout.width}mm ${layout.height}mm;margin:0}`;
        document.body.classList.toggle('label-thermal',thermal);
    }
    function settingsChanged() {
        const sheet=$('barcode-layout').value==='sheet';$('barcode-a4-settings').hidden=!sheet;
        $('barcode-template-fields').hidden=!sheet||$('barcode-sheet-template').value!=='custom';
        $('barcode-sheet-hint').hidden=$('barcode-sheet-template').value==='custom';
        $('barcode-small-check').hidden=$('barcode-layout').value!=='thermal30x20';
        const selected=formats[$('barcode-layout').value];
        $('barcode-paper-help').textContent=sheet?'Use A4 paper and a template matching your actual sticker sheet. Print at 100% scale.':`Set printer paper to ${selected?.width||50} x ${selected?.height||30} mm. Use 100% scale, no margins, and headers/footers off.`;
    }
    function requestId() {
        if(typeof crypto.randomUUID==='function')return crypto.randomUUID();
        const bytes=crypto.getRandomValues(new Uint8Array(16));bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;
        const hex=[...bytes].map(b=>b.toString(16).padStart(2,'0')).join('');return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
    }
    let lotSize=StockFlowPagination.defaultSize,pieceSize=StockFlowPagination.defaultSize,lotPager,piecePager;
    const collapsedGroups = new Set();
    const date = value => value ? new Date(value).toLocaleString(undefined,{year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'}) : '-';
    const codeMatch = (row,term) => [row.productName,row.productCode,row.sku,row.barcode,row.serialNumber].join(' ').toLowerCase().includes(term);
    function message(error) {
        const box = $('barcode-preview-dialog').open ? $('barcode-preview-error') : $('barcode-reprint-dialog').open ? $('barcode-reprint-error') : $('barcode-message');
        box.textContent = error?.message || ''; box.hidden = !error;
    }
    async function api(path, body) {
        const response = await fetch(path,body === undefined ? {} : {method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]').content},body:JSON.stringify(body)});
        const data = await response.json().catch(()=>null);
        if (!response.ok || data === null) throw new Error(data?.message || Object.values(data?.errors || {}).flat().join(' ') || 'Unable to complete this barcode action. Reload the received lot and try again.');
        return data;
    }
    async function run(action) {
        if (busy) return; busy = true; message(null); refreshActions();
        try { await action(); } catch(error) { message(error); }
        finally { busy = false; refreshActions(); }
    }
    const cell = (row,value,className) => {const td = document.createElement('td');td.textContent = value ?? '-';if(className)td.className=className;row.append(td);return td;};
    function barcodeCell(row,code) {
        const td=cell(row,'','bc-code');if(!code){td.textContent='-';return td;}
        const wrap=document.createElement('div');wrap.className='bc-barcode-cell';
        try{const graphic=StockFlowBarcode.svg(code);graphic.classList.add('bc-mini-barcode');wrap.append(graphic);}catch{wrap.title='Barcode image is unavailable for this identifier.';}
        const text=document.createElement('span');text.textContent=code;wrap.append(text);td.append(wrap);return td;
    }
    function badge(value,style) { const node=document.createElement('span');node.className='bc-badge bc-badge-'+style;node.textContent=value;return node; }
    function empty(body,columns,text) {const tr=document.createElement('tr'),td=cell(tr,text);td.colSpan=columns;body.append(tr);}
    function caption(button,text) {
        button.replaceChildren(document.createTextNode(text));
        if(!/^(Preview|Print|Reprint)/.test(text))return;
        const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('aria-hidden','true');svg.classList.add('bc-action-icon');
        const path=document.createElementNS(svg.namespaceURI,'path');path.setAttribute('d',text.startsWith('Preview')?'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Zm10-3a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z':'M6 9V3h12v6M6 17H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2M6 14h12v7H6Z');svg.append(path);button.prepend(svg);
    }
    const button = (text,action,style) => {const b=document.createElement('button');b.type='button';b.className='btn '+(style||(text==='Preview'?'btn-outline-secondary':'btn-outline-primary'));caption(b,text);b.onclick=()=>run(action);return b;};
    function rowsForMode() { const term=$('barcode-row-search').value.trim().toLowerCase();const rows=(workspace?.rows||[]).filter(r=>(mode!=='REPRINT'||r.lastRequestedAt)&&codeMatch(r,term));return $('barcode-group-mode').value==='variant'?rows.sort((a,b)=>a.variantId-b.variantId):rows; }
    const eligible = row => mode==='REPRINT' ? Boolean(row.lastRequestedAt) : !row.lastRequestedAt && Boolean(row.barcode || row.canGenerate && permissions.generate==='true');
    function renderLots() {
        const term=$('barcode-lot-search').value.trim().toLowerCase(),filter=$('barcode-lot-kind').value,purchaseId=query.get('purchaseId');
        const filtered=lots.filter(l=>(!filter||l.kind===filter)&&(!purchaseId||String(l.purchaseId)===purchaseId)&&[l.lotNumber,l.purchaseNumber,l.supplierName].join(' ').toLowerCase().includes(term));
        lotPage=Math.min(lotPage,Math.max(0,Math.ceil(filtered.length/lotSize)-1));
        const body=$('barcode-lot-rows');body.replaceChildren();
        filtered.slice(lotPage*lotSize,(lotPage+1)*lotSize).forEach((lot,index)=>{
            const tr=document.createElement('tr');
            [lotPage*lotSize+index+1,lot.lotNumber,lot.receivedDate?.slice(0,10),lot.purchaseNumber||'Opening stock',lot.supplierName||'-',lot.items,lot.quantity].forEach(value=>cell(tr,value));
            cell(tr,'').append(badge(lot.barcodeStatus,lot.barcodeStatus==='Completed'?'green':lot.barcodeStatus==='Partial'?'amber':'pink'));
            const link=document.createElement('a');link.className='btn btn-outline-primary';link.textContent='View Barcodes';link.href='/Labels?'+new URLSearchParams({[lot.kind==='receipt'?'receiptId':'intakeId']:lot.lotId});cell(tr,'').append(link);body.append(tr);
        });
        if(!filtered.length)empty(body,9,'No received lots match your filters.');
        $('barcode-lot-count').textContent=filtered.length+' received lots';$('barcode-lot-page').textContent=String(lotPage+1);
        $('barcode-lot-prev').disabled=lotPage===0;$('barcode-lot-next').disabled=(lotPage+1)*lotSize>=filtered.length;$('barcode-all-lots').hidden=!purchaseId;lotPager?.set({total:filtered.length,page:lotPage+1,size:lotSize});
    }
    function countFor(row) {return copies.has(row.key)?copies.get(row.key):Number($('barcode-default-copies').value)||1;}
    function refreshActions() {
        const rows=(workspace?.rows||[]).filter(r=>selected.has(r.key)),total=rows.reduce((n,r)=>n+countFor(r),0);
        const permitted=permissions[mode==='PRINT'?'print':'reprint']==='true';
        const invalid=rows.some(r=>!Number.isInteger(countFor(r))||countFor(r)<1||countFor(r)>500||mode==='PRINT'&&r.lastRequestedAt);
        $('barcode-preview').disabled=busy||!rows.length||total>500||invalid;
        $('barcode-print').disabled=busy||!rows.length||total>500||invalid||!permitted;
        caption($('barcode-print'),mode==='PRINT'?'Print Labels':'Reprint Selected');
        $('barcode-preview-print').disabled=busy||permissions[previewAction==='PRINT'?'print':'reprint']!=='true';
        $('barcode-select-all').disabled=busy;$('barcode-clear').disabled=busy;
        $('barcode-selection-count').textContent=`${rows.length} of ${workspace?.rows.length||0} items selected${total>500?' - reduce copies to 500 or fewer':''}`;
        $('barcode-reprint-selected').disabled=busy||!rows.some(r=>r.lastRequestedAt)||rows.some(r=>!Number.isInteger(countFor(r))||countFor(r)<1||countFor(r)>500)||total>500||permissions.reprint!=='true';
        $('barcode-reprint-count').textContent=rows.filter(r=>r.lastRequestedAt).length+' labels selected';
        document.querySelectorAll('.bc-row-action').forEach(b=>b.disabled=busy||b.dataset.permitted==='false');
    }
    function toggle(row,checked) { if(checked)selected.add(row.key);else selected.delete(row.key);refreshActions(); }
    function renderPieces() {
        const filtered=rowsForMode(),body=$('barcode-piece-rows');body.replaceChildren();piecePage=Math.min(piecePage,Math.max(0,Math.ceil(filtered.length/pieceSize)-1));
        const current=filtered.slice(piecePage*pieceSize,(piecePage+1)*pieceSize);
        let previousVariant=null;
        for(const row of current) {
            if($('barcode-group-mode').value==='variant') {
                if(previousVariant!==row.variantId) {
                    const group=document.createElement('tr');group.className='bc-group-row';const td=cell(group,'');td.colSpan=8;
                    const toggleGroup=button(collapsedGroups.has(row.variantId)?'Show units':'Hide units',()=>{if(collapsedGroups.has(row.variantId))collapsedGroups.delete(row.variantId);else collapsedGroups.add(row.variantId);renderPieces();},'btn-outline-secondary');
                    td.append(document.createTextNode(row.productName+' - '+filtered.filter(r=>r.variantId===row.variantId).length+' units '),toggleGroup);body.append(group);previousVariant=row.variantId;
                }
                if(collapsedGroups.has(row.variantId))continue;
            }
            const tr=document.createElement('tr');tr.dataset.key=row.key;
            const check=document.createElement('input');check.type='checkbox';check.checked=selected.has(row.key);check.disabled=mode==='REPRINT'&&!row.lastRequestedAt||mode==='PRINT'&&Boolean(row.lastRequestedAt)||!row.barcode&&!row.canGenerate;check.setAttribute('aria-label','Select '+(row.barcode||row.productName+' item '+row.itemNumber));check.onchange=()=>toggle(row,check.checked);cell(tr,'').append(check);
            const productCell=cell(tr,row.productName,'bc-product');
            const detail=value=>{const small=document.createElement('small');small.className='d-block text-muted';small.textContent=value;productCell.append(small);};
            if(row.productCode)detail('Product Code: '+row.productCode);
            if(row.isSerialTracked||row.unitId)detail((row.serialSource==='MANUFACTURER'?'Manufacturer Serial: ':row.serialSource==='INTERNAL'?'Internal Unit ID: ':'Serial / Unit ID (source unverified): ')+(row.serialNumber||'Missing'));
            cell(tr,row.sku);const barcode=barcodeCell(tr,row.barcode);
            const sources={MANUFACTURER:'Manufacturer barcode',INTERNAL:'Generated SKU barcode',INTERNAL_UNIT:'Generated unit barcode',MANUFACTURER_UNIT:'Manufacturer unit barcode',SAVED_UNIT:'Saved unit barcode',SAVED_ITEM:'Saved item label',UNIT_IDENTIFIER:'Unit identifier used for scanning'};
            if(row.barcodeSource){const source=document.createElement('small');source.className='d-block text-muted';source.textContent=sources[row.barcodeSource]||'Saved barcode';barcode.append(source);}
            const status=row.lastRequestedAt?'Print requested':row.barcode?'Not requested':'Needs Barcode';
            cell(tr,'').append(badge(status,row.lastRequestedAt?'green':row.barcode?'amber':'pink'));cell(tr,1);
            const input=document.createElement('input');input.type='number';input.className='bc-copies';input.min=1;input.max=500;input.step=1;input.value=countFor(row);input.setAttribute('aria-label','Copies for '+row.key);input.oninput=()=>{const value=Number(input.value);copies.set(row.key,Number.isInteger(value)&&value>=1?value:0);input.setCustomValidity(value>=1&&value<=500&&Number.isInteger(value)?'':'Use 1 to 500 copies.');refreshActions();};cell(tr,'').append(input);
            const actions=cell(tr,'');actions.className='bc-row-actions';const rowAction=row.lastRequestedAt?'REPRINT':'PRINT';
            const preview=button('Preview',()=>preparePreview([row.key],rowAction));preview.classList.add('bc-row-action');actions.append(preview);
            const print=button(rowAction==='REPRINT'?'Reprint':'Print',async()=>{await preparePreview([row.key],rowAction);await sendPrint();});print.classList.add('bc-row-action');print.dataset.permitted=String(permissions[rowAction==='PRINT'?'print':'reprint']==='true');actions.append(print);body.append(tr);
        }
        if(!filtered.length)empty(body,8,mode==='REPRINT'?'No previous print requests for this received lot.':'No matching received items.');
        $('barcode-piece-page').textContent=String(piecePage+1);$('barcode-piece-prev').disabled=piecePage===0;$('barcode-piece-next').disabled=(piecePage+1)*pieceSize>=filtered.length;
        const checks=current.filter(eligible);$('barcode-select-page').checked=checks.length>0&&checks.every(r=>selected.has(r.key));
        piecePager?.set({total:filtered.length,page:piecePage+1,size:pieceSize});refreshActions();
    }
    function renderHistory() {
        const body=$('barcode-history-rows');body.replaceChildren();
        const history=workspace?.history||[];
        (allHistory?history:history.slice(0,5)).forEach((h,index)=>{const tr=document.createElement('tr');const format=h.format==='sheet'?`A4 ${h.sheetTemplate?.widthMm||63} x ${h.sheetTemplate?.heightMm||38} mm`:(formats[h.format]?.name||h.format);[index+1,h.barcode,h.productName,date(h.requestedAt),h.requestedBy,h.copies,format].forEach(v=>cell(tr,v));body.append(tr);});
        if(!history.length)empty(body,7,workspace?.historyAvailable?'No print requests yet.':'Print history is not available yet. Ask the shop owner to complete barcode setup.');
        $('barcode-history-all').hidden=history.length<=5;$('barcode-history-all').textContent=allHistory?'Show recent':'View all';
    }
    function renderWorkspace() {
        const lot=workspace.lot,withCode=workspace.rows.filter(r=>r.barcode).length;
        const crumb=$('breadcrumb-current');
        if(crumb) {
            if(!$('barcode-breadcrumb-parent')){const li=document.createElement('li'),link=document.createElement('a');li.id='barcode-breadcrumb-parent';link.href='/Labels';link.textContent='Barcodes';li.append(link);crumb.parentElement.before(li);}
            crumb.textContent=lot.lotNumber;
            const back=$('breadcrumb-back');if(back)back.href='/Labels';
        }
        $('barcode-lot-summary').textContent=`GRN / Lot: ${lot.lotNumber}  |  Purchase: ${lot.purchaseNumber||'-'}  |  Received: ${lot.receivedDate.slice(0,10)}  |  Supplier: ${lot.supplierName||'-'}`;
        $('barcode-total').textContent=lot.quantity;$('barcode-with').textContent=withCode;$('barcode-missing').textContent=Math.max(0,lot.quantity-withCode);
        renderPieces();renderHistory();
    }
    function setMode(value) {
        mode=value;selected.clear();piecePage=0;
        for(const [id,active] of [['barcode-print-tab',value==='PRINT'],['barcode-reprint-tab',value==='REPRINT']]){$(id).classList.toggle('active',active);$(id).setAttribute('aria-selected',String(active));}
        $('barcode-reprint-open').hidden=value!=='REPRINT';renderPieces();
    }
    function validCopies(row) {const value=countFor(row);if(!Number.isInteger(value)||value<1||value>500)throw new Error('Enter 1 to 500 copies for each selected label.');return value;}
    async function preparePreview(keys,action) {
        const layout=printLayout();
        if(!keys.length||keys.length>500)throw new Error('Select 1 to 500 items.');
        const chosen=keys.map(key=>workspace.rows.find(r=>r.key===key));
        if(chosen.some(r=>!r))throw new Error('Reload the received lot before selecting labels.');
        if(chosen.reduce((n,r)=>n+validCopies(r),0)>500)throw new Error('Print up to 500 labels per batch.');
        if(action==='REPRINT'&&chosen.some(r=>!r.lastRequestedAt||!r.barcode))throw new Error('Reprint only labels with a saved print request.');
        if(action==='PRINT') {
            if(chosen.some(r=>!r.barcode)&&permissions.generate!=='true')throw new Error('Barcode generation permission is required for the selected missing codes.');
            workspace=await api(url+'/prepare',{keys});renderWorkspace();
        }
        previewRows=keys.map(key=>{const row=workspace.rows.find(r=>r.key===key);if(!row?.barcode)throw new Error('A barcode could not be saved. No labels will be printed.');return {...row,copies:validCopies(row)};});
        previewAction=action;
        previewLayout=layout;applyLayout(layout);
        const count=previewRows.reduce((n,r)=>n+r.copies,0);
        $('barcode-preview-summary').textContent=`${count} labels (${layout.name})`;
        $('label-preview-area').replaceChildren();
        for(const row of previewRows)for(let i=0;i<row.copies;i++) {
            const label=document.createElement('article');label.className='stock-label';
            const name=document.createElement('strong');name.textContent=row.productName;
            const value=document.createElement('div');value.className='label-code';value.textContent=row.barcode;
            const sku=document.createElement('div');sku.className='label-detail label-sku';sku.textContent='SKU: '+row.sku;
            const lot=document.createElement('div');lot.className='label-detail label-lot';lot.textContent='GRN: '+workspace.lot.lotNumber;
            label.append(name,StockFlowBarcode.svg(row.barcode),value,sku,lot);$('label-preview-area').append(label);
        }
        $('barcode-preview-error').hidden=true;
        if($('barcode-reprint-dialog').open)$('barcode-reprint-dialog').close();
        if(!$('barcode-preview-dialog').open)$('barcode-preview-dialog').showModal();
        refreshActions();
    }
    async function sendPrint() {
        if(!previewRows.length)throw new Error('Preview selected labels first.');
        if(!previewLayout)throw new Error('Preview the selected label size first.');
        const body={action:previewAction,format:previewLayout.format,sheetTemplate:previewLayout.sheetTemplate,smallLabelScanTested:previewLayout.smallLabelScanTested,items:previewRows.map(r=>({key:r.key,barcode:r.barcode,copies:r.copies}))};
        const fingerprint=JSON.stringify(body);
        if(!pending||pending.fingerprint!==fingerprint)pending={fingerprint,request:{...body,requestId:requestId()}};
        // A retry keeps its request ID. A barcode is never generated here.
        workspace=await api(url+'/'+previewAction.toLowerCase(),pending.request);renderWorkspace();
        applyLayout(previewLayout);
        window.print();
    }
    function renderReprint() {
        const term=$('barcode-reprint-search').value.trim().toLowerCase(),body=$('barcode-reprint-rows');body.replaceChildren();
        const rows=workspace.rows.filter(r=>r.lastRequestedAt&&codeMatch(r,term));
        for(const row of rows) {
            const tr=document.createElement('tr'),check=document.createElement('input');check.type='checkbox';check.checked=selected.has(row.key);check.setAttribute('aria-label','Reprint '+row.barcode);check.onchange=()=>{toggle(row,check.checked);renderPieces();};cell(tr,'').append(check);
            cell(tr,row.productName);barcodeCell(tr,row.barcode);[date(row.lastRequestedAt),row.lastRequestedBy].forEach(v=>cell(tr,v));
            cell(tr,'').append(button('Preview',()=>preparePreview([row.key],'REPRINT')),button('Reprint',async()=>{await preparePreview([row.key],'REPRINT');await sendPrint();},'btn-primary'));body.append(tr);
        }
        if(!rows.length)empty(body,6,'No previous print requests match your search.');refreshActions();
    }
    for(const id of ['barcode-lot-search','barcode-lot-kind'])$(id).addEventListener(id.endsWith('search')?'input':'change',()=>{lotPage=0;renderLots();});
    for(const [id,delta] of [['barcode-lot-prev',-1],['barcode-lot-next',1]])$(id).onclick=()=>{lotPage+=delta;renderLots();};
    for(const [id,delta] of [['barcode-piece-prev',-1],['barcode-piece-next',1]])$(id).onclick=()=>{piecePage+=delta;renderPieces();};
    $('barcode-row-search').oninput=()=>{piecePage=0;renderPieces();};
    $('barcode-layout').onchange=settingsChanged;$('barcode-sheet-template').onchange=settingsChanged;
    $('barcode-group-mode').onchange=()=>{piecePage=0;renderPieces();};
    $('barcode-print-tab').onclick=()=>setMode('PRINT');$('barcode-reprint-tab').onclick=()=>setMode('REPRINT');
    $('barcode-select-all').onclick=()=>{rowsForMode().filter(eligible).slice(0,500).forEach(r=>selected.add(r.key));renderPieces();};
    $('barcode-clear').onclick=()=>{selected.clear();renderPieces();};
    $('barcode-select-page').onchange=event=>{rowsForMode().slice(piecePage*pieceSize,(piecePage+1)*pieceSize).filter(eligible).forEach(r=>toggle(r,event.target.checked));renderPieces();};
    const defaultCopies=()=>{const value=Number($('barcode-default-copies').value);if(Number.isInteger(value)&&value>=1&&value<=500){for(const key of selected)copies.set(key,value);renderPieces();}};
    $('barcode-default-copies').onchange=defaultCopies;
    for(const [id,delta] of [['barcode-copies-less',-1],['barcode-copies-more',1]])$(id).onclick=()=>{$('barcode-default-copies').value=Math.min(500,Math.max(1,Number($('barcode-default-copies').value)+delta));defaultCopies();};
    $('barcode-preview').onclick=()=>run(()=>preparePreview([...selected],mode));
    $('barcode-print').onclick=()=>run(async()=>{await preparePreview([...selected],mode);await sendPrint();});
    $('barcode-preview-print').onclick=()=>run(sendPrint);
    $('barcode-history-all').onclick=()=>{allHistory=!allHistory;renderHistory();};
    $('barcode-reprint-open').onclick=()=>{renderReprint();$('barcode-reprint-dialog').showModal();};
    $('barcode-reprint-search').oninput=renderReprint;
    $('barcode-reprint-copies').oninput=()=>{const value=Number($('barcode-reprint-copies').value);for(const row of workspace.rows.filter(r=>r.lastRequestedAt&&selected.has(r.key)))copies.set(row.key,Number.isInteger(value)&&value>=1&&value<=500?value:0);renderPieces();refreshActions();};
    $('barcode-reprint-all').onchange=event=>{const term=$('barcode-reprint-search').value.trim().toLowerCase();workspace.rows.filter(r=>r.lastRequestedAt&&codeMatch(r,term)).slice(0,500).forEach(r=>toggle(r,event.target.checked));renderReprint();renderPieces();};
    $('barcode-reprint-selected').onclick=()=>run(()=>preparePreview(workspace.rows.filter(r=>selected.has(r.key)&&r.lastRequestedAt).map(r=>r.key),'REPRINT'));
    document.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>{if(!busy)$(b.dataset.close).close();});
    for(const id of ['barcode-preview-dialog','barcode-reprint-dialog'])$(id).addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    for(const id of ['barcode-preview','barcode-preview-print','barcode-reprint-selected'])caption($(id),$(id).textContent);
    $('barcode-lot-rows').dataset.sfPaginationRemote='true';$('barcode-piece-rows').dataset.sfPaginationRemote='true';
    lotPager=StockFlowPagination.create({host:$('barcode-lot-prev').closest('.bc-pagination'),label:'received lots',legacy:{previous:'#barcode-lot-prev',next:'#barcode-lot-next',position:'#barcode-lot-page',range:'#barcode-lot-count'},onChange(state){lotPage=state.page-1;lotSize=state.size;renderLots();}});
    const pieceFooter=document.createElement('div');$('barcode-piece-prev').closest('.bc-pagination').after(pieceFooter);
    piecePager=StockFlowPagination.create({host:pieceFooter,legacy:{previous:'#barcode-piece-prev',next:'#barcode-piece-next',position:'#barcode-piece-page'},onChange(state){piecePage=state.page-1;pieceSize=state.size;renderPieces();}});
    settingsChanged();
    run(async()=>{
        if(lotId) {
            $('barcode-lots').hidden=true;$('barcode-detail').hidden=false;$('barcode-back').hidden=false;$('barcode-page-title').textContent='Barcode Labels';
            $('barcode-page-description').hidden=true;workspace=await api(url);renderWorkspace();
        } else { lots=await api(base);renderLots(); }
    });
})();
