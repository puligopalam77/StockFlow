// Current Razor/assets with controlled posting responses. Actual transactions are checked by --expense-modern-checks.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/expense-modern-fixtures');
const screens=path.join(root,'.artifacts/expense-create-compact');fs.mkdirSync(screens,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(8000);
 const source=JSON.parse(fs.readFileSync(path.join(fixtures,'expense.json'))),options=JSON.parse(fs.readFileSync(path.join(fixtures,'options.json'))),records=new Map(),writes=[],errors=[];
 let fail=false,lost=false,canPay=true;page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({contentType:'application/json',status,body:JSON.stringify(body)});
  if(p==='/Expenses/Create'||p==='/Expenses/Details'){
   const name=p.endsWith('Create')?'Create':'Details';let html=fs.readFileSync(path.join(fixtures,name+'.html'),'utf8');
   if(name==='Details')html=html.replace('data-id="'+source.expense_id+'"','data-id="'+url.searchParams.get('id')+'"');
   return route.fulfill({contentType:'text/html',body:html});
  }
  if(p==='/api/expenses/options')return json({...options,permissions:{...options.permissions,PAY:canPay}});
  if(/^\/api\/expenses\/[^/]+\/(save|record)$/.test(p)){
   assert(req.headers()['x-csrf-token']);const input=req.postDataJSON(),id=p.split('/')[3],action=p.split('/')[4];writes.push({action,input,id});
   if(lost){lost=false;return route.abort('failed');}if(fail)return json({message:'Could not record this expense. Please try again.'},400);
   const details=action==='record'?input.body.expense:input.body,payment=action==='record'?input.body.payment:null;
   const row={...structuredClone(source),expense_id:id,category_id:details.categoryId,payee_id:details.payeeId,incurred_date:details.incurredDate,description:details.description,amount:details.amount,status:action==='record'?'POSTED':'DRAFT',paid:payment?.amount||0,version:1,payments:payment?[{payment_id:'payment-id',kind:'PAYMENT',paid_date:payment.paidDate,payment_mode:payment.paymentMode,amount:payment.amount,reference:payment.reference,actor_id:1}]:[],events:[],attachments:[]};
   records.set(id,row);return json({version:1});
  }
  if(/^\/api\/expenses\/[^/]+$/.test(p))return records.has(p.split('/')[3])?json(records.get(p.split('/')[3])):json({message:'Not found'},404);
  if(p.startsWith('/api/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 const money=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(n);
 const field=id=>page.locator('#expense-'+id);
 async function go(){await page.evaluate(()=>StockFlowUnsaved.allowNavigation()).catch(()=>{});await page.goto('https://stockflow.test/Expenses/Create');await page.waitForFunction(()=>document.querySelector('#expense-state').textContent!=='Loading...');}
 async function enter(){await selectValue(field('category'),options.categories[0].id);await selectValue(field('payee'),options.payees[0].id);await field('amount').fill('1800');await field('description').fill('Shop rent payment');}
 async function preview(amount,paid,balance,status){assert.equal(await field('summary-amount').textContent(),money(amount));assert.equal(await field('summary-paid').textContent(),money(paid));assert.equal(await field('summary-outstanding').textContent(),balance===null?'—':money(balance));assert.equal(await field('summary-payment').textContent(),status);}
 await go();await preview(0,0,0,'Not set');
 for(const status of ['PAID','PARTIAL','UNPAID']){
  await selectValue(field('initial-state'),status);if(status==='PARTIAL')await field('initial-amount').fill('600');
  await field('amount').fill('0');await preview(0,0,0,'Not set');await field('amount').fill('');await preview(0,0,0,'Not set');
 }
 await enter();assert.equal(await field('summary-title').textContent(),'Expense Preview');assert.equal(await page.locator('.expense-sidebar>section:first-child>.expense-summary-values>div').count(),4);
 assert.deepEqual(await field('initial-state').locator('option').allTextContents(),['Unpaid','Partially Paid','Paid']);await preview(1800,0,1800,'Unpaid');assert(await field('initial-fields').isHidden());
 assert.match(await field('receipt-section').textContent(),/PDF, JPG or PNG/);assert(await field('receipt-choose').isVisible());assert.equal(await page.locator('.expense-notice').count(),0);
 assert.equal(await field('receipt-choose').locator('svg').count(),1,'Upload icon survives label updates');assert.equal(await field('receipt-choose').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(32, 35, 40)');assert.equal(await field('receipt-choose').evaluate(e=>getComputedStyle(e).color),'rgb(255, 255, 255)');await field('receipt-choose').hover();await page.waitForFunction(()=>getComputedStyle(document.getElementById('expense-receipt-choose')).backgroundColor==='rgb(255, 0, 127)');await page.mouse.move(0,0);
 assert.equal(await field('save').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(32, 35, 40)');assert.equal(await field('save').evaluate(e=>getComputedStyle(e).color),'rgb(255, 255, 255)');assert.equal(await field('save').locator('svg').count(),1);await field('save').hover();await page.waitForFunction(()=>getComputedStyle(document.getElementById('expense-save')).backgroundColor==='rgb(255, 0, 127)');await page.mouse.move(0,0);assert.equal(await page.locator('.expense-back-link').getAttribute('href'),'/Expenses');
 await selectValue(field('initial-state'),'PAID');assert(await field('initial-method-search').isVisible());assert(await field('initial-amount-wrap').isHidden());await preview(1800,1800,0,'Paid');
 assert(await field('initial-notes').isHidden());assert.equal((await page.locator('.expense-confirm').first().textContent()).trim(),'Payment has been made');
 assert.deepEqual(await field('initial-method').locator('option').evaluateAll(es=>es.map(e=>e.value)),['CASH','UPI','DEBIT_CARD','CREDIT_CARD','BANK_TRANSFER']);
 await selectValue(field('initial-state'),'PARTIAL');await field('initial-amount').fill('600');await preview(1800,600,1200,'Partially Paid');assert.equal(await field('initial-balance').textContent(),money(1200));
 await field('amount').fill('2000');await preview(2000,600,1400,'Partially Paid');await field('amount').fill('0.30');await field('initial-amount').fill('0.10');await preview(0.3,0.1,0.2,'Partially Paid');
 await field('amount').fill('1800');await field('initial-amount').fill('1801');await preview(1800,1801,null,'Partially Paid');let count=writes.length;await field('record').click();assert.equal(writes.length,count,'Overpayment not submitted');
 await field('initial-amount').fill('1800');await field('initial-confirm').check();await field('record').click();assert.equal(writes.length,count,'Partial equal to expense not submitted');
 await field('initial-amount').fill('600');await field('initial-confirm').uncheck();await field('record').click();assert.equal(writes.length,count,'Paid recording still needs actual-payment confirmation');
 await field('initial-confirm').check();await selectValue(field('initial-method'),'CREDIT_CARD');await field('initial-reference').fill('Card reference');await page.locator('#expense-initial-more summary').click();await field('initial-notes').fill('Keep these notes');
 fail=true;await field('record').click();await field('message').getByText(/Could not record/).waitFor();assert.equal(await field('initial-method').inputValue(),'CREDIT_CARD');assert.equal(await field('initial-amount').inputValue(),'600');await preview(1800,600,1200,'Partially Paid');fail=false;
 // Save Draft stores expense details only, with the payment plan kept in session storage.
 await field('initial-confirm').uncheck();await field('save').click();await page.waitForURL(/Expenses\/Details\?id=/);await page.waitForFunction(()=>document.querySelector('#expense-state').textContent.includes('Draft'));
 const draft=writes.at(-1);assert.equal(draft.action,'save');assert(!Object.hasOwn(draft.input.body,'payment'));assert.equal(records.get(draft.id).paid,0);assert.equal(records.get(draft.id).payments.length,0);
 assert.equal(await field('initial-amount').inputValue(),'600');assert.equal(await field('initial-method').inputValue(),'CREDIT_CARD');assert.equal(await field('initial-reference').inputValue(),'Card reference');assert.equal(await field('initial-notes').inputValue(),'Keep these notes');assert(!await field('initial-confirm').isChecked());assert.equal(await field('summary-title').textContent(),'Expense Summary');assert(await field('attachment-form').isVisible());
 // The same existing method tokens are submitted for actual recording.
 for(const method of ['CASH','UPI','BANK_TRANSFER','DEBIT_CARD','CREDIT_CARD']){
  await go();await enter();await selectValue(field('initial-state'),'PAID');await selectValue(field('initial-method'),method);await field('initial-confirm').check();await field('record').click();await page.waitForURL(/Expenses\/Details\?id=/);
  const write=writes.at(-1);assert.equal(write.action,'record');assert.equal(write.input.body.payment.paymentMode,method);assert.equal(write.input.body.payment.amount,1800);assert.equal(write.input.body.payment.confirmed,true);
 }
 await go();await enter();await selectValue(field('initial-state'),'PARTIAL');await field('initial-amount').fill('600');await field('initial-confirm').check();await field('record').click();await page.waitForURL(/Expenses\/Details\?id=/);assert.equal(writes.at(-1).input.body.payment.amount,600);
 await go();await enter();await field('record').click();await page.waitForURL(/Expenses\/Details\?id=/);assert.equal(writes.at(-1).input.body.payment,null);
 // Exact retry survives a lost request and reload, including the paid preview.
 await go();await enter();await selectValue(field('initial-state'),'PARTIAL');await field('initial-amount').fill('600');await field('initial-confirm').check();lost=true;await field('record').click();await field('retry').waitFor({state:'visible'});const retry=writes.at(-1);await page.reload();await field('retry').waitFor({state:'visible'});await preview(1800,600,1200,'Partially Paid');await field('retry').click();await page.waitForURL(/Expenses\/Details\?id=/);assert.deepEqual(writes.at(-1).input,retry.input);
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640],[853,512]]){
  await page.setViewportSize({width,height});await go();await enter();await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No page overflow '+width);
  const columns=await page.locator('#expense-fields>.expense-fields-grid').evaluate(e=>getComputedStyle(e).gridTemplateColumns.split(' ').length);
  const formWidth=await page.locator('.expense-main').evaluate(e=>e.getBoundingClientRect().width);assert.equal(columns,formWidth>=1120?4:formWidth>=840?3:formWidth>=560?2:1,'Columns follow available form width '+width);
  const box=async id=>field(id).evaluate(e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,width:r.width};});
  const sameRow=async ids=>{const boxes=await Promise.all(ids.map(box));assert(boxes.every(b=>Math.abs(b.y-boxes[0].y)<1),'Aligned row '+ids.join('/')+' at '+width+': '+JSON.stringify(boxes));};
  if(columns===4){await sameRow(['category-search','payee-search','incurred','amount']);const description=await box('description'),category=await box('category-search'),amount=await box('amount');assert(description.y>amount.y&&Math.abs(description.x-category.x)<1&&Math.abs(description.right-amount.right)<1,'Description spans the full row');}
  else if(columns===3){await sameRow(['category-search','payee-search','incurred']);await sameRow(['amount','description']);const description=await box('description'),payee=await box('payee-search'),date=await box('incurred');assert(Math.abs(description.x-payee.x)<1&&Math.abs(description.right-date.right)<1,'Description spans two columns');}
  else if(columns===2){await sameRow(['category-search','payee-search']);await sameRow(['incurred','amount']);}
  else{const category=await box('category-search'),payee=await box('payee-search');assert(payee.y>category.y&&Math.abs(payee.x-category.x)<1,'Single field column');}
  for(const id of ['category-search','payee-search','incurred','amount'])assert((await box(id)).width>=220,'Readable input width '+id+' at '+width);
  assert.equal(await page.locator('.expense-create #expense-form').evaluate(e=>getComputedStyle(e).paddingTop),'16px');assert(await field('receipt-section').evaluate(e=>e.getBoundingClientRect().height<160),'Compact receipt '+width);
  if(width>=575){const [info,button]=await page.locator('.expense-receipt-header>div,#expense-receipt-choose').evaluateAll(es=>es.map(e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom};}));assert(button.left>info.right&&button.top<info.bottom&&button.bottom>info.top,'Horizontal receipt row '+width);}
  assert(await page.evaluate(()=>{const header=document.querySelector('.sf-topbar').getBoundingClientRect(),crumb=document.querySelector('.sf-breadcrumb').getBoundingClientRect(),title=document.querySelector('#expenses h1').getBoundingClientRect();return header.bottom<=crumb.top&&header.bottom<=title.top;}),'Header clears breadcrumbs and title '+width);
  await page.screenshot({path:path.join(screens,'unpaid-'+width+'x'+height+'.png'),fullPage:true});
  await selectValue(field('initial-state'),'PAID');await preview(1800,1800,0,'Paid');
  if(columns===4)await sameRow(['initial-state-search','initial-method-search','initial-date','initial-reference']);
  else if(columns===3)await sameRow(['initial-state-search','initial-method-search','initial-date']);
  await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));await page.screenshot({path:path.join(screens,'paid-'+width+'x'+height+'.png'),fullPage:true});
  await selectValue(field('initial-state'),'PARTIAL');await field('initial-amount').fill('600');await preview(1800,600,1200,'Partially Paid');
  if(columns===4)await sameRow(['initial-state-search','initial-amount','initial-method-search','initial-date']);
  else if(columns===3){await sameRow(['initial-state-search','initial-amount','initial-method-search']);await sameRow(['initial-date','initial-reference']);}
  await field('initial-amount').focus();await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.id),'expense-initial-method-search','Keyboard follows visible payment order');
  for(const id of ['save','record']){await field(id).scrollIntoViewIfNeeded();assert(await field(id).evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),'Action reachable '+width);}
  await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));await page.screenshot({path:path.join(screens,'partial-'+width+'x'+height+'.png'),fullPage:true});
 }
 canPay=false;await go();await enter();assert(await field('initial-payment').isHidden());await preview(1800,0,1800,'Unpaid');assert.deepEqual(errors,[]);
 console.log('PASS compact Create: four-row live preview, unpaid/paid/partial and paise balance, confirmation/overpayment guards, exact method mappings, draft/payment separation and plan restore, failure retention/exact retry, receipts and no-GST message, permissions and ten desktop/mobile viewports. Controlled HTTP.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
