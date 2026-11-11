// Actual Razor/assets, controlled APIs. PostgreSQL behavior has separate local tests.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),dir=path.join(root,'tests/bin/bank-accounts-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const html=fs.readFileSync(path.join(dir,'Index.html'),'utf8'),base=JSON.parse(fs.readFileSync(path.join(dir,'detail.json'),'utf8')),list=JSON.parse(fs.readFileSync(path.join(dir,'list.json'),'utf8'));
 base.account.displayName='Synthetic bank account with a long translated display name for responsive layout verification';
 const id=base.account.id,page=await browser.newPage(),errors=[],writes=[],measurements=[];
 let admin=false,failRead=false,failPost=false,rejectPost=false,emptyPost=false;
 page.setDefaultTimeout(10000);page.on('pageerror',e=>{errors.push(e.message);console.error('Browser:',e.message);});
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname.startsWith('/Settings/BankAccounts'))return route.fulfill({contentType:'text/html',body:url.pathname==='/Settings/BankAccounts'?html.replace(/data-account="[^"]*"/,'data-account=""'):html});
  if(url.pathname.startsWith('/api/bankaccounts')){
   if(req.method()==='POST'){
    writes.push({url:url.pathname,...req.postDataJSON()});if(failPost){failPost=false;return route.abort('failed');}
    if(emptyPost){emptyPost=false;return route.fulfill({status:200,contentType:'application/json',body:''});}
    if(rejectPost){rejectPost=false;return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:'Bank evidence is incomplete'})});}
    return route.fulfill({contentType:'application/json',body:JSON.stringify({accountId:id,status:'Manually Verified'})});
   }
   if(failRead)return route.fulfill({status:500,contentType:'application/json',body:JSON.stringify({message:'Bank account data unavailable'})});
   const data=structuredClone(url.pathname==='/api/bankaccounts'?list:base);
   if(data.account){data.account.active=true;data.canVerify=true;data.canReverse=true;data.canOpen=true;
    data.sources=[{id:'34566800-b386-4ad3-b035-71f7a527c3a0',kind:'SALE_PAYMENT',key:'123',direction:'IN',amount:5000,remaining:5000,date:'2026-11-10'}];
    data.candidates=[{kind:'SALE_PAYMENT',key:'456',reference:'SYNTHETIC-INVOICE',mode:'UPI',amount:2000,day:'2026-11-10'}];
    if(admin){data.ownerOverview=false;delete data.recordedBalance;delete data.opening;delete data.shops;delete data.account.recordedBalance;delete data.account.stores;for(const r of data.transactions)delete r.runningBalance;for(const k of ['canCreate','canEdit','canAssign','canDefault','canStatus','canOpen'])data[k]=false;}
   }
   return route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  }
  if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:JSON.stringify(url.pathname.includes('notifications')?{items:[],unread:0}:{})});
  const asset=path.join(root,'src/StockFlow.Web/wwwroot',decodeURIComponent(url.pathname).replace(/\.[a-z0-9]{10}(?=\.(?:css|js|svg|png|webp)$)/i,''));
  if(fs.existsSync(asset)&&fs.statSync(asset).isFile())return route.fulfill({path:asset,contentType:asset.endsWith('.js')?'application/javascript':asset.endsWith('.css')?'text/css':undefined});
  return route.fulfill({status:404,body:''});
 });
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,600],[1024,768],[768,844],[390,844],[375,667],[320,568]]){
  await page.setViewportSize({width,height});await page.goto('https://stockflow.test/Settings/BankAccounts/'+id);await page.locator('#bank-kpis strong').first().waitFor();
  assert.equal(await page.locator('#bank-kpis .bank-kpi').count(),4);assert.equal(await page.locator('#bank-title').innerText(),base.account.displayName);
  assert((await page.locator('#breadcrumb-current').innerText()).includes(base.account.displayName));
  assert.equal(await page.locator('.sf-shop-nav a[href="/Settings/BankAccounts"]').count(),1,'Bank Accounts belongs under Business Setup');
  assert.equal(await page.locator('.sf-shop-nav>a[href="/CashInHand"]').count(),1,'Cash remains a direct sidebar link');
  await page.locator('#bank-settings button').filter({hasText:'Edit Account'}).click();
  assert.equal(await page.locator('#bank-field-accountNumber').inputValue(),'','Full number never appears in edit form');
  const m=await page.evaluate(()=>{const d=document.querySelector('#bank-dialog'),footer=d.querySelector('footer'),body=d.querySelector('.bank-dialog-body');return {width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,dialog:d.getBoundingClientRect().toJSON(),footer:footer.getBoundingClientRect().toJSON(),dialogOverflow:d.scrollWidth>d.clientWidth,nestedScroll:d.scrollHeight>d.clientHeight,bodyScroll:body.scrollHeight>body.clientHeight,inputHeight:document.querySelector('#bank-field-bankName').getBoundingClientRect().height};});
  assert(m.pageWidth<=width,'No page horizontal overflow at '+width);assert(!m.dialogOverflow);assert(!m.nestedScroll,'Dialog itself must not become a second scroll container');assert(m.dialog.width<=520&&m.dialog.x>=0);assert(m.footer.bottom<=height+1);assert.equal(m.inputHeight,42);measurements.push(m);
  await page.locator('#bank-cancel').click();await page.waitForFunction(()=>!document.querySelector('#bank-dialog').open);
  await page.goto('https://stockflow.test/Settings/BankAccounts');await page.locator('#bank-add').waitFor();await page.locator('#bank-add').click();
  assert(await page.locator('#bank-field-accountNumber').evaluate(n=>n.required));assert(await page.locator('#bank-fields input[name=stores]').count()>0);
  await page.keyboard.press('Escape');await page.waitForFunction(()=>!document.querySelector('#bank-dialog').open);
 }
 await page.setViewportSize({width:1366,height:768});await page.goto('https://stockflow.test/Settings/BankAccounts/'+id);await page.locator('#bank-sources input[type=checkbox]').waitFor();
 await page.locator('#bank-sources input[type=checkbox]').check();await page.locator('#bank-verify').click();
 assert(await page.locator('#bank-field-afterOpening').evaluate(n=>n.required));
 await page.locator('#bank-field-reference').fill('SYNTHETIC-VERIFIED-UTR');await page.locator('#bank-field-evidence').fill('Synthetic statement evidence for actual bank credit');await page.locator('#bank-field-afterOpening').check();
 failPost=true;await page.locator('#bank-save').click();await page.waitForFunction(()=>document.querySelector('#bank-save').textContent.includes('Retry'));
 assert(await page.locator('#bank-fields').evaluate(n=>n.disabled));assert(await page.locator('#bank-field-reference').isDisabled());assert(await page.locator('#bank-cancel').isDisabled());
 await page.locator('#bank-save').click();await page.waitForFunction(()=>!document.querySelector('#bank-dialog').open);assert.deepEqual(writes[0],writes[1],'Lost response retries exact same operation identity and payload');
 await page.locator('#bank-sources input[type=checkbox]').check();await page.locator('#bank-verify').click();await page.locator('#bank-field-reference').fill('');await page.locator('#bank-save').click();
 assert.equal(writes.length,2,'Required bank evidence prevents submission');assert(await page.locator('#bank-field-reference').evaluate(n=>n.validity.valueMissing));
 await page.locator('#bank-field-reference').fill('SYNTHETIC-BACKEND');await page.locator('#bank-field-evidence').fill('Synthetic bank evidence needing backend review');await page.locator('#bank-field-afterOpening').check();
 rejectPost=true;await page.locator('#bank-save').click();await page.waitForFunction(()=>document.querySelector('#bank-form-error').textContent.includes('incomplete'));
 assert(await page.locator('#bank-field-reference').isEnabled());assert(await page.locator('#bank-cancel').isEnabled());await page.locator('#bank-cancel').click();
 await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>!document.querySelector('#bank-dialog').open);
 await page.locator('#bank-sources input[type=checkbox]').check();await page.locator('#bank-verify').click();
 await page.locator('#bank-field-reference').fill('SYNTHETIC-EMPTY-RESPONSE');await page.locator('#bank-field-evidence').fill('Synthetic bank evidence with a lost successful response');await page.locator('#bank-field-afterOpening').check();
 emptyPost=true;await page.locator('#bank-save').click();await page.waitForFunction(()=>document.querySelector('#bank-save').textContent.includes('Retry'));
 assert(await page.locator('#bank-field-reference').isDisabled());assert(await page.locator('#bank-cancel').isDisabled());
 await page.keyboard.press('Escape');assert(await page.locator('#bank-dialog').evaluate(n=>n.open));assert.equal(await page.locator('#sf-confirm-dialog[open]').count(),0,'Unknown request cannot be discarded through shared Escape handling');
 await page.locator('#bank-save').click();await page.waitForFunction(()=>!document.querySelector('#bank-dialog').open);assert.deepEqual(writes[3],writes[4],'Empty successful response retries exact request identity and payload');
 admin=true;await page.reload();await page.locator('#bank-kpis strong').first().waitFor();assert.equal(await page.locator('#bank-kpis .bank-kpi').count(),3);
 assert(!(await page.locator('#bank-kpis').innerText()).includes('Recorded Bank Balance'));assert(await page.locator('#bank-running-head').isHidden());assert(await page.locator('#bank-opening-audit').isHidden());assert.equal(await page.locator('#bank-settings button').count(),0);
 failRead=true;await page.locator('#bank-date').dispatchEvent('change');await page.waitForFunction(()=>document.querySelector('#bank-message').textContent.includes('unavailable'));assert.equal(await page.locator('#bank-kpis strong').count(),0,'Failed API does not fabricate zero or retain stale balances');
 assert.deepEqual(errors,[]);fs.writeFileSync(path.join(dir,'measurements.json'),JSON.stringify(measurements,null,2));
 console.log('PASS Bank Accounts UI: nine viewports, 42px fields, bounded single-scroll dialogs, list/detail, setup navigation, admin privacy, failed reads and exact lost-response retry. APIs mocked.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});

