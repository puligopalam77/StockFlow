const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/inventory-control-ui-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[],posts=[];
  let failed=false,deny=false,destination=false;
  const balances=[{productId:1,variantId:1,productName:'Shirt / Blue M',sku:'BLUE-M',currentStock:2,minimumStockLevel:5,isLowStock:true,isActive:true,isSerialTracked:false},{productId:2,variantId:2,productName:'Battery',sku:'BAT',currentStock:1,availableUnits:1,minimumStockLevel:2,isLowStock:true,isActive:true,isSerialTracked:true}];
  const unit={productUnitId:42,serialNumber:'BAT-42',barcode:'SFU42',barcodes:['BAT-42','SFU42','MAKER42'],resolution:'IN_TRANSIT'};
  const transfer={transferId:1,transferNumber:'TR-1',status:'APPROVED',fromLocationId:1,toLocationId:2,fromLocation:'Main',toLocation:'Second',operations:[],items:[{transferItemId:1,variantId:2,productName:'Battery',isSerialTracked:true,requestedQuantity:1,dispatchedQuantity:0,receivedQuantity:0,damagedQuantity:0,lostQuantity:0,units:[]}]};
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),url=new URL(req.url()),json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(['/Inventory','/Inventory/LowStock','/Transfers'].includes(url.pathname)){
    let html=fs.readFileSync(path.join(output,url.pathname==='/Inventory'?'Inventory.html':url.pathname==='/Transfers'?'Transfers.html':'LowStock.html'),'utf8');
    if(destination)html=html.replace('name="stockflow-location" content="1"','name="stockflow-location" content="2"').replace('stockflow.transfer.1.1.1','stockflow.transfer.1.1.2');
    return route.fulfill({contentType:'text/html',body:html});
   }
   if(url.pathname==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Counter'},{locationId:2,storeId:2,storeName:'Second',locationName:'Counter'}],locationId:destination?2:1,storeId:destination?2:1});
   if(req.method()==='POST'){
    posts.push({url:url.pathname,body:req.postDataJSON()});
    if(deny)return json({message:'Permission revoked'},403);
    if(!failed){failed=true;return json({message:'Result lost; retry saved request'},503);}
    if(url.pathname.includes('/dispatch')){transfer.status='DISPATCHED';transfer.items[0].dispatchedQuantity=1;transfer.items[0].units=[unit];}
    if(url.pathname.includes('/receive')){transfer.status='RECEIVED';transfer.items[0].receivedQuantity=1;unit.resolution='RECEIVED';}
    return json(url.pathname.includes('adjustments')?{adjustmentId:7}:{transferId:1});
   }
   if(url.pathname==='/api/inventory')return json(balances);
   if(url.pathname==='/api/inventory/units')return json([unit]);
   if(url.pathname==='/api/inventory/history')return json([{inventoryTransactionId:1,productName:'Battery',serialNumber:'BAT-42',transactionType:'TRANSFER_OUT',quantity:1,referenceType:'TRANSFER',referenceId:1,createdAt:'2026-10-03T12:00:00',createdByName:'Owner'}]);
   if(url.pathname==='/api/transfers/locations')return json([{locationId:1,storeName:'Main',locationName:'Counter'},{locationId:2,storeName:'Second',locationName:'Counter'}]);
   if(url.pathname==='/api/transfers/variants')return json([{variantId:2,productName:'Battery',sku:'BAT',isSerialTracked:true}]);
   if(url.pathname==='/api/transfers')return json([transfer]);
   if(url.pathname==='/api/transfers/1')return json(transfer);
   if(url.pathname==='/api/transfers/1/dispatch-units')return json([unit]);
   if(url.pathname.startsWith('/api/'))return json([]);
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Inventory');await page.waitForSelector('#balances button');
  assert.equal(await page.locator('.stock-overview-card').count(),4);assert.equal(await page.locator('#stock-total-items').textContent(),'2');assert.equal(await page.locator('#stock-total-units').textContent(),'3');assert.equal(await page.locator('#stock-total-low').textContent(),'2');
  await page.locator('.stock-sort').filter({hasText:'Available stock'}).click();assert((await page.locator('#balances tr').first().textContent()).includes('Battery'));
  await page.locator('.stock-sort').filter({hasText:'Available stock'}).click();assert((await page.locator('#balances tr').first().textContent()).includes('Shirt'));
  assert.equal(await page.locator('.stock-status.low').count(),2);
  const panels=await page.locator('.stock-tools>.card').evaluateAll(nodes=>nodes.map(n=>n.getBoundingClientRect().top));assert.equal(panels[0],panels[1],'Stock correction and count must sit side by side on desktop');
  await page.locator('#balances tr').filter({hasText:'Battery'}).locator('button').click();await page.waitForSelector('#history a[href="/Transfers?id=1"]');assert((await page.locator('#history').textContent()).includes('BAT-42'));
  await page.locator('#adjust-product').selectOption('2');await page.locator('#adjust-type').selectOption('DAMAGED');await page.waitForSelector('#adjust-units input');
  await page.locator('#adjust-unit-scan').fill('MAKER42');await page.locator('#adjust-unit-scan').press('Enter');await page.waitForFunction(()=>document.querySelector('#adjust-quantity').value==='1');
  await page.locator('#adjust-reason').fill('Counted damaged battery');await page.locator('#post-adjustment').click();await page.waitForFunction(()=>!document.querySelector('#adjustment-pending').hidden&&!document.querySelector('#adjustment-retry').disabled);
  assert.deepEqual(posts[0].body.productUnitIds,[42]);assert.equal(posts[0].body.locationId,1);
  await page.reload();await page.waitForFunction(()=>!document.querySelector('#adjustment-pending').hidden);deny=true;
  await page.locator('#adjustment-retry').click();await page.waitForFunction(()=>document.querySelector('#adjustment-message').textContent.includes('revoked'));assert(await page.locator('#adjustment-editor').evaluate(n=>n.disabled));
  deny=false;await page.locator('#adjustment-retry').click();await page.waitForFunction(()=>document.querySelector('#adjustment-message').textContent.includes('#7'));
  assert.deepEqual(posts[0].body,posts[1].body);assert.deepEqual(posts[0].body,posts[2].body);
  await page.goto('http://stockflow.test/Inventory/LowStock');await page.waitForSelector('#low-stock-results a');assert((await page.locator('#low-stock-results').textContent()).includes('5'));
  for(const url of ['/Inventory','/Inventory/LowStock'])for(const width of [1440,1024,768,390,320]){
   await page.setViewportSize({width,height:1000});await page.goto('http://stockflow.test'+url);await page.waitForSelector(url==='/Inventory'?'#balances button':'#low-stock-results a');
   await page.waitForFunction(()=>innerWidth>991||(parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft)===0&&document.querySelector('.sf-sidebar').getBoundingClientRect().right<=0));
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),url+' fits '+width);
   await page.screenshot({path:path.join(output,(url==='/Inventory'?'inventory':'low-stock')+'-'+width+'.png'),fullPage:true});
  }
  await page.setViewportSize({width:1440,height:1000});
  await page.goto('http://stockflow.test/Transfers?id=1');await page.waitForSelector('[data-unit]');assert(await page.locator('#transfer-from').isDisabled());
  await page.locator('[aria-label^="Scan transfer serial"]').fill('SFU42');await page.locator('[aria-label^="Scan transfer serial"]').press('Enter');await page.waitForFunction(()=>document.querySelector('[data-unit]').checked);
  failed=false;await page.locator('[data-action="dispatch"]').click();await page.waitForFunction(()=>!document.querySelector('#transfer-pending').hidden&&!document.querySelector('#transfer-retry').disabled);
  const dispatch=posts.at(-1);await page.reload();await page.waitForFunction(()=>!document.querySelector('#transfer-pending').hidden);await page.locator('#transfer-retry').click();await page.waitForFunction(()=>document.querySelector('#transfer-title').textContent.includes('Dispatched'));
  assert.deepEqual(posts.at(-1),dispatch);assert.deepEqual(dispatch.body.items[0].unitIds,[42]);
  destination=true;await page.goto('http://stockflow.test/Transfers?id=1');await page.waitForSelector('[data-unit]');assert(await page.locator('[data-action="dispatch"]').isHidden());
  await page.locator('[aria-label^="Scan transfer serial"]').fill('MAKER42');await page.locator('[aria-label^="Scan transfer serial"]').press('Enter');await page.waitForFunction(()=>document.querySelector('[data-quantity]').value==='1');
  await page.locator('[data-action="receive"]').click();await page.waitForFunction(()=>document.querySelector('#transfer-title').textContent.includes('Received'));assert((await page.locator('#transfer-progress').textContent()).startsWith('0 units'));
  for(const width of [1440,390,320]){
   await page.setViewportSize({width,height:1000});await page.waitForFunction(()=>innerWidth>991||(parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft)===0&&document.querySelector('.sf-sidebar').getBoundingClientRect().right<=0));
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'transfers fit '+width);
   await page.screenshot({path:path.join(output,`transfers-${width}.png`),fullPage:true});
  }
  assert.deepEqual(errors,[]);console.log('PASS inventory, low stock, serial adjustment, dispatch/receipt, exact saved retries, revoked permissions and responsive browser checks');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
