// Real Razor/CSS/scripts with API fixtures only; no application database access.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {selectValue}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/barcode-management-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1050}}),errors=[],prepares=[],prints=[],ids=new Set();
  const lot={kind:'receipt',lotId:2,lotNumber:'GRN260001000012',receivedDate:'2026-10-30',purchaseId:6,purchaseNumber:'PUR-2026-000006',supplierName:'Test Supplier',items:4,quantity:6};
  let workspace={lot,historyAvailable:true,rows:[
   {key:'17:1',sourceItemId:17,itemNumber:1,variantId:1,productName:'Helmet / XS / Black',sku:'SF-87CE9957065EE43C-01',barcode:'SFR17X1',lastRequestedAt:'2026-10-30T10:15:00Z',lastRequestedBy:'Admin'},
   {key:'17:2',sourceItemId:17,itemNumber:2,variantId:1,productName:'Helmet / XS / Black',sku:'SF-87CE9957065EE43C-01',barcode:'0123456789012'},
   {key:'18:1',sourceItemId:18,itemNumber:1,variantId:2,productName:'Helmet / S / Black',sku:'SF-87CE9957065EE43C-02',barcode:null},
   {serialNumber:'MFR-SN-145',serialSource:'MANUFACTURER',unitId:145,isSerialTracked:true,barcodeSource:'INTERNAL_UNIT',productCode:'HLM-0007',key:'19:1',sourceItemId:19,itemNumber:1,variantId:3,productName:'Helmet / M / Black',sku:'SF-87CE9957065EE43C-03',barcode:'SFR19X1',lastRequestedAt:'2026-10-30T10:20:00Z',lastRequestedBy:'Admin'},
   {serialNumber:'SFN0000000146',serialSource:'INTERNAL',unitId:146,isSerialTracked:true,barcodeSource:'INTERNAL_UNIT',productCode:'HLM-0007',key:'19:2',sourceItemId:19,itemNumber:2,variantId:3,productName:'Helmet / M / Black',sku:'SF-87CE9957065EE43C-03',barcode:'SFR19X2'},
   {key:'20:1',sourceItemId:20,itemNumber:1,variantId:4,productName:'Helmet / L / Black',sku:'SF-87CE9957065EE43C-04',barcode:null}
  ].map(r=>({canGenerate:true,sellingPrice:4000,...r})),history:[]};
  workspace.history=workspace.rows.filter(r=>r.lastRequestedAt).map(r=>({requestId:crypto.randomUUID(),rowKey:r.key,barcode:r.barcode,productName:r.productName,requestedBy:'Admin',requestedAt:r.lastRequestedAt,copies:1,format:'sheet',action:'PRINT'}));
  let failPrepare=false,failPrint=true;
  page.on('pageerror',error=>errors.push(error.message));
  await page.addInitScript(()=>window.print=()=>window.printedCodes=[...document.querySelectorAll('#label-preview-area .label-code')].map(n=>n.textContent));
  await page.route('**/*',async route=>{
   const req=route.request(),url=new URL(req.url()),p=url.pathname,json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
   if(p==='/Labels')return route.fulfill({contentType:'text/html',body:fs.readFileSync(fs.existsSync(path.join(output,'Labels.html'))?path.join(output,'Labels.html'):path.join(root,'tests/bin/receiving-ui-fixtures/Labels.html'),'utf8')});
   if(p==='/api/labels/management/lots')return json([{...lot,withBarcode:workspace.rows.filter(r=>r.barcode).length,barcodeStatus:'Partial'},{...lot,lotId:3,lotNumber:'GRN260001000011',purchaseNumber:'PUR-2026-000005',quantity:2,withBarcode:2,barcodeStatus:'Completed'},{...lot,lotId:4,lotNumber:'GRN260001000010',quantity:3,withBarcode:0,barcodeStatus:'Pending'}]);
   if(p==='/api/labels/management/lots/receipt/2')return json(workspace);
   if(p.endsWith('/receipt/2/prepare')){
    const body=req.postDataJSON();prepares.push(body);
    if(failPrepare){failPrepare=false;return route.fulfill({status:400,contentType:'application/json',body:'{"message":"Unable to save selected barcode"}'});}
    for(const row of workspace.rows)if(body.keys.includes(row.key)&&!row.barcode)row.barcode='SFR'+row.sourceItemId+'X'+row.itemNumber;
    return json(workspace);
   }
   if(/\/receipt\/2\/(print|reprint)$/.test(p)){
    const body=req.postDataJSON();prints.push(body);
    if(!ids.has(body.requestId)){
     ids.add(body.requestId);
     for(const item of body.items){const row=workspace.rows.find(r=>r.key===item.key);assert.equal(item.barcode,row.barcode);row.lastRequestedAt='2026-10-30T12:00:00Z';row.lastRequestedBy='Store Owner';workspace.history.unshift({requestId:body.requestId,rowKey:item.key,barcode:item.barcode,productName:row.productName,requestedBy:'Store Owner',requestedAt:row.lastRequestedAt,copies:item.copies,format:body.format,action:body.action,sheetTemplate:body.sheetTemplate,smallLabelScanTested:body.smallLabelScanTested});}
    }
    if(failPrint){failPrint=false;return route.fulfill({status:503,contentType:'application/json',body:'{"message":"Print response interrupted. Retry this saved request."}'});}
    return json(workspace);
   }
   if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Stock'}],locationId:1,storeId:1});
   if(p==='/api/notifications/unread-count')return json({count:0});
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p.startsWith('/api/'))return json([]);
   const file=path.join(root,'src/StockFlow.Web/wwwroot',p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
  });
  const screenshot=async name=>{await page.evaluate(()=>{document.activeElement?.blur();scrollTo(0,0);});await page.screenshot({path:path.join(output,name),fullPage:true});};
  await page.goto('http://stockflow.test/Labels');await page.waitForFunction(()=>document.querySelectorAll('#barcode-lot-rows a').length===3);
  assert.equal(prepares.length,0);assert.equal(prints.length,0);
  assert((await page.locator('#barcode-lot-rows').textContent()).includes('Partial'));
  await screenshot('barcode-lots-desktop.png');
  await page.locator('#barcode-lot-search').fill('000012');assert.equal(await page.locator('#barcode-lot-rows a').count(),1);
  await page.locator('#barcode-lot-rows a').click();await page.waitForFunction(()=>document.querySelectorAll('#barcode-piece-rows tr').length===6);
  assert.equal(await page.locator('#barcode-layout').inputValue(),'thermal50x30','50 x 30 mm is the default');
  assert.equal(await page.locator('#barcode-piece-rows .bc-mini-barcode').count(),4,'saved barcode numbers also have barcode images');
  assert.equal(prepares.length,0,'opening receipt never generates barcodes');assert.equal(await page.locator('#barcode-total').textContent(),'6');assert.equal(await page.locator('#barcode-with').textContent(),'4');assert.equal(await page.locator('#barcode-missing').textContent(),'2');
  assert((await page.locator('#barcode-piece-rows').innerText()).includes('Manufacturer Serial: MFR-SN-145'));
  assert((await page.locator('#barcode-piece-rows').innerText()).includes('Internal Unit ID: SFN0000000146'));
  assert((await page.locator('#barcode-piece-rows').innerText()).includes('Generated unit barcode'));
  await page.locator('#barcode-row-search').fill('MFR-SN-145');assert.equal(await page.locator('#barcode-piece-rows tr[data-key]').count(),1);await page.locator('#barcode-row-search').fill('');
  assert.equal(await page.locator('.sf-nav-child.active[href="/Labels"]').count(),1);
  assert(await page.locator('#barcode-print').isDisabled());
  await screenshot('barcode-details-desktop.png');
  assert.equal(await page.locator('#breadcrumb-current').textContent(),lot.lotNumber);
  await selectValue(page.locator('#barcode-group-mode'),'variant');assert.equal(await page.locator('.bc-group-row').count(),4);
  await page.getByRole('button',{name:'Hide units',exact:true}).first().click();assert.equal(await page.locator('#barcode-piece-rows tr[data-key]').count(),4);
  await selectValue(page.locator('#barcode-group-mode'),'units');assert.equal(await page.locator('#barcode-piece-rows tr[data-key]').count(),6);
  await page.locator('#barcode-select-all').click();assert((await page.locator('#barcode-selection-count').textContent()).startsWith('4 of 6'));
  await selectValue(page.locator('#barcode-layout'),'sheet');assert(await page.locator('#barcode-a4-settings').isVisible());
  await page.locator('[aria-label="Copies for 17:2"]').fill('0');assert(await page.locator('#barcode-print').isDisabled());await page.locator('[aria-label="Copies for 17:2"]').fill('1');
  failPrepare=true;await page.locator('#barcode-preview').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview').disabled);
  assert(!(await page.locator('#barcode-preview-dialog').evaluate(n=>n.open)));assert.equal(prints.length,0,'generation failure blocks printing');assert((await page.locator('#barcode-message').textContent()).includes('Unable to save'));
  await page.locator('#barcode-preview').click();await page.waitForSelector('#barcode-preview-dialog[open]');
  assert.deepEqual(prepares.at(-1).keys.sort(),['17:2','18:1','19:2','20:1']);
  const expected=['0123456789012','SFR18X1','SFR19X2','SFR20X1'];assert.deepEqual(await page.locator('#label-preview-area .label-code').allTextContents(),expected);
  await screenshot('barcode-preview-desktop.png');
  await page.locator('#barcode-preview-print').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview-print').disabled);
  assert.equal(await page.evaluate(()=>window.printedCodes),undefined,'failed response does not open print dialog');
  const first=prints[0];await page.locator('#barcode-preview-print').click();await page.waitForFunction(()=>window.printedCodes?.length===4);
  assert.equal(prints[1].requestId,first.requestId,'retry uses original print request ID');assert.equal(ids.size,1);assert.deepEqual(await page.evaluate(()=>window.printedCodes),expected);
  assert.equal(await page.locator('#barcode-history-rows tr').count(),5,'history shows five recent selections');
  await page.emulateMedia({media:'print'});const a4=await page.pdf({preferCSSPageSize:true});fs.writeFileSync(path.join(output,'barcode-preview-a4.pdf'),a4);assert.equal((a4.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,1);
  await page.emulateMedia({media:'screen'});await page.locator('[data-close="barcode-preview-dialog"]').first().click();
  await page.locator('#barcode-history-all').click();assert.equal(await page.locator('#barcode-history-rows tr').count(),6);
  await page.locator('#barcode-reprint-tab').click();await selectValue(page.locator('#barcode-layout'),'thermal50x30');await page.locator('#barcode-reprint-open').click();await page.waitForSelector('#barcode-reprint-dialog[open]');
  await page.locator('#barcode-reprint-search').fill('SFR18X1');assert.equal(await page.locator('#barcode-reprint-rows tr').count(),1);
  await page.getByRole('checkbox',{name:'Reprint SFR18X1',exact:true}).check();await page.locator('#barcode-reprint-copies').fill('2');
  await screenshot('barcode-reprint-desktop.png');
  await page.locator('#barcode-reprint-selected').click();await page.waitForSelector('#barcode-preview-dialog[open]');assert.deepEqual(await page.locator('#label-preview-area .label-code').allTextContents(),['SFR18X1','SFR18X1']);
  const beforeReprint=prepares.length;await page.locator('#barcode-preview-print').click();await page.waitForFunction(()=>window.printedCodes?.length===2);assert.equal(prepares.length,beforeReprint,'reprinting never generates another barcode');assert.equal(prints.at(-1).format,'thermal50x30');assert.equal(prints.at(-1).action,'REPRINT');
  await page.emulateMedia({media:'print'});const thermal=await page.pdf({preferCSSPageSize:true});fs.writeFileSync(path.join(output,'barcode-preview-thermal.pdf'),thermal);assert.equal((thermal.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,2,'two copies produce two thermal pages');
  const box=thermal.toString('latin1').match(/\/MediaBox\s*\[0 0 ([\d.]+) ([\d.]+)\]/);assert(box&&Math.abs(Number(box[1])-50*72/25.4)<1&&Math.abs(Number(box[2])-30*72/25.4)<1);
  await page.emulateMedia({media:'screen'});await page.locator('[data-close="barcode-preview-dialog"]').first().click();
  for(const [format,width,height] of [['thermal40x25',40,25],['thermal30x20',30,20]]) {
   await selectValue(page.locator('#barcode-layout'),format);
   if(format==='thermal30x20'){
    const count=prints.length;await page.locator('#barcode-preview').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview').disabled);
    assert(!(await page.locator('#barcode-preview-dialog').evaluate(n=>n.open)));assert.equal(prints.length,count);assert((await page.locator('#barcode-message').textContent()).includes('scan test'));
    await page.locator('#barcode-small-tested').check();
   }
   await page.locator('#barcode-preview').click();await page.waitForSelector('#barcode-preview-dialog[open]');
   await page.locator('#barcode-preview-print').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview-print').disabled);
   assert.equal(prints.at(-1).format,format);if(format==='thermal30x20')assert.equal(prints.at(-1).smallLabelScanTested,true);
   await page.emulateMedia({media:'print'});const pdf=await page.pdf({preferCSSPageSize:true});fs.writeFileSync(path.join(output,`barcode-${width}x${height}.pdf`),pdf);
   const media=pdf.toString('latin1').match(/\/MediaBox\s*\[0 0 ([\d.]+) ([\d.]+)\]/);assert(media&&Math.abs(Number(media[1])-width*72/25.4)<1&&Math.abs(Number(media[2])-height*72/25.4)<1);
   assert.equal((pdf.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,2);
   await page.emulateMedia({media:'screen'});await page.locator('[data-close="barcode-preview-dialog"]').first().click();
  }
  await selectValue(page.locator('#barcode-layout'),'sheet');await selectValue(page.locator('#barcode-sheet-template'),'custom');
  await page.locator('#barcode-sheet-columns').fill('8');await page.locator('#barcode-preview').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview').disabled);assert(!(await page.locator('#barcode-preview-dialog').evaluate(n=>n.open)),'invalid sheet geometry blocks preview');
  for(const [id,value] of [['width',50],['height',30],['columns',3],['rows',8],['left',10],['top',15],['column-gap',5],['row-gap',3]])await page.locator('#barcode-sheet-'+id).fill(String(value));
  await page.locator('#barcode-preview').click();await page.waitForSelector('#barcode-preview-dialog[open]');await page.locator('#barcode-preview-print').click();await page.waitForFunction(()=>!document.querySelector('#barcode-preview-print').disabled);
  assert.equal(prints.at(-1).sheetTemplate.widthMm,50);assert.equal(prints.at(-1).sheetTemplate.columnGapMm,5);
  await page.emulateMedia({media:'print'});const custom=await page.pdf({preferCSSPageSize:true});fs.writeFileSync(path.join(output,'barcode-custom-a4.pdf'),custom);assert.equal((custom.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,1);
  await page.emulateMedia({media:'screen'});await page.locator('[data-close="barcode-preview-dialog"]').first().click();
  await selectValue(page.locator('#barcode-sheet-template'),'21');await page.locator('#barcode-default-copies').fill('22');await page.locator('#barcode-default-copies').dispatchEvent('change');
  await page.locator('#barcode-preview').click();await page.waitForSelector('#barcode-preview-dialog[open]');assert.equal(await page.locator('#label-preview-area .stock-label').count(),22);
  await page.emulateMedia({media:'print'});const sheets=await page.pdf({preferCSSPageSize:true});fs.writeFileSync(path.join(output,'barcode-a4-21-template.pdf'),sheets);assert.equal((sheets.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,2,'22 labels continue to a second 21-label A4 sheet');
  await page.emulateMedia({media:'screen'});await page.locator('[data-close="barcode-preview-dialog"]').first().click();await page.setViewportSize({width:390,height:1000});await screenshot('barcode-details-mobile.png');assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.reload();await page.waitForFunction(()=>document.querySelectorAll('#barcode-piece-rows tr').length===6);assert.equal(await page.locator('#barcode-missing').textContent(),'0');assert((await page.locator('#barcode-history-rows').textContent()).includes('Store Owner'),'reload retains print history');
  assert.deepEqual(errors,[]);console.log('PASS: reference layout, lot statuses, per-unit table, summary, selective generation, existing manufacturer code, failure handling, idempotent print retry, history/reload, reprint dialog, copy validation, A4/thermal PDF dimensions and mobile layout. Mock APIs only.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
