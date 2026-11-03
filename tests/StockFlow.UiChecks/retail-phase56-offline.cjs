const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/retail-phase56-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  for(const profile of ['CLOTHING','FOOTWEAR','BIKE_ACCESSORIES']){
   const context=await browser.newContext({reducedMotion:'reduce',viewport:{width:1440,height:1000}}),page=await context.newPage();page.setDefaultTimeout(10000);
   const sizes=profile==='FOOTWEAR'?['7','8']:['M','L'],material=profile==='CLOTHING'?'Cotton':profile==='FOOTWEAR'?'Rubber':'City 125';
   const products=sizes.map((size,i)=>({productId:1,variantId:i+1,variantName:size+' / Black',productName:'Retail item / '+size+' / Black',sku:'RETAIL-'+size,barcode:'CODE'+i,purchasePrice:50,sellingPrice:100,taxPercentage:0,currentStock:0,attributes:{size,color:'Black',material},unitOfMeasure:profile==='FOOTWEAR'?'PAIR':'PIECE',isSerialTracked:false,isActive:true,isAvailable:true,packQuantity:1}));
   const stock=new Map([[1,0],[2,0]]),receipts=[],sales=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
   const all=()=>products.map(p=>({...p,currentStock:stock.get(p.variantId),minimumStockLevel:0,categoryName:'Retail',isLowStock:stock.get(p.variantId)===0}));
   await page.route('**/*',async route=>{
    const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
    const pages={'/Purchases/Create':'Receive','/Sales/Create':'Billing','/Inventory':'Stock','/Transfers':'Transfers','/Sales/Invoice/1':'Invoice','/StockInflow/Details/PURCHASE/1':'Receipt'};
    if(pages[p])return route.fulfill({path:path.join(fixtures,profile+'-'+pages[p]+'.html'),contentType:'text/html'});
    if(p==='/api/products/variants'||p==='/api/inventory')return json(all());
    if(p==='/api/scan'){const product=all().find(p=>p.barcode===url.searchParams.get('code'));return json({...product,isAvailable:stock.get(product.variantId)>0});}
    if(p==='/api/suppliers')return json([{supplierId:1,supplierName:'Retail supplier'}]);
    if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[{locationId:1,storeId:1,storeName:'Retail shop',locationName:'Main stock'}]});
    if(p==='/api/notifications')return json({unread:0,items:[]});
    if(p==='/api/purchases'&&req.method()==='POST'){
     const body=req.postDataJSON();assert(req.headers()['x-csrf-token']);receipts.push(body);body.items.forEach(i=>stock.set(i.variantId,stock.get(i.variantId)+i.receivedQuantity));return json({purchaseId:1});
    }
    if(p.endsWith('/receipts'))return json([{receiptId:1,receiptNumber:'GRN-RETAIL'}]);
    if(p==='/api/purchases')return json([]);
    if(p==='/api/stock-receipts/PURCHASE/1')return json({source:'PURCHASE',sourceId:1,receiptNumber:'GRN-RETAIL',typeLabel:'Purchase Receipt',status:'COMPLETED',items:products.map(product=>({...product,attributeSummary:'Saved size: '+product.attributes.size,quantity:1,sku:'SAVED-'+product.sku,serialNumbers:[]}))});
    if(p==='/api/sales/billing/policy')return json({canOverridePrice:true,canDiscount:true,canApproveDiscount:true,canTradeIn:false,discountLimitPercent:10});
    if(p==='/api/sales/quote'){
     const body=req.postDataJSON(),subtotal=body.items.reduce((sum,i)=>sum+i.quantity*i.sellingPrice-(i.discountAmount||0),0);return json({subtotal,taxAmount:0,discountAmount:0,totalAmount:subtotal,items:body.items.map(i=>({taxPercentage:0,lineTotal:i.quantity*i.sellingPrice,currentStock:stock.get(i.variantId),catalogPrice:100,warrantyMonths:0}))});
    }
    if(p==='/api/sales'&&req.method()==='POST'){const body=req.postDataJSON();sales.push(body);for(const i of body.items){assert(stock.get(i.variantId)>=i.quantity);stock.set(i.variantId,stock.get(i.variantId)-i.quantity);}return json({saleId:1});}
    if(p==='/api/transfers/variants')return json(all());
    if(p==='/api/transfers/locations')return json([]);
    if(p.startsWith('/api/'))return json([]);
    const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
    if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
    return route.fulfill({status:404,body:''});
   });
   await page.goto('http://stockflow.test/Purchases/Create');await page.locator('#goods-fields:not([disabled])').waitFor();
   await page.locator('#goods-search').fill(material);await page.locator('#goods-suggestions button').first().waitFor();assert.equal(await page.locator('#goods-suggestions button').count(),2,'receiving searches common attributes');
   await page.locator('#goods-search').fill('');await page.locator('#goods-bulk summary').click();await page.locator('#goods-bulk-product').selectOption('1');
   assert((await page.locator('#goods-bulk-variants').innerText()).includes(material));const quantities=page.locator('#goods-bulk-variants input');await quantities.nth(0).fill('2');await quantities.nth(1).fill('10001');await page.locator('#goods-bulk-add').click();assert.equal(await page.locator('.goods-item').count(),0,'invalid bulk row does not partially add earlier rows');
   await quantities.nth(1).fill('3');await page.locator('#goods-bulk-add').click();assert.equal(await page.locator('.goods-item').count(),2);assert((await page.locator('#goods-items').innerText()).includes('Size: '+sizes[0]));
   await page.locator('#goods-supplier').selectOption('1');await page.locator('#purchase-save').click();await page.locator('#receipt-confirm[open]').waitFor();await page.locator('#goods-confirm').click();await page.locator('#receipt-success').waitFor({state:'visible'});
   assert.deepEqual(receipts[0].items.map(i=>[i.variantId,i.receivedQuantity]),[[1,2],[2,3]]);
   await page.goto('http://stockflow.test/Sales/Create');await page.locator('#sale-scan').waitFor();
   const scan=async code=>{await page.locator('#sale-scan').fill(code);await page.locator('#sale-scan').press('Enter');};
   await scan('CODE0');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1);await scan('CODE0');await page.waitForFunction(()=>document.querySelector('#sale-cart input').value==='2');await scan('CODE1');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===2);
   assert((await page.locator('#sale-cart').innerText()).includes('Size: '+sizes[0]));assert((await page.locator('#sale-cart').innerText()).includes(material));assert(!(await page.locator('#sale-cart').innerText()).includes('Warranty:'));
   await page.waitForFunction(()=>document.querySelector('#sale-totals').textContent.includes('300.00'));
   for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'billing overflow '+width);}
   await page.setViewportSize({width:1440,height:1000});await page.locator('#complete-sale').click();await page.waitForURL('**/Sales/Invoice/1');assert.deepEqual(sales[0].items.map(i=>[i.variantId,i.quantity]),[[1,2],[2,1]]);assert.equal(stock.get(1),0);assert.equal(stock.get(2),2);
   assert((await page.locator('article').innerText()).includes('Size: M'));if(profile==='FOOTWEAR')assert((await page.locator('article').innerText()).includes('PAIR'));
   await page.goto('http://stockflow.test/StockInflow/Details/PURCHASE/1');await page.locator('#receipt-detail-table tbody tr').nth(1).waitFor();assert((await page.locator('#receipt-detail-table').innerText()).includes('Saved size: '+sizes[0]));assert((await page.locator('#receipt-detail-table').innerText()).includes('SAVED-RETAIL-'));
   await page.goto('http://stockflow.test/Inventory');await page.locator('#balances tr').nth(1).waitFor();assert((await page.locator('#balances').innerText()).includes(material));assert((await page.locator('#balances').innerText()).includes('Size: '+sizes[1]));
   await page.goto('http://stockflow.test/Transfers');await page.locator('#transfer-new summary').click();await page.locator('#transfer-find').click();await page.locator('#transfer-variant option[value="1"]').waitFor({state:'attached'});assert((await page.locator('#transfer-variant').innerText()).includes(material));
   assert.deepEqual(errors,[]);await context.close();console.log('PASS: '+profile+' attribute search, atomic bulk input, receiving/billing exact IDs, repeated scans, units, invoice identity, stock and transfer display. Mock ledger only.');
  }
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
