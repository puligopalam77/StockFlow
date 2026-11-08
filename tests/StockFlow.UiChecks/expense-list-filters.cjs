// Current rendered Razor and assets; controlled API responses exercise UI state.
// Financial totals and RLS are checked separately by --expense-list-checks.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/expense-list-fixtures');
const category='10000000-0000-0000-0000-000000000001',payee='20000000-0000-0000-0000-000000000001';
const records=Array.from({length:26},(_,i)=>({expense_id:'30000000-0000-0000-0000-'+String(i+1).padStart(12,'0'),category_id:category,payee_id:payee,category:'Repairs',payee:'Local contractor',incurred_date:'2026-11-07',amount:100+i,paid:i===0?20:0,description:i===0?'AC repair':'Shop overhead '+i,status:i===25?'DRAFT':'POSTED',receiptId:i===0?'40000000-0000-0000-0000-000000000001':null}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:900}});page.setDefaultTimeout(10000);
 const requests=[],errors=[],reportRequests=[];let emptyShop=false,allowPay=true,apiFailure=false,reportFailure=false;
 page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({contentType:'application/json',status,body:JSON.stringify(body)});
  if(p==='/Expenses'||/^\/Expenses\/(Create|Details|Report)$/.test(p)){
   const name=p==='/Expenses'?'Index':p.split('/')[2];let html=fs.readFileSync(path.join(fixtures,name+'.html'),'utf8');
   if(name==='Details')html=html.replace('data-id=""','data-id="'+url.searchParams.get('id')+'"');
   return route.fulfill({contentType:'text/html',body:html});
  }
  if(p==='/api/expenses/options')return json({categories:[{id:category,name:'Repairs'}],payees:[{id:payee,name:'Local contractor'}],taxRules:[],permissions:{CREATE:true,EDIT:true,POST:true,PAY:allowPay,ATTACH:true,REVERSE:true,CANCEL:true}});
  if(p==='/api/expenses'){
   const q=Object.fromEntries(url.searchParams);requests.push(q);if(apiFailure)return json({message:'List unavailable'},503);
   let rows=emptyShop?[]:records.filter(e=>(!q.term||(e.description+' '+e.category+' '+e.payee).toLowerCase().includes(q.term.toLowerCase()))&&(!q.from||e.incurred_date>=q.from)&&(!q.to||e.incurred_date<=q.to)&&(!q.category||e.category_id===q.category)&&(!q.payee||e.payee_id===q.payee)&&(q.status?e.status===q.status:e.status!=='DRAFT')&&(!q.paymentStatus||e.status==='POSTED'&&(q.paymentStatus==='UNPAID'?e.paid===0:q.paymentStatus==='PARTIAL'?e.paid>0&&e.paid<e.amount:e.paid===e.amount)));
   if(q.sort==='amount-desc')rows.sort((a,b)=>b.amount-a.amount);if(q.sort==='amount-asc')rows.sort((a,b)=>a.amount-b.amount);
   const posted=rows.filter(e=>e.status==='POSTED'),summary={totalExpenses:posted.reduce((s,e)=>s+e.amount,0),amountPaid:posted.reduce((s,e)=>s+e.paid,0),outstanding:posted.reduce((s,e)=>s+e.amount-e.paid,0)};
   return json({total:rows.length,hasExpenses:!emptyShop,legacyDraftCount:emptyShop?0:records.filter(e=>e.status==='DRAFT').length,summary,rows:rows.slice((Number(q.page)-1)*q.pageSize,Number(q.page)*q.pageSize)});
  }
  if(p!=='/api/expenses/report'&&/^\/api\/expenses\/[\w-]+$/.test(p)){const e=records.find(e=>e.expense_id===p.split('/')[3]);return json({...e,version:1,created_by:1,created_at:'2026-11-07T00:00:00Z',updated_at:'2026-11-07T00:00:00Z',payments:[],events:[],attachments:[],gst_details:null});}
  if(p==='/api/expenses/report'){
   const q=Object.fromEntries(url.searchParams);reportRequests.push(q);if(reportFailure)return json({message:'Report unavailable'},503);
   const posted=records.filter(e=>e.status==='POSTED'&&e.incurred_date>=q.from&&e.incurred_date<=q.to);
   return json({summary:{totalExpenses:posted.reduce((s,e)=>s+e.amount,0),amountPaid:posted.reduce((s,e)=>s+e.paid,0),outstanding:posted.reduce((s,e)=>s+e.amount-e.paid,0)},incurred:100,paid:20,outstandingAtCutoff:80,expenses:[],payments:[],monthly:[],categories:[],outstanding:[],definition:'Existing cutoff report'});
  }
  if(p.startsWith('/api/')||p.startsWith('/account/session/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 const field=id=>page.locator('#expense-'+id),last=()=>requests.at(-1);
 async function change(action){const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/expenses');await action();await response;await page.waitForFunction(()=>document.querySelector('#expense-pager').getAttribute('aria-busy')==='false');}
 async function go(){await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation());await change(()=>page.goto('https://stockflow.test/Expenses'));}
 async function open(){await field('filter-toggle').click();await field('filter-panel').waitFor({state:'visible'});}
 await go();assert.equal(await field('rows').locator('tr').count(),10);assert(await field('filter-panel').isHidden());assert(await field('filter-count').isHidden());assert(await field('filter-chips').isHidden());
 assert.equal(await page.locator('.expense-list-totals,.expense-report-totals,#expense-total-amount').count(),0,'Financial cards are absent from the list');
 assert(await page.evaluate(()=>{const notice=document.getElementById('expense-legacy-notice'),previous=notice.hidden?document.querySelector('.sf-breadcrumb'):notice;return document.querySelector('.expense-list-controls').getBoundingClientRect().top-previous.getBoundingClientRect().bottom<=16;}),'Toolbar follows breadcrumbs or legacy notice compactly');
 await change(()=>page.getByRole('button',{name:'Next page',exact:true}).click());assert.equal(last().page,'2');
 await open();await field('filter-category').selectOption(category);await field('filter-close').click();assert.equal(requests.length,2,'Closing does not request');await open();assert.equal(await field('filter-category').inputValue(),'');
 await field('filter-category').selectOption(category);await field('filter-payee').selectOption(payee);await field('filter-payment').selectOption('PARTIAL');await field('from').fill('2026-11-01');await field('to').fill('2026-11-07');
 await change(()=>page.getByRole('button',{name:'Apply Filters',exact:true}).click());assert.equal(last().page,'1');assert.equal(last().category,category);assert.equal(last().payee,payee);assert.equal(last().paymentStatus,'PARTIAL');assert.equal(await field('filter-count').textContent(),'4');assert.equal(await field('rows').locator('tr').count(),1);
 await open();await field('from').fill('2026-12-01');const before=requests.length;await page.getByRole('button',{name:'Apply Filters',exact:true}).click();assert.equal(requests.length,before);assert(await field('to').evaluate(e=>!e.validity.valid));await page.keyboard.press('Escape');assert(await field('filter-panel').isHidden());
 await change(()=>page.getByRole('button',{name:'Remove Payment: Partially Paid',exact:true}).click());assert(!last().paymentStatus);assert.equal(await field('filter-count').textContent(),'3');
 const countBefore=requests.length;await change(()=>field('term').fill('AC repair'));assert.equal(requests.length,countBefore+1,'Debounced search makes one request');assert.equal(last().category,category);assert.equal(last().term,'AC repair');assert.equal(await field('rows').locator('tr').count(),1);
 await field('rows').getByRole('button',{name:'Actions for AC repair',exact:true}).click();assert(await page.getByRole('link',{name:'Record Payment',exact:true}).isVisible());assert(await page.getByRole('link',{name:'View Receipt',exact:true}).isVisible());assert((await page.getByRole('link',{name:'View Receipt',exact:true}).getAttribute('href')).includes('/attachments/'));await page.keyboard.press('Escape');
 await change(()=>field('filter-reset').click());assert(!last().term&&!last().category&&!last().from);assert(await field('filter-chips').isHidden());
 assert.equal(await field('sort').count(),0,'Sort selector removed');assert(!last().sort||last().sort==='newest','Newest first remains the default');
 assert.equal(await page.locator('[data-expense-view]').count(),0,'Draft tabs removed');await change(()=>field('legacy-review').click());assert.equal(last().status,'DRAFT');assert.equal(await field('rows').locator('.expense-list-badge').textContent(),'Draft');
 await field('rows').getByRole('button').click();assert(await page.getByRole('link',{name:'Review Saved Expense'}).isVisible());assert.equal(await page.getByRole('link',{name:'Record Payment',exact:true}).count(),0);await page.keyboard.press('Escape');
 await change(()=>field('filter-reset').click());await change(()=>field('term').fill('missing expense'));assert.equal(await field('empty-title').textContent(),'No expenses match your filters.');assert(await field('empty-add').isHidden());await change(()=>field('empty-clear').click());
 emptyShop=true;await go();assert.equal(await field('empty-title').textContent(),'No expenses recorded yet.');assert(await field('empty-add').isVisible());assert(await field('empty-clear').isHidden());
 assert(await page.evaluate(()=>document.querySelector('.expense-results thead').getBoundingClientRect().top-document.querySelector('.expense-results').getBoundingClientRect().top<=14),'Table starts at compact card padding');
 assert(await page.evaluate(()=>document.querySelector('.expense-results').getBoundingClientRect().top-document.querySelector('.expense-list-controls').getBoundingClientRect().bottom<=12),'Compact gap between toolbar and table');
 fs.mkdirSync(path.join(root,'.artifacts/expense-list'),{recursive:true});await page.screenshot({path:path.join(root,'.artifacts/expense-list/list-empty-compact.png'),fullPage:true});emptyShop=false;
 allowPay=false;await go();await field('rows').getByRole('button',{name:'Actions for AC repair',exact:true}).click();assert.equal(await page.getByRole('link',{name:'Record Payment',exact:true}).count(),0);await page.keyboard.press('Escape');allowPay=true;
 apiFailure=true;await change(()=>field('term').fill('outage'));assert.equal(await field('message').textContent(),'List unavailable');apiFailure=false;
 for(const [width,height]of [[1440,900],[1024,768],[768,1024],[390,844],[320,700]]){
  await page.setViewportSize({width,height});await go();await open();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No overflow '+width);assert(await field('filter-panel').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'Panel fits '+width);await field('filter-close').click();
  fs.mkdirSync(path.join(root,'.artifacts/expense-list'),{recursive:true});await page.screenshot({path:path.join(root,'.artifacts/expense-list/list-'+width+'.png'),fullPage:true});
 }
 await page.goto('https://stockflow.test/Expenses/Details?id='+records[0].expense_id+'&payment=1');await field('payment-dialog').waitFor({state:'visible'});assert.equal(await field('later-amount').inputValue(),'80');await field('payment-cancel').click();
 await page.goto('https://stockflow.test/Expenses/Details?id='+records[0].expense_id+'&edit=1');await field('correction').waitFor({state:'visible'});assert(await field('fields').isEnabled());
 await page.evaluate(()=>StockFlowUnsaved.allowNavigation());await page.goto('https://stockflow.test/Expenses/Create');await field('fields').waitFor({state:'visible'});await page.waitForFunction(()=>!document.getElementById('expense-fields').disabled);assert(await field('state').isHidden());assert.equal(await field('summary-state').count(),0);assert(await field('record').isVisible());assert.equal(await field('save').count(),0);assert(await page.getByRole('link',{name:'Cancel',exact:true}).isVisible());
 await page.goto('https://stockflow.test/Expenses/Report');await page.waitForFunction(()=>document.getElementById('expense-definition').textContent==='Existing cutoff report');assert.equal(await field('from').inputValue().then(v=>v.slice(-2)),'01');assert(await field('summary').isVisible());
 const currency=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(n);
 async function applyReport(from,to){await field('from').fill(from);await field('to').fill(to);const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/expenses/report');await page.getByRole('button',{name:'Apply',exact:true}).click();await response;await page.waitForFunction(()=>document.getElementById('expenses').getAttribute('aria-busy')==='false');}
 await applyReport('2026-11-01','2026-11-30');assert.equal(await field('total-amount').textContent(),currency(2800));assert.equal(await field('total-paid').textContent(),currency(20));assert.equal(await field('total-outstanding').textContent(),currency(2780));assert((await field('summary').textContent()).includes('Payments in period: '+currency(20)));
 assert(await page.evaluate(()=>document.querySelector('.expense-report-totals').previousElementSibling.id==='expense-filters'),'Cards follow date filters');
 await applyReport('2026-10-01','2026-10-31');assert.equal(await field('total-amount').textContent(),currency(0));assert.equal(await field('total-paid').textContent(),currency(0));assert.equal(await field('total-outstanding').textContent(),currency(0));
 for(const [width,height]of [[1440,900],[768,1024],[390,844],[320,700]]){await page.setViewportSize({width,height});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Report fits '+width);const boxes=await page.locator('.expense-report-totals .card').evaluateAll(es=>es.map(e=>{const b=e.getBoundingClientRect();return {y:b.y,height:b.height};}));assert(boxes.every(b=>b.height===boxes[0].height),'Equal card heights '+width);assert(width>575?boxes.every(b=>b.y===boxes[0].y):boxes[1].y>boxes[0].y,'Responsive cards '+width);await page.screenshot({path:path.join(root,'.artifacts/expense-list/report-'+width+'.png'),fullPage:true});}
 reportFailure=true;await applyReport('2026-11-01','2026-11-30');assert.equal(await field('total-amount').textContent(),'—');assert.equal(await field('message').textContent(),'Report unavailable');
 assert.deepEqual(errors,[]);console.log('PASS: list loading, drawer apply/cancel, dates, combined filters, chips, search, reset, totals, paging, default ordering, legacy review, row permissions/receipts/payment/correction, empty/error states, responsive layouts, create and report regression.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
