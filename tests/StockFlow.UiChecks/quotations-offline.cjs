const {selectValue}=require('./address-test-helpers.cjs');
const {fillAddress}=require('./address-test-helpers.cjs');
// Actual Razor/application assets; controlled synthetic responses test presentation,
// not financial correctness. Real PostgreSQL quotation checks run separately.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/quotation-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1.5}),errors=[],saves=[],previews=[],committed=new Map(),revisions=new Map();
 let id='c75ae01a-1b09-4469-9af5-c3e09f261401',saved=null,loseSave=true,failPreview=false,requireReasons=false,sentinel=false,delayOld=false,createdCustomers=0,evidence=null;
 page.on('pageerror',e=>{errors.push(e.message);console.error(e.message);});page.setDefaultTimeout(15000);
 const customer={customerId:1,customerName:'Quotation Customer',mobileNumber:'9876543210',address:'Verified customer address',isActive:true};
 const options={today:'2026-11-04',pricesIncludeTax:false,permissions:{create:true,edit:true,status:true,convert:true,print:true,export:true,manual:true}};
 function quote(r){
  const items=r.items.map(i=>{const gross=i.quantity*i.sellingPrice,discountAmount=i.discountPercent==null?i.discountAmount:Math.round(gross*i.discountPercent)/100,base=gross-discountAmount;return{mrp:i.manualItem?i.manualItem.mrp:1000,catalogPrice:100,currentStock:20,taxPercentage:5,discountAmount,taxAmount:base*.05,lineTotal:base*1.05};});
  const itemSubtotal=r.items.reduce((n,i)=>n+i.quantity*i.sellingPrice,0),itemDiscountAmount=items.reduce((n,i)=>n+i.discountAmount,0),taxableAmount=itemSubtotal-itemDiscountAmount-(r.discountAmount||0);
  return{items,itemSubtotal,itemDiscountAmount,invoiceDiscountAmount:r.discountAmount||0,taxableAmount,taxAmount:taxableAmount*.05,totalAmount:taxableAmount*1.05,adjustmentReview:{requiresApproval:requireReasons,invoiceReasonRequired:requireReasons&&r.discountAmount>0,lineReasons:r.items.map(()=>({discount:requireReasons,price:false,tax:false}))}};
 }
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname,json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  if(evidence&&p==='/api/quotations/options')return json({...options,pricesIncludeTax:evidence.selected.body.request.pricesIncludeTax});
  if(evidence&&p==='/api/quotations/'+id)return json(evidence);
  if(evidence&&p==='/api/quotations/preview')return json(evidence.selected.body.quote);
  if(p==='/api/quotations/options')return json(options);
  if(p==='/api/notifications')return json({unread:0,items:[]});
  if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Counter'}],locationId:1,storeId:1});
  if(p==='/api/customers/search')return json(url.searchParams.has('mobileNumber')||url.searchParams.has('gstNumber')?[]:[customer]);
  if(p==='/api/customers'&&req.method()==='POST'){createdCustomers++;const c=req.postDataJSON();assert(c.mobileNumber&&c.customerName);return json({...c,customerId:2,isActive:true});}
  if(p==='/api/products/variants')return json(url.searchParams.get('term')==='Serial'?[{productId:2,variantId:2,productName:'Serialized fixture',variantName:'Exact variant',sku:'SERIAL-SKU',sellingPrice:100,mrp:1000,currentStock:1,isSerialTracked:true}]:[{productId:1,variantId:1,productName:'Cable',variantName:'Red',sku:'CABLE-RED',sellingPrice:100,mrp:1000,currentStock:20,isSerialTracked:false}]);
  if(p==='/api/inventory/units')return json([{productUnitId:201,variantId:2,serialNumber:'SERIAL-201'},{productUnitId:202,variantId:99,serialNumber:'FOREIGN-VARIANT'}]);
  if(p==='/api/sales/manual-tax-rules')return json([]);
  if(p==='/api/quotations/preview'){
   const r=req.postDataJSON();previews.push(r);if(failPreview)return route.fulfill({status:422,contentType:'application/json',body:JSON.stringify({message:'Synthetic preview rejected'})});
   const q=quote(r);if(sentinel){q.totalAmount=9876.54;q.taxAmount=432.10;q.items[0].lineTotal=8765.43;}
   if(delayOld&&r.items[0]?.sellingPrice===111)await new Promise(resolve=>setTimeout(resolve,1000));return json(q);
  }
  if(p==='/api/quotations'&&req.method()==='POST')id=req.postDataJSON().quotationId;
  if((p==='/api/quotations'||p==='/api/quotations/'+id)&&['POST','PUT'].includes(req.method())){
   const r=req.postDataJSON();saves.push(structuredClone(r));
   if(committed.has(r.operationId)){assert.deepEqual(r,committed.get(r.operationId));return json({quotationId:id,version:saved.header.version});}
   committed.set(r.operationId,structuredClone(r));const revision=saved?saved.header.currentRevision+1:1,version=saved?saved.header.version+1:1;
   const body={documentDate:r.documentDate,validUntil:r.validUntil,terms:r.terms,reference:r.reference,request:r.request,customer:{...customer,customerId:r.request.customerId},quote:quote(r.request),lines:r.request.items.map(i=>({name:i.manualItem?.name||'Cable / Red',sku:i.manualItem?null:'CABLE-RED',quantity:i.quantity,unit:'PIECE',sellingPrice:i.sellingPrice,calculation:quote({items:[i]}).items[0]}))};
   saved={header:{quotationId:id,quotationNumber:'QTN-0001',customerName:customer.customerName,documentDate:r.documentDate,validUntil:r.validUntil,itemCount:r.request.items.length,total:body.quote.totalAmount,status:'DRAFT',currentRevision:revision,version},selected:{revision,body,preparedBy:'Owner'},revisions:Array.from({length:revision},(_,i)=>revision-i),history:[]};revisions.set(revision,structuredClone(saved.selected));
   if(loseSave){loseSave=false;return route.abort('failed');}return json({quotationId:id,version});
  }
  if(p==='/api/quotations/'+id+'/state'){const r=req.postDataJSON();assert(r.notes.length>=3);saved.header.status=r.status;saved.header.version++;return json({version:saved.header.version});}
  if(p==='/api/quotations/'+id){const revision=Number(url.searchParams.get('revision'));return json(revision?{...saved,selected:revisions.get(revision)}:saved);}
  if(p==='/api/quotations')return json({rows:saved?[saved.header]:[],total:saved?1:0,page:1,pageSize:20});
  const screens={'/Quotations':'Quotations','/Quotations/Create':'Create','/Quotations/Edit':'Edit','/Quotations/Details':'Details','/Quotations/Print':'Print'};
  if(screens[p])return route.fulfill({contentType:'text/html',path:path.join(fixtures,screens[p]+'.html')});
  if(p.startsWith('/api/'))return json({});
  const file=path.join(root,'src/StockFlow.Web/wwwroot',decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({body:''});
 });
 const ready=async()=>{try{await page.waitForFunction(()=>!document.querySelector('#quotation-save').disabled);}catch(error){console.error(await page.evaluate(()=>({validation:document.querySelector('#quotation-validation')?.textContent,status:document.querySelector('#quotation-calculation-status')?.textContent,error:document.querySelector('#quotation-message')?.textContent,reasons:[...document.querySelectorAll('[data-quote-reason]')].filter(e=>e.required).map(e=>({kind:e.dataset.quoteReason,value:e.value})),date:document.querySelector('#quotation-date-error')?.textContent})));throw error;}};
 const total=()=>page.locator('#quotation-summary p').last().locator('strong').textContent();
 const pick=async()=>{await page.locator('#quotation-customer-search').fill('Customer');await page.locator('#quotation-customers [role=option]').waitFor();await page.locator('#quotation-customer-search').press('Enter');};
 if(!process.env.QUOTATION_LAYOUT_ONLY){
 await page.goto('http://stockflow.test/Quotations/Create');await page.waitForFunction(()=>typeof document.querySelector('#quotation-customer-search').oninput==='function');
 assert(await page.locator('#quotation-customer').isHidden());assert(await page.locator('#quotation-empty').isVisible());assert(await page.locator('#quotation-save').isDisabled());
 assert.equal(await page.locator('#quotation-validation').textContent(),'');assert.equal(await page.locator('#quotation-items-error').textContent(),'','No warning before editing');
 await page.locator('#quotation-help-button').focus();await page.keyboard.press('Enter');assert(await page.locator('#quotation-help').evaluate(e=>e.matches(':popover-open')));await page.keyboard.press('Escape');assert(!(await page.locator('#quotation-help').evaluate(e=>e.matches(':popover-open'))));
 assert.equal(await page.locator('#quotation-summary #quotation-discount').count(),1);assert.equal(await page.locator('#quotation-discount').count(),1);
 await pick();assert(await page.locator('#quotation-customer-current').isVisible());assert.equal(await page.locator('#quotation-product-search').evaluate(e=>document.activeElement===e),true);
 await page.locator('#quotation-product-search').fill('Cable');await page.locator('#quotation-products button').click();await ready();assert.equal((await total()).replace(/[^0-9.]/g,''),'105.00');
 assert.equal(await page.locator('#quotation-save').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(255, 0, 127)','Primary save stays pink despite shared charcoal button theme');
 assert(await page.locator('#quotation-reason-field').isHidden());assert(await page.locator('.quote-line-reason').isHidden());assert.equal(await page.locator('#quotation-lines [data-quote-value=tax] input').count(),0);
 assert.equal(await page.locator('#quotation-lines [data-quote-input=quantity]').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(255, 255, 255)');
 await page.locator('#quotation-customer-clear').click();assert.equal(await page.locator('#quotation-lines tr').count(),1);assert(await page.locator('#quotation-save').isDisabled());await pick();await ready();
 // Reuse the actual quick-customer component, including required mobile validation.
 await page.locator('#quotation-customer-actions button').click();await page.locator('#new-customer-name').fill('New quotation customer');await page.locator('#create-customer').click();assert.equal(createdCustomers,0);
 await page.locator('#new-customer-mobile').fill('9876543212');await page.locator('#create-customer').click();await page.waitForFunction(()=>document.querySelector('#quotation-customer-name').textContent==='New quotation customer');assert.equal(createdCustomers,1);await ready();
 await page.locator('#quotation-customer-clear').click();await pick();await ready();
 // The editor must render arbitrary returned amounts, not calculate its own totals.
 sentinel=true;await page.locator('#quotation-lines [data-quote-input=quantity]').fill('2');await ready();assert.equal((await total()).replace(/[^0-9.]/g,''),'9876.54');assert.equal((await page.locator('[data-quote-value=total]').textContent()).replace(/[^0-9.]/g,''),'8765.43');
 sentinel=false;const previous=await total();await page.locator('#quotation-lines [data-quote-input=quantity]').fill('');assert(await page.locator('#quotation-save').isDisabled());assert.equal(await total(),previous);assert(!(await page.locator('#quotation-summary').innerText()).includes('NaN'));
 await page.locator('#quotation-lines [data-quote-input=quantity]').fill('1');await ready();
 delayOld=true;const oldRequest=page.waitForRequest(r=>r.url().includes('/api/quotations/preview')&&r.postDataJSON().items[0].sellingPrice===111);
 await page.locator('#quotation-lines [data-quote-input=sellingPrice]').fill('111');await oldRequest;await page.locator('#quotation-lines [data-quote-input=sellingPrice]').fill('222');await ready();const newer=await total();await page.waitForTimeout(1100);assert.equal(await total(),newer,'Older preview cannot overwrite current total');delayOld=false;
 failPreview=true;await page.locator('#quotation-lines [data-quote-input=sellingPrice]').fill('223');await page.waitForFunction(()=>document.querySelector('#quotation-calculation-status').textContent.includes('Synthetic preview rejected'));assert(await page.locator('#quotation-save').isDisabled());assert.equal(await total(),newer);failPreview=false;
 await page.locator('#quotation-lines [data-quote-input=sellingPrice]').fill('100');await ready();
 requireReasons=true;await page.locator('#quotation-discount').fill('5');await page.locator('#quotation-reason-field').waitFor({state:'visible'});assert(await page.locator('#quotation-save').isDisabled());
 await page.locator('#quotation-reason').fill('Reviewed commercial exception');await page.locator('[data-quote-reason=discount]').fill('Customer offer requested');await ready();assert((await page.locator('#quotation-policy').textContent()).includes('final checkout'));
 requireReasons=false;await page.locator('#quotation-discount').fill('0');await ready();assert(await page.locator('#quotation-reason-field').isHidden());
 await page.locator('#quotation-until').fill('2026-11-03');assert(await page.locator('#quotation-save').isDisabled());await page.locator('#quotation-until').fill('2026-11-11');await ready();
 await page.locator('#quotation-terms-options summary').click();await page.locator('#quotation-terms').fill('Original offer terms');await page.locator('#quotation-notes').fill('Original note');await page.locator('#quotation-reference').fill('Customer reference');
 await page.locator('#quotation-delivery-options summary').click();const explicitDeliveryAddress=await fillAddress(page.locator('#quotation-delivery'),'Explicit quotation delivery address');await selectValue(page.locator('#quotation-pos'),'36');await ready();
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No page overflow '+width);await page.locator('#quotation-save').scrollIntoViewIfNeeded();const button=await page.locator('#quotation-save').boundingBox();assert(button.x>=0&&button.x+button.width<=width+1&&button.height>=34,'Save reachable '+width);
  await page.evaluate(()=>{document.activeElement?.blur();window.scrollTo({top:0,behavior:'instant'});const main=document.querySelector('.sf-main');if(main)main.scrollTop=0;});
  await page.screenshot({path:path.join(fixtures,`quotation-editor-${width}x${height}.png`),fullPage:true,animations:'disabled'});console.log('PASS quotation layout '+width+'x'+height);
 }
 await page.setViewportSize({width:1920,height:1080});await page.locator('#quotation-save').click();await page.waitForFunction(()=>document.querySelector('#quotation-save').textContent.includes('Retry Same'));
 assert(await page.locator('#quotation-product-search').isDisabled());await page.reload();await page.waitForFunction(()=>document.querySelector('#quotation-save').textContent.includes('Retry Same'));assert(await page.locator('#quotation-customer-clear').isDisabled());
 await page.locator('#quotation-save').click();await page.waitForURL('**/Quotations/Details?id=*');assert.equal(committed.size,1);assert.deepEqual(saves[0],saves[1]);assert.deepEqual(saves[0].request.payments,[]);assert(!saves[0].request.sourceQuotationId);
 assert((await page.locator('#quotation-terms-detail').textContent()).includes('Original offer terms'));assert(await page.locator('#quotation-convert').isHidden());
 await page.locator('#quotation-edit').click();await page.waitForFunction(()=>document.querySelector('#quotation-title')?.textContent?.includes('Revision 1'));await ready();
 assert.equal(await page.locator('#quotation-reference').inputValue(),'Customer reference');assert.equal(await page.locator('#quotation-delivery').inputValue(),explicitDeliveryAddress);assert.equal(await page.locator('#quotation-notes').inputValue(),'Original note');
 await page.locator('#quotation-customer-clear').click();await pick();assert.equal(await page.locator('#quotation-delivery').inputValue(),explicitDeliveryAddress,'Customer refresh preserves explicit address');await ready();
 await page.locator('#quotation-terms-options summary').click();await page.locator('#quotation-terms').fill('Second immutable offer');await page.locator('#quotation-lines [data-quote-input=quantity]').first().fill('3');
 await page.locator('#quotation-manual').click();await page.locator('#quotation-manual-name').fill('Synthetic manual service');await page.locator('#quotation-manual-price').fill('25');await page.locator('#quotation-add-manual').click();await ready();
 await page.locator('#quotation-product-search').fill('Serial');await page.locator('#quotation-products button').click();const serialDialog=page.locator('dialog').filter({has:page.getByRole('heading',{name:'Choose an available serial'})});await serialDialog.waitFor();
 assert.deepEqual(await serialDialog.locator('option').allTextContents(),['SERIAL-201']);await serialDialog.getByRole('button',{name:'Use serial'}).click();await ready();
 assert.equal(await page.locator('#quotation-lines tr').count(),3);assert.equal(await page.locator('#quotation-save').textContent(),'Save New Revision');await page.locator('#quotation-save').click();await page.waitForURL('**/Quotations/Details?id=*');assert.equal(committed.size,2);assert.equal(saves[2].expectedVersion,1);assert.equal(saves[2].request.items[0].quantity,3);assert.equal(saves[2].request.items[1].manualItem.name,'Synthetic manual service');assert.equal(saves[2].request.items[2].variantId,2);assert.equal(saves[2].request.items[2].productUnitId,201);assert.notEqual(saves[0].operationId,saves[2].operationId);
 await page.goto('http://stockflow.test/Quotations/Details?id='+id+'&revision=1');await page.waitForFunction(()=>document.querySelector('#quotation-title')?.textContent?.includes('Revision 1'));assert((await page.locator('#quotation-terms-detail').textContent()).includes('Original offer terms'));assert(!(await page.locator('#quotation-terms-detail').textContent()).includes('Second immutable offer'));assert(await page.locator('#quotation-edit').isHidden());
 await page.goto('http://stockflow.test/Quotations/Details?id='+id);await page.locator('#quotation-state-notes').fill('Printed PDF manually shared');await page.locator('#quotation-state-form button').click();await page.waitForFunction(()=>document.querySelector('#quotation-status').textContent.includes('SENT'));
 await selectValue(page.locator('#quotation-state'),'ACCEPTED');await page.locator('#quotation-state-notes').fill('Customer accepted this revision');await page.locator('#quotation-state-form button').click();await page.waitForFunction(()=>document.querySelector('#quotation-status').textContent.includes('ACCEPTED'));assert.equal(await page.locator('#quotation-convert').getAttribute('href'),'/Sales/Create?quotationId='+id);assert((await page.locator('#quotation-print').getAttribute('href')).includes('revision=2'));
 await page.goto('http://stockflow.test/Quotations');await page.waitForFunction(()=>document.querySelector('#quotation-rows').textContent.includes('QTN-0001'));assert((await page.locator('#quote-export').getAttribute('href')).startsWith('/api/quotations/export?'));
 await page.goto('http://stockflow.test/Quotations/Print?id='+id+'&revision=1');await page.emulateMedia({media:'print'});const pdf=path.join(fixtures,'quotation-a4.pdf');await page.pdf({path:pdf,preferCSSPageSize:true});assert(fs.statSync(pdf).size>5000);assert((await page.locator('.quotation-paper').textContent()).includes('Original commercial terms'));assert.deepEqual(errors,[]);
 }
 await page.emulateMedia({media:'screen'});
 const camel=value=>Array.isArray(value)?value.map(camel):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([key,v])=>[key[0].toLowerCase()+key.slice(1),camel(v)])):value;
 for(const count of [3,5,10,20]){
  evidence=camel(JSON.parse(fs.readFileSync(path.join(fixtures,'populated-'+count+'.json'),'utf8')));id=evidence.header.quotationId;
  await page.goto('http://stockflow.test/Quotations/Edit?id='+id);await ready();
  assert.equal(await page.locator('#quotation-lines tr').count(),count);assert((await page.locator('#quotation-lines').innerText()).includes('Manual non-stock goods'));
  const q=evidence.selected.body.quote,format=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(n);
  for(const [index,key]of ['itemSubtotal','itemDiscountAmount','taxableAmount','taxAmount','totalAmount'].entries())assert.equal(await page.locator('.quote-summary-value strong').nth(index).textContent(),format(q[key]));
  assert.equal(await page.locator('#quotation-discount').inputValue(),String(evidence.selected.body.request.discountAmount));
  assert.equal(await page.locator('#quotation-pricing-mode').textContent(),evidence.selected.body.request.pricesIncludeTax?'Selling prices include GST; the server extracts applicable tax.':'Selling prices exclude GST; the server adds applicable tax.');
  for(const [width,height]of (count===3?[[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]:[[1920,1080],[390,844]])){
   await page.setViewportSize({width,height});await page.waitForTimeout(400);await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Populated overflow '+count+'/'+width+' '+JSON.stringify(await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,overflow:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+2&&!e.closest('.quote-table-scroll')).slice(0,8).map(e=>({tag:e.tagName,id:e.id,cls:e.className,parent:e.parentElement.outerHTML.slice(0,900),right:e.getBoundingClientRect().right}))}))));
   await page.locator('#quotation-save').scrollIntoViewIfNeeded();await page.locator('#quotation-save').click({trial:true});
   await page.evaluate(()=>{document.activeElement?.blur();window.scrollTo({top:0,behavior:'instant'});const main=document.querySelector('.sf-main');if(main)main.scrollTop=0;});
   await page.screenshot({path:path.join(fixtures,'quotation-populated-'+count+'-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'});
  }
  console.log('PASS real PostgreSQL saved '+count+'-line quotation rendered with exact authoritative summary; contained layout and unobstructed Save');
 }
 assert.deepEqual(errors,[]);
 console.log('PASS quotation browser: shared quick customer, keyboard/clear, exact SKU/manual line, server-returned sentinel totals, stale/failed/invalid preview guards, policy reasons, 10 viewports, lost-response reload/exact retry, revision metadata/history, lifecycle/conversion link and selected-revision PDF. HTTP mocked; physical printing unverified.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
