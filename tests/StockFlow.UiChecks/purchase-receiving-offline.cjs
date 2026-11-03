// Purchase-order authoring remains available separately from physical receiving.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({reducedMotion:'reduce'});page.setDefaultTimeout(9000);
  const errors=[],writes=[];let completeCount=0,status='DRAFT',interrupt=false;
  const product={productId:1,variantId:11,productName:'Cable',sku:'CABLE',variantName:'DEFAULT',purchasePrice:100,taxPercentage:0,isSerialTracked:false};
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),url=new URL(req.url()),p=url.pathname;
   const json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Purchases/Create')return route.fulfill({path:path.join(root,'tests/bin/variant-ui-fixtures/Purchases-Order.html'),contentType:'text/html'});
   if(p.startsWith('/Purchases/Details/'))return route.fulfill({contentType:'text/html',body:'<h1>Purchase saved</h1>'});
   if(p==='/api/products/variants')return json([product]);
   if(p==='/api/suppliers')return json([{supplierId:1,supplierName:'Supplier',countryCode:'IN'}]);
   if(p==='/api/suppliers/options')return json({countries:[],states:[],districts:[]});
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Stock'}]});
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/purchases'&&req.method()==='POST'){writes.push(req.postDataJSON());status='DRAFT';return json({purchaseId:writes.length});}
   if(p==='/api/purchases/99'){
    if(req.method()==='PUT'){writes.push(req.postDataJSON());return route.fulfill({status:204});}
    return json({purchaseId:99,status:'DRAFT',supplierId:1,purchaseDate:'2026-01-01',items:[{...product,quantity:2,purchasePrice:100,discountAmount:1,taxPercentage:0,serialNumbers:[]},{...product,quantity:3,purchasePrice:110,discountAmount:2,taxPercentage:0,serialNumbers:[]}]});
   }
   if(/^\/api\/purchases\/\d+\/complete$/.test(p)){
    completeCount++;status='COMPLETED';if(interrupt){interrupt=false;return route.fulfill({status:500,contentType:'application/json',body:'{"message":"Response interrupted after posting"}'});}return route.fulfill({status:204});
   }
   if(/^\/api\/purchases\/\d+$/.test(p))return json({status});
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  const ready=()=>page.locator('#purchase-fields:not([disabled])').waitFor();
  await page.goto('http://stockflow.test/Purchases/Create?mode=order');await ready();
  assert.equal(await page.locator('#purchase-registration').count(),0);assert.equal(await page.locator('#purchase-register').getAttribute('href'),'/Products');
  await page.locator('#supplierId').selectOption('1');await page.locator('#purchase-items select').first().selectOption('11');
  await page.locator('#purchase-items').getByLabel('Expected quantity *',{exact:true}).fill('3');
  assert(await page.locator('#purchase-as-order').isChecked());
  await page.locator('#purchase-save').click();await page.waitForURL('**/Purchases/Details/1');
  assert.equal(writes[0].completeImmediately,false);assert.equal(writes[0].items[0].receivedQuantity,0);assert.equal(completeCount,0,'Ordering creates no stock');
  await page.goto('http://stockflow.test/Purchases/Create?mode=order');await ready();await page.locator('#supplierId').selectOption('1');await page.locator('#purchase-items select').first().selectOption('11');
  await page.locator('#purchase-as-order').uncheck();await page.locator('#purchase-items').getByLabel('Accepted Quantity *',{exact:true}).fill('1');interrupt=true;
  await page.locator('#purchase-save').click();await page.getByText('Response interrupted after posting',{exact:true}).waitFor();assert.equal(completeCount,1);
  await page.reload();await page.locator('#purchase-retry').click();await page.waitForURL('**/Purchases/Details/2');assert.equal(completeCount,1,'Recovery checks completed state instead of posting again');
  await page.goto('http://stockflow.test/Purchases/Create?id=99');await ready();
  const rows=page.locator('.purchase-line');assert.equal(await rows.count(),2);assert.equal(await rows.nth(1).getByLabel('Unit Price (INR) *',{exact:true}).inputValue(),'110');
  assert.equal(await rows.nth(1).getByLabel('Item Discount (INR)',{exact:true}).inputValue(),'2','Different prices and discounts remain separate while editing');
  await page.locator('#draft-change-reason').fill('Correct draft price');page.once('dialog',dialog=>dialog.accept());await page.locator('#purchase-save').click();await page.waitForURL('**/Purchases/Details/99');assert.equal(writes.at(-1).items.length,2);assert.equal(completeCount,1);
  assert.deepEqual(errors,[]);
  console.log('PASS: existing purchase-order entry, Product Registration navigation, draft quantities, completed-stock recovery and draft editing.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
