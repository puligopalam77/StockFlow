const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const fixture=fs.readFileSync(path.join(root,'tests/bin/variant-ui-fixtures/Purchases-Create.html'),'utf8');
const products=[
 {productId:1,variantId:11,productName:'Samsung Charger 25W',variantName:'DEFAULT',sku:'CHG-25W',barcode:'CHARGER',purchasePrice:500,taxPercentage:0,isSerialTracked:false,unitOfMeasure:'PIECE',packQuantity:1},
 {productId:2,variantId:22,productName:'USB Type-C Cable 1M',variantName:'DEFAULT',sku:'CABLE',barcode:'CABLE',currentStock:0,purchasePrice:120,taxPercentage:0,isSerialTracked:false,unitOfMeasure:'PIECE',packQuantity:1},
 {productId:3,variantId:33,productName:'Battery 150 Ah',variantName:'150 Ah',sku:'BATTERY',barcode:'BATTERY',purchasePrice:1000,taxPercentage:18,isSerialTracked:true,unitOfMeasure:'PIECE',packQuantity:1}
];
const order={purchaseId:45,purchaseNumber:'PO-00045',supplierId:1,purchaseDate:'2026-01-01',status:'PARTIALLY_RECEIVED',supplierInvoiceNumber:null,items:[{...products[0],purchaseItemId:101,quantity:10,receivedQuantity:4,damagedQuantity:1,purchasePrice:450,discountAmount:50,taxPercentage:5,packQuantity:1,unitOfMeasure:'PIECE'}]};
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1600,height:1000},reducedMotion:'reduce'});page.setDefaultTimeout(9000);
  const errors=[],direct=[],receipts=[],commits=new Map(),allocations=[];let interrupt=false,interruptIdentifier=true;
  page.on('pageerror',e=>errors.push(e.message));
  await page.context().route('**/*',async route=>{
   const req=route.request(),url=new URL(req.url()),p=url.pathname;
   const json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Purchases/Create')return route.fulfill({contentType:'text/html',body:fixture});
   if(p==='/Products/Create')return route.fulfill({contentType:'text/html',body:'<h1>Existing Product Registration</h1>'});
   if(p==='/api/receiving/unit-identifiers'){
    const body=req.postDataJSON();allocations.push(body);assert(req.headers()['x-csrf-token']);assert.equal(body.businessProfileCode,'ELECTRICAL');assert.equal(body.locationId,1);assert.equal(body.variantId,33);
    if(interruptIdentifier){interruptIdentifier=false;return route.fulfill({status:503,contentType:'application/json',body:'{"message":"Identifier response interrupted; retry."}'});}
    return json({codes:['SFN0000000145'],source:'INTERNAL'});
   }
   if(p==='/api/products/variants'){const id=url.searchParams.get('productId'),term=(url.searchParams.get('term')||'').toLowerCase();return json(products.filter(p=>(!id||p.productId===Number(id))&&(!term||[p.productName,p.sku,p.barcode].join(' ').toLowerCase().includes(term))));}
   if(p==='/api/suppliers')return json([{supplierId:1,supplierCode:'SUP0001',supplierName:'Test Supplier',paymentTermsDays:30}]);
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main Store',locationName:'Main Stock'},{locationId:2,storeId:2,storeName:'Second Store',locationName:'Stock'}]});
   if(p==='/api/purchases'&&req.method()==='GET')return json([order]);
   if(p==='/api/purchases/45')return json(order);
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/purchases'&&req.method()==='POST'){
    const body=req.postDataJSON();direct.push(body);assert(req.headers()['x-csrf-token']);
    if(!commits.has(body.requestId))commits.set(body.requestId,{purchaseId:7});
    if(interrupt){interrupt=false;return route.abort();}return json(commits.get(body.requestId));
   }
   if(p==='/api/purchases/45/receipts'&&req.method()==='POST'){receipts.push(req.postDataJSON());return json({receiptId:9});}
   if(p.endsWith('/receipts'))return json([{receiptId:9,receiptNumber:'GRN-00128'}]);
   if(p==='/api/scan'){const p=products.find(p=>p.barcode===url.searchParams.get('code'));return p?json(p):route.fulfill({status:404,contentType:'application/json',body:'{"message":"Not found"}'});}
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  async function selectSupplier(id){if(!await page.locator('#goods-supplier-picker').evaluate(n=>n.open))await page.locator('#goods-supplier-label').click();await page.locator('#goods-supplier').selectOption(id);}
  async function ready(){await page.locator('#goods-fields:not([disabled])').waitFor();}
  async function scan(code){await page.locator('#goods-search').fill(code);await page.locator('#goods-search').press('Enter');}
  await page.goto('http://stockflow.test/Purchases/Create');await ready();
  assert(await page.locator('#goods-empty').isVisible());assert.equal(await page.locator('.goods-item').count(),0);
  assert.equal(await page.locator('.goods-step').count(),5,'five numbered sections');assert.equal(await page.locator('#goods-bulk').count(),0,'standalone Add Variants removed');
  await scan('CHARGER');assert.equal(await page.locator('.goods-item').count(),0,'receipt context required before product selection');assert(await page.locator('#goods-context').isVisible());
  assert.equal(await page.locator('#purchase-registration').count(),0,'Receiving has no duplicate registration editor');
  assert.equal(await page.locator('#goods-register').getAttribute('href'),'/Products/Create');
  await selectSupplier('1');
  await page.locator('#goods-search').fill('Charger');await page.waitForSelector('#goods-suggestions button[data-result]');await page.locator('#goods-search').press('ArrowDown');await page.locator('#goods-search').press('Enter');
  const charger=page.locator('.goods-item').first();await charger.getByLabel('Received Quantity *',{exact:true}).fill('10');await charger.getByLabel('Damaged Quantity',{exact:true}).fill('2');
  assert.equal(await charger.getByLabel('Accepted Quantity',{exact:true}).inputValue(),'8');assert(await charger.getByLabel('Accepted Quantity',{exact:true}).getAttribute('readonly')!==null);
  await charger.getByRole('button',{name:'Edit details for Samsung Charger 25W'}).click();await page.locator('#goods-item-dialog').getByLabel('Damage reason',{exact:true}).fill('Cracked housing');await page.getByRole('button',{name:'Done',exact:true}).click();
  await scan('CABLE');await page.waitForFunction(()=>document.querySelectorAll('.goods-item').length===2);
  await scan('CABLE');await page.waitForFunction(()=>document.querySelectorAll('.goods-item')[1].querySelector('input').value==='2');
  await page.locator('.goods-item').nth(1).getByLabel('Received Quantity *',{exact:true}).fill('20');
  assert((await page.locator('#goods-totals').innerText()).includes('7,400.00'),'Invoice value follows backend received-quantity rules');
  assert((await page.locator('#goods-damage-warning').innerText()).includes('6,400.00'),'Accepted goods value is disclosed separately');
  const pdf=Buffer.from('%PDF-1.4\nfixture');await page.locator('#goods-file').setInputFiles({name:'invoice.pdf',mimeType:'application/pdf',buffer:pdf});await page.locator('#goods-file-preview').waitFor({state:'visible'});
  await page.locator('#goods-draft').click();await page.getByText('Draft saved on this browser. No stock has been updated.',{exact:true}).waitFor();
  const opened=page.waitForEvent('popup');await page.locator('#goods-register').click();const registration=await opened;await registration.waitForURL('**/Products/Create');await registration.close();assert(page.url().endsWith('/Purchases/Create'));assert.equal(direct.length,0);
  await page.goto('http://stockflow.test/Purchases/Create');await ready();assert.equal(await page.locator('.goods-item').count(),2);assert((await page.locator('#goods-file-preview').innerText()).includes('invoice.pdf'));
  for(const width of [1600,1366,1024,768,390,320]){
   await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`page overflow ${width}: `+await page.evaluate(()=>JSON.stringify([...document.querySelectorAll('main *')].filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,12).map(e=>({id:e.id,cls:e.className,right:e.getBoundingClientRect().right})))));
   if(await page.locator('.goods-summary').evaluate(n=>getComputedStyle(n).position==='sticky'))assert((await page.locator('.goods-summary').boundingBox()).height<260,'Summary must not cover the form at '+width+': '+JSON.stringify(await page.locator('.goods-summary').boundingBox()));
   assert(await page.locator('.goods-item').evaluateAll(items=>items.every(e=>e.scrollWidth<=e.clientWidth+1)),`row overflow ${width}`);
  }
  await page.setViewportSize({width:1600,height:1000});await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:path.join(root,'tests/bin/receive-goods-main.png'),fullPage:true});
  await page.locator('.goods-item').first().getByRole('button',{name:'Edit details for Samsung Charger 25W'}).click();await page.screenshot({path:path.join(root,'tests/bin/receive-goods-item.png')});await page.getByRole('button',{name:'Done',exact:true}).click();
  await page.locator('#purchase-save').click();await page.locator('#receipt-confirm[open]').waitFor();assert.equal(direct.length,0);assert((await page.locator('#goods-review-content').innerText()).includes('Total Accepted'));
  await page.screenshot({path:path.join(root,'tests/bin/receive-goods-review.png')});
  await page.locator('[data-close="receipt-confirm"]').last().click();assert.equal(direct.length,0);
  await page.locator('#purchase-save').click();interrupt=true;await page.locator('#goods-confirm').click();await page.locator('#goods-recovery').waitFor({state:'visible'});assert.equal(direct.length,1);
  await page.reload();await page.locator('#goods-retry').waitFor({state:'visible'});await page.locator('#goods-retry').click();await page.locator('#receipt-success').waitFor({state:'visible'});
  assert.equal(direct.length,2);assert.deepEqual(direct[0],direct[1],'Retry preserves the exact payload, attachment and request ID');assert.equal(commits.size,1);
  assert.equal(direct[1].items[0].receivedQuantity,8);assert.equal(direct[1].items[0].damagedQuantity,2);assert(direct[1].notes.includes('Cracked housing'));assert(direct[1].invoiceAttachmentData);
  assert((await page.locator('#goods-success-stock').innerText()).includes('28 units'));assert.equal(await page.locator('#receipt-view').getAttribute('href'),'/StockInflow/Details/PURCHASE/9');await page.screenshot({path:path.join(root,'tests/bin/receive-goods-success.png')});
  // Partial PO receipts use the existing receipt endpoint, never a new purchase.
  await page.goto('http://stockflow.test/Purchases/Create');await ready();await selectSupplier('1');await page.locator('[name="goods-kind"][value="order"]').check();await page.locator('#goods-order').selectOption('45');await page.waitForFunction(()=>document.querySelectorAll('.goods-item').length===1);await ready();
  assert.equal(await page.locator('.goods-item').getByLabel('Received Quantity *',{exact:true}).inputValue(),'5');
  assert(await page.locator('#goods-supplier').isDisabled(),'supplier is determined by selected PO');
  assert(await page.locator('#goods-file').isDisabled());assert(await page.locator('.goods-item').getByLabel('Unit Price (₹) *',{exact:true}).getAttribute('readonly')!==null);
  await page.locator('.goods-item').getByLabel('Received Quantity *',{exact:true}).fill('6');await page.locator('#purchase-save').click();await page.locator('#goods-item-dialog[open]').waitFor();assert.equal(receipts.length,0);
  await page.locator('#goods-item-dialog').getByLabel('Received Quantity *',{exact:true}).fill('3');await page.getByRole('button',{name:'Done',exact:true}).click();
  await page.locator('#purchase-save').click();await page.locator('#goods-confirm').click();await page.locator('#receipt-success').waitFor({state:'visible'});
  assert.equal(receipts.length,1);assert.equal(receipts[0].items[0].purchaseItemId,101);assert.equal(receipts[0].items[0].quantity,3);assert.equal(direct.length,2,'PO receiving does not create a replacement order');
  // Serial identifiers are required only for tracked products and must be unique.
  await page.goto('http://stockflow.test/Purchases/Create');await ready();await selectSupplier('1');await scan('BATTERY');await page.locator('#goods-item-dialog[open]').waitFor();
  await page.locator('#goods-item-dialog').getByLabel('Received Quantity *',{exact:true}).fill('3');await page.locator('#goods-item-dialog').getByLabel('Damaged Quantity',{exact:true}).fill('1');await page.locator('#goods-item-dialog').getByLabel('Damage reason',{exact:true}).fill('Terminal damage');
  await page.locator('#goods-item-dialog').getByLabel('Serial / Unit IDs (for accepted units)',{exact:true}).fill('SN1\nSN2');await page.locator('#goods-item-dialog').getByLabel('Serial / Unit IDs (for damaged units)',{exact:true}).fill('sn1');await page.getByRole('button',{name:'Done',exact:true}).click();
  await page.locator('#purchase-save').click();await page.locator('#goods-item-dialog[open]').waitFor();assert((await page.locator('#purchase-message').innerText()).includes('unique'));
  await page.locator('#goods-item-dialog').getByLabel('Serial / Unit IDs (for damaged units)',{exact:true}).fill('SN3');await page.getByRole('button',{name:'Done',exact:true}).click();await page.locator('#purchase-save').click();await page.locator('#receipt-confirm[open]').waitFor();
  await page.locator('[data-close="receipt-confirm"]').last().click();await page.locator('.goods-item').getByRole('button',{name:'Edit details for Battery 150 Ah'}).click();
  await page.locator('#goods-item-dialog').getByLabel('Received Quantity *',{exact:true}).fill('4');
  const generate=page.getByRole('button',{name:'Generate internal unit IDs',exact:true}).first();await generate.click();await page.waitForFunction(()=>document.querySelector('#purchase-message').textContent.includes('Identifier response interrupted'));
  await generate.click();await page.waitForFunction(()=>document.querySelector('[aria-label="Scan good serial"]').closest('.goods-serials').querySelector('textarea').value.includes('SFN0000000145'));
  assert.equal(await page.locator('#goods-item-dialog').getByLabel('Serial / Unit IDs (for accepted units)',{exact:true}).inputValue(),'SN1\nSN2\nSFN0000000145');
  assert.equal(allocations.length,2);assert.deepEqual(allocations[0],allocations[1],'Allocation retry keeps original request, scope and count');
  assert((await page.locator('#goods-item-dialog').innerText()).includes('not manufacturer serials'));
  await page.getByRole('button',{name:'Done',exact:true}).click();await page.locator('#purchase-save').click();await page.locator('#goods-confirm').click();await page.locator('#receipt-success').waitFor({state:'visible'});
  assert.deepEqual(direct.at(-1).items[0].serialNumbers,['SN1','SN2','SFN0000000145']);assert.deepEqual(direct.at(-1).items[0].damagedSerialNumbers,['SN3']);
  // Receiving permits zero-stock registered SKUs and preserves the draft while
  // managing variants through the existing product editor.
  await page.goto('http://stockflow.test/Purchases/Create');await ready();await selectSupplier('1');
  await page.locator('#goods-search').fill('Cable');await page.waitForSelector('#goods-suggestions button[data-result]');
  assert((await page.locator('#goods-suggestions').textContent()).includes('CABLE'));assert(await page.locator('#goods-suggestions button[data-result]').isEnabled());
  assert.equal(await page.locator('#goods-suggestions .goods-manage').getAttribute('href'),'/Products/Edit?id=2');
  await page.locator('#goods-suggestions button[data-result]').click();await page.waitForFunction(()=>document.querySelectorAll('.goods-item').length===1);
  const countBefore=await page.locator('.goods-item').count();await page.locator('#goods-add-item').click();assert.equal(await page.locator('.goods-item').count(),countBefore,'Add More Items never creates a blank line');
  await page.locator('.goods-item').getByRole('button',{name:/Increase quantity/}).click();assert.equal(await page.locator('.goods-item').getByLabel('Received Quantity *',{exact:true}).inputValue(),'2');
  await page.locator('#goods-clear').click();await page.locator('#sf-confirm-cancel').click();assert.equal(await page.locator('.goods-item').count(),1,'clear cancellation preserves receipt lines');
  await page.locator('#goods-clear').click();await page.locator('#sf-confirm-accept').click();assert.equal(await page.locator('.goods-item').count(),0);assert.equal(await page.locator('#goods-supplier').inputValue(),'1','clear preserves supplier');
  assert.deepEqual(errors,[]);
  console.log('PASS: empty state, product navigation, unified scan/search, quantity calculations, damage reasons, invoice draft recovery, responsive rows, review/cancel, exact safe retry, success details, partial PO receiving, over-receipt blocking and serial validation.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
