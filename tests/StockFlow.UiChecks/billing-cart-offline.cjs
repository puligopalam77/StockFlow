// Presentation tests: synthetic server responses, never values copied from the design image.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),output=path.join(root,'tests/bin/cart-ui-fixtures');fs.mkdirSync(output,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const context=await browser.newContext({viewport:{width:1920,height:1080}}),page=await context.newPage(),errors=[],drafts=new Map();let posts=0,soldOut=false;const queries=[];
 page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(15000);
 const products=Array.from({length:20},(_,i)=>({productId:i+1,variantId:i+1,productName:(i===19?'An unusually long registered product name that must wrap safely without changing any adjacent financial fields':'Test family '+(i+1))+' / Red / M',variantName:'Red / M',sku:'TEST-SKU-'+String(i+1).padStart(3,'0'),attributes:{Color:'Red',Size:'M'},sellingPrice:123.45+i,mrp:500,taxPercentage:12,currentStock:9,isAvailable:true,isActive:true,isSerialTracked:false,hasProductImage:false}));
 const numbers=new Map();const handleDraft=require('./draft-lifecycle-mock.cjs')(drafts,id=>{if(!numbers.has(id))numbers.set(id,'DRF-'+String(7000+numbers.size).padStart(4,'0'));return numbers.get(id);});
 const serve=async route=>{
  const request=route.request(),url=new URL(request.url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(url.pathname==='/Sales/Create')return route.fulfill({path:path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),contentType:'text/html'});
  if(url.pathname==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Counter'}],locationId:1,storeId:1});
  if(url.pathname==='/api/sales/billing/policy')return json({canOverridePrice:true,canOverrideTax:true,canDiscount:true,canApproveDiscount:true,discountLimitPercent:10});
  if(url.pathname==='/api/sales/gst-context')return json({mapped:false});
  if(url.pathname==='/api/customers/search')return json([{customerId:10,customerName:'Synthetic cart customer',mobileNumber:'9876543210'}]);
  if(url.pathname.startsWith('/api/sales/billing/drafts'))return handleDraft(request,url,route,json);
  if(url.pathname==='/api/scan'){
   const code=url.searchParams.get('code');if(code==='SERIAL')return json({...products[0],productId:30,variantId:30,sku:'SERIAL-TEST',productName:'Serialized test',productUnitId:30,serialNumber:'TEST-UNIT-30',serialSource:'MANUFACTURER',isSerialTracked:true,unitStatus:'IN_STOCK'});
   return json(products.find(p=>p.sku===code));
  }
  if(url.pathname==='/api/products/variants')return json(products.filter(p=>p.sku===url.searchParams.get('term')).map(p=>({...p,minimumStockLevel:p.variantId===1?10:2})));
  if(url.pathname==='/api/sales/quote'){
   const b=request.postDataJSON();queries.push(b);const money=n=>Math.round(n*100)/100;
   const items=b.items.map(i=>{const base=money(i.quantity*i.sellingPrice),discountAmount=i.discountPercent!=null?money(base*i.discountPercent/100):i.discountAmount,taxPercentage=i.manualItem?0:i.taxOverride??12,taxAmount=money((base-discountAmount)*taxPercentage/100);return {mrp:i.manualItem?i.manualItem.mrp:500,currentStock:i.manualItem?0:soldOut&&i.variantId===1?0:9,catalogPrice:i.manualItem?i.sellingPrice:products.find(p=>p.variantId===i.variantId)?.sellingPrice??123.45,discountAmount,taxPercentage,taxAmount,lineTotal:money(base-discountAmount+taxAmount)};});
   const totalAmount=money(items.reduce((sum,i)=>sum+i.lineTotal,0)),taxAmount=money(items.reduce((sum,i)=>sum+i.taxAmount,0));
   return json({items,totalAmount,taxAmount,discountAmount:0,subtotal:money(totalAmount-taxAmount),itemSubtotal:b.items.reduce((sum,i)=>sum+i.quantity*i.sellingPrice,0),itemDiscountAmount:items.reduce((sum,i)=>sum+i.discountAmount,0),invoiceDiscountAmount:0,taxableAmount:money(totalAmount-taxAmount),adjustmentReview:{requiresApproval:false,approvalStatus:'NOT_REQUIRED',lineReasons:b.items.map(()=>({discount:false,price:false,tax:false})),invoiceReasonRequired:false}});
  }
  if(url.pathname==='/api/sales'){posts++;return json({saleId:42});}
  if(url.pathname.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 };await context.route('**/*',serve);
 await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 assert(await page.locator('#sale-empty').isVisible());assert(await page.locator('.billing-cart-scroll').isHidden());assert(await page.locator('#clear-bill-items').isHidden());
 await page.locator('#customer-search').fill('Synthetic');await page.locator('#customer-results [role=option]').first().click();
 async function scan(code,count){await page.locator('#sale-scan').fill(code);await page.locator('#sale-scan').press('Enter');await page.waitForFunction(n=>document.querySelectorAll('#sale-cart tr').length===n,count);}
 async function calculated(){await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);}
 for(let i=0;i<20;i++){
  await scan(products[i].sku,i+1);
  if([1,5,10,20].includes(i+1)){
   await calculated();assert((await page.locator('#sale-item-count').textContent()).startsWith(`${i+1} item`));
   assert.equal(await page.locator('#sale-cart details').count(),0,'No ordinary expandable metadata panels');
   assert.equal(await page.locator('#sale-cart .billing-line-sku').count(),0,'SKU is not repeated inside product identity');
   const layout=await page.locator('#sale-cart tr').first().evaluate(row=>({height:row.getBoundingClientRect().height,align:[...row.cells].map(n=>getComputedStyle(n).verticalAlign),white:[...row.querySelectorAll('[data-price-editor],.billing-quantity input')].map(n=>getComputedStyle(n).backgroundColor),tax:row.querySelector('[data-tax-display]').readOnly,qty:row.querySelector('.billing-quantity').getBoundingClientRect().height}));
   assert(layout.height>=50&&layout.height<=180,JSON.stringify(layout));assert(layout.align.every(a=>a==='middle'));assert(layout.white.every(c=>c==='rgb(255, 255, 255)'));assert(layout.tax);assert(layout.qty>=28&&layout.qty<=44);
   await page.screenshot({path:path.join(output,`cart-${i+1}-items.png`),fullPage:true});
  }
 }
 assert.equal(await page.locator('#line-stock-0').getAttribute('data-state'),'neutral','No green badge when scanner response supplies no threshold');
 await page.waitForFunction(()=>document.querySelector('#billing-draft-reference').textContent.includes('DRF-'));const reference=await page.locator('#billing-draft-reference').textContent();await page.locator('#billing-notes > summary').evaluate(n=>{if(!n.parentElement.open)n.click();});await page.locator('#sale-notes').fill('Preserve this cart note');await page.locator('[data-payment-mode="UPI"]').first().click();
 const oldLine=await page.locator('#line-total-0').textContent(),qty=page.locator('#sale-cart tr').first().locator('.billing-quantity input');
 await qty.fill('2');assert.equal(await page.locator('#line-total-0').textContent(),oldLine);assert.equal(await page.locator('#line-total-0').getAttribute('data-provisional'),'true');await calculated();assert(await qty.evaluate(n=>document.activeElement===n));
 const confirmed=await page.locator('#summary-total').textContent(),confirmedLine=await page.locator('#line-total-0').textContent();await qty.fill('0');assert.equal(await page.locator('#summary-total').textContent(),confirmed);assert.equal(await page.locator('#line-total-0').textContent(),confirmedLine);assert(await page.locator('#complete-sale').isDisabled());await qty.fill('2');await calculated();
 assert.equal(await page.locator('#billing-draft-reference').textContent(),reference);
 const viewports=[[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]];
 for(const [width,height] of viewports){
  await page.setViewportSize({width,height});await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`No page-wide overflow ${width}`);
  assert.equal(await page.locator('.billing-cart thead').evaluate(n=>getComputedStyle(n).display),width>=768?'table-header-group':'none','Settled cart header at '+width+'x'+height);
  const region=page.locator('.billing-cart-scroll');assert(await region.evaluate(n=>n.scrollWidth<=n.clientWidth+1),'Cart has no horizontal overflow');
  const remove=page.locator('#sale-cart tr').first().getByRole('button',{name:/Remove Test family/});await remove.scrollIntoViewIfNeeded();assert(await remove.isVisible());
  await page.locator('#complete-sale').scrollIntoViewIfNeeded();const final=await page.locator('#complete-sale').boundingBox();assert(final.y>=0&&final.y+final.height<=height+1,'Checkout remains reachable');
  if([1366,390,320].includes(width))await page.screenshot({path:path.join(output,`cart-20-${width}.png`),fullPage:true});
 }
 await page.setViewportSize({width:1366,height:768});await page.locator('#sale-cart tr').last().getByRole('button',{name:/Remove /}).click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===19);await calculated();assert.equal(await page.locator('#sale-customer').inputValue(),'10');assert.equal(await page.locator('#sale-notes').inputValue(),'Preserve this cart note');assert.equal(await page.locator('#payment-mode').inputValue(),'UPI');assert.equal(await page.locator('#billing-draft-reference').textContent(),reference);
 for(const [sku,state,index] of [[products[0].sku,'low',0],[products[1].sku,'available',1]]){await page.locator('#sale-scan').fill(sku);await page.locator('#product-results button').first().click();await calculated();assert.equal(await page.locator('#line-stock-'+index).getAttribute('data-state'),state,'Configured threshold controls stock badge');}
 await scan('SERIAL',20);await calculated();const unit=page.locator('#sale-cart tr').last();assert((await unit.locator('.billing-item-serial').textContent()).includes('TEST-UNIT-30'));assert(await unit.locator('.billing-quantity input').evaluate(n=>n.readOnly));assert(await unit.locator('.billing-qty-button').first().isDisabled());
 // Revalidation displays a changed source stock value without changing cart identity.
 soldOut=true;const stockPrice=page.locator('#sale-cart tr').first().locator('[data-price-editor]');await stockPrice.fill(await stockPrice.inputValue());await page.waitForFunction(()=>document.querySelector('#line-stock-0').textContent==='Stock: 0');assert.equal(await page.locator('#line-stock-0').getAttribute('data-state'),'out');assert(await page.locator('#complete-sale').isDisabled());
 assert.equal(posts,0,'Cart layout, edits and draft persistence never finalize a sale');assert.deepEqual(errors,[]);
 await page.locator('#clear-bill-items').click();await page.locator('#sf-confirm-cancel').click();assert.equal(await page.locator('#sale-cart tr').count(),20,'Cancelling Clear Bill preserves all lines');
 await page.locator('#clear-bill-items').click();await page.locator('#sf-confirm-accept').click();await page.locator('#sale-empty').waitFor();assert(await page.locator('.billing-cart-scroll').isHidden());assert(await page.locator('#clear-bill-items').isHidden());assert.equal(await page.locator('#sale-customer').inputValue(),'');assert.equal(await page.locator('#sale-notes').inputValue(),'');assert.equal(await page.locator('#payment-mode').inputValue(),'CASH');assert.equal(await page.locator('#billing-draft-reference').textContent(),reference,'Clear Bill keeps the same draft identity');assert.equal(posts,0);
 await context.close();
 // A touch pointer uses a larger remove target while retaining contained scrolling.
 const touch=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true});await touch.route('**/*',serve);const touchPage=await touch.newPage();touchPage.on('pageerror',e=>errors.push(e.message));await touchPage.goto('http://stockflow.test/Sales/Create');await touchPage.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);await touchPage.locator('#sale-scan').fill(products[1].sku);await touchPage.locator('#sale-scan').press('Enter');await touchPage.locator('#sale-cart tr').waitFor();
 const removeTouch=touchPage.locator('.billing-item-actions button[aria-label^="Remove"]');await removeTouch.scrollIntoViewIfNeeded();const target=await removeTouch.boundingBox();assert(target.width>=44&&target.height>=44,'Touch remove target is at least 44px');assert(await touchPage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));assert.deepEqual(errors,[]);await touch.close();
 console.log('PASS: 1/5/10/20-row cart, long names, centered fields, white editors, read-only tax, exact SKUs, stock states, serial constraints, stable totals/draft/payment/customer, removal, confirmed/cancelled Clear Bill and ten responsive no-horizontal-scroll viewports.');
 }finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
