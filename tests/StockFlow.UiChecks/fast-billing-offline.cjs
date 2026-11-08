const {fillAddress}=require('./address-test-helpers.cjs');
// Real rendered Razor + local scripts/styles; mocked HTTP isolates browser behavior.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixture=path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),output=path.join(root,'tests/bin/fast-billing-ui-fixtures');fs.mkdirSync(output,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768}}),errors=[],posts=[],created=[],drafts=new Map();const quoteRequests=[];let fastPolicy=false;let failQuote=false,delayTax=false,canOverrideTax=true,canApproveDiscount=true,approvalStatus='REQUIRED',quoteFailureMessage='Synthetic quote rejected';let failCustomer=false,duplicate=false,gstContext={mapped:false},manualRules=[{ruleId:7,ruleName:'Reviewed small goods',hsnSac:'610910',uqc:'NOS',treatment:'TAXABLE',taxRate:5}];
 const draftNumbers=new Map(),referenceFor=id=>{if(!draftNumbers.has(id))draftNumbers.set(id,'DRF-'+String(draftNumbers.size+1).padStart(4,'0'));return draftNumbers.get(id);};
 const handleDraft=require('./draft-lifecycle-mock.cjs')(drafts,referenceFor);
 page.setDefaultTimeout(Number(process.env.STOCKFLOW_UI_TIMEOUT_MS||12000));page.on('pageerror',e=>errors.push(e.message));
 const product={productId:1,variantId:1,productName:'Cable / Red',sku:'CABLE-RED',attributes:{Color:'Red'},sellingPrice:100,mrp:120,taxPercentage:18,currentStock:3,isActive:true,isAvailable:true,isSerialTracked:false};
 let loseSaleResponse=false;const committedRequests=new Map(),recoveredRequests=[];
 const customer={customerId:10,customerName:'Counter customer',mobileNumber:'9876543210'};
 let customerResults=[customer];
 let paginationTestTotal=null;
 await page.route('**/*',async route=>{const req=route.request(),url=new URL(req.url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(url.pathname==='/Sales/Create')return route.fulfill({contentType:'text/html',body:fs.readFileSync(fixture,'utf8')});
  if(url.pathname.startsWith('/Sales/Invoice/'))return route.fulfill({contentType:'text/html',body:'<h1>Invoice saved</h1>'});
  if(url.pathname==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Counter'}],locationId:1,storeId:1});
  if(url.pathname==='/api/notifications')return json({unread:0,items:[]});
  if(url.pathname==='/api/sales/billing/policy')return json({canOverridePrice:true,canOverrideTax,canDiscount:true,canApproveDiscount,discountLimitPercent:10});
  if(url.pathname==='/api/sales/gst-context')return json(gstContext);
  if(url.pathname==='/api/sales/manual-tax-rules')return json(manualRules);
  if(url.pathname.startsWith('/api/quotations/')){const source='c75ae01a-1b09-4469-9af5-c3e09f261401';if(url.pathname.endsWith('/checkout'))return json({changes:['Current prices checked.'],snapshot:{sourceQuotationId:source,sourceQuotationRevision:2,sourceQuotationVersion:4,sourceQuotationNumber:'QTN-0001',draftId:'58a68f73-189a-4aa0-a0a2-e9430e75770e',savedAt:Date.now(),cart:[{...product,quantity:1,discountAmount:0,discountPercent:0,catalogPrice:100,catalogTaxPercentage:18}],customerId:10,customerLabel:customer.customerName,discount:0,mode:'CASH',payments:[],pricesIncludeTax:false}});return json({header:{version:4,currentRevision:2}});}
  if(url.pathname==='/api/scan')return json(product);
  if(url.pathname==='/api/products/variants')return json([product]);
  if(url.pathname==='/api/customers/search')return json(url.searchParams.has('mobileNumber')||url.searchParams.has('gstNumber')?duplicate?[customer]:[]:customerResults);
  if(url.pathname==='/api/customers'){if(failCustomer)return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:'Customer save unavailable'})});created.push(req.postDataJSON());assert.equal(url.searchParams.get('requireMobile'),'true');return json({...customer,...req.postDataJSON(),customerId:11});}
  if(url.pathname.endsWith('/approval')){approvalStatus='PENDING';return json({});}
  if(paginationTestTotal!==null&&req.method()==='GET'&&url.pathname==='/api/sales/billing/drafts')return json({total:paginationTestTotal,items:Array.from({length:paginationTestTotal},(_,n)=>({draftId:'pagination-'+n,draftNumber:'DRF-'+n,draftStatus:'HELD',customerName:'Pagination fixture',itemCount:1,units:1,estimatedTotal:100,createdAt:'2026-11-04T10:00:00Z',updatedAt:'2026-11-04T10:00:00Z'}))});
  if(url.pathname.startsWith('/api/sales/billing/drafts'))return handleDraft(req,url,route,json);
  if(url.pathname==='/api/sales/quote'){
   const b=req.postDataJSON();quoteRequests.push(b);if(delayTax&&b.items[0]?.discountPercent===6)await new Promise(resolve=>setTimeout(resolve,900));if(failQuote)return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:quoteFailureMessage})});const money=n=>Math.round((n+Number.EPSILON)*100)/100;let remainingBase=b.items.reduce((s,i)=>s+i.quantity*i.sellingPrice-(i.discountPercent!=null?money(i.quantity*i.sellingPrice*i.discountPercent/100):i.discountAmount),0),remainingDiscount=b.allocateInvoiceDiscount?b.discountAmount:0;const items=b.items.map(i=>{const tax=i.manualItem?(i.manualItem.taxRuleId?5:0):(i.taxOverride??18),discount=i.discountPercent!=null?Math.round(i.quantity*i.sellingPrice*i.discountPercent)/100:i.discountAmount,gross=i.quantity*i.sellingPrice-discount,allocation=remainingBase?money(remainingDiscount*gross/remainingBase):0;remainingBase-=gross;remainingDiscount-=allocation;const net=gross-allocation,taxAmount=Math.round(net*tax)/100;return {discountAmount:discount,taxPercentage:tax,taxAmount,lineTotal:net+taxAmount,currentStock:i.manualItem?0:3,catalogPrice:i.manualItem?i.sellingPrice:100,mrp:i.manualItem?i.manualItem.mrp:120};});
   const subtotal=items.reduce((s,i)=>s+i.lineTotal-i.taxAmount,0),taxAmount=items.reduce((s,i)=>s+i.taxAmount,0);const requiresApproval=(b.items.reduce((sum,i,n)=>sum+i.quantity*(i.manualItem?i.sellingPrice:100)*(1+items[n].taxPercentage/100),0)-(subtotal+taxAmount-(b.allocateInvoiceDiscount?0:b.discountAmount)))>b.items.reduce((sum,i,n)=>sum+i.quantity*(i.manualItem?i.sellingPrice:100)*(1+items[n].taxPercentage/100),0)*.1;return json({itemSubtotal:b.items.reduce((s,i)=>s+i.quantity*i.sellingPrice,0),itemDiscountAmount:items.reduce((s,i)=>s+i.discountAmount,0),invoiceDiscountAmount:b.discountAmount,taxableAmount:subtotal,subtotal,taxAmount,discountAmount:b.allocateInvoiceDiscount?0:b.discountAmount,totalAmount:Math.round((subtotal+taxAmount-(b.allocateInvoiceDiscount?0:b.discountAmount))*100)/100,adjustmentReview:{thresholdPercent:10,requiresApproval,approvalStatus,policyVersion:2,invoiceReasonRequired:b.discountAmount>0&&(!fastPolicy||requiresApproval),lineReasons:b.items.map(i=>({discount:(i.discountPercent>0||i.discountAmount>0)&&(!fastPolicy||requiresApproval),price:!i.manualItem&&i.sellingPrice!==100,tax:false}))},items});
  }
  if(url.pathname==='/api/sales/recover'){const payload=req.postDataJSON();recoveredRequests.push(payload);assert.deepEqual(payload,committedRequests.get(payload.requestId),'Verification uses the exact saved snapshot');return json({saleId:42});}
  if(url.pathname==='/api/sales'){const payload=req.postDataJSON();posts.push(payload);if(committedRequests.has(payload.requestId))assert.deepEqual(payload,committedRequests.get(payload.requestId),'Retry preserves the exact finalization snapshot');else committedRequests.set(payload.requestId,payload);if(loseSaleResponse){loseSaleResponse=false;return route.abort('failed');}return json({saleId:42});}
  if(url.pathname.startsWith('/api/'))return json([]);
  const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 async function fillReason(index,value){const dialog=page.locator('#line-reason-panel-'+index);await dialog.evaluate(n=>{if(!n.open)n.showModal();});const field=page.locator('#line-reason-'+index);if(await field.isVisible())await field.fill(value);else assert((await field.inputValue()).trim(),'Hidden completed reason must already be saved');await dialog.getByRole('button',{name:'Done',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);}
 async function editTax(index,value){await page.locator('#sale-cart tr').nth(index).locator('button[data-tax-correction]').click();await page.locator('#line-tax-override-'+index).fill(value);await page.locator('#line-reason-panel-'+index).getByRole('button',{name:'Done',exact:true}).click();}
 await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 assert.equal(await page.locator('#billing-draft-reference').textContent(),'New Draft');
 assert.equal(await page.locator('#billing-draft-reference').evaluate(n=>n.tagName),'SPAN','Existing draft updates target the inline badge');
 assert.equal(await page.locator('#billing-workspace > section.card').count(),0,'Moved header toolbar leaves no spacer card');
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1024,768],[768,1024],[390,844],[320,640]]){
  await page.setViewportSize({width,height});await page.waitForTimeout(280);
  const compact=await page.evaluate(()=>{
   const get=s=>document.querySelector(s),rect=s=>get(s).getBoundingClientRect(),title=rect('.billing-title-row h1'),badge=rect('#billing-draft-reference'),customer=rect('#customer-search'),add=rect('.billing-customer-row>.btn'),scan=rect('#sale-scan');
   const controls=[...get('.billing-scan-row:has(#sale-scan)').querySelectorAll(':scope>.btn')].map(n=>({height:n.getBoundingClientRect().height,top:n.getBoundingClientRect().top}));
   return {headerToLookup:rect('.billing-lookup-card').top-rect('.sf-page-heading').bottom,breadcrumbToHeader:rect('.sf-page-heading').top-rect('.sf-breadcrumb').bottom,titleFont:getComputedStyle(get('.billing-title-row h1')).fontSize,badge:{height:badge.height,font:parseFloat(getComputedStyle(get('#billing-draft-reference')).fontSize),gap:badge.left-title.right,center:Math.abs(badge.top+badge.height/2-title.top-title.height/2)},customer:{height:customer.height,font:getComputedStyle(get('#customer-search')).fontSize},add:{height:add.height,top:add.top-customer.top},scan:{height:scan.height,top:scan.top},controls,scanRowWidth:rect('.billing-scan-row:has(#sale-scan)').width,overflow:document.documentElement.scrollWidth>innerWidth+1};
  });
  assert.equal(compact.titleFont,'22px');assert(compact.badge.height>=22&&compact.badge.height<=24);assert(compact.badge.font>=11&&compact.badge.font<=12);assert(compact.badge.gap>=10&&compact.badge.gap<=12);assert(compact.badge.center<1,'Badge centered next to title');assert(!compact.overflow);
  assert(compact.headerToLookup>=0&&compact.headerToLookup<=12,'Compact gap above customer/summary panels: '+JSON.stringify(compact));
  assert(compact.breadcrumbToHeader>=0&&compact.breadcrumbToHeader<=6,'Compact gap below breadcrumbs');
  if(width>=768){assert.equal(compact.customer.height,40);assert.equal(compact.customer.font,'14px');assert.equal(compact.add.height,40);assert(Math.abs(compact.add.top)<1,'Customer/Add aligned');assert.equal(compact.scan.height,40);for(const c of compact.controls){assert.equal(c.height,40);if(compact.scanRowWidth>420)assert(Math.abs(c.top-compact.scan.top)<1,'Search actions align with input');}}
  else{assert(compact.customer.height>=44&&compact.add.height>=44&&compact.scan.height>=44);for(const c of compact.controls)assert(c.height>=44);}
  if(width===1366||width===390)await page.screenshot({path:path.join(output,`new-sale-compact-${width}.png`),fullPage:true});
 }
 await page.setViewportSize({width:1366,height:768});
 console.log('PASS inline live draft badge and aligned 40px desktop / accessible mobile lookup controls at seven sizes.');
 for(const total of [0,1,10,11]){
  paginationTestTotal=total;await page.locator('#show-held').click();await page.waitForFunction(total=>document.querySelector('#saved-bills-count').textContent===total+' '+(total===1?'Draft':'Drafts'),total);
  assert.equal(await page.locator('.billing-saved-pagination').isVisible(),total>10,'Saved Bills threshold '+total);await page.keyboard.press('Escape');
 }
 paginationTestTotal=null;console.log('PASS Saved Bills pagination hidden at 0/1/10 and visible at 11');
 assert(!await page.locator('#customer-current').innerText().then(t=>t.includes('Walk-in')));
 assert.equal(await page.locator('#billing-draft-pill').count(),0);assert(await page.locator('#billing-notes').isVisible());assert(await page.locator('#cash-change-field').isHidden());assert.equal(await page.locator('#payment-amount').count(),0);assert.equal(await page.locator('.billing-scan-row #quick-add-item').count(),1);
 assert.equal(await page.locator('.billing-payment-tile').count(),5);assert.equal(await page.locator('#billing-split-toggle').isVisible(),false);
 await page.evaluate(()=>{window.shortcutHolds=0;document.querySelector('#hold-bill').addEventListener('click',()=>window.shortcutHolds++);document.activeElement?.blur();});
 await page.keyboard.press('Alt+s');assert(await page.locator('#sale-scan').evaluate(n=>document.activeElement===n));await page.keyboard.press('Alt+h');assert.equal(await page.evaluate(()=>window.shortcutHolds),0,'Hold shortcut does not run while typing');
 await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').dispatchEvent('keydown',{key:'Enter',repeat:true,bubbles:true});await page.locator('#sale-scan').dispatchEvent('keydown',{key:'Enter',ctrlKey:true,bubbles:true});assert.equal(await page.locator('#sale-cart tr').count(),0,'Held/modifier Enter never adds an item');
 await page.locator('#sale-scan').press('Enter');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1);
 await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');await page.waitForFunction(()=>document.querySelector('.billing-quantity input')?.value==='2');
 assert.equal(await page.locator('#sale-cart tr').count(),1);await page.locator('[data-discount-editor]').fill('10');
 await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='212.40');assert.equal(await page.locator('#line-mrp-0').textContent(),'120.00');
 await fillReason(0,'Counter discount');
 assert(await page.locator('#line-tax-0').evaluate(n=>n.readOnly),'Ordinary cart tax remains read-only');
 await editTax(0,'12');await page.waitForFunction(()=>document.querySelector('#line-warning-0').textContent==='Tax Reason');
 await page.locator('#line-warning-0').click();await page.locator('#line-tax-reason-0').fill('Reviewed ordinary bill tax correction');await page.locator('#line-reason-panel-0').getByRole('button',{name:'Done',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='201.60');
 await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 assert.equal(quoteRequests.at(-1).items[0].taxAdjustmentReason,'Reviewed ordinary bill tax correction');
 await editTax(0,'18');await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='212.40');
 assert.notEqual(await page.locator('#line-warning-0').textContent(),'!','No information icon remains');
 assert.equal(await page.locator('#line-mrp-0').textContent(),'120.00','MRP survives quote refresh and tax changes');
 await page.getByRole('button',{name:/Decrease quantity for Cable/}).click();await page.waitForFunction(()=>document.querySelector('.billing-quantity input').value==='1');
 await page.getByRole('button',{name:/Increase quantity for Cable/}).click();await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='212.40');
 // An unfinished number preserves committed cart values and confirmed amounts.
 const confirmed=await page.locator('#summary-total').textContent();
 await page.locator('[data-discount-editor]').fill('');assert.equal(await page.locator('#summary-total').textContent(),confirmed);assert(await page.locator('#complete-sale').isDisabled());await page.locator('[data-discount-editor]').fill('10');await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 for(const selector of ['.billing-quantity input','[data-price-editor]']){
  const editor=page.locator(selector).first(),value=await editor.inputValue();await editor.fill('');assert.equal(await page.locator('#summary-total').textContent(),confirmed);assert(await page.locator('#complete-sale').isDisabled());await editor.fill(value);await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 }
 await page.locator('#calculate-change').check();await page.locator('#cash-received').fill('200');assert(await page.locator('#complete-sale').isDisabled());assert((await page.locator('#cash-change').textContent()).includes('12.40'));await page.locator('#cash-received').fill(' 250 ');assert.equal(await page.locator('#cash-received').inputValue(),'250');assert((await page.locator('#cash-change').textContent()).includes('37.60'));
 assert.deepEqual(await page.locator('#cash-received').evaluate(n=>({align:getComputedStyle(n).textAlign,background:getComputedStyle(n).backgroundColor,type:n.type})),{align:'left',background:'rgb(255, 255, 255)',type:'text'});
 for(const invalid of ['250x','-1','250.001','2 50']){await page.locator('#cash-received').fill(invalid);assert((await page.locator('#cash-change').textContent()).includes('Enter a valid cash amount'));assert(await page.locator('#complete-sale').isDisabled());}await page.locator('#cash-received').fill('');
 // Old tax responses cannot replace the most recent input and confirmation.
 delayTax=true;await page.locator('[data-discount-editor]').fill('6');await page.waitForFunction(()=>document.querySelector('#billing-summary').getAttribute('aria-busy')==='true');await page.waitForTimeout(300);
 failQuote=true;await page.locator('[data-discount-editor]').fill('11');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').textContent.includes('Synthetic quote rejected'));assert.equal(await page.locator('#summary-total').textContent(),confirmed);assert(await page.locator('#complete-sale').isDisabled());failQuote=false;await page.locator('#billing-retry-calculation').click();await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='210.04');await page.locator('[data-discount-editor]').fill('10');await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 // Inline edits preserve confirmed amounts until a new valid quote arrives.
 assert(await page.locator('#sale-discount').isVisible());assert.equal(await page.locator('#invoice-discount-add').count(),0);
 await page.locator('#sale-discount').fill('');assert.equal(await page.locator('#summary-total').textContent(),confirmed);assert(await page.locator('#complete-sale').isDisabled());
 await page.locator('#sale-discount').fill('1.25');await page.locator('#invoice-discount-reason').fill('Small invoice promotion');await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='210.93');
 assert.equal(await page.locator('[data-discount-editor]').inputValue(),'10');
 await page.locator('#sale-discount').fill('-1');assert.equal(await page.locator('#summary-total').textContent(),'₹210.93');assert(await page.locator('#complete-sale').isDisabled());
 await page.locator('#sale-discount').fill('0');await page.waitForFunction(()=>document.querySelector('#sale-total-amount').textContent==='212.40');
 await page.locator('#calculate-change').uncheck();assert(await page.locator('#cash-change-field').isHidden());
 // Registered customer requirement opens search and retains the cart.
 await fillReason(0,'Counter promotion');assert(await page.locator('#complete-sale').isDisabled());await page.locator('#customer-search').focus();
 await page.locator('#customer-search').fill('9876543210');await page.locator('#customer-results [role=option]').waitFor();
 await page.locator('#customer-search').dispatchEvent('keydown',{key:'Enter',isComposing:true,bubbles:true});assert.equal(await page.locator('#sale-customer').inputValue(),'','Composition Enter must not choose a customer');
 await page.locator('#customer-search').press('Enter');assert.equal(await page.locator('#sale-customer').inputValue(),'10','Enter alone chooses the visible first customer');
 assert(await page.locator('#sale-scan').evaluate(n=>document.activeElement===n),'Customer selection focuses scanning');assert.equal(posts.length,0,'Selecting customer never submits checkout');
 await page.locator('#customer-clear').click();customerResults=[customer,{customerId:11,customerName:'Other counter customer',mobileNumber:'9876543211'}];
 await page.locator('#customer-search').fill('Counter');await page.waitForFunction(()=>document.querySelectorAll('#customer-results [role=option]').length===2);
 await page.locator('#customer-search').press('ArrowDown');await page.locator('#customer-search').press('ArrowDown');await page.locator('#customer-search').press('Enter');
 assert.equal(await page.locator('#sale-customer').inputValue(),'11','Enter respects the highlighted customer rather than selecting the first');
 await page.locator('#customer-clear').click();customerResults=[customer];await page.locator('#customer-search').fill('9876543210');await page.locator('#customer-results [role=option]').waitFor();
 await page.locator('#customer-search').press('ArrowDown');await page.locator('#customer-search').press('Enter');assert.equal(await page.locator('#sale-cart tr').count(),1);
 console.log('PASS customer keyboard selection: Enter alone, highlighted alternative, IME composition, scanner focus and no checkout submission.');
 // Clearing preserves bill contents and its server reference, persists null, and focuses closed search.
 assert.equal(await page.locator('#customer-name').textContent(),'Counter customer');
 await page.waitForFunction(()=>!document.querySelector('#complete-sale').disabled);await page.evaluate(()=>document.activeElement?.blur());await page.keyboard.press('Alt+b');assert(await page.locator('#complete-sale').evaluate(n=>document.activeElement===n));assert.equal(posts.length,0,'Checkout shortcut focuses but never posts');
 await page.evaluate(()=>document.activeElement?.blur());await page.keyboard.press('Alt+c');assert(await page.locator('#customer-clear').evaluate(n=>document.activeElement===n),'Selected customer is focused without clearing it');
 assert.equal(await page.locator('#customer-mobile').textContent(),'Mobile: 9876543210');
 await page.waitForFunction(()=>document.querySelector('#billing-draft-reference').textContent.includes('DRF-'));
 const customerDraftReference=await page.locator('#billing-draft-reference').textContent();
 await page.locator('#customer-clear').click();
 assert.equal(await page.locator('#customer-search').evaluate(n=>n===document.activeElement),true);
 assert(await page.locator('#customer-results').isHidden());assert.equal(await page.locator('#sale-customer').inputValue(),'');
 await page.waitForFunction(()=>document.querySelector('#draft-status').textContent.includes('saved'));
 assert.equal(await page.locator('#billing-draft-reference').textContent(),customerDraftReference);
 assert.equal(await page.locator('#sale-cart tr').count(),1);assert.equal(await page.locator('[data-discount-editor]').inputValue(),'10');
 await page.waitForTimeout(800);assert.equal([...drafts.values()].find(d=>d.draftStatus==='ACTIVE').payload.customerId,null);
 // Results float below the field without moving the product section at every supported size.
 for(const [width,height] of [[1280,650],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.locator('#customer-search').scrollIntoViewIfNeeded();
  const before=await page.locator('#billing-add-card').evaluate(n=>n.getBoundingClientRect().top-n.parentElement.getBoundingClientRect().top);
  await page.locator('#customer-search').fill('Counter');await page.locator('#customer-results [role=option]').waitFor();
  const input=await page.locator('#customer-search').boundingBox(),results=await page.locator('#customer-results').boundingBox();
  assert(results.x>=0&&results.x+results.width<=width+1,'Customer results fit viewport '+width);
  assert(Math.abs(results.y-input.y-input.height-4)<2,'Customer results anchored below search');
  assert(Math.abs(await page.locator('#billing-add-card').evaluate(n=>n.getBoundingClientRect().top-n.parentElement.getBoundingClientRect().top)-before)<2,'Results do not push bill layout '+width);
  await page.locator('#customer-search').press('Escape');assert(await page.locator('#customer-results').isHidden());
 }
 await page.setViewportSize({width:1366,height:768});
 // Mobile required, optional fields collapsed, successful save auto-selects and restores scanning.
 await page.getByRole('button',{name:'+ Add Customer',exact:true}).click();assert(await page.locator('#new-customer-mobile').getAttribute('required')!==null);
 assert.equal(await page.locator('#new-customer-address').isVisible(),false);await page.locator('#new-customer-name').fill('New counter customer');await page.locator('#create-customer').click();assert.equal(created.length,0);
 await page.locator('#new-customer-mobile').fill('123');await page.locator('#create-customer').click();assert.equal(created.length,0);
 duplicate=true;await page.locator('#new-customer-mobile').fill('9876543210');await page.getByRole('button',{name:'Select Existing Customer',exact:true}).waitFor();await page.getByRole('button',{name:'Select Existing Customer',exact:true}).click();
 assert(await page.locator('#customer-quick-add-dialog').isHidden());assert.equal(await page.locator('#sale-customer').inputValue(),'10');assert.equal(await page.locator('#sale-cart tr').count(),1);assert.equal(await page.locator('#billing-draft-reference').textContent(),customerDraftReference);assert.equal(created.length,0);
 duplicate=false;await page.getByRole('button',{name:'+ Add Customer',exact:true}).click();await page.locator('#new-customer-name').fill('New counter customer');
 await page.screenshot({path:path.join(output,'add-customer-default.png')});
 assert.equal(await page.locator('#quick-customer-additional').getAttribute('open'),null);
 const nameBox=await page.locator('#new-customer-name').boundingBox(),phoneBox=await page.locator('#new-customer-mobile').boundingBox();assert(Math.abs(nameBox.width-phoneBox.width)<1,'Both mandatory fields have equal full width');
 await page.locator('#quick-customer-additional summary').click();await page.locator('#new-customer-email').fill('bad-email');await page.locator('#new-customer-mobile').fill('9861200066');await page.locator('#create-customer').click();assert.equal(created.length,0);
 await page.locator('#new-customer-email').fill('accounts@example.test');await page.locator('#new-customer-gst').fill('29ABCDE1234F1Z5');await page.locator('#create-customer').click();assert.equal(created.length,0,'Bad GST checksum cannot create customer');
 await page.locator('#new-customer-gst').fill('29abcde1234f1zw');const enteredCustomerAddress=await fillAddress(page.locator('#new-customer-address'),'Main Road, Bengaluru, Karnataka 560001');
 // Optional fields and both footer actions stay reachable across small screens.
 for(const [width,height] of [[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});const dialog=page.locator('#customer-quick-add-dialog');
  const rect=await dialog.boundingBox();assert(rect.x>=0&&rect.x+rect.width<=width+1&&rect.y>=0&&rect.y+rect.height<=height+1,'Dialog fits '+width);
  await page.locator('#create-customer').scrollIntoViewIfNeeded();assert(await page.locator('#create-customer').isVisible());
  assert(await dialog.evaluate(n=>n.scrollWidth<=n.clientWidth+1),'No horizontal dialog overflow '+width);if(width===320)await page.screenshot({path:path.join(output,'add-customer-expanded-320.png')});
 }
 await page.setViewportSize({width:1366,height:768});
 failCustomer=true;await page.locator('#create-customer').click();await page.waitForFunction(()=>document.querySelector('#quick-customer-alert').textContent.includes('Customer save unavailable'));
 assert(await page.locator('#customer-quick-add-dialog').isVisible());assert.equal(await page.locator('#new-customer-email').inputValue(),'accounts@example.test');assert.equal(await page.locator('#new-customer-address').inputValue(),enteredCustomerAddress);assert.equal(created.length,0);failCustomer=false;
 await page.locator('#new-customer-mobile').fill('9861200066');await page.locator('#create-customer').click();await page.waitForFunction(()=>!document.querySelector('#customer-quick-add')?.open && document.querySelector('#sale-customer').value==='11');
 assert.equal(created.length,1);assert.equal(created[0].email,'accounts@example.test');assert.equal(created[0].gstNumber,'29ABCDE1234F1ZW');assert(created[0].address.includes('560001'));assert.equal(await page.locator('#billing-draft-reference').textContent(),customerDraftReference);assert.equal(await page.locator('#sale-cart tr').count(),1);
 await page.locator('#quick-add-item').click();
 assert.equal(await page.locator('#manual-rule').getAttribute('type'),'text');assert(await page.locator('#manual-submit').isDisabled());
 assert.equal(await page.locator('.billing-manual-modal .sf-validation-summary').count(),0);assert.equal(await page.locator('.billing-manual-modal .is-invalid').count(),0);
 await page.locator('#manual-name').fill('Loose cord');await page.locator('#manual-price').fill('50');await page.locator('#manual-quantity').fill('2');await page.locator('#manual-mrp').fill('60');await page.locator('#manual-discount').fill('5');
 await page.locator('#manual-rule').fill('18');assert(await page.locator('#manual-submit').isDisabled());
 await page.locator('#manual-rule').fill('Reviewed small goods · 610910 · 5% (#7)');
 await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);assert((await page.locator('#manual-preview').textContent()).includes('99.75'));await page.locator('#manual-submit').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===2);
 assert.equal(await page.locator('#line-stock-1').textContent(),'Non-stock');assert((await page.locator('#sale-cart tr').nth(1).textContent()).includes('Manual Item'));
 await page.locator('.billing-manual-edit').click();assert.equal(await page.locator('#manual-price').inputValue(),'50');await page.locator('#manual-quantity').fill('3');await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);await page.locator('#manual-submit').click();
 await fillReason(1,'Loose cord discount');
 const reviewedRules=manualRules;manualRules=[];
 await page.locator('#quick-add-item').click();await page.locator('#manual-name').fill('Loose cord');await page.locator('#manual-price').fill('5');
 assert.equal(await page.locator('#manual-rule').getAttribute('required'),null);await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 assert((await page.locator('#manual-preview').textContent()).includes('Tax: ₹0.00'));await page.locator('#manual-submit').click();manualRules=reviewedRules;
 await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===3&&document.querySelector('#line-mrp-2').textContent==='—');
 await page.locator('#sale-cart tr').nth(2).locator('.billing-manual-edit').click();assert.equal(await page.locator('#manual-rule').inputValue(),'');await page.locator('#manual-cancel').click();
 await page.locator('#sale-cart tr').nth(2).locator('.billing-item-actions button').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===2);
 await page.locator('#sale-notes').evaluate((n,value)=>{n.value=value;n.dispatchEvent(new Event('input',{bubbles:true}));},'Held mixed bill');await Promise.all([page.waitForEvent('load'),page.locator('#hold-bill').click()]);await page.waitForFunction(()=>document.querySelector('#billing-draft-reference').textContent==='New Draft'&&!document.querySelector('#sale-editor').disabled).catch(async e=>{console.log({errors,state:await page.locator('#draft-status').textContent(),reference:await page.locator('#billing-draft-reference').textContent()});throw e;});
 await page.locator('#show-held').click();await page.getByRole('button',{name:/Resume Bill/}).first().click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===2&&!document.querySelector('#sale-editor').disabled&&!document.querySelector('#saved-bills-drawer').open);
 assert.equal(await page.locator('#sale-notes').inputValue(),'Held mixed bill');assert.equal(await page.locator('#sale-customer').inputValue(),'11');assert.equal(await page.locator('#sale-cart tr').nth(1).locator('.billing-quantity input').inputValue(),'3');
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});
  // Wait for the existing sidebar margin transition before measuring container-query rows.
  await page.waitForFunction(()=>innerWidth>991||(Math.abs(parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft))<1&&document.querySelector('.sf-sidebar').getBoundingClientRect().right<=1),null,{timeout:3000});
  await page.locator('#quick-add-item').scrollIntoViewIfNeeded();if(width===1366||width===320)await page.screenshot({path:path.join(output,'billing-layout-'+width+'.png'),fullPage:true});
  const quick=await page.locator('#quick-add-item').boundingBox(),searchAction=await page.locator('#scan-sale').boundingBox();assert(quick.x>=searchAction.x+searchAction.width-1&&Math.abs(quick.y-searchAction.y)<3,'Quick Add Item beside search '+width+' '+JSON.stringify({quick,searchAction}));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'page overflow '+width+'x'+height);
  const billingLayout=await page.locator('.billing-layout').evaluate(n=>{const catalog=n.querySelector('.billing-catalog-pane').getBoundingClientRect(),checkout=n.querySelector('.billing-checkout-pane').getBoundingClientRect();return {container:document.querySelector('#billing-workspace').getBoundingClientRect().width,columns:getComputedStyle(n).gridTemplateColumns,sideBySide:checkout.left>=catalog.right-1};});
  assert.equal(billingLayout.sideBySide,billingLayout.container>=980,'Existing checkout container breakpoint '+width);
  console.log('CHECK billing layout '+width+': '+JSON.stringify(billingLayout));
  assert(await page.locator('#sale-discount').isVisible());await page.locator('#calculate-change').check();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Expanded checkout fits '+width);await page.locator('#calculate-change').uncheck();
  await page.locator('#quick-add-item').click();const box=await page.locator('.billing-manual-modal').boundingBox();assert(box.x>=0&&box.x+box.width<=width+1&&box.height<=height,'modal fits '+width+'x'+height);
  const closeBox=await page.locator('#manual-close').boundingBox();
  assert(closeBox.x>=box.x+box.width-70&&closeBox.y<=box.y+45,'close icon stays at top right '+width+'x'+height);
  assert.equal(await page.locator('.billing-manual-modal .sf-validation-summary:not([hidden])').count(),0);assert.equal(await page.locator('.billing-manual-modal .is-invalid').count(),0);
  await page.locator('#manual-close').click();assert(!await page.locator('.billing-manual-modal').isVisible(),'top-right close dismisses the popup');
  await page.screenshot({path:path.join(output,`fast-billing-${width}x${height}.png`),fullPage:true});console.log('PASS viewport '+width+'x'+height);
 }
 const scaled=await page.context().newCDPSession(page);await scaled.send('Emulation.setDeviceMetricsOverride',{width:911,height:512,deviceScaleFactor:1.5,mobile:false});
 await page.locator('#complete-sale').scrollIntoViewIfNeeded();const scaledAction=await page.locator('#complete-sale').boundingBox();assert(scaledAction.y>=0&&scaledAction.y+scaledAction.height<=512,'Checkout reachable at 150% equivalent CSS viewport');assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No overflow at 150% equivalent viewport');await page.screenshot({path:path.join(output,'billing-effective-150-percent.png'),fullPage:true});await scaled.send('Emulation.clearDeviceMetricsOverride');await scaled.detach();
 await page.setViewportSize({width:1366,height:768});await page.locator('.billing-payment-tile[data-payment-mode="UPI"]').click();await page.locator('#payment-reference').fill('');
 for(const tile of ['[data-payment-mode="UPI"]','[data-payment-mode="CARD"][data-card-type="DEBIT"]','[data-payment-mode="CARD"][data-card-type="CREDIT"]','[data-payment-mode="BANK_TRANSFER"]']){
  await page.locator('.billing-payment-tile'+tile).click();assert.equal(await page.locator('#payment-reference').evaluate(n=>n.required),false);assert(await page.locator('#complete-sale').isEnabled(),'Blank reference allows checkout for '+tile);
 }
 await page.locator('.billing-payment-tile[data-payment-mode="UPI"]').click();
 await page.locator('#complete-sale').click();await page.locator('#payment-confirmation-modal.show').waitFor();assert.equal(posts.length,0);await page.locator('#confirm-payment').click();await page.waitForURL('**/Sales/Invoice/42');
 assert.equal(posts.length,1);assert.equal(posts[0].payments[0].referenceNumber,null);assert.equal(posts[0].payments.length,1);assert.equal(posts[0].payments[0].collectionConfirmed,true);assert.equal(posts[0].customerId,11);assert.equal(posts[0].items[1].manualItem.taxRuleId,7);assert.equal(posts[0].items[1].discountPercent,5);assert.equal(posts[0].notes,'Held mixed bill');
 gstContext={mapped:true,registrationType:'REGULAR'};
 await page.goto('http://stockflow.test/Sales/Create');await page.locator('#quick-add-item').click();await page.locator('#manual-name').fill('GST goods');await page.locator('#manual-price').fill('50');
 await page.waitForFunction(()=>document.querySelector('#manual-rule').required);assert(await page.locator('#manual-submit').isDisabled());
 await page.locator('#manual-rule').fill('Reviewed small goods · 610910 · 5% (#7)');await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);await page.locator('#manual-cancel').click();
 console.log('PASS optional GST: ordinary billing works with no reviewed rules; GST invoicing retains required classification.');
 canOverrideTax=false;canApproveDiscount=false;gstContext={mapped:false};await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');await page.locator('#line-tax-0').waitFor();assert(await page.locator('#line-tax-0').evaluate(n=>n.readOnly),'Ordinary cashier without Tax Override permission cannot edit tax');
 await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);assert(await page.locator('#line-warning-0').isHidden(),'Normal sale has no adjustment warning');
 await page.locator('[data-discount-editor]').fill('7');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 assert((await page.locator('#line-warning-0').getAttribute('title')).includes('Discount reason required'));assert(!(await page.locator('#line-warning-0').getAttribute('title')).includes('approval'),'Below-threshold reason is distinct from approval');
 await fillReason(0,'Customer promotion');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);assert(await page.locator('#line-warning-0').isHidden());
 await page.locator('[data-discount-editor]').fill('10');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);assert(await page.locator('#request-discount-approval').isHidden(),'Exact threshold does not request approval');
 await page.locator('[data-discount-editor]').fill('15');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>!document.querySelector('#request-discount-approval').hidden);assert((await page.locator('#discount-policy-status').textContent()).includes('Manager approval required'));
 await page.locator('#request-discount-approval').click();await page.locator('#billing-approval-dialog').getByRole('button',{name:'Request Approval',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#discount-policy-status').textContent.includes('Approval pending'));approvalStatus='APPROVED';await page.locator('#request-discount-approval').click();await page.waitForFunction(()=>document.querySelector('#discount-policy-status').textContent.includes('Discount approved'));assert(await page.locator('#line-warning-0').isHidden());
 approvalStatus='STALE';await page.getByRole('button',{name:/Increase quantity for Cable/}).click();await page.waitForFunction(()=>document.querySelector('#discount-policy-status').textContent.includes('previous approval changed'));assert(await page.locator('#line-warning-0').isVisible());
 assert(!await page.locator('#billing-adjustment-fields').isVisible(),'Generic bill-wide reason panel is not shown');
 await page.locator('[data-discount-editor]').fill('0');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);await page.waitForTimeout(1100);
 // Fast Billing is an explicit saved-policy opt-in; server flags control routine UI.
 fastPolicy=true;await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');
 for(const rate of ['0','5','10']){await page.locator('[data-discount-editor]').fill(rate);await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);assert(await page.locator('#line-warning-0').isHidden(),'Routine '+rate+'% has no action indicator');assert(await page.locator('#line-reason-panel-0').isHidden());assert(await page.locator('#request-discount-approval').isHidden());}
 await page.screenshot({path:path.join(output,'fast-policy-routine.png'),fullPage:true});
 await page.locator('[data-discount-editor]').fill('5');await page.locator('#sale-discount').fill('1');await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);assert(await page.locator('#invoice-discount-context').isHidden(),'Routine invoice discount has no reason input');
 await page.locator('#sale-discount').fill('0');await page.locator('[data-discount-editor]').fill('15');await page.waitForFunction(()=>!document.querySelector('#request-discount-approval').hidden);
 await page.locator('#request-discount-approval').click();assert(await page.locator('#billing-approval-dialog').isVisible());
 for(const [width,height]of [[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){await page.setViewportSize({width,height});const rect=await page.locator('#billing-approval-dialog').boundingBox();assert(rect.x>=0&&rect.x+rect.width<=width+1&&rect.y>=0&&rect.y+rect.height<=height+1,'Approval dialog fits '+width);assert(await page.locator('#billing-approval-dialog').evaluate(n=>n.scrollWidth<=n.clientWidth+1));}
 await page.locator('#billing-approval-dialog').getByRole('button',{name:'Cancel',exact:true}).click();assert.equal(await page.locator('[data-discount-editor]').inputValue(),'15');
 await page.setViewportSize({width:1366,height:768});console.log('PASS opted-in routine discounts and invoice discounts have no warning/reason/approval; exceptional dialog fits eight requested viewports.');
 // Collect different reasons together, then carry completed reasons forward.
 fastPolicy=false;canOverrideTax=true;approvalStatus='REQUIRED';await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);await page.locator('[data-discount-editor]').fill('7');await editTax(0,'3');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 await page.locator('#line-warning-0').click();assert.equal(await page.locator('#line-reason-panel-0 h2').textContent(),'Adjustment Reasons','combined reasons have an honest shared heading');
 await page.locator('#line-reason-0').fill('Seasonal customer promotion');await page.locator('#line-reason-panel-0').getByRole('button',{name:'Done',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 await page.locator('#line-warning-0').click();assert.equal(await page.locator('#line-reason-panel-0 h2').textContent(),'Tax Change Reason');assert(await page.locator('#line-reason-0').isHidden(),'Saved discount reason is not requested again for tax');
 const taxContext=await page.locator('#line-reason-panel-0 [data-adjustment-summary]').textContent();assert(taxContext.includes(' → 3%')&&!taxContext.includes('undefined'),taxContext);assert(!/Discount:|Approval Status|Approving User/.test(taxContext),'Tax summary excludes unrelated approval/discount context');
 await page.screenshot({path:path.join(output,'tax-reason-without-repeated-discount.png'),fullPage:true});
 await page.locator('#line-tax-reason-0').fill('Reviewed ordinary tax correction');await page.locator('#line-reason-panel-0').getByRole('button',{name:'Done',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').hidden);
 assert.equal(quoteRequests.at(-1).items[0].adjustmentReason,'Seasonal customer promotion');assert.equal(quoteRequests.at(-1).items[0].taxAdjustmentReason,'Reviewed ordinary tax correction','Separate saved reasons still reach the server');
 await page.locator('[data-discount-editor]').fill('15');await page.locator('#sale-scan').focus();await page.waitForFunction(()=>!document.querySelector('#request-discount-approval').hidden);await page.locator('#request-discount-approval').click();
 assert.equal(await page.locator('#billing-approval-dialog input').count(),0,'Saved line reason is carried into approval without another entry');assert((await page.locator('#billing-approval-dialog').textContent()).includes('Seasonal customer promotion'));await page.locator('#billing-approval-dialog').getByRole('button',{name:'Cancel',exact:true}).click();
 console.log('PASS combined adjustment reasons, focused tax-only follow-up and reuse of saved reasons for approval.');
 canOverrideTax=true;gstContext={mapped:true,registrationType:'REGULAR'};failQuote=true;quoteFailureMessage='Review effective GST classification for the selected item';await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');
 await page.waitForFunction(()=>document.querySelector('#billing-calculation-status').textContent.includes('GST classification'));assert((await page.locator('#line-warning-0').getAttribute('title')).includes('Tax configuration needs review'));assert(await page.locator('#line-tax-0').evaluate(n=>n.readOnly));assert(await page.locator('#complete-sale').isDisabled());
 await page.locator('#line-warning-0').click();assert((await page.locator('#line-reason-panel-0 h2').textContent()).includes('Tax Configuration Needs Review'));assert(await page.locator('#line-reason-0').isHidden(),'Tax setup error does not ask the cashier for a discount reason');
 console.log('PASS contextual reason/approval indicators, no warning for zero or justified below-threshold discount, exact threshold, pending/approved/stale status and per-line reason.');
 console.log('PASS stable totals for empty/invalid price, quantity, item discount and tax; delayed/error quotes; inline invoice discount editing; optional cash change; cashier tax restriction.');
 failQuote=false;gstContext={mapped:false};await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});
 await page.goto('http://stockflow.test/Sales/Create?quotationId=c75ae01a-1b09-4469-9af5-c3e09f261401');await page.waitForFunction(()=>document.querySelector('#sale-quotation-context')?.textContent.includes('QTN-0001'));await page.waitForFunction(()=>!document.querySelector('#complete-sale').disabled);
 assert.equal(quoteRequests.at(-1).sourceQuotationRevision,2);assert.equal(quoteRequests.at(-1).sourceQuotationVersion,4);
 await page.waitForTimeout(1000);await Promise.all([page.waitForResponse(r=>r.url().endsWith('/release')),page.evaluate(()=>window.dispatchEvent(new PageTransitionEvent('pagehide')))]);await page.reload();await page.waitForFunction(()=>document.querySelector('#sale-quotation-context')?.textContent.includes('QTN-0001'));assert.equal(await page.locator('#sale-cart tr').count(),1);
 try{await page.waitForFunction(()=>!document.querySelector('#complete-sale').disabled);}catch(e){console.error(await page.evaluate(()=>({draft:document.querySelector('#draft-status').textContent,message:document.querySelector('#sale-message').textContent,calculation:document.querySelector('#billing-calculation-status').textContent})));throw e;}
 loseSaleResponse=true;const beforeAttempts=posts.length,beforeCommitted=committedRequests.size;await page.evaluate(()=>{const complete=document.querySelector('#complete-sale');complete.click();complete.click();});
 await page.waitForFunction(()=>!document.querySelector('#pending-sale').hidden&&!document.querySelector('#retry-sale').disabled);assert.equal(posts.length,beforeAttempts+1,'Double click sends one finalization');const uncertain=posts.at(-1);
 assert(await page.locator('#sale-editor').evaluate(n=>n.disabled),'Unknown result locks the saved snapshot');await page.reload();await page.waitForSelector('#pending-sale:not([hidden])');await page.locator('#retry-sale').click();try{await page.waitForURL('**/Sales/Invoice/42');}catch(error){console.error(await page.evaluate(()=>({url:location.href,message:document.querySelector('#sale-message')?.textContent,pending:document.querySelector('#pending-sale')?.textContent,status:document.querySelector('#draft-status')?.textContent})));console.error('Attempts',posts.length,'Committed',committedRequests.size);throw error;}
 assert.equal(committedRequests.size,beforeCommitted+1,'Lost response and refreshed retry resolve one committed request');assert.equal(posts.length,beforeAttempts+1,'Refreshed recovery does not submit another sale');assert.deepEqual(recoveredRequests.at(-1),uncertain);assert.equal(posts.at(-1).sourceQuotationId,'c75ae01a-1b09-4469-9af5-c3e09f261401');assert.equal(posts.at(-1).sourceQuotationRevision,2);console.log('PASS quotation lineage, double click, unknown outcome lock, refresh and same-snapshot lost-response verification without posting again.');
 assert.deepEqual(errors,[]);console.log('PASS browser: customer requirement/quick-add, scan-repeat, MRP, percentage, manual add/edit, hold/resume, single verified payment, 11 viewports; no JavaScript errors.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
