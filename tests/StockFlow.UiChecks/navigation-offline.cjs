const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'tests/bin/navigation-ui-fixtures');
(async () => {
 const browser = await chromium.launch({channel:'msedge',headless:true});
 try {
  const page = await browser.newPage();
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/Sales')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(output,'Sales.html'),'utf8')});
   if(url.pathname==='/api/branches/context')return route.fulfill({contentType:'application/json',body:JSON.stringify({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}],locationId:1,storeId:1})});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  for(const width of [1440,768,390,320]){
   await page.setViewportSize({width,height:900});await page.goto('http://stockflow.test/Sales');
   const mobile=width<=991;
   if(mobile){await page.locator('#sidebar-toggle').click();assert(await page.locator('#app-sidebar').evaluate(el=>!el.inert));}
   const billing=page.locator('[data-nav-group="Sell / Billing"]');
   assert(await billing.evaluate(el=>el.open),'current task group opens');
   assert.equal(await page.locator('.sf-shop-nav [aria-current="page"]').count(),1);
   const setup=page.locator('[data-nav-group="Setup"]');
   assert(!(await setup.evaluate(el=>el.open)),'Setup starts collapsed');
   await setup.locator('summary').focus();await page.keyboard.press('Enter');
   assert(await setup.locator('a[href="/Configuration"]').isVisible(),'keyboard opens Setup');
   await page.screenshot({path:path.join(output,`navigation-${width}.png`),fullPage:true});
   if(mobile){await page.keyboard.press('Escape');assert(await page.locator('#app-sidebar').evaluate(el=>el.inert));}
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`overflow at ${width}`);
   assert(await page.locator('.sf-page-heading').isVisible());
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: navigation groups, keyboard operation, mobile drawer and layout at 1440/768/390/320px.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
