const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),output=path.join(root,'tests/bin/list-screen-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1920,height:1080},acceptDownloads:true}),errors=[],requests=[];page.on('pageerror',e=>errors.push(e.message));
 const all=Array.from({length:29},(_,i)=>({saleId:i+1,invoiceNumber:'INV-2026-'+String(29-i).padStart(6,'0'),saleDate:'2026-10-10T04:21:40Z',customerName:i===0?'=FORMULA':i%2?'PULIKONDA GOPALAM':null,customerMobile:i%2?'9876543210':null,itemCount:3,lineCount:2,totalAmount:14000,collectedAmount:i%4?9000:14000,outstandingAmount:i%4?5000:0,paymentDueDate:'2026-10-05',paymentMode:['CREDIT_CARD','UPI','CARD','MIXED'][i%4],paymentStatus:i%4?'PARTIAL':'PAID',invoiceStatus:i%3?'COMPLETED':'CANCELLED',status:i%3?'COMPLETED':'CANCELLED',returnedAmount:0,refundedAmount:0}));let fail=false,delay=false;
 const sum=rows=>{const completed=rows.filter(r=>r.status==='COMPLETED');return {count:rows.length,invoiceCount:completed.length,netSales:completed.reduce((s,r)=>s+r.totalAmount-r.returnedAmount,0),amountCollected:completed.reduce((s,r)=>s+r.collectedAmount,0),outstandingDues:completed.reduce((s,r)=>s+r.outstandingAmount,0)};};
 await page.route('**/*',async route=>{const u=new URL(route.request().url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(u.pathname==='/Sales')return route.fulfill({path:path.join(output,'Sales.html'),contentType:'text/html'});
  if(u.pathname==='/api/sales/list'){
   requests.push(u.searchParams);if(delay)await new Promise(r=>setTimeout(r,350));
   const q=u.searchParams,filtered=all.filter(r=>(!q.get('status')||r.status===q.get('status')||(['OUTSTANDING','OVERDUE'].includes(q.get('status'))&&r.status==='COMPLETED'&&r.outstandingAmount>0))&&(!q.get('term')||r.invoiceNumber.includes(q.get('term'))||q.get('term')==='HLM-0001-01')&&(!q.get('paymentStatus')||r.paymentStatus===q.get('paymentStatus'))&&(!q.get('paymentMethod')||r.paymentMode===q.get('paymentMethod'))),start=Number(q.get('offset'));
   if(fail)return route.fulfill({status:500,contentType:'application/json',body:'{"message":"List unavailable"}'});
   return json({rows:filtered.slice(start,start+Number(q.get('limit'))),summary:sum(filtered),timeZoneId:'Asia/Kolkata'});
  }
  if(u.pathname.startsWith('/api/customers/search'))return json([{customerId:11,customerName:'Selected customer',mobileNumber:'9876543210'}]);
  if(u.pathname==='/api/notifications')return json({unread:0,items:[]});
  if(u.pathname.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 async function ready(){await page.waitForFunction(()=>document.querySelector('.sf-pagination')?.getAttribute('aria-busy')==='false'&&document.querySelectorAll('#sales-rows tr').length>0);}
 async function submit(){await page.locator('#sales-filter button[type=submit]').click();await ready();}
 await page.goto('http://stockflow.test/Sales?from=2026-10-01&to=2026-10-10');await ready();
 assert.equal(await page.locator('.sales-summary-card').count(),4);assert.equal(await page.locator('#sales-rows tr').count(),10);assert((await page.locator('#sales-showing').textContent()).includes('of 29'));
 assert.equal(await page.locator('h1').textContent(),'Customer Bills');assert.equal(await page.locator('#sale-search').getAttribute('placeholder'),'Search invoice no., customer, mobile, SKU or product...');
 assert((await page.locator('#sales-summary-total').textContent()).includes('2,66,000'));assert((await page.locator('#sales-summary-count').textContent()).includes('19'));
 assert.equal(await page.locator('.sales-list-table thead th').count(),11);assert((await page.locator('#sales-rows').textContent()).includes('Credit Card'));assert((await page.locator('#sales-rows').textContent()).includes('Card'));
 assert((await page.locator('.sales-invoice').first().getAttribute('href')).startsWith('/Sales/Details/'));assert((await page.locator('a[aria-label^="Print invoice"]').first().getAttribute('href')).endsWith('?print=1'));
 assert(await page.locator('a[aria-label^="Collect payment"]').first().isVisible());assert(await page.locator('a[aria-label^="Return / refund"]').first().isVisible());
 const dimensions=await page.locator('.sales-row-actions a').first().evaluate(n=>{const s=getComputedStyle(n),svg=n.querySelector('svg');return [s.width,s.height,getComputedStyle(svg).width,getComputedStyle(n.parentElement).gap];});assert.deepEqual(dimensions,['26px','26px','13px','4px']);
 await page.getByRole('button',{name:'Last page',exact:true}).click();await ready();assert.equal(await page.locator('#sales-rows tr').count(),9);assert(await page.locator('#sales-next').isDisabled());await page.getByRole('button',{name:'First page',exact:true}).click();await ready();
 await page.locator('#sales-select-all').check();assert.equal(await page.locator('#sales-rows input:checked').count(),10);await page.locator('#sales-select-all').uncheck();
 await page.locator('#sales-rows input').first().check();let downloadPromise=page.waitForEvent('download');await page.locator('#sales-export').click();let download=await downloadPromise,content=fs.readFileSync(await download.path(),'utf8');assert.equal(content.split('\r\n').length,2);assert(content.includes("'=FORMULA"));assert(content.includes('Balance Due INR'));
 await page.locator('#sales-rows input').first().uncheck();downloadPromise=page.waitForEvent('download');await page.locator('#sales-export').click();download=await downloadPromise;content=fs.readFileSync(await download.path(),'utf8');assert.equal(content.split('\r\n').length,30,'Export includes all filtered pages');
 await page.locator('#sales-more-filters > summary').click();await page.locator('#sales-status').selectOption('COMPLETED');await page.locator('#sales-payment-status').selectOption('PARTIAL');await page.locator('#sales-payment-method').selectOption('UPI');await page.locator('#sale-search').fill(' HLM-0001-01 ');await submit();
 assert.equal(requests.at(-1).get('term'),'HLM-0001-01');assert.equal(requests.at(-1).get('paymentStatus'),'PARTIAL');assert.equal(requests.at(-1).get('paymentMethod'),'UPI');assert(await page.locator('.sf-pagination').isHidden(),'Pagination hidden for at most ten matches');
 assert((await page.locator('#sales-rows').textContent()).includes('UPI'));assert.equal(await page.locator('#sales-summary-count').textContent(),String(all.filter(r=>r.status==='COMPLETED'&&r.paymentMode==='UPI').length));
 await page.locator('#sales-customer-search').fill('Selected');await page.waitForSelector('#customer-filter option[value="11"]',{state:'attached'});await page.locator('#customer-filter').selectOption('11');await submit();assert.equal(requests.at(-1).get('customerId'),'11');
 await page.locator('.sales-sort').filter({hasText:'Bill Total'}).click();await ready();assert.equal(requests.at(-1).get('sort'),'total');assert.equal(requests.at(-1).get('ascending'),'true');
 await page.locator('#sales-reset').click();await ready();assert.equal(await page.locator('#sales-from').inputValue(),'');assert.equal(await page.locator('#sales-status').inputValue(),'');assert.equal(await page.locator('#sales-payment-status').inputValue(),'');assert.equal(await page.locator('#sales-more-filters').evaluate(n=>n.open),false);
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No page overflow at '+width);assert.equal(await page.locator('.sales-summary-card:visible').count(),4);await page.screenshot({path:path.join(output,'customer-bills-'+width+'x'+height+'.png'),fullPage:true});
 }
 await page.setViewportSize({width:1366,height:768});await page.locator('#sales-range').selectOption('YESTERDAY');assert.equal(await page.locator('#sales-from').inputValue(),await page.locator('#sales-to').inputValue());await page.locator('#sales-range').selectOption('WEEK');assert(await page.locator('#sales-from').inputValue()<await page.locator('#sales-to').inputValue());
 await page.locator('#sales-from').fill('2026-10-11');await page.locator('#sales-to').fill('2026-10-01');const before=requests.length;await submit();assert.equal(requests.length,before,'Invalid dates cannot reach the API');
 await page.locator('#sales-reset').click();await ready();fail=true;await submit();assert.equal(await page.locator('#sale-message').textContent(),'List unavailable');assert(await page.locator('#sales-export').isDisabled());
 assert.deepEqual(errors,[]);console.log('PASS: Customer Bills cards, combined filters, historical method labels, shared pagination threshold, full/selected export, compact computed dimensions, actions, invalid dates, API errors and all nine viewports.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
