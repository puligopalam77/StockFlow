const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/reports-ui-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   const files={'/Reports/Activity':'Reports','/Sales':'Sales','/Inventory':'Inventory','/Service':'Service'};
   if(files[url.pathname])return route.fulfill({path:path.join(output,files[url.pathname]+'.html'),contentType:'text/html'});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales?fromReports=1');
  const reports=page.locator('[data-nav-group="Reports"]');
  assert(await reports.evaluate(n=>n.open));
  assert.equal(await reports.locator('a[aria-current="page"]').textContent(),'Sales & Invoices');
  await reports.locator(':scope > summary').click();assert(!await reports.evaluate(n=>n.open));
  const before=page.url();await reports.locator(':scope > summary').click();assert.equal(page.url(),before,'Clicking Reports expands submenus without opening cards');
  assert.equal(await reports.locator('.sf-nav-children > a').count(),6);
  await reports.getByRole('link',{name:'Business Summary',exact:true}).click();
  assert(new URL(page.url()).pathname==='/Reports/Activity');
  assert.equal(await page.locator('.sf-list-caption h1').textContent(),'Shop summary');
  assert.equal(await page.locator('.sf-shop-nav [aria-current="page"]').count(),1);
  assert.equal(await reports.locator('a[aria-current="page"]').textContent(),'Business Summary');
  assert.equal(await page.locator('.reports-page, .report-card').count(),0);
  await reports.getByRole('link',{name:'Inventory Overview',exact:true}).click();
  assert(new URL(page.url()).pathname==='/Inventory');
  assert.equal(await reports.locator('a[aria-current="page"]').textContent(),'Inventory Overview');
  assert.equal(await page.locator('.sf-shop-nav [aria-current="page"]').count(),1);
  await reports.locator('.sf-report-more > summary').click();
  await reports.getByRole('link',{name:'Completed Repairs',exact:true}).click();
  assert.equal(new URL(page.url()).searchParams.get('status'),'DELIVERED');
  assert(await reports.locator('.sf-report-more').evaluate(n=>n.open));
  assert.equal(await reports.locator('a[aria-current="page"]').textContent(),'Completed Repairs');
  assert((await page.locator('#app-breadcrumb').textContent()).includes('Reports'));
  await page.goto('http://stockflow.test/Reports/Activity?fromReports=1');
  await page.screenshot({path:path.join(output,'reports-sidebar.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);
  await page.locator('#sidebar-toggle').click();assert(await reports.getByRole('link',{name:'Business Summary',exact:true}).isVisible());
  console.log('PASS: Reports expands without navigation; direct submenu reports, one active link, preserved status filters, More reports, breadcrumbs and mobile navigation.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
