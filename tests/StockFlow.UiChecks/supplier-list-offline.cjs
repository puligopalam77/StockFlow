const {selectValue}=require('./address-test-helpers.cjs');
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/supplier-directory-fixtures'),shots=path.join(root,'tests/bin/supplier-directory-ui');
fs.mkdirSync(shots,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(8000);
 const original=Array.from({length:48},(_,i)=>({supplierId:i+1,supplierName:i===1?'Bengaluru very long supplier name with multiple words and <script> characters preserved literally '.repeat(2):'Supplier '+String(i+1).padStart(2,'0'),supplierCode:'SUP'+String(i+1).padStart(6,'0'),supplierType:i%3?'DISTRIBUTOR':null,mobileNumber:i%4?'+9198000000'+String(i).padStart(2,'0'):null,gstNumber:i===6?'29ABCDE1234F1Z5':null,email:'kept@example.test',address:'Legacy retained address',countryCode:'IN',isActive:i%3!==0}));
 let data=structuredClone(original),readonly=false,fail=false,delay=false,allCalls=0;const writes=[],queries=[],errors=[],otherWrites=[];
 page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());
 await page.route('**/*',async route=>{const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({contentType:'application/json',body:JSON.stringify(body),status});
  if(p.startsWith('/api/')&&req.method()!=='GET'&&!p.startsWith('/api/suppliers/'))otherWrites.push(p);
  if(p==='/Suppliers'||p==='/Suppliers/Index')return route.fulfill({path:path.join(fixtures,readonly?'Readonly.html':'List.html'),contentType:'text/html'});
  if(/^\/Suppliers\/(?:Edit|View|Create)$/.test(p))return route.fulfill({path:path.join(root,'tests/bin/master-screen-fixtures/Suppliers-'+p.split('/')[2]+'.html'),contentType:'text/html'});
  if(p==='/api/suppliers/page'){
   const q=Object.fromEntries(url.searchParams);queries.push(q);const term=(q.term||'').toLowerCase();let items=data.filter(s=>(q.status==='all'||s.isActive===(q.status==='active'))&&[s.supplierName,s.supplierCode,s.mobileNumber,s.gstNumber].some(v=>v?.toLowerCase().includes(term))).sort((a,b)=>a.supplierName.toLowerCase().localeCompare(b.supplierName.toLowerCase())||a.supplierId-b.supplierId);
   return json({items:items.slice(Number(q.offset),Number(q.offset)+Number(q.limit)),total:items.length});
  }
  const match=p.match(/^\/api\/suppliers\/(\d+)(\/status)?$/);
  if(match){const record=data.find(s=>s.supplierId===Number(match[1]));
   if(match[2]){writes.push({id:Number(match[1]),body:req.postDataJSON()});assert(req.headers()['x-csrf-token']);if(delay)await new Promise(r=>setTimeout(r,250));if(fail)return json({message:'Supplier status could not be saved.'},503);if(!record)return json({},404);record.isActive=req.postDataJSON().isActive;return route.fulfill({status:204});}
   if(req.method()==='PUT'){Object.assign(record,req.postDataJSON());return route.fulfill({status:204});}return json(record||{},record?200:404);
  }
  if(p==='/api/suppliers/options')return json({countries:[{code:'IN',name:'India'}]});
  if(p==='/api/suppliers/duplicates')return json([]);
  if(p==='/api/suppliers'){allCalls++;return json(data);}
  if(p==='/api/notifications')return json({items:[],unread:0});if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 const idle=()=>page.waitForFunction(()=>document.getElementById('master-list')?.getAttribute('aria-busy')==='false');
 const ready=()=>page.waitForFunction(()=>document.getElementById('master-data')?.dataset.screenReady==='true');
 const tooltip=async(action,label)=>{
  await page.mouse.move(0,0);await action.evaluate(e=>e.blur());await action.scrollIntoViewIfNeeded();await page.waitForTimeout(300);await action.hover();
  await page.getByRole('tooltip',{name:label,exact:true}).waitFor().catch(async error=>{console.error(await action.evaluate(e=>({width:innerWidth,height:innerHeight,hover:e.matches(':hover'),focus:e===document.activeElement,disabled:e.disabled,title:e.title,html:e.outerHTML,tooltips:[...document.querySelectorAll('[role=tooltip]')].map(t=>t.outerHTML)})));throw error;});
  assert.equal(await page.locator('[role="tooltip"]:visible').count(),1);
  assert.equal(await action.getAttribute('title'),null);
  assert(['none','normal'].includes(await action.evaluate(e=>getComputedStyle(e,'::after').content)));
  assert(await page.locator('#sf-action-tooltip').evaluate(e=>{const r=e.getBoundingClientRect();return r.x>=0&&r.y>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}));
  await action.focus();assert.equal(await page.locator('[role="tooltip"]:visible').count(),1);
  await page.keyboard.press('Escape');assert.equal(await page.locator('#sf-action-tooltip').count(),0);
 };
 const reload=async()=>{await page.goto('http://stockflow.test/Suppliers');await ready();await tooltip(page.locator('.supplier-row-action').first(),await page.locator('.supplier-row-action').first().getAttribute('aria-label'));};
 const search=page.locator('#master-search'),status=page.locator('#status-filter'),rows=page.locator('#master-rows tr');
 const term=async value=>{await search.fill(value);await search.press('Enter');await page.waitForFunction(value=>new URL(location.href).searchParams.get('search')===(value||null),value);await idle();};
 await reload();assert.equal(await page.getByText('Saved Suppliers',{exact:true}).count(),0);
 assert(await page.getByText('Manage suppliers, contact details and purchasing preferences.',{exact:true}).isVisible());
 assert.deepEqual(await page.locator('#master-head th').allTextContents(),['Supplier','Supplier Code','Type','Mobile','Status','Actions']);
 assert.equal(await rows.count(),10);assert((await page.locator('#supplier-pager').textContent()).includes('of 48 suppliers'));
 await tooltip(page.getByRole('link',{name:'Edit Supplier',exact:true}).first(),'Edit Supplier');
 const adjacent=page.getByRole('button',{name:'Deactivate Supplier',exact:true}).first();
 for(let i=0;i<4;i++){await page.getByRole('link',{name:'Edit Supplier',exact:true}).first().hover();await adjacent.hover();}
 await page.getByRole('tooltip',{name:'Deactivate Supplier',exact:true}).waitFor();assert.equal(await page.locator('[role="tooltip"]:visible').count(),1);
 await page.waitForFunction(()=>getComputedStyle(document.getElementById('add-record')).backgroundColor==='rgb(255, 0, 127)');
 await page.locator('#status-filter-search').focus();await page.locator('#status-filter-search').press('Enter');await page.locator('#status-filter-query').press('Home');await page.locator('#status-filter-query').press('ArrowDown');await page.locator('#status-filter-query').press('Enter');await idle();assert.equal(await status.inputValue(),'true','Status supports shared keyboard selection');
 await page.locator('.supplier-filter-card').getByRole('button',{name:'Reset Filters',exact:true}).click();await idle();
 const ids=await rows.evaluateAll(rs=>rs.map(r=>r.dataset.recordId));await page.getByRole('button',{name:'Next page',exact:true}).click();await page.waitForFunction(()=>new URL(location.href).searchParams.get('page')==='2');await idle();assert(!(await rows.evaluateAll(rs=>rs.map(r=>r.dataset.recordId))).some(id=>ids.includes(id)));
 await page.getByRole('button',{name:'Last page',exact:true}).click();await page.waitForFunction(()=>new URL(location.href).searchParams.get('page')==='5');await idle();assert.equal(await rows.count(),8);
 await page.getByRole('button',{name:'First page',exact:true}).click();
 await term('SUP000007');assert.equal(await rows.count(),1);assert.equal(await rows.first().getAttribute('data-record-id'),'7');
 await term('9800000006');assert.equal(await rows.count(),1);await term('29abcde1234f1z5');assert.equal(await rows.count(),1);
 await term('Supplier 08');assert.equal(await rows.count(),1);
 const edit=page.getByRole('link',{name:'Edit Supplier',exact:true});await edit.click();await ready();
 assert.equal(await page.locator('#supplierName').inputValue(),'Supplier 08');assert.equal(await page.locator('#email').inputValue(),'kept@example.test');
 await page.getByRole('link',{name:'Back to Suppliers',exact:true}).click();await ready();assert.equal(await search.inputValue(),'Supplier 08');assert.equal(await rows.count(),1);
 await page.locator('.supplier-filter-card').getByRole('button',{name:'Reset Filters',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('#master-rows tr').length===10);
 await selectValue(status,'inactive');await page.waitForFunction(()=>document.querySelector('#master-rows .supplier-status')?.textContent==='Inactive');assert(await page.getByRole('button',{name:'Activate Supplier',exact:true}).count()>0);
 const inactive=data.find(s=>!s.isActive);const inactiveRow=page.locator('[data-record-id="'+inactive.supplierId+'"]');
 const before=structuredClone(inactive);await tooltip(inactiveRow.getByRole('button',{name:'Activate Supplier',exact:true}),'Activate Supplier');await inactiveRow.getByRole('button',{name:'Activate Supplier',exact:true}).click();await page.waitForFunction(id=>!document.querySelector('[data-record-id="'+id+'"]'),inactive.supplierId);
 assert.equal(writes.at(-1).body.isActive,true);assert.equal(inactive.address,before.address);assert.equal(inactive.email,before.email);
 await selectValue(status,'true');await page.waitForFunction(()=>document.querySelector('#master-rows .supplier-status')?.textContent==='Active');
 const deactivate=page.getByRole('button',{name:'Deactivate Supplier',exact:true}).first();const id=Number(await deactivate.evaluate(e=>e.closest('tr').dataset.recordId));
 const selected=data.find(s=>s.supplierId===id),count=writes.length;await deactivate.click();
 const dialog=page.getByRole('dialog',{name:'Deactivate Supplier?',exact:true});
 await dialog.waitFor();assert.equal(await page.locator('#sf-action-tooltip').count(),0);assert((await dialog.textContent()).includes(selected.supplierName));assert.equal(await page.locator(':focus').textContent(),'Cancel');
 await page.keyboard.press('Escape');assert.equal(writes.length,count);assert.equal(await deactivate.evaluate(e=>e===document.activeElement),true);
 await deactivate.click();const close=dialog.getByRole('button',{name:'Close dialog',exact:true});await close.click();await dialog.waitFor({state:'hidden'});assert.equal(writes.length,count);assert(await deactivate.evaluate(e=>e===document.activeElement));
 await deactivate.click();await dialog.getByRole('button',{name:'Cancel',exact:true}).click();await dialog.waitFor({state:'hidden'});assert.equal(writes.length,count);assert(await deactivate.evaluate(e=>e===document.activeElement));
 await deactivate.click();await close.focus();await page.keyboard.press('Shift+Tab');assert.equal(await page.locator(':focus').textContent(),'Deactivate Supplier');await page.keyboard.press('Tab');assert.equal(await page.locator(':focus').getAttribute('aria-label'),'Close dialog');
 await close.hover();await page.waitForTimeout(220);assert.equal(await page.locator('[role="tooltip"]:visible').count(),0);
 fail=true;await dialog.getByRole('button',{name:'Deactivate Supplier',exact:true}).click();await page.getByText('Supplier status could not be saved.',{exact:true}).waitFor();assert(selected.isActive);assert(await dialog.isVisible());await dialog.getByText('Supplier remains active. Try again or cancel.',{exact:true}).waitFor();
 fail=false;delay=true;await dialog.getByRole('button',{name:'Deactivate Supplier',exact:true}).click();
 assert(await close.isDisabled());assert(await dialog.getByRole('button',{name:'Cancel',exact:true}).isDisabled());await page.keyboard.press('Escape');assert(await dialog.isVisible());assert.equal(await dialog.getAttribute('aria-busy'),'true');await dialog.locator('[data-confirm]').evaluate(e=>e.click());
 await page.waitForFunction(id=>!document.querySelector('[data-record-id="'+id+'"]'),id);assert.equal(writes.length,count+2,'Failed attempt plus one guarded successful attempt');delay=false;
 await page.locator('.supplier-filter-card').getByRole('button',{name:'Reset Filters',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#status-filter').value==='false');
 for(const size of ['25','50','100','10']){await selectValue(page.locator('.sf-pagination-size select'),size);await page.waitForFunction(size=>new URL(location.href).searchParams.get('size')===(size==='10'?null:size),size);await idle();assert.equal(await rows.count(),Math.min(Number(size),data.length));await tooltip(page.getByRole('link',{name:'Edit Supplier',exact:true}).first(),'Edit Supplier');}
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+1);
  assert.equal(await rows.count(),10);assert.equal(await page.locator('#master-head th').first().evaluate(e=>getComputedStyle(e).color),'rgb(20, 37, 60)');if(width<=700)assert.equal(await rows.first().locator('td').first().evaluate(e=>getComputedStyle(e).borderTopWidth),'0px');const button=page.getByRole('button',{name:'Deactivate Supplier',exact:true}).first();await button.scrollIntoViewIfNeeded();assert(await button.evaluate(e=>{const b=e.getBoundingClientRect();return e.contains(document.elementFromPoint(b.x+b.width/2,b.y+b.height/2));}),'Action unobstructed '+width);
  await tooltip(button,'Deactivate Supplier');
  await button.click();assert.equal(await page.locator('#sf-action-tooltip').count(),0);const box=await dialog.boundingBox();assert(box.x>=0&&box.y>=0&&box.x+box.width<=width+1&&box.y+box.height<=height+1,'Dialog fits '+width);
  assert(await dialog.evaluate(e=>e.scrollWidth<=e.clientWidth),'No dialog horizontal overflow');
  const x=await close.boundingBox(),heading=await dialog.locator('h2').boundingBox();assert(x.x>=heading.x+heading.width,'Close does not overlap title');assert(x.y>=box.y&&x.x+x.width<=box.x+box.width,'Close fits');
  assert(await dialog.getByRole('button',{name:'Cancel',exact:true}).evaluate(e=>e.getBoundingClientRect().height>=43.9),'44px footer target at '+width+': '+await dialog.getByRole('button',{name:'Cancel',exact:true}).evaluate(e=>JSON.stringify({height:e.getBoundingClientRect().height,min:getComputedStyle(e).minHeight})));
  await page.screenshot({path:path.join(shots,'supplier-confirm-'+width+'x'+height+'.png')});await dialog.getByRole('button',{name:'Cancel',exact:true}).click();
  await page.screenshot({path:path.join(shots,'suppliers-'+width+'x'+height+'.png')});
 }
 data=[];await term('missing');await page.getByRole('heading',{name:'No suppliers found',exact:true}).waitFor();
 await page.locator('.supplier-filter-card').getByRole('button',{name:'Reset Filters',exact:true}).click();await page.getByRole('heading',{name:'No suppliers yet',exact:true}).waitFor();
 await selectValue(status,'inactive');await page.getByRole('heading',{name:'No inactive suppliers found.',exact:true}).waitFor();
 data=Array.from({length:11},(_,i)=>({...original[i],isActive:true}));await selectValue(status,'true');await page.getByRole('button',{name:'Next page',exact:true}).waitFor();await page.getByRole('button',{name:'Next page',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('#master-rows tr').length===1);
 await page.getByRole('button',{name:'Deactivate Supplier',exact:true}).click();await dialog.getByRole('button',{name:'Deactivate Supplier',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('#master-rows tr').length===10);assert(await page.locator('#supplier-pager').isHidden(),'Pager hides at ten filtered suppliers after valid-page recovery');
 readonly=true;await reload();assert.equal(await page.locator('#add-record').count(),0);assert.equal(await page.getByRole('link',{name:'Edit Supplier',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:/^(?:Activate|Deactivate) Supplier$/}).count(),0);assert(await page.getByRole('link',{name:'View Supplier',exact:true}).count()>0);
 assert(queries.every(q=>[10,25,50,100].includes(Number(q.limit))));assert.equal(allCalls,0,'Directory never downloads the unpaged supplier collection');
 assert.deepEqual(otherWrites,[],'Directory actions never post purchases, receipts, mappings or inventory changes');
 assert.deepEqual(errors,[]);console.log('PASS supplier directory: six columns, bounded server queries, four search fields, filters/reset/state return, page sizes/navigation, status cancel/error/retry/double-click, valid-page recovery, preserved metadata, read-only actions, three empty states, ten viewports and DPR 1.5. Controlled APIs.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
