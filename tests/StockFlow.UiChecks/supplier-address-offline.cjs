const {fillLocation}=require('./address-test-helpers.cjs');
// Real supplier Razor fixture/assets with controlled APIs. No database writes.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),out=path.join(root,'tests/bin/supplier-address-ui');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(8000);
 const errors=[],writes=[];page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());
 let record={supplierId:8,supplierCode:'SUP000008',supplierName:'Legacy supplier',address:'Original full address, floor 3\nDo not split this text',countryCode:'IN',isActive:false};
 await page.route('**/*',route=>{const req=route.request(),u=new URL(req.url()),p=u.pathname,json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
  if(p.startsWith('/Suppliers'))return route.fulfill({path:path.join(root,'tests/bin/master-screen-fixtures/Suppliers-'+(p.split('/')[2]||'List')+'.html'),contentType:'text/html'});
  if(p==='/api/suppliers/options')return json({countries:[{code:'IN',name:'India'},{code:'US',name:'United States'}],indianStates:['Karnataka','Tamil Nadu']});
  if(p==='/api/suppliers/duplicates')return json([]);
  if(p==='/api/suppliers/8'&&req.method()==='GET')return json(record);
  if(p==='/api/suppliers/8'&&req.method()==='PUT'||p==='/api/suppliers'&&req.method()==='POST'){
   const body=req.postDataJSON();assert(!('supplierCode' in body));writes.push(body);
   record={...record,...body,supplierName:body.supplierName,isActive:req.method()==='POST'?true:body.isActive};
   if(body.useStructuredAddress)record.address=[body.addressLine1,body.addressLine2].filter(Boolean).join('\n')||null;
   return req.method()==='PUT'?route.fulfill({status:204}):json(record,201);
  }
  if(p==='/api/suppliers')return json([record]);
  if(p==='/api/notifications')return json({items:[],unread:0});if(p.startsWith('/api/'))return json([]);
  const file=path.join(assets,decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({body:''});
 });
 const ready=()=>page.waitForFunction(()=>document.querySelector('#master-data')?.dataset.screenReady==='true'),save=()=>page.getByRole('button',{name:/^Save (Supplier|Changes)$/}),contact=()=>page.getByText('Contact & Address (Optional)',{exact:true});
 await page.goto('http://stockflow.test/Suppliers/Edit?id=8');await ready();
 assert.equal(await page.locator('.supplier-legacy-address').textContent(),record.address);
 assert.equal(await page.locator('#addressLine1').inputValue(),'');assert.equal(await page.locator('#addressLine2').inputValue(),'');assert(await page.locator('#addressLine1').isDisabled());
 assert(!await page.locator('#isActive').isChecked());await page.locator('#supplierName').fill('Legacy renamed');await save().click();await page.waitForURL('**/Suppliers');
 assert.equal(record.address,'Original full address, floor 3\nDo not split this text');assert.equal(record.isActive,false);assert.equal(record.supplierCode,'SUP000008');
 await page.goto('http://stockflow.test/Suppliers/Edit?id=8');await ready();await page.locator('#useStructuredAddress').check();
 await page.locator('#addressLine1').fill('12 Market Road');await page.locator('#addressLine2').fill('First floor');await page.locator('#city').fill('Bengaluru');await fillLocation(page.locator('#state'),'Karnataka');await page.locator('#postalCode').fill('560001');
 await contact().click();assert(!await page.locator('#addressLine1').isVisible());await contact().click();assert.equal(await page.locator('#addressLine2').inputValue(),'First floor');
 await save().click();await page.waitForURL('**/Suppliers');assert.equal(record.address,'12 Market Road\nFirst floor');assert.equal(record.isActive,false);
 await page.goto('http://stockflow.test/Suppliers/Edit?id=8');await ready();assert.equal(await page.locator('#addressLine1').inputValue(),'12 Market Road');assert.equal(await page.locator('#addressLine2').inputValue(),'First floor');assert(!await page.locator('.supplier-legacy-address').isVisible());
 await page.locator('#addressLine2').fill('');await save().click();await page.waitForURL('**/Suppliers');assert.equal(record.address,'12 Market Road');
 for(const [line1,line2] of [['New street','Landmark'],['New street',''],['','']]){
  await page.goto('http://stockflow.test/Suppliers/Create');await ready();assert(!await page.locator('.supplier-status').isVisible());assert.equal(await page.locator('.supplier-optional[open]').count(),0);
  await page.locator('#supplierName').fill('New optional address supplier');await contact().click();await page.locator('#addressLine1').fill(line1);await page.locator('#addressLine2').fill(line2);
  const header=page.locator('.supplier-optional').first().locator('summary');await page.waitForFunction(()=>document.querySelector('.supplier-optional summary').getAttribute('aria-expanded')==='true');await header.press('Enter');await page.waitForFunction(()=>document.querySelector('.supplier-optional summary').getAttribute('aria-expanded')==='false');await header.press('Enter');
  await page.locator('#postalCode').fill('123');await header.press('Enter');await save().click();await page.locator('#postalCode-validation-error').waitFor();assert(await page.locator('#postalCode').isVisible());
  await page.locator('#postalCode').fill('');await save().evaluate(button=>{button.form.requestSubmit();button.form.requestSubmit();});await page.waitForURL('**/Suppliers');assert.equal(writes.at(-1).addressLine1,line1||null);assert.equal(writes.at(-1).addressLine2,line2||null);
 }
 fs.mkdirSync(out,{recursive:true});
 for(const mode of ['Create','Edit'])for(const [width,height] of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Suppliers/'+mode+(mode==='Edit'?'?id=8':''));await ready();
  assert(await page.locator('#supplierCode').isDisabled());assert.equal(await page.locator('#supplierCode').getAttribute('tabindex'),'-1');assert.equal(await page.locator('.supplier-locked').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(240, 242, 245)');
  assert.equal(await page.locator('#supplierCode').evaluate(e=>getComputedStyle(e).cursor),'default');assert.equal(await page.locator('.supplier-locked svg').count(),1);
  if(!await page.locator('#addressLine1').isVisible())await contact().click();
  const boxes=await page.locator('[name=addressLine1],[name=addressLine2],[name=city],[name=state],[name=postalCode],[name=countryCode]').evaluateAll(nodes=>nodes.map(e=>(e.sfAddressPicker?.input||e).getBoundingClientRect().toJSON()));
  assert(boxes[1].y>boxes[0].y);assert(Math.abs(boxes[0].width-boxes[1].width)<1);
  if(width<=390)assert(boxes.every((b,i)=>i===0||b.y>boxes[i-1].y),'Mobile address fields stack');
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal overflow');
  for(const button of [save(),page.getByRole('button',{name:'Cancel',exact:true})]){await button.scrollIntoViewIfNeeded();assert(await button.evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),'Footer action unobstructed');}
  await page.screenshot({path:path.join(out,mode+'-'+width+'x'+height+'.png')});console.log('PASS structured supplier '+mode+' '+width+'x'+height);
 }
 record={...record,useStructuredAddress:true,addressLine1:'View address',addressLine2:'View landmark',address:'View address\nView landmark'};
 await page.goto('http://stockflow.test/Suppliers/View?id=8');await ready();
 assert.equal(await page.locator('#addressLine1').inputValue(),'View address');assert.equal(await page.locator('#addressLine2').inputValue(),'View landmark');assert(await page.locator('#addressLine1').isDisabled());assert(await page.locator('#supplierCode').isDisabled());
 assert.deepEqual(errors,[]);console.log('PASS: optional lines, legacy preservation/explicit replacement, inactive edits, reopening, collapsed-field validation, keyboard accordions, locked codes, repeated-submit protection and 20 responsive screens. Controlled HTTP.');
 }finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
