const {fillAddress}=require('./address-test-helpers.cjs');
// Real rendered Razor + local scripts/styles; mocked HTTP isolates browser behavior.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixture=path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),output=path.join(root,'tests/bin/payment-method-ui-fixtures');fs.mkdirSync(output,{recursive:true});
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
 async function editTax(index,value){await page.locator('#line-edit-'+index).click();await page.locator('.billing-editor-advanced summary').click();await page.locator('[data-inline-tax-correction]').click();await page.locator('#line-tax-override-'+index).fill(value);await page.locator('#line-reason-panel-'+index).getByRole('button',{name:'Done',exact:true}).click();}
 await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 const choose=(mode,type='')=>page.locator(`.billing-payment-tile[data-payment-mode="${mode}"][data-card-type="${type}"]`);
 assert.equal(await page.locator('#payment-mode').isVisible(),false,'Backing select is not a duplicate visible field');
 assert.equal(await page.locator('#payment-mode').inputValue(),'CASH','Existing fresh-bill default retained');
 assert.equal(await page.locator('#calculate-change').isChecked(),false);
 assert(await page.locator('#sale-notes').isHidden(),'New-sale notes start collapsed');
 assert.equal(await page.locator('#exact-cash-help').count(),0);
 const sizes=[[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844],[320,640]];
 for(const [width,height]of sizes){await page.setViewportSize({width,height});for(const collapsed of [false,true]){
  if(width>=992){const expanded=await page.locator('#sidebar-toggle').getAttribute('aria-expanded');if((expanded==='true')===collapsed)await page.locator('#sidebar-toggle').click();}
  await page.waitForTimeout(280);await page.locator('.billing-payment-tiles').scrollIntoViewIfNeeded();
  const m=await page.locator('.billing-payment-tiles').evaluate(n=>{const r=n.getBoundingClientRect();return {width:r.width,overflow:document.documentElement.scrollWidth>innerWidth+1,tiles:[...n.children].map(t=>{const b=t.getBoundingClientRect(),label=t.querySelector('.billing-payment-label'),icon=t.querySelector('svg'),range=document.createRange();range.selectNodeContents(label);return {left:b.left-r.left,right:b.right-r.left,top:b.top,height:b.height,font:parseFloat(getComputedStyle(t).fontSize),icon:icon.getBoundingClientRect().width,ink:range.getBoundingClientRect().width,labelWidth:label.getBoundingClientRect().width,labelFits:label.getBoundingClientRect().right<=b.right-1&&label.getBoundingClientRect().left>=b.left+1};})};});
  assert.equal(m.tiles.length,5);assert(!m.overflow);for(const t of m.tiles){assert(t.left>=-1&&t.right<=m.width+1);assert(t.height>=44&&t.height<=48);assert(t.font>=12&&t.font<=13&&t.icon===16);assert(t.ink<=t.labelWidth+1&&t.labelFits,'Full label fits inside its button');}
  const columns=m.width>=350?5:m.width>=260?3:2;
  assert.equal(m.tiles.filter(t=>Math.abs(t.top-m.tiles[0].top)<1).length,columns,'Columns follow the payment container width');
  if(m.width>=350)assert(m.tiles.every(t=>Math.abs(t.top-m.tiles[0].top)<1),'Five methods fit one row in standard laptop payment cards');
  assert(await page.locator('.billing-payment-tile[aria-pressed=true] .billing-payment-check').isVisible());
  if(!collapsed&&(width===1366||width===390||width===1920))await page.screenshot({path:path.join(output,'payment-methods-'+width+'.png'),fullPage:true});
 }}
 await page.setViewportSize({width:1920,height:1080});
 const card=page.locator('#billing-payment-card');
 for(const contentWidth of [240,259,260,300,349,350,380,430,560]){
  await card.evaluate((n,w)=>n.style.maxWidth=(w+22)+'px',contentWidth);
  const layout=await page.locator('.billing-payment-tiles').evaluate(n=>({width:n.getBoundingClientRect().width,columns:getComputedStyle(n).gridTemplateColumns.split(' ').length,overflow:n.scrollWidth>n.clientWidth+1,labels:[...n.querySelectorAll('.billing-payment-label')].map(label=>{const range=document.createRange();range.selectNodeContents(label);return {ink:range.getBoundingClientRect().width,width:label.getBoundingClientRect().width};})}));
  assert.equal(layout.columns,layout.width>=350?5:layout.width>=260?3:2,'Container queries work without changing viewport');assert(!layout.overflow);assert(layout.labels.every(l=>l.ink<=l.width+1),'All labels readable at container '+layout.width);
 }
 await card.evaluate(n=>n.style.removeProperty('max-width'));
 await page.setViewportSize({width:1366,height:768});
 async function fresh(){await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});drafts.clear();await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);await page.locator('#customer-search').fill('Counter');await page.locator('#customer-results [role=option]').first().click();await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');await page.waitForFunction(()=>!document.querySelector('#complete-sale').disabled);}
 await fresh();
 const notesToggle=page.locator('#billing-notes > summary');
 await notesToggle.focus();await page.keyboard.press('Enter');assert(await page.locator('#sale-notes').isVisible());
 await page.locator('#billing-notes > summary').evaluate(n=>{if(!n.parentElement.open)n.click();});await page.locator('#sale-notes').fill('Preserved collapsed notes');await notesToggle.focus();await page.keyboard.press('Space');
 assert(await page.locator('#sale-notes').isHidden());assert.equal(await page.locator('#sale-notes').inputValue(),'Preserved collapsed notes');
 await page.locator('#calculate-change').focus();await page.keyboard.press('Space');assert(await page.locator('#cash-change-field').isVisible());
 await page.locator('#cash-received').fill('100');assert(await page.locator('#complete-sale').isDisabled());assert((await page.locator('#cash-change').textContent()).includes('Cash still required:'));
 await page.locator('#cash-received').fill('118');assert((await page.locator('#cash-change').textContent()).includes('0.00'));assert(await page.locator('#complete-sale').isEnabled());
 await page.locator('#cash-received').fill('200');assert((await page.locator('#cash-change').textContent()).includes('82.00'));
 await Promise.all([page.waitForEvent('load'),page.locator('#hold-bill').click()]);await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 await page.locator('#show-held').click();await page.getByRole('button',{name:/Resume Bill/}).first().click();
 await page.waitForFunction(()=>!document.querySelector('#complete-sale').disabled&&!document.querySelector('#saved-bills-drawer').open);
 assert(await page.locator('#sale-notes').isVisible(),'Resumed notes expand');assert.equal(await page.locator('#sale-notes').inputValue(),'Preserved collapsed notes');
 assert(await page.locator('#calculate-change').isChecked());assert.equal(await page.locator('#cash-received').inputValue(),'200');assert((await page.locator('#cash-change').textContent()).includes('82.00'));
 await page.locator('#complete-sale').click();await page.waitForURL('**/Sales/Invoice/42');assert.equal(posts.at(-1).payments[0].amount,118);assert.equal(posts.at(-1).cashTendered,200);assert.equal(posts.at(-1).notes,'Preserved collapsed notes');
 posts.length=0;
 await fresh();await page.locator('#calculate-change').check();await page.locator('#cash-received').fill('200');assert(await page.locator('#cash-change-field').isVisible());
 await choose('UPI').click();assert.equal(await page.locator('#cash-received').inputValue(),'');assert.equal(await page.locator('#calculate-change').isChecked(),false);assert(await page.locator('#cash-change-field').isHidden());
 await page.locator('#payment-reference').fill('UPI-only-reference');await choose('CARD','DEBIT').click();assert.equal(await page.locator('#payment-reference').inputValue(),'');
 await page.locator('#payment-reference').fill('Debit-only-reference');await choose('CARD','DEBIT').click();assert.equal(await page.locator('#payment-reference').inputValue(),'Debit-only-reference','Clicking selected method preserves its own input');await choose('CARD','CREDIT').click();assert.equal(await page.locator('#payment-reference').inputValue(),'');
 await page.locator('#payment-reference').fill('Credit-only-reference');await choose('BANK_TRANSFER').click();assert.equal(await page.locator('#payment-reference').inputValue(),'');await page.locator('#payment-reference').fill('Bank-only-reference');await choose('CASH').click();assert.equal(await page.locator('#payment-reference').inputValue(),'');assert(await page.locator('#payment-reference-field').isHidden());assert(await page.locator('#billing-simple-payment').isHidden());
 let completedSales=0;
 for(const [mode,type]of [['CASH',''],['UPI',''],['CARD','DEBIT'],['CARD','CREDIT'],['BANK_TRANSFER','']]){
  await fresh();await choose(mode,type).click();assert.equal(await page.locator('#payment-mode').inputValue(),mode);assert.equal(await page.locator('#payment-card-type').inputValue(),type);assert.equal(await page.locator('.billing-payment-tile[aria-pressed=true]').count(),1);
  const reference=mode==='CASH'?null:'REF-'+mode+'-'+type;if(reference)await page.locator('#payment-reference').fill(reference);
  await page.locator('#complete-sale').click();if(mode!=='CASH'){await page.locator('#payment-confirmation-modal.show').waitFor();assert.equal(posts.length,completedSales,'Electronic payments await confirmation');await page.locator('#confirm-payment').click();}
  await page.waitForURL('**/Sales/Invoice/42');const payment=posts.at(-1).payments[0];assert.equal(posts.at(-1).payments.length,1);assert.equal(payment.paymentMode,mode);assert.equal(payment.cardType||'',type);assert.equal(payment.referenceNumber,reference);assert.equal(payment.amount,118);assert.equal(posts.at(-1).cashTendered,null);completedSales++;console.log('PASS checkout payload '+mode+' '+type);
 }
 await fresh();await choose('CARD','CREDIT').click();await page.locator('#payment-reference').fill('RESTORED-CARD-REFERENCE');await page.locator('#billing-notes > summary').evaluate(n=>{if(!n.parentElement.open)n.click();});await page.locator('#sale-notes').fill('Keep existing payment selection');await page.waitForFunction(()=>document.querySelector('#draft-status').textContent.includes('saved'));await Promise.all([page.waitForEvent('load'),page.locator('#hold-bill').click()]);await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled&&document.querySelector('#billing-draft-reference').textContent==='New Draft');await page.locator('#show-held').click();await page.getByRole('button',{name:/Resume Bill/}).first().click();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled&&document.querySelector('#payment-mode').value==='CARD'&&!document.querySelector('#saved-bills-drawer').open);assert.equal(await page.locator('#payment-card-type').inputValue(),'CREDIT');assert.equal(await page.locator('#payment-reference').inputValue(),'RESTORED-CARD-REFERENCE');assert(await choose('CARD','CREDIT').getAttribute('aria-pressed')==='true');
 assert.deepEqual(errors,[]);console.log('PASS: 16 responsive/sidebar cases, hidden backing select, conditional fields, five confirmed sale payloads, stale-input clearing, same-method input retention and restored Credit Card draft. Mocked APIs; no shop database or live payments.');
 }finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
