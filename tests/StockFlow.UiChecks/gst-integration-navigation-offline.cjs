// Actual Razor/assets, controlled HTTP; no provider or application writes.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/gst-ui-fixtures'),out=path.join(root,'tests/bin/gst-integration-navigation');
fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({deviceScaleFactor:1.5});page.setDefaultTimeout(12000);const errors=[],writes=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{const req=route.request(),p=new URL(req.url()).pathname,json=b=>route.fulfill({contentType:'application/json',body:JSON.stringify(b)});if(req.method()!=='GET')writes.push(p);
  if(p==='/Admin/Gst'||p==='/EWayBills/Provider')return route.fulfill({path:path.join(fixtures,p==='/Admin/Gst'?'settings.html':'provider.html'),contentType:'text/html'});
  if(p==='/api/gst/settings/states')return json({'27':'Maharashtra'});
  if(p==='/api/gst/settings')return json({available:true,today:'2026-11-05',businesses:[],registrations:[],mappings:[],taxes:[],shops:[{storeId:1,storeName:'Synthetic shop'}],products:[],history:[],company:{companyName:'Synthetic company',address:'Synthetic street',pinCode:'400001',state:'Maharashtra'}});
  if(p==='/api/ewaybills/provider')return json({name:'No authorized adapter',status:'Blocked: authorized integration contract and credentials required',sandboxVerified:false,productionVerified:false,supportedOperations:[],storesCredentials:false});
  if(p==='/api/notifications')return json({items:[],unread:0});if(p.startsWith('/api/'))return json([]);
  const file=path.join(assets,decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({body:''});
 });
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Admin/Gst');
  const start=page.getByRole('button',{name:'Start GST Setup',exact:true});await start.waitFor();
  assert.equal(await page.locator('#gst-settings a[href="/EWayBills/Provider"]').count(),0);assert.equal(await page.locator('#gst-title').textContent(),'GST Setup');
  await page.waitForFunction(()=>getComputedStyle([...document.querySelectorAll('#gst-panel button')].find(e=>e.textContent==='Start GST Setup')).backgroundColor==='rgb(255, 0, 127)');
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await start.scrollIntoViewIfNeeded();
  assert(await start.evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}));
  await page.screenshot({path:path.join(out,'gst-'+width+'x'+height+'.png')});await start.click();await page.locator('#gst-legalName').waitFor();
  await page.goto('http://stockflow.test/Admin/Gst');await page.getByRole('button',{name:'Use Existing Company Details',exact:true}).click();await page.locator('#gst-preview[open]').waitFor();
  assert((await page.locator('#gst-preview-body').textContent()).includes('Synthetic company'));await page.locator('#gst-preview-cancel').click();
  await page.goto('http://stockflow.test/Admin/Gst');await start.waitFor();
  const side=page.locator('#app-sidebar');if(await side.evaluate(e=>e.inert))await page.locator('#sidebar-toggle').click();
  const admin=page.locator('.sf-shop-nav [data-nav-group="Setup"]');if(!await admin.evaluate(e=>e.open))await admin.locator(':scope>summary').click();
  const integrations=admin.locator('[data-nav-group="Integrations"]');assert.equal(await integrations.count(),1);
  if(!await integrations.evaluate(e=>e.open)){await integrations.locator(':scope>summary').focus();await page.keyboard.press('Enter');}
  assert.equal(await page.locator('.sf-shop-nav a[href="/EWayBills/Provider"]').count(),1);await integrations.getByRole('link',{name:'E-Way Bill Provider',exact:true}).click();
  await page.waitForURL('**/EWayBills/Provider');await page.locator('#eway-provider-status').getByText(/Blocked:/).waitFor();
  assert.equal(await page.locator('h1').textContent(),'E-Way Bill Provider');assert.equal(await page.locator('#eway-workspace input').count(),0);
  assert.equal(await page.locator('.sf-shop-nav a[href="/EWayBills/Provider"]').getAttribute('aria-current'),'page');
  assert(await page.locator('.sf-shop-nav [data-nav-group="Setup"]').evaluate(e=>e.open));assert(await page.locator('.sf-shop-nav [data-nav-group="Integrations"]').evaluate(e=>e.open));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  if(width<=991){assert(await side.evaluate(e=>e.inert));await page.locator('#sidebar-toggle').click();await page.keyboard.press('Escape');assert(await side.evaluate(e=>e.inert));}
  const back=page.locator('#eway-workspace a[href="/EWayBills"]');await back.scrollIntoViewIfNeeded();assert(await back.evaluate(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),'Provider return link unobstructed');await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(out,'provider-'+width+'x'+height+'.png')});console.log('PASS GST / Integrations navigation '+width+'x'+height);
 }
 assert.equal(writes.length,0,'Navigation, Start and company-copy preview make no writes');assert.deepEqual(errors,[]);
 assert(fs.readFileSync(path.join(fixtures,'reports.html'),'utf8').includes('href="/EWayBills"'));
 console.log('PASS setup handlers, optional provider navigation, blocked readiness, keyboard/mobile and10 viewports. Controlled HTTP.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
