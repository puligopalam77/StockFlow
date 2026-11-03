const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/reports-ui-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1536,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  const data={canSeeStock:true,canSeeSales:true,financial:[{storeName:'Main store',locationName:'Main stock',salesValue:14000,collections:14000,creditOutstanding:0,invoiceCount:1,invoiceQuantity:1}],previousFinancial:[{salesValue:10000,collections:10000,creditOutstanding:0,invoiceCount:1,invoiceQuantity:1}],trend:[{day:'2026-10-10T00:00:00',sales:14000,collections:14000}],topProducts:[{productName:'Battery / DEFAULT',quantity:1,amount:14000}],currentStock:[{productName:'Battery',currentStock:17,minimumStock:2},{productName:'Cable',currentStock:15,minimumStock:2},{productName:'Device',currentStock:2,minimumStock:1}],recent:[{type:'Sale',reference:'INV-2026-000003',quantity:1,amount:14000,date:'2026-10-10T05:00:00Z',id:3},{type:'Receive',reference:'GRN-2026-000002',quantity:10,amount:null,date:'2026-10-10T00:15:00Z',id:2}]};let fail=false;
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/Reports/Activity')return route.fulfill({path:path.join(output,'Reports.html'),contentType:'text/html'});
   if(url.pathname==='/api/reports/activity')return route.fulfill({status:fail?500:200,contentType:'application/json',body:JSON.stringify(fail?{message:'Test report error'}:data)});
   if(url.pathname==='/api/branches/context')return route.fulfill({contentType:'application/json',body:JSON.stringify({canReviewStores:true,locations:[{storeId:1,locationId:1,storeName:'Main store',locationName:'Main stock'}],storeId:1,locationId:1})});
   if(url.pathname==='/api/notifications')return route.fulfill({contentType:'application/json',body:JSON.stringify({unread:0,items:[]})});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Reports/Activity?fromReports=1&from=2026-10-04&to=2026-10-10');await page.waitForSelector('#activity-results:not([hidden])');
  assert.equal(await page.locator('.summary-kpi').count(),5);assert.equal(await page.locator('.summary-panel').count(),6);
  assert((await page.locator('#summary-sales').textContent()).includes('14,000'));assert.equal(await page.locator('#compare-sales').textContent(),'40.0%');assert.equal(await page.locator('#summary-stock-qty').textContent(),'34');
  assert((await page.locator('#summary-low-stock').textContent()).includes('No low stock items'));assert.equal(await page.locator('#summary-trend polyline').count(),2);assert.equal(await page.locator('#summary-recent tr').count(),2);
  assert.equal(await page.locator('#summary-stockouts').textContent(),'0');
  assert((await page.locator('#summary-recent').textContent()).includes('INV-2026-000003'));assert.equal(new URL(page.url()).searchParams.get('fromReports'),'1');
  const link=await page.locator('[data-summary-link]').getAttribute('href');assert(link.includes('from=2026-10-04')&&link.includes('to=2026-10-10'));
  await page.screenshot({path:path.join(output,'shop-summary-desktop.png'),fullPage:true});
  const original=JSON.parse(JSON.stringify(data));
  data.currentStock[0].currentStock=0;data.currentStock[1].currentStock=-1;
  await page.locator('#activity-load').click();await page.waitForSelector('#activity-results:not([hidden])');
  assert.equal(await page.locator('#summary-stockouts').textContent(),'2');
  data.topProducts=Array.from({length:5},(_,i)=>({productName:'Sample product '+i,quantity:5,amount:10000-i*1000}));
  data.recent=Array.from({length:5},(_,i)=>({...original.recent[0],reference:'INV-2026-00000'+i}));
  data.currentStock=Array.from({length:5},(_,i)=>({productName:'Low stock product '+i,currentStock:1,minimumStock:2}));
  await page.locator('#activity-load').click();await page.waitForSelector('#activity-results:not([hidden])');
  for(const [width,height]of [[1920,948],[1536,864],[1366,768]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(300);
   const size=await page.evaluate(()=>({page:document.documentElement.scrollHeight,screen:innerHeight,blocks:[...document.querySelectorAll('.sf-main,.summary-kpis,.shop-summary-grid,.summary-panel')].map(n=>[n.className,n.getBoundingClientRect().height])}));
   assert(size.page<=size.screen+1,'Full summary must fit desktop '+width+'x'+height+': '+JSON.stringify(size));
   assert.equal(await page.locator('.summary-panel:visible').count(),6);
  }
  await page.screenshot({path:path.join(output,'shop-summary-single-screen.png'),fullPage:true});
  for(const [width,height]of [[1024,768],[820,1180],[768,1024],[600,960],[430,932],[390,844],[320,568],[844,390]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(100);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Page overflow at '+width+'x'+height);
   assert.equal(await page.locator('.summary-panel:visible').count(),6);
   assert(await page.locator('.summary-panel .table-responsive').evaluateAll(nodes=>nodes.every(n=>n.scrollWidth<=n.clientWidth+1)),'Table overflow at '+width);
   if(width<=600){assert.equal(await page.locator('#summary-recent td').first().getAttribute('data-label'),'Type');assert.equal(await page.locator('#activity-financial td').first().getAttribute('data-label'),'Store / location');}
  }
  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:path.join(output,'shop-summary-mobile.png'),fullPage:true});
  Object.assign(data,original);await page.locator('#activity-load').click();await page.waitForSelector('#activity-results:not([hidden])');
  await page.setViewportSize({width:1920,height:948});await page.waitForTimeout(150);
  const filled=await page.evaluate(()=>({bottom:document.querySelector('.shop-summary-grid').getBoundingClientRect().bottom,footer:document.querySelector('.sf-app-footer').getBoundingClientRect().top,page:document.documentElement.scrollHeight}));
  assert(filled.footer-filled.bottom<25,'Dashboard must fill available height even with few records');assert(filled.page<=949);
  await page.screenshot({path:path.join(output,'shop-summary-full-screen.png'),fullPage:true});

  for(const width of [768,390,320]){await page.setViewportSize({width,height:1000});await page.waitForTimeout(300);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Overflow at '+width);}
  await page.locator('.summary-date-picker summary').click();await page.locator('#activity-from').fill('2026-10-11');await page.locator('#activity-load').click();await page.waitForSelector('#activity-error:not([hidden])',{state:'attached'});assert((await page.locator('#activity-error').textContent()).includes('start date'));
  await page.locator('#activity-from').fill('2026-10-04');data.currentStock[0].currentStock=1;await page.locator('#activity-load').click();await page.waitForSelector('#activity-results:not([hidden])');assert.equal(await page.locator('#summary-stock-low').textContent(),'1');assert((await page.locator('#summary-low-stock').textContent()).includes('Battery'));
  data.financial=[];data.previousFinancial=[];data.currentStock=[];data.topProducts=[];data.recent=[];data.trend=[];await page.locator('#activity-load').click();await page.waitForSelector('#activity-results:not([hidden])');assert((await page.locator('#summary-products').textContent()).includes('No product sales'));assert.equal(await page.locator('#summary-stock-qty').textContent(),'0');
  fail=true;await page.locator('#activity-load').click();await page.waitForSelector('#activity-error:not([hidden])',{state:'attached'});assert.equal(await page.locator('#activity-error').textContent(),'Test report error');assert(await page.locator('#activity-results').isHidden());assert.deepEqual(errors,[]);
  console.log('PASS: five KPIs, real comparisons, six panels, trend, top products, current/low stock, activity, shared dates, responsive layouts, empty results and error handling.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});




