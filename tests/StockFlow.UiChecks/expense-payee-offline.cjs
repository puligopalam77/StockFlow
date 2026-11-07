// Actual Razor and assets with controlled HTTP; database/API coverage is in ExpenseModernChecks.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const fixtures=path.join(root,'tests/bin/expense-modern-fixtures'),screens=path.join(root,'.artifacts/expense-payee');
fs.mkdirSync(screens,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(8000);
  const errors=[],writes=[],options=JSON.parse(fs.readFileSync(path.join(fixtures,'options.json')));
  const expense=JSON.parse(fs.readFileSync(path.join(fixtures,'expense.json')));expense.status='DRAFT';
  let failure=false,serverDuplicate=false,hold=false,releaseSave,editable=true;
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),url=new URL(req.url()),p=url.pathname;
   const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Expenses/Create'||p==='/Expenses/Details')return route.fulfill({contentType:'text/html',path:path.join(fixtures,(p.endsWith('Create')?'Create':'Details')+'.html')});
   if(p==='/api/expenses/options')return json({...options,permissions:Object.fromEntries(Object.entries(options.permissions).map(([k,v])=>[k,editable&&v]))});
   if(p.startsWith('/api/expenses/masters/')){
    assert(req.headers()['x-csrf-token']);const input=req.postDataJSON();writes.push({kind:p.split('/').pop(),...input});
    if(hold)await new Promise(resolve=>releaseSave=resolve);
    if(failure)return json({message:'Could not save the payee. Please try again.'},500);
    if(serverDuplicate)return json({id:options.payees[0].id,existing:true});
    options[p.endsWith('PAYEE')?'payees':'categories'].push({id:input.id,name:input.name});
    return json({id:input.id,existing:false});
   }
   if(p==='/api/expenses/'+expense.expense_id)return json(expense);
   if(p.startsWith('/api/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  const dialog=page.locator('#expense-master-dialog'),name=page.locator('#expense-master-name'),mobile=page.locator('#expense-master-contact');
  const save=page.locator('#expense-master-save'),cancel=page.locator('#expense-master-close'),add=page.locator('[data-master="PAYEE"]');
  const snapshot=()=>page.evaluate(()=>[...document.querySelectorAll('#expense-form input,#expense-form select,#expense-form textarea,#expense-initial-payment input,#expense-initial-payment select')]
   .filter(e=>!e.id.startsWith('expense-payee')).map(e=>[e.id,['checkbox','radio'].includes(e.type)?e.checked:e.value]));
  async function go(edit=false){await page.evaluate(()=>StockFlowUnsaved.allowNavigation()).catch(()=>{});await page.goto('https://stockflow.test/Expenses/'+(edit?'Details?id='+expense.expense_id:'Create'));await page.waitForFunction(()=>document.querySelector('#expense-state').textContent!=='Loading...');}
  async function open(){await add.click();await dialog.waitFor();assert.equal(await name.evaluate(e=>e===document.activeElement),true,'Autofocus name');}
  await go();await selectValue(page.locator('#expense-category'),options.categories[0].id);await selectValue(page.locator('#expense-payee'),options.payees[0].id);
  await page.locator('#expense-amount').fill('2500');await page.locator('#expense-description').fill('Keep this expense and its payment instructions');
  await selectValue(page.locator('#expense-initial-state'),'PARTIAL');await page.locator('#expense-initial-amount').fill('500');
  await selectValue(page.locator('#expense-initial-method'),'UPI');await page.locator('#expense-initial-reference').fill('Payment reference');
  await page.locator('#expense-initial-more summary').click();await page.locator('#expense-initial-notes').fill('Payment notes');await page.locator('#expense-initial-confirm').check();const before=await snapshot();
  await open();assert.equal(await page.locator('#expense-master-title').textContent(),'Add Payee');
  assert.equal(await dialog.locator('input:visible').count(),2);assert.equal(await name.getAttribute('placeholder'),'e.g., Shop Owner, Electricity Board');
  assert.equal(await mobile.getAttribute('placeholder'),'Enter mobile number');assert.equal(await mobile.getAttribute('inputmode'),'numeric');
  await save.click();await page.locator('#expense-master-name-validation-error').getByText('Enter a payee name.',{exact:true}).waitFor();
  await name.fill('   ');await save.click();assert.equal(writes.length,0);assert.equal(await name.getAttribute('aria-invalid'),'true');
  await name.fill('Electricity Board');for(const invalid of ['12345','1234567890','98765abcde']){
   await mobile.fill(invalid);await save.click();assert.equal(writes.length,0);assert.equal(await mobile.getAttribute('aria-invalid'),'true');
   assert.match(await page.locator('#expense-master-contact-validation-error').textContent(),/valid 10-digit mobile/);
  }
  await mobile.fill('9876543210');await name.fill('  Electricity Board  ');failure=true;await save.click();
  await page.locator('#expense-master-error').getByText(/Could not save/).waitFor();assert(await dialog.isVisible());
  assert.equal(await name.inputValue(),'Electricity Board');assert.equal(await mobile.inputValue(),'9876543210');assert.deepEqual(await snapshot(),before);
  const retryId=writes.at(-1).id;failure=false;hold=true;await save.click();await page.waitForFunction(()=>document.querySelector('#expense-master-form').getAttribute('aria-busy')==='true');
  assert.equal(await save.textContent(),'Saving…');assert(await save.isDisabled());assert(await cancel.isDisabled());assert(await mobile.isDisabled());
  await page.locator('#expense-master-form').evaluate(form=>{form.requestSubmit();form.requestSubmit();});
  assert.equal(writes.length,2,'Repeated submit ignored while busy');assert.equal(writes.at(-1).id,retryId,'Retry keeps creation identity');
  hold=false;releaseSave();await dialog.waitFor({state:'hidden'});assert.equal(await page.locator('#expense-payee').inputValue(),retryId);
  assert.equal(await page.locator('#expense-payee-search').inputValue(),'Electricity Board','Shared dropdown shows selected payee');
  assert.deepEqual(await snapshot(),before);await page.locator('.sf-toast-success').getByText('Payee added successfully',{exact:true}).waitFor();
  assert.equal(await add.evaluate(e=>e===document.activeElement),true,'Focus returns to Add Payee');
  await open();await name.fill(' electricity BOARD ');await save.click();await page.locator('#expense-master-error').getByText(/already exists/).waitFor();assert.equal(writes.length,2,'Local duplicate makes no request');
  await cancel.click();await dialog.waitFor({state:'hidden'});assert.deepEqual(await snapshot(),before);
  await open();await name.fill('Concurrent existing name');serverDuplicate=true;await save.click();await page.locator('#expense-master-error').getByText(/already exists/).waitFor();assert(await dialog.isVisible());serverDuplicate=false;
  await page.keyboard.press('Escape');await dialog.waitFor({state:'hidden'});assert.equal(await add.evaluate(e=>e===document.activeElement),true);
  await open();await name.fill('Cancelled payee');await page.locator('#expense-master-x').click();await dialog.waitFor({state:'hidden'});assert.equal(writes.length,3);
  await open();await name.fill('Name only payee');await name.press('Enter');await dialog.waitFor({state:'hidden'});assert.equal(writes.at(-1).mobileNumber,null);assert.equal(await page.locator('#expense-payee').inputValue(),writes.at(-1).id);assert.deepEqual(await snapshot(),before);
  // Desktop and mobile bounds, actual action hit targets, and trapped keyboard focus.
  await page.evaluate(()=>StockFlowNotify.dismiss('expense-payee-added'));
  for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640],[853,512]]){
   await page.setViewportSize({width,height});await open();
   await name.fill('Shop Owner');await mobile.fill('9876543210');
   assert(await dialog.evaluate(e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1&&e.scrollHeight<=e.clientHeight+1&&e.scrollWidth<=e.clientWidth+1;}),'Dialog fits without scrolling '+width);
   assert(await page.locator('#expense-master-x').evaluate(e=>{const r=e.getBoundingClientRect(),h=e.closest('header').getBoundingClientRect();return Math.abs(h.right-r.right-14)<=1&&Math.abs(r.top-h.top-14)<=1;}),'Close X anchored top-right '+width);
   for(const control of [save,cancel,page.locator('#expense-master-x')])assert(await control.evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),'Action reachable '+width);
   await name.focus();await page.keyboard.press('Tab');assert.equal(await mobile.evaluate(e=>e===document.activeElement),true);await page.keyboard.press('Tab');assert.equal(await cancel.evaluate(e=>e===document.activeElement),true);
   await page.keyboard.press('Tab');assert.equal(await save.evaluate(e=>e===document.activeElement),true);await page.keyboard.press('Tab');
   // Native dialogs may pass focus through browser chrome before wrapping.
   assert(await dialog.evaluate(e=>e.contains(document.activeElement)||document.activeElement===document.body),'Tab cannot reach the underlying form');
   if(await page.evaluate(()=>document.activeElement===document.body))await page.keyboard.press('Tab');
   assert(await dialog.evaluate(e=>e.contains(document.activeElement)),'Tab returns to modal '+width);
   await page.screenshot({path:path.join(screens,'payee-'+width+'x'+height+'.png'),fullPage:true});await cancel.click();
  }
  await go(true);await page.locator('#expense-description').fill('Existing draft edit stays intact');const editBefore=await snapshot();await open();await name.fill('Draft payee');await save.click();await dialog.waitFor({state:'hidden'});assert.deepEqual(await snapshot(),editBefore);assert.equal(await page.locator('#expense-payee').inputValue(),writes.at(-1).id);
  await page.locator('[data-master="CATEGORY"]').click();assert(await page.locator('#expense-master-contact-field').isHidden());assert(await mobile.isDisabled());await name.fill('Category still works');await save.click();await dialog.waitFor({state:'hidden'});assert.equal(writes.at(-1).kind,'CATEGORY');assert(!Object.hasOwn(writes.at(-1),'mobileNumber'));
  editable=false;await go(true);assert(await add.isHidden());assert.deepEqual(errors,[]);
  console.log('PASS Add Payee: actual Razor/assets; required name, optional Indian mobile, trim, local/server duplicates, failure retention, stable retry identity, repeated-submit guard, toast, exact dropdown auto-selection, full expense/payment preservation, Cancel/X/Escape, keyboard focus, draft editing, category compatibility, permissions and ten viewports at DPR 1.5.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
