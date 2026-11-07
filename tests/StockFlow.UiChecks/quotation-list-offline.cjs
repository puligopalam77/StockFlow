// Actual Razor/assets and real saved PostgreSQL list records. HTTP transport is
// controlled for browser races/errors; database/API authorization tested separately.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/quotation-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1.5}),errors=[],requests=[];
  page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(15000);
  const records=JSON.parse(fs.readFileSync(path.join(fixtures,'list-records.json'),'utf8')).rows;
  assert(records.length>25);for(const state of ['DRAFT','SENT','ACCEPTED','REJECTED','EXPIRED','CONVERTED','CANCELLED'])assert(records.some(r=>r.status===state));
  let fail=false,empty=false,recordLimit=null,permissions={create:true,edit:true,status:true,convert:true,print:true,export:true,manual:true};
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url()),p=url.pathname,json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/quotations/options')return json({today:'2026-11-04',pricesIncludeTax:false,permissions});
   if(p==='/api/quotations'){
    requests.push(url.search);const term=(url.searchParams.get('term')||'').toLowerCase(),status=url.searchParams.get('status'),from=url.searchParams.get('from'),to=url.searchParams.get('to'),size=Number(url.searchParams.get('pageSize')),n=Number(url.searchParams.get('page'));
    assert([10,25,50,100].includes(size));if(fail)return route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({message:'Synthetic list outage'})});
    let rows=empty?[]:records.slice(0,recordLimit??records.length).filter(r=>(!term||[r.quotationNumber,r.customerName,r.customerMobile].some(v=>v?.toLowerCase().includes(term)))&&(!status||r.status===status)&&(!from||r.documentDate.slice(0,10)>=from)&&(!to||r.documentDate.slice(0,10)<=to));
    if(term==='slow'){rows=records;await new Promise(resolve=>setTimeout(resolve,1000));}
    return json({rows:rows.slice((n-1)*size,n*size),total:rows.length,page:n,pageSize:size});
   }
   if(p==='/Quotations')return route.fulfill({contentType:'text/html',path:path.join(fixtures,'Quotations.html')});
   if(p.startsWith('/Quotations/')||p.startsWith('/Sales/'))return route.fulfill({contentType:'text/html',body:'<!doctype html><title>Existing route target</title><p>Navigation target only; full workflow covered separately.</p>'});
   if(p.startsWith('/api/'))return json({});
   const file=path.join(root,'src/StockFlow.Web/wwwroot',decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({body:''});
  });
  const idle=()=>page.waitForFunction(()=>document.querySelector('#quote-pagination')?.getAttribute('aria-busy')==='false'&&document.querySelector('#quote-list-status')?.textContent.includes('saved quotation'));
  const search=async term=>{await page.locator('#quote-search').fill(term);await page.waitForTimeout(350);await idle();};
  const reset=async()=>{await page.locator('#quote-reset').click();await idle();};
  await page.goto('http://stockflow.test/Quotations');await idle();
  assert.equal(await page.locator('#quotation-rows tr').count(),10);assert.equal(await page.locator('#quote-new').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(255, 0, 127)');
  assert((await page.locator('#quote-count').textContent()).includes('of '+records.length));assert((await page.locator('#quotation-rows').textContent()).includes(records[0].customerMobile));
  assert.equal(await page.locator('#quotation-rows td.quote-saved-total').first().textContent(),new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(records[0].total));
  const before=requests.length;await page.locator('#quote-search').fill('Q');await page.locator('#quote-search').fill('QT');await page.locator('#quote-search').fill(records[0].quotationNumber);await page.waitForTimeout(350);await idle();
  assert.equal(requests.length,before+1,'Search is debounced');assert.equal(await page.locator('#quotation-rows tr').count(),1);
  await search(records[0].customerMobile);assert((await page.locator('#quote-count').textContent()).includes('of '+records.length));
  await search(records[0].customerName);assert.equal(await page.locator('#quotation-rows tr').count(),10);await reset();
  for(const state of ['DRAFT','SENT','ACCEPTED','REJECTED','EXPIRED','CONVERTED','CANCELLED']){
   await page.locator('#quote-status').selectOption(state);await idle();
   assert.deepEqual(await page.locator('.quote-status').allTextContents(),Array(await page.locator('.quote-status').count()).fill(state[0]+state.slice(1).toLowerCase()));
   const button=page.locator('.quote-row-trigger').first();await button.focus();await page.keyboard.press('Enter');
   const menu=page.locator('.quote-row-menu:popover-open');await menu.waitFor({state:'visible'});
   assert.equal(await menu.getByText('Convert to Sale',{exact:true}).count(),state==='ACCEPTED'?1:0);
   assert.equal(await menu.getByText('Edit',{exact:true}).count(),['CONVERTED','CANCELLED'].includes(state)?0:1);
   assert.equal(await menu.getByText('Print / Save PDF',{exact:true}).count(),1);
   await page.keyboard.press('Escape');assert.equal(await page.locator('.quote-row-menu:popover-open').count(),0);assert(await button.evaluate(e=>e===document.activeElement));
  }
  await reset();
  for(const size of [25,50,100,10]){
   await page.locator('#quote-pagination select').selectOption(String(size));await idle();assert.equal(await page.locator('#quotation-rows tr').count(),Math.min(size,records.length));
  }
  await page.getByRole('button',{name:'Next page',exact:true}).click();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'2');
  assert((await page.locator('#quote-count').textContent()).includes('Showing 11–20'));
  const detailHref=await page.locator('#quotation-rows td:first-child a').first().getAttribute('href');await page.locator('#quotation-rows td:first-child a').first().click();assert(page.url().endsWith(detailHref));await page.goBack();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'2');
  await page.reload();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'2');
  await reset();const day=records.find(r=>r.status==='SENT').documentDate.slice(0,10);
  await page.locator('#quote-status').selectOption('SENT');await idle();await page.locator('#quote-from').fill(day);await page.locator('#quote-from').dispatchEvent('change');await idle();await page.locator('#quote-to').fill(day);await page.locator('#quote-to').dispatchEvent('change');await idle();
  const csv=new URL(await page.locator('#quote-export').getAttribute('href'),'http://stockflow.test');assert.equal(csv.searchParams.get('from'),day);assert.equal(csv.searchParams.get('status'),'SENT');assert(!csv.searchParams.has('page'));
  const beforeInvalid=requests.length;await page.locator('#quote-from').fill('2099-01-01');await page.locator('#quote-from').dispatchEvent('change');assert(await page.locator('#quote-filter-error').isVisible());assert.equal(requests.length,beforeInvalid);assert.equal(await page.locator('#quote-export').getAttribute('href'),null);await reset();
  const slow=page.waitForRequest(r=>r.url().includes('term=slow'));await page.locator('#quote-search').fill('slow');await slow;await search('no-such-quotation');assert(await page.locator('#quote-list-empty').isVisible());await page.waitForTimeout(1100);assert.equal(await page.locator('#quotation-rows tr').count(),0);assert.equal(await page.locator('#quote-empty-title').textContent(),'No quotations match your filters');await reset();
  fail=true;await page.locator('#quote-status').selectOption('SENT');await page.locator('#quote-list-error').waitFor({state:'visible'});assert(await page.locator('#quote-list-empty').isHidden());assert.equal(await page.locator('#quote-export').getAttribute('href'),null);
  fail=false;await page.locator('#quote-retry').click();await idle();await reset();
  // Page correction when the authorized dataset shrinks.
  await page.getByRole('button',{name:'Last page',exact:true}).click();await idle();empty=true;await page.reload();await idle();assert.equal(new URL(page.url()).searchParams.get('page'),'1');assert.equal(await page.locator('#quote-empty-title').textContent(),'No quotations yet');assert(await page.locator('#quote-pagination').isHidden());
  empty=false;await page.reload();await idle();
  for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(400);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No page overflow '+width);
   await page.locator('#quote-new').click({trial:true});await page.locator('#quote-export').click({trial:true});
   const region=page.locator('#quote-list-table');await region.evaluate(e=>e.scrollLeft=e.scrollWidth);const trigger=page.locator('.quote-row-trigger').first();await trigger.click();await page.locator('.quote-row-menu:popover-open a').first().click({trial:true});await page.keyboard.press('Escape');
   await region.evaluate(e=>e.scrollLeft=0);await page.evaluate(()=>{document.activeElement?.blur();window.scrollTo({top:0,behavior:'instant'});});
   await page.screenshot({path:path.join(fixtures,'quotation-list-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'});console.log('PASS saved quotation list layout '+width+'x'+height);
  }
  await page.goto('http://stockflow.test/Quotations?page=2&pageSize=10');await idle();
  for(const [width,height]of [[1920,1080],[390,844]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(400);
   await page.evaluate(()=>{document.activeElement?.blur();window.scrollTo({top:0,behavior:'instant'});});
   await page.screenshot({path:path.join(fixtures,'quotation-list-statuses-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'});
  }
  for(const total of [0,1,10,11,25]){
   recordLimit=total;await page.goto('http://stockflow.test/Quotations');await idle();
   assert.equal(await page.locator('#quote-pagination').isVisible(),total>10,'Quotation threshold '+total);
   assert.equal(await page.locator('.quote-list-footer').isVisible(),total>10,'No empty footer '+total);
   const exportLink=page.locator('#quote-export');
   assert(await exportLink.isVisible(),'Export remains visible when disabled');
   assert.equal(await exportLink.getAttribute('aria-disabled'),String(total===0));
   assert.equal((await exportLink.getAttribute('href'))!==null,total>0);
   if(total===0){const beforeClick=page.url();await exportLink.evaluate(e=>e.click());assert.equal(page.url(),beforeClick,'Disabled export does not navigate');}
  }
  recordLimit=11;await page.goto('http://stockflow.test/Quotations?page=2');await idle();assert.equal(await page.locator('#quotation-rows tr').count(),1);assert(await page.locator('#quote-pagination').isVisible(),'Last page uses total, not one visible row');
  const shared=await page.evaluate(()=>{
   const results=[];const host=document.createElement('div');document.querySelector('main').append(host);const pager=StockFlowPagination.create({host});
   for(const total of [0,1,10,11,25]){pager.set({total,size:25,page:1});results.push({total,visible:getComputedStyle(host).display!=='none'});}
   pager.dispose();return results;
  });assert.deepEqual(shared,[0,1,10,11,25].map(total=>({total,visible:total>10})));console.log('PASS shared application threshold 0/1/10 hidden, 11/25 visible, independent of page size and last-page row count');
  recordLimit=null;
  permissions={create:false,edit:false,status:false,convert:false,print:false,export:false,manual:false};await page.goto('http://stockflow.test/Quotations?status=ACCEPTED');await idle();assert(await page.locator('#quote-new').isHidden());assert(await page.locator('#quote-export').isHidden());await page.locator('.quote-row-trigger').first().click();assert.deepEqual(await page.locator('.quote-row-menu:popover-open a').allTextContents(),['View']);
  assert.deepEqual(errors,[]);console.log('PASS list: real saved multi-status rows, saved mobile/totals, debounce, all status filters, menu keyboard/Escape, sizes 10/25/50/100, paging/back/reload, dates/export/reset, stale/error/empty states, page correction, restricted actions. Browser HTTP controlled; DB/API tests separate.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
