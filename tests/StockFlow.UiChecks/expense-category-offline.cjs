// Current Razor and shared assets; HTTP responses are controlled here.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const fixtures=path.join(root,'tests/bin/expense-modern-fixtures'),screens=path.join(root,'.artifacts/expense-category');
fs.mkdirSync(screens,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(8000);
 const options=JSON.parse(fs.readFileSync(path.join(fixtures,'options.json'))),expense=JSON.parse(fs.readFileSync(path.join(fixtures,'expense.json')));expense.status='DRAFT';
 const writes=[],errors=[];let fail=false,duplicate=false,hold=false,release,editable=true;page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{
  const req=route.request(),p=new URL(req.url()).pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Expenses/Create'||p==='/Expenses/Details')return route.fulfill({contentType:'text/html',path:path.join(fixtures,(p.endsWith('Create')?'Create':'Details')+'.html')});
  if(p==='/api/expenses/options')return json({...options,permissions:Object.fromEntries(Object.entries(options.permissions).map(([k,v])=>[k,editable&&v]))});
  if(p==='/api/expenses/masters/CATEGORY'){
   assert(req.headers()['x-csrf-token']);const input=req.postDataJSON();writes.push(input);assert(!Object.hasOwn(input,'mobileNumber'));
   if(hold)await new Promise(resolve=>release=resolve);
   if(fail)return json({message:'Could not save the category. Please try again.'},500);
   if(duplicate)return json({id:options.categories[0].id,existing:true});
   options.categories.push({id:input.id,name:input.name});return json({id:input.id,existing:false});
  }
  if(p==='/api/expenses/'+expense.expense_id)return json(expense);
  if(p.startsWith('/api/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 const add=page.locator('[data-master="CATEGORY"]'),dialog=page.locator('#expense-master-dialog'),name=page.locator('#expense-master-name'),save=page.locator('#expense-master-save');
 const snapshot=()=>page.evaluate(()=>[...document.querySelectorAll('#expense-form input,#expense-form select,#expense-form textarea,#expense-initial-payment input,#expense-initial-payment select')].filter(e=>!e.id.startsWith('expense-category')).map(e=>[e.id,['checkbox','radio'].includes(e.type)?e.checked:e.value]));
 async function go(edit=false){await page.evaluate(()=>StockFlowUnsaved.allowNavigation()).catch(()=>{});await page.goto('https://stockflow.test/Expenses/'+(edit?'Details?id='+expense.expense_id:'Create'));await page.waitForFunction(()=>document.querySelector('#expense-state').textContent!=='Loading...');}
 async function open(){await add.click();assert.equal(await name.evaluate(e=>e===document.activeElement),true);}
 async function close(){await page.locator('#expense-master-close').click();if(await page.locator('#sf-confirm-dialog').isVisible())await page.locator('#sf-confirm-dialog button').filter({hasText:/Discard/}).click();await dialog.waitFor({state:'hidden'});}
 await go();await selectValue(page.locator('#expense-payee'),options.payees[0].id);await selectValue(page.locator('#expense-category'),options.categories[0].id);
 await page.locator('#expense-amount').fill('1800');await page.locator('#expense-description').fill('Preserve the complete expense draft');
 await selectValue(page.locator('#expense-initial-state'),'PARTIAL');await page.locator('#expense-initial-amount').fill('600');await page.locator('#expense-initial-reference').fill('Keep reference');await page.locator('#expense-initial-more summary').click();await page.locator('#expense-initial-notes').fill('Keep notes');await page.locator('#expense-initial-confirm').check();const before=await snapshot();
 await open();assert.equal(await page.locator('#expense-master-title').textContent(),'Add Expense Category');assert.equal(await page.locator('#expense-master-subtitle').textContent(),'Create a category to organize your expenses.');
 assert.equal(await page.locator('#expense-master-name-label').textContent(),'Category Name *');assert.equal(await name.getAttribute('placeholder'),'e.g., Shop Rent, Electricity');assert.equal(await save.textContent(),'Save Category');
 assert.equal(await dialog.locator('input:visible').count(),1);assert(await page.locator('#expense-master-contact').isDisabled());assert(await page.locator('#expense-master-category-icon').isVisible());assert(await page.locator('#expense-master-payee-icon').isHidden());
 await name.fill('   ');await save.click();await page.locator('#expense-master-name-validation-error').getByText('Enter a category name.',{exact:true}).waitFor();assert.equal(writes.length,0);
 await name.fill(' '+options.categories[0].name.toUpperCase()+' ');await save.click();await page.locator('#expense-master-error').getByText(/already exists/).waitFor();assert.equal(writes.length,0);await close();
 await open();await name.fill('  Festival Decoration  ');fail=true;await save.click();await page.locator('#expense-master-error').getByText(/Could not save/).waitFor();assert.equal(await name.inputValue(),'Festival Decoration');assert.deepEqual(await snapshot(),before);
 const id=writes.at(-1).id;fail=false;hold=true;await save.click();await page.waitForFunction(()=>document.querySelector('#expense-master-form').getAttribute('aria-busy')==='true');
 assert.equal(await save.textContent(),'Saving…');assert(await save.isDisabled());await page.locator('#expense-master-form').evaluate(f=>{f.requestSubmit();f.requestSubmit();});assert.equal(writes.length,2);assert.equal(writes.at(-1).id,id);hold=false;release();
 await dialog.waitFor({state:'hidden'});assert.equal(await page.locator('#expense-category').inputValue(),id);assert.equal(await page.locator('#expense-category-search').inputValue(),'Festival Decoration');assert.deepEqual(await snapshot(),before);await page.locator('.sf-toast-success').getByText('Expense category added successfully',{exact:true}).waitFor();assert.equal(await add.evaluate(e=>e===document.activeElement),true);
 await open();await name.fill('Concurrent category name');duplicate=true;await save.click();await page.locator('#expense-master-error').getByText(/already exists/).waitFor();assert(await dialog.isVisible());duplicate=false;await close();await page.evaluate(()=>StockFlowNotify.dismiss('expense-category-added'));
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640],[853,512]]){
  await page.setViewportSize({width,height});await open();
  await page.waitForFunction(()=>getComputedStyle(document.querySelector('#expense-master-name')).borderTopColor==='rgb(255, 8, 125)');
  assert(await name.evaluate(e=>{const s=getComputedStyle(e);return s.borderTopWidth==='1px'&&s.outlineStyle==='none'&&s.boxShadow==='none';}),'Single focus border '+width);
  assert(await dialog.evaluate(e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1&&e.scrollHeight<=e.clientHeight+1&&e.scrollWidth<=e.clientWidth+1;}),'Compact popup fits '+width);
  assert(await page.locator('#expense-master-x').evaluate(e=>{const s=getComputedStyle(e),p=getComputedStyle(e,'::before');return s.backgroundColor==='rgba(0, 0, 0, 0)'&&p.backgroundColor==='rgba(0, 0, 0, 0)'&&s.borderTopWidth==='0px'&&s.boxShadow==='none';}),'Bare X '+width);
  for(const element of [save,page.locator('#expense-master-close'),page.locator('#expense-master-x')])assert(await element.evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),'Reachable action '+width);
  await page.screenshot({path:path.join(screens,'category-'+width+'x'+height+'.png')});
  await name.press('Tab');assert.equal(await page.locator('#expense-master-close').evaluate(e=>e===document.activeElement),true);await page.keyboard.press('Tab');assert.equal(await save.evaluate(e=>e===document.activeElement),true);
  await page.keyboard.press('Escape');await dialog.waitFor({state:'hidden'});assert.equal(await add.evaluate(e=>e===document.activeElement),true);
 }
 await open();await page.locator('#expense-master-x').click();await dialog.waitFor({state:'hidden'});
 await go(true);await page.locator('#expense-description').fill('Keep existing draft edits');const draft=await snapshot();await open();await name.fill('Draft category');await name.press('Enter');await dialog.waitFor({state:'hidden'});assert.deepEqual(await snapshot(),draft);assert.equal(await page.locator('#expense-category').inputValue(),writes.at(-1).id);
 editable=false;await go(true);assert(await add.isHidden());assert.deepEqual(errors,[]);
 console.log('PASS Add Expense Category: actual Razor/assets; one field, folder-plus, captions, inline validation, trim, local/server duplicates, failure/retry, repeated-submit guard, toast, selected ID/caption, expense/payment preservation, draft edits, keyboard/Cancel/X/Escape, permissions, bare X and single focus border at ten viewports.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
