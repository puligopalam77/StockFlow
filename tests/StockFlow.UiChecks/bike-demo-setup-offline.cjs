const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/bike-demo-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000},reducedMotion:'reduce'});page.setDefaultTimeout(10000);
  let code='ELECTRICAL',version=1,saveCalls=0,bikeProducts=[],registered;
  const categories=JSON.parse(fs.readFileSync(path.join(fixtures,'Categories.json'))),schema=JSON.parse(fs.readFileSync(path.join(fixtures,'Schema.json')));
  const electrical=[1,2,3].map(i=>({productId:i,productName:'Existing Electrical '+i,sku:'KEEP-'+i,categoryId:1,categoryName:'Existing batteries',trackingType:'SERIAL',warrantyMonths:12,isActive:true}));
  const original=JSON.stringify(electrical),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const req=route.request(),p=new URL(req.url()).pathname,json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
   if(p==='/Settings/BusinessProfile')return route.fulfill({path:path.join(fixtures,code==='ELECTRICAL'?'Setup.html':'BikeSetup.html'),contentType:'text/html'});
   if(p==='/Products')return route.fulfill({path:path.join(fixtures,code==='ELECTRICAL'?'ElectricalProducts.html':'BikeProducts.html'),contentType:'text/html'});
   if(p==='/api/business-profile'){
    if(req.method()==='PUT'){const r=req.postDataJSON();assert(req.headers()['x-csrf-token']);assert.equal(r.expectedVersion,version);assert(r.acknowledged&&r.reason.length>=5&&r.createStarterCategories);code=r.code;version++;saveCalls++;return json({code,version});}
    return json({code,name:code==='ELECTRICAL'?'Electrical / Electronics':'Bike Accessories',version,available:true,catalogSwitchingAvailable:true,dataVisibilityAvailable:true,canEdit:true,impact:{productCount:3+bikeProducts.length,categoryCount:11,transactionCount:33},profiles:[{code:'ELECTRICAL',name:'Electrical / Electronics',categories:[],serialTracking:true,warrantyAndRepairs:true},{code:'BIKE_ACCESSORIES',name:'Bike Accessories',categories:categories.map(c=>c.categoryName),serialTracking:false,warrantyAndRepairs:false}]});
   }
   if(p==='/api/categories')return json(code==='ELECTRICAL'?[{categoryId:1,categoryName:'Existing batteries',categoryPath:'Existing batteries'}]:categories.map(c=>({...c,categoryPath:c.categoryName})));
   if(p==='/api/categories/schema')return json(code==='ELECTRICAL'?{available:true,version:0,attributes:[],categories:[],lockedCategoryIds:[]}:schema);
   if(p==='/api/brands')return json([]);
   if(p==='/api/products'&&req.method()==='POST'){registered=req.postDataJSON();bikeProducts.push({...registered,productId:4,isActive:true});return json(bikeProducts.at(-1));}
   if(p==='/api/products')return json(code==='ELECTRICAL'?electrical:bikeProducts);
   if(p==='/api/products/variants')return json(registered?.variants.map((v,i)=>({...v,variantId:i+4,productId:4}))||[]);
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[]});
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Settings/BusinessProfile');await page.locator('#business-profile-code option[value="BIKE_ACCESSORIES"]').waitFor({state:'attached'});
  assert.equal(await page.locator('#business-profile-code option').count(),2);await page.locator('#business-profile-code').selectOption('BIKE_ACCESSORIES');assert(await page.locator('#business-profile-save').isEnabled());assert(await page.locator('#business-profile-reason').isHidden());
  assert((await page.locator('#business-profile-preservation').innerText()).includes('purchases, receipts, sales, payments, reports and totals follow the selected shop setup'));
  await page.locator('#business-profile-save').click();await page.waitForURL('**/Products');await page.locator('#add-record').click();await page.waitForFunction(()=>!document.querySelector('#purchase-registration-fields').disabled);
  assert.equal(code,'BIKE_ACCESSORIES');assert.equal(await page.locator('#quick-category option').count(),11);assert(await page.locator('#quick-warranty').isHidden());assert(await page.locator('#quick-tracking').isHidden());assert((await page.locator('.configuration-help').innerText()).includes('bike spare parts'));
  assert.equal(await page.locator('#active-shop-profile').count(),0,'Setup information banner is removed from the shared layout');
  assert((await page.locator('#retail-screen-guide').innerText()).includes('bike part or accessory model'));
  assert(!(await page.locator('#master-table thead').count())||!(await page.locator('#master-table thead').innerText()).includes('Warranty'));
  const category=name=>String(categories.find(c=>c.categoryName===name).categoryId);
  await page.locator('#quick-category').selectOption(category('Head Lights'));assert(await page.getByLabel('Compatible bike models *',{exact:true}).isVisible());assert(await page.getByLabel('Voltage (V)',{exact:true}).isVisible());assert(await page.getByLabel('Connector / socket type',{exact:true}).isVisible());
  await page.locator('#quick-category').selectOption(category('Side Guards'));assert(await page.getByLabel('Fitting / side *',{exact:true}).isVisible());assert((await page.getByLabel('Fitting / side *',{exact:true}).innerText()).includes('Pair'));
  await page.locator('#quick-category').selectOption(category('Helmets'));await page.locator('#quick-product-name').fill('Demo Helmet');await page.locator('#quick-sku').fill('HELMET');await page.locator('#quick-has-variants').check();
  await page.getByRole('listbox',{name:'Helmet / glove size options',exact:true}).selectOption(['M','L']);await page.getByRole('listbox',{name:'Color options',exact:true}).selectOption(['Black']);await page.getByRole('button',{name:'Preview combinations',exact:true}).click();
  assert.equal(await page.locator('#quick-combinations tbody tr').count(),2);
  for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert(await page.locator('#purchase-registration').evaluate(e=>e.scrollWidth<=e.clientWidth),'bike registration overflow '+width);}
  await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration').waitFor({state:'hidden'});assert.equal(registered.variants.length,2);assert.equal(registered.trackingType,'QUANTITY');assert.equal(JSON.stringify(electrical),original);
  await page.goto('http://stockflow.test/Settings/BusinessProfile');await page.locator('#business-profile-code').selectOption('ELECTRICAL');await page.locator('#business-profile-save').click();await page.waitForURL('**/Products');await page.waitForFunction(()=>document.querySelector('#master-rows').textContent.includes('Existing Electrical 3'));
  assert.equal(saveCalls,2);assert.equal(JSON.stringify(electrical),original);await page.locator('#add-record').click();await page.waitForFunction(()=>!document.querySelector('#purchase-registration-fields').disabled);assert.equal(await page.locator('#quick-tracking').inputValue(),'SERIAL');assert.equal(await page.locator('#quick-category option').count(),2);assert.deepEqual(errors,[]);
  console.log('PASS: populated Electrical → Bike apply opens Products, ready category fields, helmet variants, mobile dialog, preserved data and Electrical restoration. Mock APIs only.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
