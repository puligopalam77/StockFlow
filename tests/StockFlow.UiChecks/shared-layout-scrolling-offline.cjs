// Static layout smoke checks use rendered Razor HTML and current production CSS.
// Application scripts are omitted here; functional form/popup checks live in
// page-scrolling-offline.cjs and the existing supplier/product UI suites.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const fixtures=[
 ['Inventory','variant-ui-fixtures/Inventory.html'],['Transfers','variant-ui-fixtures/Transfers.html'],
 ['Purchases Create','variant-ui-fixtures/Purchases-Create.html'],['Purchase Details','variant-ui-fixtures/Purchases-Details-1.html'],['Purchase Order','variant-ui-fixtures/Purchases-Order.html'],
 ['Sales Create','variant-ui-fixtures/Sales-Create.html'],['Billing','billing-ui-fixtures/Billing.html'],['Invoice','billing-ui-fixtures/Invoice.html'],
 ['Receiving','quick-supplier-ui-fixtures/Receiving.html'],['Reports Stores','variant-ui-fixtures/Reports-Stores.html'],
 ['Admin Stores','variant-ui-fixtures/Admin-Stores.html'],['Admin Users','security-ui-fixtures/Users.html'],['Admin Roles','security-ui-fixtures/Roles.html'],['Audit Logs','security-ui-fixtures/AuditLogs.html'],
 ['Shop Settings','business-profile-phase2-fixtures/Settings.html'],['Attribute Settings','category-attributes-fixtures/Settings-BIKE_ACCESSORIES.html']
];
(async()=>{const fixtureBodies=await Promise.all(fixtures.map(async fixture=>{const file=path.join(root,'tests/bin',fixture[1]);if(fs.existsSync(file))return fs.readFileSync(file,'utf8');const route={'Shop Settings':'/Settings/BusinessProfile','Attribute Settings':'/Categories/Attributes'}[fixture[0]];assert(route&&process.env.STOCKFLOW_SCROLL_AUDIT_ORIGIN==='http://127.0.0.1:55863','Missing fixture: '+file);const response=await fetch(process.env.STOCKFLOW_SCROLL_AUDIT_ORIGIN+route);assert.equal(response.status,200,'Loopback synthetic fixture: '+route);return response.text();}));const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({reducedMotion:'reduce'});page.setDefaultTimeout(7000);
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname;if(p.startsWith('/scroll-shell/')){const fixture=fixtures[Number(p.split('/').pop())];return route.fulfill({contentType:'text/html',body:fixtureBodies[Number(p.split('/').pop())].replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'')});}const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});});
 for(const width of [1366,390]){await page.setViewportSize({width,height:560});for(let index=0;index<fixtures.length;index++){
  await page.goto('http://localhost:5220/scroll-shell/'+index);
  assert(!['hidden','clip'].includes(await page.evaluate(()=>getComputedStyle(document.body).overflowY)),fixtures[index][0]+' permits document scrolling');
  await page.evaluate(()=>{const main=document.querySelector('.sf-main')||document.querySelector('main')||document.body,section=document.createElement('section');section.style.height='1400px';section.textContent='Long content fixture';const end=document.createElement('p');end.id='scroll-end';end.textContent='End of page';main.append(section,end);});
  await page.mouse.move(12,480);for(let i=0;i<8;i++)await page.mouse.wheel(0,1000);await page.waitForFunction(()=>scrollY>0&&innerHeight+scrollY>=document.documentElement.scrollHeight-3);assert((await page.locator('#scroll-end').boundingBox()).y<560,fixtures[index][0]+' bottom reachable');
 }console.log('PASS: 16 operational, administration, reporting and settings layouts allow wheel scrolling to long-content bottoms at '+width+'px. Static Razor/CSS smoke check.');}
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1});
