const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),output=path.join(root,'tests/bin/list-screen-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768}}),errors=[],posts=[];page.on('pageerror',e=>errors.push(e.message));
 let paid=20,fail=true,gate=null;
 const line={saleItemId:1,productId:1,variantId:1,productName:'Historic Helmet / Black L',sku:'HLM-OLD-01',quantity:1,sellingPrice:100,discountAmount:0,taxPercentage:0,lineTotal:100,attributesJson:'{}'};
 await page.route('**/*',async route=>{const req=route.request(),u=new URL(req.url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(u.pathname==='/Sales/Details/1')return route.fulfill({path:path.join(output,'Sales-Details.html'),contentType:'text/html'});
  if(u.pathname==='/api/sales/1')return json({saleId:1,invoiceNumber:'INV-HISTORIC-01',saleDate:'2001-01-15T10:00:00Z',customerName:'Historic Customer',status:'COMPLETED',subtotal:100,taxAmount:0,discountAmount:0,totalAmount:100,createdByName:'Owner',lines:[line],payments:[{paymentMode:'CASH',amount:20,paymentDate:'2001-01-15T10:00:00Z'},{paymentMode:'CREDIT',amount:80,paymentDate:'2001-01-15T10:00:00Z'},...(paid>20?[{paymentMode:'CARD',cardType:'CREDIT',amount:paid-20,paymentDate:'2026-11-01T10:00:00Z'}]:[])]});
  if(u.pathname==='/api/operations/sales/1')return json({documentTotal:100,returnedTotal:0,paidTotal:paid,refundedTotal:0,outstanding:100-paid,payments:[{id:1,date:'2001-01-15T10:00:00Z',type:'PAYMENT',status:'CASH',amount:20},{id:2,date:'2001-01-15T10:00:00Z',type:'PAYMENT',status:'CREDIT',amount:80}],returns:[],returnedLines:[],fulfilments:[],tradeIns:[]});
  if(u.pathname==='/api/operations/sales/1/payments'){
   const body=req.postDataJSON();posts.push(body);if(gate)await gate;
   if(fail)return route.fulfill({status:500,contentType:'application/json',body:'{"message":"Collection unconfirmed; retry the same payment."}'});
   paid+=body.amount;return json({id:3});
  }
  if(u.pathname==='/api/branches/context')return json({locationId:1,storeId:1,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Counter'}]});
  if(u.pathname==='/api/notifications')return json({unread:0,items:[]});
  if(u.pathname.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://localhost/Sales/Details/1?collect=1');await page.waitForFunction(()=>document.querySelector('#customer-payment-amount').value==='80');
 assert(await page.locator('#sale-collect-payment').evaluate(n=>n.open));assert((await page.locator('#sale-lines').textContent()).includes('HLM-OLD-01'));assert((await page.locator('#sale-payments').textContent()).includes('Customer Credit / Pay Later'));
 await page.locator('#customer-payment-mode').selectOption('CARD:CREDIT');await page.locator('#customer-payment-reference').fill('CARD-RECEIPT-01');await page.locator('#customer-payment-form button').click();assert.equal(posts.length,0,'Confirmation required before recording receipt');
 await page.locator('#customer-payment-confirmed').check();let release;gate=new Promise(r=>release=r);
 await page.locator('#customer-payment-form button').click();await page.waitForFunction(()=>document.querySelector('#customer-payment-amount').disabled);
 await page.evaluate(()=>document.querySelector('#customer-payment-form').requestSubmit());assert.equal(posts.length,1,'Pending submission cannot post twice');
 release();await page.waitForFunction(()=>!document.querySelector('#customer-payment-amount').disabled);assert((await page.locator('#sale-message').textContent()).includes('Collection unconfirmed'));assert.equal(await page.locator('#customer-payment-reference').inputValue(),'CARD-RECEIPT-01');
 gate=null;fail=false;await page.locator('#customer-payment-form button').click();await page.waitForFunction(()=>document.querySelector('#sale-collect-payment').hidden);
 assert.equal(posts.length,2);assert.equal(posts[0].requestId,posts[1].requestId,'Retry retains idempotency key');assert.equal(posts[1].paymentMode,'CARD');assert.equal(posts[1].cardType,'CREDIT');assert.equal(posts[1].amount,80);
 assert((await page.locator('#sale-account-summary').textContent()).includes('Outstanding 0.00'));assert((await page.locator('#sale-payments').textContent()).includes('Credit Card'));
 for(const [width,height]of [[1366,768],[1280,650],[390,844],[320,640]]){await page.setViewportSize({width,height});await page.waitForFunction(()=>innerWidth>991||(parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft)<1&&document.querySelector('.sf-sidebar').getBoundingClientRect().right<1));await page.evaluate(()=>window.scrollTo({top:0,behavior:'instant'}));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Details do not overflow at '+width);await page.screenshot({path:path.join(output,'customer-bill-details-'+width+'.png'),fullPage:true});}
 assert.deepEqual(errors,[]);console.log('PASS: read-only historical SKU/payment dates, collect deep link, receipt confirmation, pending duplicate prevention, retry key, retained errors/input, Card subtype, full settlement and responsive details.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
