// Matching before/after captures of the existing expense UI with controlled APIs.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/expense-list-fixtures');
const phase=process.argv[2]||'after',output=path.join(root,'.artifacts/expense-visual-standard',phase);fs.mkdirSync(output,{recursive:true});
const id='30000000-0000-0000-0000-000000000001',category='10000000-0000-0000-0000-000000000001',payee='20000000-0000-0000-0000-000000000001';
const expense={expense_id:id,category_id:category,payee_id:payee,category:'Repairs & Maintenance',payee:'Local contractor',incurred_date:'2026-11-07',amount:1180,paid:500,description:'Shop air conditioner repair',status:'POSTED',version:3,created_by:1,created_at:'2026-11-07T00:00:00Z',updated_at:'2026-11-07T00:00:00Z',payments:[{payment_id:id,paid_date:'2026-11-07',kind:'PAYMENT',payment_mode:'BANK_TRANSFER',amount:500,reference:'BANK-001',actor_id:1}],events:[{action:'POST',version:2,at:'2026-11-07T00:00:00Z',actor:1,reason:null,snapshot:{amount:1180}}],attachments:[],gst_details:null};
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage();page.setDefaultTimeout(10000);const errors=[],metrics=[],requests=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{
  const url=new URL(route.request().url()),p=url.pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Expenses'||/^\/Expenses\/(Create|Details|Report)$/.test(p)){const name=p==='/Expenses'?'Index':p.split('/')[2];let html=fs.readFileSync(path.join(fixtures,name+'.html'),'utf8');if(name==='Details')html=html.replace('data-id=""','data-id="'+id+'"');return route.fulfill({contentType:'text/html',body:html});}
  if(p.startsWith('/api/expenses'))requests.push(p);
  if(p==='/api/expenses/options')return json({categories:[{id:category,name:expense.category}],payees:[{id:payee,name:expense.payee}],taxRules:[],permissions:{CREATE:true,EDIT:true,POST:true,PAY:true,CANCEL:true,ATTACH:true,REVERSE:true}});
  if(p==='/api/expenses')return json({rows:[expense],total:1,hasExpenses:true,legacyDraftCount:0});
  if(p==='/api/expenses/'+id)return json(expense);
  if(p==='/api/expenses/report')return json({summary:{totalExpenses:1180,amountPaid:500,outstanding:680},incurred:1180,paid:500,outstandingAtCutoff:680,definition:'Recorded expense totals include GST. Signed payments and cutoff balances retain their existing scope. GST ITC remains unclaimed.',expenses:[expense],payments:expense.payments,monthly:[{month:'2026-11',count:1,total:1180}],categories:[{category:expense.category,count:1,total:1180}],outstanding:[expense]});
  if(p.startsWith('/api/')||p.startsWith('/account/session/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 const field=name=>page.locator('#expense-'+name);
 async function go(name){await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});await page.goto('https://stockflow.test/Expenses'+(name==='Index'?'':'/'+name)+(name==='Details'?'?id='+id:''));if(name==='Index')await field('rows').locator('tr').waitFor();else if(name==='Report')await field('report-monthly').locator('tr').waitFor();else await page.waitForFunction(()=>document.getElementById('expense-state').textContent!=='Loading...');}
 async function capture(name,width){await page.evaluate(()=>{window.scrollTo(0,0);});await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No page overflow '+name+' '+width);metrics.push(await page.evaluate(({name,width})=>{const control=e=>{const s=getComputedStyle(e);return {id:e.id,height:Math.round(e.getBoundingClientRect().height),font:s.fontFamily,fontSize:s.fontSize,color:s.color,background:s.backgroundColor};};return {name,width,title:control(document.querySelector('#expenses h1')),controls:[...document.querySelectorAll('#expenses :is(input.form-control,select.form-select,button.btn,a.btn)')].filter(e=>e.getClientRects().length).slice(0,16).map(control)};},{name,width}));await page.screenshot({path:path.join(output,name+'-'+width+'.png'),fullPage:true});}
 for(const [width,height]of [[1920,1080],[1366,900],[768,1024],[390,844]]){
  await page.setViewportSize({width,height});await go('Index');await capture('list',width);
  await field('filter-toggle').click();assert(await field('filter-panel').isVisible());await page.keyboard.press('Escape');assert(await field('filter-panel').isHidden());
  await go('Create');await selectValue(field('category'),category);await selectValue(field('payee'),payee);await field('amount').fill('1180');await field('description').fill(expense.description);await selectValue(field('initial-state'),'PARTIAL');await field('initial-amount').fill('500');await field('initial-confirm').check();await capture('create',width);
  await page.locator('[data-master=CATEGORY]').click();await field('master-name').focus();await page.keyboard.press('Tab');assert(await field('master-dialog').evaluate(e=>e.contains(document.activeElement)),'Keyboard focus stays in modal');assert(await field('master-dialog').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'Master dialog fits');await capture('category-dialog',width);await page.keyboard.press('Escape');
  await go('Details');await capture('details',width);await field('payment-open').click();await field('later-method').focus();await page.keyboard.press('Tab');assert(await field('payment-dialog').evaluate(e=>e.contains(document.activeElement)),'Payment keyboard focus remains in modal');assert(await field('payment-dialog').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'Payment dialog fits');await capture('payment-dialog',width);await field('payment-cancel').click();
  await go('Report');await capture('report',width);
  if(phase==='after'){
   assert.equal(await field('report-categories').locator('td').last().evaluate(e=>getComputedStyle(e).textAlign),'right');
   for(const column of [4,5,6])assert(await page.locator('table:has(>#expense-report-outstanding) :is(th,td):nth-child('+column+')').evaluateAll(cells=>cells.every(e=>getComputedStyle(e).textAlign==='right')),'Outstanding headings and values align');
   if(width>575)assert((await field('from').boundingBox()).width<=212,'Short report date fields have bounded widths');
  }
 }
 if(phase==='after'){
  for(const metric of metrics)for(const control of metric.controls)assert(control.height>=(metric.width<=575?44:38),metric.name+' '+control.id+' control height at '+metric.width);
  const baseline=path.join(root,'.artifacts/expense-visual-standard/before/metrics.json');
  if(fs.existsSync(baseline)){
   const before=JSON.parse(fs.readFileSync(baseline,'utf8'));
   assert.deepEqual(requests,before.requests,'No additional expense API requests');
   metrics.forEach((metric,i)=>{assert.equal(metric.title.font,before.metrics[i].title.font,'Existing font family retained');assert.equal(metric.title.color,before.metrics[i].title.color,'Existing navy retained');const primary=metric.controls.find(c=>c.id==='expense-record');if(primary)assert.equal(primary.background,before.metrics[i].controls.find(c=>c.id==='expense-record').background,'Primary magenta retained');});
  }
 }
 assert.deepEqual(errors,[]);fs.writeFileSync(path.join(output,'metrics.json'),JSON.stringify({metrics,requests},null,2));console.log('PASS: '+phase+' captures for list/create/details/reports and category/payment dialogs at four viewports; keyboard and overflow checks passed.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
