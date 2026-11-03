const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/phase13-ui-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[],queries=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{const request=route.request(),url=new URL(request.url()),p=url.pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/StockInflow')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,'Admin-stock-receipts.html'),'utf8')});
  if(p==='/StockInflow/Details/PURCHASE/1')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,'Admin-stock-receipt-detail.html'),'utf8')});
  if(p==='/api/branches/context')return json({canReviewStores:false,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}],locationId:1,storeId:1});
  if(p==='/api/stock-receipts/suppliers')return json([{supplierId:1,supplierName:'Test Supplier'}]);
  if(p==='/api/stock-receipts/items'){queries.push(url.search);return json({totalCount:2,page:1,pageSize:25,rows:[{occurredAt:'2026-10-02T09:25:00',productName:'TEST Device / DEFAULT',quantity:2,movementType:'PURCHASE',typeLabel:'Purchase Receipt',receiptNumber:'GRN-2026-000001',source:'PURCHASE',sourceId:1,partyName:'Test Supplier'},{occurredAt:'2026-10-01T10:30:00',productName:'TEST Cable / DEFAULT',quantity:5,movementType:'ADJUSTMENT_IN',typeLabel:'Adjustment In',receiptNumber:'ADJ-2026-000012',source:'ADJUSTMENT',sourceId:12}]});}
  if(p==='/api/stock-receipts/PURCHASE/1')return json({source:'PURCHASE',sourceId:1,receiptNumber:'GRN-2026-000001',typeLabel:'Purchase Receipt',partyName:'Test Supplier',supplierInvoiceNumber:'INV-4582',supplierInvoiceDate:'2026-10-01',receivedDate:'2026-10-02T09:25:00',referenceNumber:'PUR-2026-000021',receivedBy:'Store Owner',status:'COMPLETED',notes:'Checked',subtotal:2000,discount:0,tax:360,total:2360,items:[{productName:'TEST Device / DEFAULT',quantity:2,damagedQuantity:0,purchasePrice:500,discount:0,taxPercentage:18,total:1180,serialNumbers:['BAT-01','BAT-02']},{productName:'TEST Cable / DEFAULT',quantity:10,damagedQuantity:0,purchasePrice:100,discount:0,taxPercentage:18,total:1180,serialNumbers:[]}]});
  if(p==='/api/stock-receipts'){queries.push(url.search);return json({summary:{receipts:24,unitsReceived:1245,purchaseValue:482500,adjustments:3},totalCount:60,page:Number(url.searchParams.get('page')||1),pageSize:Number(url.searchParams.get('pageSize')||25),rows:[{source:'PURCHASE',sourceId:1,occurredAt:'2026-10-02T09:25:00',receiptNumber:'GRN-2026-000001',movementType:'PURCHASE',typeLabel:'Purchase Receipt',partyName:'Test Supplier',itemCount:3,quantity:12,value:2360,status:'COMPLETED'},{source:'ADJUSTMENT',sourceId:12,occurredAt:'2026-10-01T10:30:00',receiptNumber:'ADJ-2026-000012',movementType:'ADJUSTMENT_IN',typeLabel:'Adjustment In',itemCount:1,quantity:5,value:null,status:'COMPLETED'}]});}
  if(p.startsWith('/api/'))return json([]);const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/StockInflow');await page.getByText('GRN-2026-000001',{exact:true}).waitFor();
 assert.equal(await page.locator('#receipt-results tbody tr').count(),2,'one receipt per table row');assert.equal(await page.locator('#summary-receipts').innerText(),'24');assert((await page.locator('#summary-value').innerText()).includes('4,82,500'));
 const receiveMenu=page.locator('.sf-nav-group').filter({has:page.locator('a[href="/Purchases/Create"]')});
 assert.deepEqual(await receiveMenu.locator('.sf-nav-child').allTextContents(),['Receive new stock','Purchase orders & deliveries','Stock receipt history']);
 assert.equal(await receiveMenu.locator('a[aria-current="page"]').getAttribute('href'),'/StockInflow','Only the receipt history submenu is active');
 assert.equal(await page.locator('#history-orders-tab, #history-receipts-tab, #purchase-rows').count(),0,'Stock receipt history has its own screen');
 assert.equal(await page.getByRole('link',{name:'View receipt GRN-2026-000001',exact:true}).count(),1,'Each receipt has an accessible View action');
 await page.screenshot({path:path.join(fixtures,'stock-receipts-refreshed.png'),fullPage:true});
 assert.equal(await page.getByText('SERVICE IN',{exact:true}).count(),0);assert.equal(await page.getByText('TRANSFER IN',{exact:true}).count(),0);
 await page.locator('#receipt-term').fill('INV-4582');await page.locator('#receipt-type').selectOption('PURCHASE');await page.locator('#receipt-supplier').selectOption('1');await page.locator('#receipt-history-filter').getByRole('button',{name:'Apply'}).click();await page.waitForFunction(()=>document.querySelectorAll('#receipt-results tbody tr').length===2);
 assert(queries.some(q=>q.includes('term=INV-4582')&&q.includes('type=PURCHASE')&&q.includes('supplierId=1')),'filters sent');
 await page.locator('#items-tab').click();await page.getByText('TEST Device / DEFAULT',{exact:true}).waitFor();assert.equal(await page.locator('#receipt-item-results tbody tr').count(),2);
 assert(await page.locator('#receipt-pages').isHidden(),'Pagination is hidden when results fit on one page');
 await page.locator('#receipts-tab').click();await page.getByRole('button',{name:'Next'}).click();await page.waitForFunction(()=>document.querySelector('#receipt-range').textContent.includes('26'));
 for(const width of [1440,768,390,320]){await page.setViewportSize({width,height:900});await page.waitForTimeout(350);const overflow=await page.evaluate(()=>({page:document.documentElement.scrollWidth-innerWidth,elements:[...document.querySelectorAll('body *')].filter(e=>{const r=e.getBoundingClientRect();return r.right>innerWidth+1||r.left< -1}).slice(0,8).map(e=>({tag:e.tagName,id:e.id,class:e.className,left:e.getBoundingClientRect().left,right:e.getBoundingClientRect().right,scroll:e.scrollWidth}))}));assert.equal(overflow.page<=0,true,`history overflow ${width}: ${JSON.stringify(overflow.elements)}`);}
 await page.setViewportSize({width:1440,height:1000});await page.getByText('GRN-2026-000001',{exact:true}).first().click();await page.waitForURL('**/StockInflow/Details/PURCHASE/1');await page.getByText('Grand total',{exact:true}).waitFor();assert((await page.locator('#receipt-detail-totals').innerText()).includes('2,360'));assert.equal(await page.locator('#receipt-detail-table tbody tr').count(),2);assert(await page.locator('#receipt-print').isVisible());
 await page.goto('http://stockflow.test/StockInflow');await page.getByText('GRN-2026-000001',{exact:true}).waitFor();
 await page.locator('#receipt-results tbody tr').first().locator('td').nth(3).click();await page.waitForURL('**/StockInflow/Details/PURCHASE/1');
 await page.getByText('Grand total',{exact:true}).waitFor();
 assert.equal(await page.locator('#receipt-heading-status').innerText(),'Completed');
 assert.equal(await page.locator('.receipt-info-column').count(),3);
 assert.equal(await page.locator('#receipt-items-title').innerText(),'Items received (2)');
 for(const width of [1440,768,390,320]){
  await page.setViewportSize({width,height:1000});
  await page.waitForTimeout(350);
  const detailOverflow=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,elements:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,10).map(e=>({tag:e.tagName,id:e.id,class:e.className,right:e.getBoundingClientRect().right}))}));
  assert(detailOverflow.scroll<=width,`Receipt detail stays within the viewport at ${width}: ${JSON.stringify(detailOverflow)}`);
 }
 await page.route('**/api/stock-receipts/PURCHASE/1',route=>route.fulfill({contentType:'application/json',body:JSON.stringify({receiptNumber:'GRN-2026-000001',typeLabel:'Purchase Receipt',partyName:'Test Supplier',receivedBy:'Store Owner',receivedDate:'2026-10-09T00:00:00',status:'COMPLETED',subtotal:340000,discount:0,tax:0,total:340000,items:[{productName:'Battery / DEFAULT',quantity:17,damagedQuantity:0,purchasePrice:20000,discount:0,taxPercentage:0,total:340000,serialNumbers:Array.from({length:17},(_,i)=>'BAT-'+(i+1))}]})}));
 await page.setViewportSize({width:1440,height:1000});await page.reload();
 const serialDetails=page.locator('.receipt-serial-details');await serialDetails.getByText('(+7 more)',{exact:false}).waitFor();
 await serialDetails.locator('summary').click();assert(await serialDetails.getByText('BAT-17',{exact:true}).isVisible());
 await serialDetails.locator('summary').click();await page.evaluate(()=>window.dispatchEvent(new Event('beforeprint')));await page.emulateMedia({media:'print'});
 assert(await serialDetails.getByText('BAT-17',{exact:true}).isVisible(),'Printing includes all serials even when collapsed');
 assert(await page.locator('#receipt-print').isHidden());await page.emulateMedia({media:'screen'});await page.evaluate(()=>window.dispatchEvent(new Event('afterprint')));
 assert.equal(await serialDetails.evaluate(details=>details.open),false,'Printing restores the previous collapsed state');
 await page.screenshot({path:path.join(fixtures,'stock-receipt-details-refreshed.png'),fullPage:true});
 assert.deepEqual(errors,[]);console.log('PASS: receipt-level history, item movements, filters, pagination, responsive details, serial expansion, printable details and row navigation.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
