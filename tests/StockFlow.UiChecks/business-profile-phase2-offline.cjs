const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/business-profile-phase2-fixtures');
const profiles=[
 {code:'ELECTRICAL',name:'Electrical / Electronics',categories:[],serialTracking:true,warrantyAndRepairs:true},
 {code:'BIKE_ACCESSORIES',name:'Bike Accessories',categories:['Helmets','Gloves','Bike Covers','Mirrors','Bike Lights','Bike Luggage'],serialTracking:false,warrantyAndRepairs:false},
 {code:'CLOTHING',name:'Clothing',categories:['Shirts','T-Shirts','Trousers','Dresses','Sarees'],serialTracking:false,warrantyAndRepairs:false},
 {code:'FOOTWEAR',name:'Footwear / Chappal Shop',categories:['Slippers','Sandals','Shoes'],serialTracking:false,warrantyAndRepairs:false}
];
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({reducedMotion:'reduce',viewport:{width:1440,height:1000}});page.setDefaultTimeout(9000);
  let code='ELECTRICAL',version=1,available=true,canEdit=true,productCount=0,saveCalls=0,registered=null;
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),p=new URL(req.url()).pathname;const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Settings/BusinessProfile')return route.fulfill({path:path.join(fixtures,code==='ELECTRICAL'?'Settings.html':'Settings-'+code+'.html'),contentType:'text/html'});
   if(p==='/Products')return route.fulfill({path:path.join(fixtures,code==='ELECTRICAL'?'Electrical.html':code+'.html'),contentType:'text/html'});
   if(p==='/api/business-profile'){
    if(req.method()==='GET')return json({code,name:profiles.find(p=>p.code===code).name,version,available,canEdit,profiles,impact:{productCount,transactionCount:0,categoryCount:0}});
    const body=req.postDataJSON();saveCalls++;assert(req.headers()['x-csrf-token']);
    if(body.expectedVersion!==version)return json({message:'The shop profile changed in another session. Reload and review it again.'},409);
    assert(body.acknowledged&&body.reason.length>=5);code=body.code;version++;return json({code,version});
   }
   if(p==='/api/categories/schema')return json({available:false,attributes:[],categories:[],lockedCategoryIds:[]});
   if(p==='/api/categories')return json(profiles.find(p=>p.code===code).categories.map((name,i)=>({categoryId:i+1,categoryPath:name,categoryName:name,businessProfileCode:code})));
   if(p==='/api/brands')return json([]);
   if(p==='/api/products'&&req.method()==='POST'){registered=req.postDataJSON();productCount++;return json({productId:2,...registered});}
   if(p==='/api/products')return json(registered?[{...registered,productId:2,isActive:true}]:[]);
   if(p==='/api/products/variants')return json(registered?.variants.map((v,i)=>({...v,variantId:i+2,productId:2}))||[]);
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[]});
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  const open=async()=>{await page.goto('http://stockflow.test/Settings/BusinessProfile');await page.locator('#business-profile-current dd').first().waitFor();};
  await open();await page.locator('#business-profile-code').selectOption('CLOTHING');
  assert((await page.locator('#business-profile-preview').innerText()).includes('Shirts'));assert((await page.locator('#business-profile-preview').innerText()).includes('hidden and direct access is denied'));
  await page.locator('#business-profile-reason').fill('Configure clothing shop');await page.locator('#business-profile-acknowledge').check();
  version++;await page.locator('#business-profile-save').click();await page.getByRole('button',{name:'Confirm & Apply',exact:true}).click();await page.locator('#business-profile-message').waitFor({state:'visible'});assert.equal(code,'ELECTRICAL');assert.equal(saveCalls,1);
  await open();await page.locator('#business-profile-code').selectOption('CLOTHING');await page.locator('#business-profile-reason').fill('Configure clothing shop');await page.locator('#business-profile-acknowledge').check();await page.locator('#business-profile-save').click();await page.getByRole('button',{name:'Confirm & Apply',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#business-profile-current').textContent.includes('Clothing'));assert.equal(code,'CLOTHING');
  for(const width of [1440,768,390,320]){
   await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Settings overflow '+width);
  }
  await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:path.join(root,'tests/bin/business-profile-phase2-settings.png'),fullPage:true});
  await page.goto('http://stockflow.test/Products');await page.locator('#add-record').click();await page.locator('#purchase-registration[open]').waitFor();
  assert.equal(await page.locator('#quick-tracking').inputValue(),'QUANTITY');assert(await page.locator('#quick-warranty').isHidden());assert(await page.locator('#quick-provider').isHidden());
  assert.equal(await page.locator('#quick-specifications select option').count(),1,'Electrical templates are not offered in clothing shop');
  assert.deepEqual(await page.locator('#quick-category option').allTextContents(),['Choose category',...profiles.find(p=>p.code==='CLOTHING').categories]);
  await page.locator('#quick-product-name').fill('Cotton Shirt');await page.locator('#quick-category').selectOption('1');await page.locator('#quick-sku').fill('SHIRT-001');await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration').waitFor({state:'hidden'});
  assert.equal(registered.trackingType,'QUANTITY');assert.equal(registered.warrantyMonths,0);assert.equal(registered.variants[0].isSerialTracked,false);
  await open();await page.locator('#business-profile-code').selectOption('FOOTWEAR');assert(await page.locator('#business-profile-save').isDisabled());assert((await page.locator('#business-profile-impact').innerText()).includes('cannot be changed'));
  productCount=0;available=false;await open();assert(await page.locator('#business-profile-fields').evaluate(e=>e.disabled));assert((await page.locator('#business-profile-status').innerText()).includes('047'));
  available=true;canEdit=false;await open();assert(await page.locator('#business-profile-fields').evaluate(e=>e.disabled));
  assert.deepEqual(errors,[]);
  console.log('PASS: profile preview, stale-save handling, apply/reload, responsive settings, clothing category scope, hidden electrical fields, quantity defaults, registration and existing-catalog/read-only guards. Mock APIs only.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
