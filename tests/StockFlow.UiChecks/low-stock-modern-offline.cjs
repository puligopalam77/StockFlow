// Actual Razor/assets, replayed real PostgreSQL shortage records. HTTP controlled.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/low-stock-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1.5});page.setDefaultTimeout(15000);
 const records=JSON.parse(fs.readFileSync(path.join(fixtures,'records.json'),'utf8')).rows,requests=[],errors=[];
 let fail=false,empty=false,limit=null;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Inventory/LowStock')return route.fulfill({path:path.join(fixtures,'LowStock.html'),contentType:'text/html'});
  if(p==='/api/notifications')return json({unread:0,items:[]});
  if(p==='/api/inventory'){
   assert.equal(req.method(),'GET');requests.push(url.search);if(fail)return json({message:'Synthetic low-stock outage'},503);
   const term=(url.searchParams.get('term')||'').toLowerCase(),status=url.searchParams.get('stockStatus'),page=Number(url.searchParams.get('page')),size=Number(url.searchParams.get('pageSize'));
   assert.equal(url.searchParams.get('lowStockOnly'),'true');assert.equal(url.searchParams.get('paged'),'true');assert([10,25,50,100].includes(size));
   let rows=empty?[]:records.slice(0,limit??records.length).filter(r=>(!term||[r.sku,r.productName,r.attributeSummary].some(s=>s?.toLowerCase().includes(term)))&&(!status||r.stockStatus===status));
   if(term==='slow'){rows=records;await new Promise(resolve=>setTimeout(resolve,1000));}
   return json({rows:rows.slice((page-1)*size,page*size),total:rows.length,page,pageSize:size,summary:{lowStockVariants:rows.length,outOfStock:rows.filter(r=>r.currentStock===0).length,unitsToReplenish:rows.reduce((n,r)=>n+r.quantityNeeded,0)}});
  }
  if(p.startsWith('/api/'))return json({});
  const file=path.join(assets,decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({body:''});
 });
 const idle=()=>page.waitForFunction(()=>document.querySelector('#low-stock-workspace')?.getAttribute('aria-busy')==='false'&&document.querySelector('#low-stock-summary').textContent.includes('matching shortage'));
 const reset=async()=>{await page.locator('#low-stock-reset').click();await idle();};
 await page.goto('http://stockflow.test/Inventory/LowStock');await idle();
 const helpButton=page.getByRole('button',{name:'How Low Stock Works',exact:true}),help=page.locator('#low-stock-help');
 assert((await page.locator('.low-stock-location').textContent()).trim().length>10,'Location rendered from authorized scope');
 assert.equal(await page.locator('.low-stock-metrics>.card>.sf-icon').count(),3);
 await helpButton.focus();await page.keyboard.press('Enter');assert(await help.isVisible());await page.waitForFunction(()=>document.activeElement===document.querySelector('#low-stock-help'));await page.keyboard.press('Escape');assert(await help.isHidden());assert(await helpButton.evaluate(e=>document.activeElement===e));
 await page.keyboard.press('Space');assert(await help.isVisible());await help.getByRole('button',{name:'Close help'}).click();assert(await help.isHidden());
 await helpButton.click();await page.locator('.low-stock-heading h1').click();assert(await help.isHidden());
 assert.equal(await page.locator('#low-stock-search-help').count(),0);
 const cardHeight=await page.locator('.low-stock-metrics .card').first().evaluate(e=>e.getBoundingClientRect().height);assert(cardHeight>=80&&cardHeight<=100,'Compact desktop metric '+cardHeight);
 assert.equal(await page.locator('#low-stock-results tr').count(),10);assert.equal(await page.locator('#low-stock-total').textContent(),String(records.length));
 assert.equal(await page.locator('#low-stock-required').textContent(),records.reduce((n,r)=>n+r.quantityNeeded,0).toLocaleString('en-IN'));
 const sample=records[0];assert.equal(await page.locator('#low-stock-results tr').first().locator('td').nth(4).textContent(),String(sample.quantityNeeded));
 const before=requests.length;await page.locator('#low-stock-term').fill('LS');await page.locator('#low-stock-term').fill(sample.sku);await page.waitForTimeout(350);await idle();assert.equal(requests.length,before+1);assert.equal(await page.locator('#low-stock-results tr').count(),1);
 await page.locator('#low-stock-term').press('Enter');await idle();await reset();
 for(const status of ['OUT','LOW']){await page.locator('#low-stock-status').selectOption(status);await idle();assert.equal(await page.locator('#low-stock-total').textContent(),String(records.filter(r=>r.stockStatus===status).length));}
 await reset();
 const trigger=page.locator('.low-stock-action').first();await trigger.focus();await page.keyboard.press('Enter');let menu=page.locator('.low-stock-menu:popover-open');await menu.waitFor({state:'visible'});
 assert.equal(await menu.getByText('View Stock',{exact:true}).getAttribute('href'),'/Inventory?term='+encodeURIComponent(records[0].sku));assert.equal(await menu.getByText('Receive Stock',{exact:true}).getAttribute('href'),'/Purchases/Create');assert.equal(await menu.getByText('Review Transfers',{exact:true}).getAttribute('href'),'/Transfers');
 await page.keyboard.press('Escape');assert(await trigger.evaluate(e=>document.activeElement===e));
 for(const size of [25,50,100,10]){await page.locator('#low-stock-pagination select').selectOption(String(size));await idle();assert.equal(await page.locator('#low-stock-results tr').count(),Math.min(size,records.length));}
 await page.getByRole('button',{name:'Next page',exact:true}).click();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'2');await page.reload();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'2');await reset();
 const slow=page.waitForRequest(r=>r.url().includes('term=slow'));await page.locator('#low-stock-term').fill('slow');await slow;await page.locator('#low-stock-term').fill('not-a-match');await page.waitForTimeout(350);await idle();await page.waitForTimeout(1000);assert.equal(await page.locator('#low-stock-empty-title').textContent(),'No items match your filters');assert(await page.locator('#low-stock-table').isHidden());await reset();
 fail=true;await page.locator('#low-stock-status').selectOption('LOW');await page.locator('#low-stock-message').waitFor({state:'visible'});assert.equal(await page.locator('#low-stock-total').textContent(),'—');assert(await page.locator('#low-stock-empty').isHidden());fail=false;await page.locator('#low-stock-retry').click();await idle();await reset();
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.waitForTimeout(400);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No page overflow '+width);
  await helpButton.click();await page.waitForFunction(()=>document.activeElement===document.querySelector('#low-stock-help'));const helpBounds=await help.boundingBox();assert(helpBounds.x>=0&&helpBounds.x+helpBounds.width<=width+1&&helpBounds.y>=0&&helpBounds.y+helpBounds.height<=height+1,'Help fits viewport');await page.keyboard.press('Escape');
  const region=page.locator('#low-stock-table');await region.evaluate(e=>e.scrollLeft=e.scrollWidth);await page.locator('.low-stock-action').first().click();await page.locator('.low-stock-menu:popover-open a').first().click({trial:true});await page.keyboard.press('Escape');await region.evaluate(e=>e.scrollLeft=0);
  await page.evaluate(()=>{document.activeElement?.blur();window.scrollTo({top:0,behavior:'instant'});});
  await page.screenshot({path:path.join(fixtures,'low-stock-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'});console.log('PASS Low Stock viewport '+width+'x'+height);
 }
 for(const count of [0,1,10,11]){limit=count;await page.goto('http://stockflow.test/Inventory/LowStock');await idle();assert.equal(await page.locator('#low-stock-footer').isVisible(),count>10);if(!count)assert.equal(await page.locator('#low-stock-empty-title').textContent(),'No low-stock items');}
 limit=null;await page.goto('http://stockflow.test/Inventory/LowStock');await idle();
 await page.evaluate(()=>{const root=document.querySelector('#low-stock-workspace');root.dataset.canReceive='false';root.dataset.canTransfer='false';});await page.locator('#low-stock-status').selectOption('LOW');await idle();await page.locator('.low-stock-action').first().click();assert.deepEqual(await page.locator('.low-stock-menu:popover-open a').allTextContents(),['View Stock']);
 assert.deepEqual(errors,[]);console.log('PASS Low Stock: saved real rows, summary/quantity parity, filters/debounce/Enter, paging/reload/reset, read-only links, keyboard menus, stale/error/empty handling, 0/1/10/11 threshold, permissions and 10 viewports. HTTP controlled; database checks separate.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
