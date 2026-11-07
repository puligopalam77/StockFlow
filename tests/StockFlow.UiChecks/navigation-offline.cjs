// Real Razor fixtures and shell assets; synthetic HTTP, no shop database/device claim.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/navigation-ui-fixtures');
const fixtures={'/Sales':'Sales','/Sales/Create':'NewSale','/Purchases':'Receipts','/Inventory':'Inventory','/Products':'Products','/Categories':'Categories','/Brands':'Brands','/Suppliers':'Suppliers','/Customers':'Customers','/Products/Create':'ProductCreate','/Products/Variants':'Variants','/Categories/Attributes':'Attributes'};
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(fixtures[url.pathname])return route.fulfill({contentType:'text/html',path:path.join(output,fixtures[url.pathname]+'.html')});
   if(url.pathname==='/api/branches/context')return route.fulfill({contentType:'application/json',body:JSON.stringify({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}],locationId:1,storeId:1})});
   if(url.pathname==='/api/notifications')return route.fulfill({contentType:'application/json',body:JSON.stringify({unread:0,items:[]})});
   if(url.pathname==='/api/suppliers/options')return route.fulfill({contentType:'application/json',body:JSON.stringify({countries:[{code:'IN',name:'India'}],states:[],districts:[]})});
   if(url.pathname==='/api/customers')return route.fulfill({contentType:'application/json',body:JSON.stringify([{customerId:1,customerName:'Synthetic navigation customer',mobileNumber:'9876543210',isActive:true}])});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  const nav=page.locator('.sf-shop-nav'),sidebar=page.locator('#app-sidebar');
  const group=name=>page.locator(`[data-nav-group="${name}"]`);
  const openDrawer=async()=>{if(await sidebar.evaluate(e=>e.inert)){try{await page.locator('#sidebar-toggle').click({timeout:4000});}catch(error){console.error('Visible notifications:',await page.locator('#sf-notifications').allTextContents());throw error;}}};
  async function check(route,parent){
   await page.goto('http://stockflow.test'+route);await openDrawer();
   assert(await group(parent).evaluate(e=>e.open),'active workflow expanded: '+route);
   assert.equal(await nav.locator('[aria-current="page"]').count(),1,'one active page: '+route);
   assert.equal(await nav.locator('[aria-current="page"]').evaluate(e=>e.closest('[data-nav-group]').dataset.navGroup),parent);
   assert(!(await group('Setup').evaluate(e=>e.open)),'Administration inactive');
   for(const label of ['Products','Categories','Brands','Suppliers','Customers'])assert.equal(await nav.locator(`a[href="/${label}"]`).count(),1,'no duplicate '+label);
   assert.equal(await group('Setup').locator('a[href="/Products"],a[href="/Categories"],a[href="/Brands"],a[href="/Suppliers"],a[href="/Customers"]').count(),0);
   assert(!(await nav.innerText()).includes('Business Partners'));
  }
  for(const [route,parent] of [['/Products','Inventory'],['/Categories','Inventory'],['/Brands','Inventory'],['/Products/Create','Inventory'],['/Products/Variants','Inventory'],['/Categories/Attributes','Inventory'],['/Suppliers','Receive Stock'],['/Customers','Customer Relationships']])await check(route,parent);
  for(const [width,height] of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
   await page.setViewportSize({width,height});await check('/Products','Inventory');
   const catalog=nav.locator('.sf-catalog-nav');assert(await catalog.evaluate(e=>e.open));
   assert.deepEqual(await catalog.locator('a').allTextContents(),['Products','Categories','Brands']);
   const summary=catalog.locator(':scope > summary');await summary.focus();await page.keyboard.press('Enter');assert(!(await catalog.evaluate(e=>e.open)));
   await page.keyboard.press('Enter');assert(await catalog.locator('a[href="/Products"]').isVisible());
   await group('Inventory').locator(':scope > summary').click();assert(!(await group('Inventory').evaluate(e=>e.open)));
   await group('Inventory').locator(':scope > summary').click();assert(await catalog.locator('a[href="/Brands"]').isVisible());
   const link=catalog.locator('a[href="/Products"]');await link.scrollIntoViewIfNeeded();const box=await link.boundingBox(),side=await sidebar.boundingBox();
   assert(box.x>=side.x&&box.x+box.width<=side.x+side.width+1,'catalog fits drawer');if(width<=991)assert(box.height>=44,'mobile touch target');
   await page.reload();await openDrawer();assert(await group('Inventory').evaluate(e=>e.open)&&await catalog.evaluate(e=>e.open),'reload restores active hierarchy');
   await group('Receive Stock').locator(':scope > summary').click();
   await group('Receive Stock').locator('a[href="/Suppliers"]').click();await page.waitForURL('**/Suppliers');
   if(width<=991)assert(await sidebar.evaluate(e=>e.inert),'navigation closes mobile drawer');await openDrawer();
   assert(await group('Receive Stock').evaluate(e=>e.open));
   await group('Customer Relationships').locator(':scope > summary').click();await group('Customer Relationships').locator('a[href="/Customers"]').click();await page.waitForURL('**/Customers');await openDrawer();
   assert(await group('Customer Relationships').evaluate(e=>e.open));
   const metrics=await nav.evaluate(e=>{const before=window.scrollY;e.scrollTop=e.scrollHeight;return {bodyUnchanged:window.scrollY===before,overflow:getComputedStyle(e).overflowY};});
   assert.equal(metrics.overflow,'auto');assert(metrics.bodyUnchanged,'drawer scroll independent from page');
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no page overflow at '+width);
   if(width<=991){await page.locator('#sidebar-close').click();assert(await sidebar.evaluate(e=>e.inert));await openDrawer();await page.keyboard.press('Escape');assert(await sidebar.evaluate(e=>e.inert));await openDrawer();}
   await page.screenshot({path:path.join(output,`navigation-${width}x${height}.png`),animations:'disabled'});
   console.log(`PASS navigation ${width}x${height}: catalog order/toggle, parent highlighting, reload, supplier/customer links and drawer.`);
  }
  assert.deepEqual(errors,[]);console.log('PASS navigation: eight existing/deep routes, ten viewports, mobile close/Escape/navigation and no JavaScript errors. Mock HTTP only; no physical-device or production-data verification.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
