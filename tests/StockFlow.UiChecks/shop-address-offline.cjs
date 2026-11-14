// Real Add Shop Razor fixture and shared controls; all postal responses are isolated.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {selectValue,fillLocation}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage(),errors=[],writes=[];page.setDefaultTimeout(8000);page.setDefaultNavigationTimeout(8000);page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),u=new URL(req.url()),p=u.pathname;
   const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(u.hostname==='api.postalpincode.in'){
    const pin=p.split('/').at(-1);
    if(pin==='999999')return json([{Status:'Error',PostOffice:null}]);
    if(pin==='888888')return route.abort();
    const offices=pin==='600001'?[{Name:'Chennai',Block:'Chennai',State:'Tamil Nadu',District:'Chennai'}]:[
     {Name:'Bengaluru GPO',Block:'Bengaluru',State:'Karnataka',District:'Bengaluru Urban'},
     {Name:'Museum Road',Block:'Bengaluru',State:'Karnataka',District:'Bengaluru Urban'}];
    return json([{Status:'Success',PostOffice:offices}]);
   }
   if(p==='/Admin/Stores/Create')return route.fulfill({path:path.join(root,'tests/bin/store-management-fixtures/Create.html'),contentType:'text/html'});
   if(p==='/Admin/Stores')return route.fulfill({contentType:'text/html',body:'<p>Shops</p>'});
   if(p==='/api/branches/directory')return json({rows:[],canCreate:true,managementAvailable:true,shopSetupAvailable:true});
   if(p==='/api/gst/settings')return json({businesses:[],registrations:[]});
   if(p==='/api/branches/stores'&&req.method()==='POST'){writes.push(req.postDataJSON());return json({storeId:99});}
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  const field=id=>page.locator('#store-'+id);
  const pinError=()=>field('pinCode').evaluate(e=>e.validationMessage);
  await page.goto('http://stockflow.test/Admin/Stores/Create');
  await page.waitForFunction(()=>document.querySelector('#store-state')?.sfAddressPicker&&document.querySelector('#store-district')?.sfAddressPicker);
  await page.evaluate(()=>StockFlowAddress.reference());
  assert.equal(await field('state').locator('option[value]:not([value=""])').count(),36);
  assert(await field('district').isHidden(),'Native district source is replaced by the visible searchable dropdown');
  assert(!(await page.locator('#store-additional-details').evaluate(e=>e.open)));
  assert(await field('district').evaluate(e=>e.sfAddressPicker.input.getClientRects().length>0),'District is available without expanding Additional Details');
  await selectValue(field('state'),'Karnataka');await fillLocation(field('district'),'Bengaluru Urban');
  await selectValue(field('state'),'Tamil Nadu');assert.equal(await field('district').inputValue(),'','State change clears stale district');
  await field('district').evaluate(e=>e.sfAddressPicker.open());
  const names=await field('district').evaluate(e=>[...e.sfAddressPicker.list.querySelectorAll('[role=option]')].map(o=>o.textContent));
  assert(names.some(n=>n==='Chennai'));assert(!names.some(n=>n==='Bengaluru Urban'));
  await page.keyboard.press('Escape');await field('storeName').fill('Address check shop');await field('addressLine1').fill('12 Actual Shop Street');
  await field('pinCode').fill('600001');
  await page.waitForFunction(()=>document.querySelector('#store-city').value==='Chennai');
  assert.equal(await field('state').inputValue(),'Tamil Nadu');assert.equal(await field('district').inputValue(),'Chennai');
  assert.equal(await field('addressLine1').inputValue(),'12 Actual Shop Street','Postal lookup never replaces street address');
  assert.equal(await pinError(),'');
  await field('city').fill('Wrong city');assert.match(await pinError(),/city\/locality must match/);
  await field('save').click();assert.equal(writes.length,0,'Conflicting address cannot be saved');
  await selectValue(field('pinCode-locality'),'0');assert.equal(await field('city').inputValue(),'Chennai');assert.equal(await pinError(),'');
  await field('pinCode').fill('560001');await page.waitForFunction(()=>document.querySelector('#store-state').value==='Karnataka');
  assert.equal(await field('district').inputValue(),'Bengaluru Urban');assert.match(await pinError(),/city\/locality must match/);
  await selectValue(field('pinCode-locality'),'1');assert.equal(await field('city').inputValue(),'Museum Road');assert.equal(await pinError(),'');
  await selectValue(field('state'),'Tamil Nadu');assert.match(await pinError(),/must match/);assert.equal(await field('district').inputValue(),'');
  await selectValue(field('pinCode-locality'),'0');assert.equal(await field('state').inputValue(),'Karnataka');assert.equal(await pinError(),'');
  await field('pinCode').fill('123');assert.match(await pinError(),/6-digit/);
  await field('pinCode').fill('999999');await page.waitForFunction(()=>document.querySelector('#store-pinCode').validationMessage.includes('not found'));
  await field('pinCode').fill('888888');await page.waitForFunction(()=>document.querySelector('#store-pinCode').validationMessage.includes('unavailable'));
  assert(await page.getByRole('button',{name:'Retry PIN lookup'}).isVisible());
  await field('pinCode').fill('600001');await page.waitForFunction(()=>document.querySelector('#store-city').value==='Chennai');assert.equal(await pinError(),'');
  await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await field('save').click();await page.waitForURL('**/Admin/Stores?*');
  assert.equal(writes.length,1);assert.equal(writes[0].state,'Tamil Nadu');assert.equal(writes[0].district,'Chennai');assert.equal(writes[0].pinCode,'600001');
  assert.deepEqual(errors,[]);console.log('PASS: Shop State/District dropdowns, postal correction, mismatch blocking, invalid PIN, offline retry and saved address.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});






