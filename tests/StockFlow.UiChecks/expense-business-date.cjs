// Reproduce the screenshot: browser date Nov 8, database date Nov 7, GST 12%, paid cash.
// Real Razor/assets, controlled APIs; never writes to the shop database.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package'),{selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/expense-list-fixtures'),output=path.join(root,'.artifacts/expense-business-date');fs.mkdirSync(output,{recursive:true});
const category='10000000-0000-0000-0000-000000000001',payee='20000000-0000-0000-0000-000000000001',day='2026-11-07';
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:900}});page.setDefaultTimeout(10000);await page.clock.setFixedTime(new Date('2026-11-08T06:00:00Z'));
 const previews=[],writes=[],records=new Map(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const quote=b=>({...b.gst,rate:12,taxableAmount:Number((b.amount/1.12).toFixed(2)),gstAmount:Number((b.amount-b.amount/1.12).toFixed(2)),expenseDate:b.incurredDate});
 await page.route('**/*',async route=>{const request=route.request(),url=new URL(request.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({contentType:'application/json',status,body:JSON.stringify(body)});
  if(p==='/Expenses/Create'||p==='/Expenses/Details'){let html=fs.readFileSync(path.join(fixtures,p.endsWith('Create')?'Create.html':'Details.html'),'utf8');if(p.endsWith('Details'))html=html.replace('data-id=""','data-id="'+url.searchParams.get('id')+'"');return route.fulfill({contentType:'text/html',body:html});}
  if(p==='/api/expenses/options')return json({businessDate:day,categories:[{id:category,name:'Bank & Payment Charges'}],payees:[{id:payee,name:'Synthetic payee'}],permissions:{CREATE:true,EDIT:true,POST:true,PAY:true},taxRules:[{id:12,name:'GST 12%',rate:12,treatment:'TAXABLE',supported:true,effectiveFrom:'2000-01-01'}]});
  if(p==='/api/expenses/tax-preview'){const body=request.postDataJSON();previews.push(body);return body.incurredDate>day?json({message:'Enter a valid expense amount and date'},400):json(quote(body));}
  if(/^\/api\/expenses\/[^/]+\/record$/.test(p)){const op=request.postDataJSON(),id=p.split('/')[3],b=op.body.expense,payment=op.body.payment;assert(b.incurredDate<=day);assert(!payment||payment.paidDate<=day);writes.push(op);records.set(id,{expense_id:id,category_id:category,payee_id:payee,incurred_date:b.incurredDate,amount:b.amount,description:b.description,status:'POSTED',paid:payment?.amount||0,version:1,payments:[],events:[],attachments:[],created_by:1,created_at:day,updated_at:day,gst_details:b.gst?quote(b):null});return json({version:1});}
  if(/^\/api\/expenses\/[^/]+$/.test(p))return json(records.get(p.split('/')[3]));
  if(p.startsWith('/api/')||p.startsWith('/account/session/'))return json(p==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
 const field=name=>page.locator('#expense-'+name);
 async function create(){await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});await page.goto('https://stockflow.test/Expenses/Create');await page.waitForFunction(()=>!document.getElementById('expense-fields').disabled);}
 await create();assert.equal(await field('incurred').inputValue(),day);assert.equal(await field('initial-date').inputValue(),day);assert.equal(await field('incurred').getAttribute('max'),day);assert((await field('date-note').textContent()).includes('08-11-2026'));
 await selectValue(field('category'),category);await selectValue(field('payee'),payee);await field('amount').fill('5400');await field('description').fill('Electricity bill and charges');await page.locator('label[for=expense-gst-includes]').click();
 assert(await page.locator('#expense-gst-includes').isChecked());
 const selected=page.locator('label[for=expense-gst-includes]');
 assert.equal(await selected.evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 244, 250)','GST selection uses a neutral surface');
 assert.match(await selected.evaluate(n=>getComputedStyle(n).boxShadow),/inset/,'Selected segment retains a visible accent');
 await selectValue(field('gst-rule'),'12');await page.waitForFunction(()=>!document.getElementById('expense-record').disabled);
 assert.equal(previews.at(-1).incurredDate,day);assert.equal(await field('gst-taxable').textContent(),'₹4,821.43');assert.equal(await field('gst-tax').textContent(),'₹578.57');
 const before=previews.length;await field('incurred').fill('2026-11-08');assert(await field('record').isDisabled());assert((await field('gst-message').textContent()).includes('07-11-2026'));assert.equal(await field('amount').inputValue(),'5400');await page.waitForTimeout(350);assert.equal(previews.length,before,'Future dates are explained before sending GST preview');await page.screenshot({path:path.join(output,'clock-mismatch.png'),fullPage:true});
 await field('incurred').fill(day);await page.waitForFunction(()=>!document.getElementById('expense-record').disabled);await selectValue(field('initial-state'),'PAID');await field('initial-confirm').check();await field('initial-date').fill('2026-11-08');await field('record').click();assert.equal(writes.length,0,'Future payment date cannot post');await field('initial-date').fill(day);await field('record').click();await page.waitForURL(/Expenses\/Details\?id=/);assert.equal(writes.length,1);assert.equal(writes[0].body.expense.amount,5400);assert.equal(writes[0].body.expense.gst.ruleId,12);assert.equal(writes[0].body.payment.amount,5400);assert.equal(writes[0].body.payment.paymentMode,'CASH');
 await page.setViewportSize({width:390,height:844});await create();assert.equal(await field('incurred').inputValue(),day);assert(await field('gst-no').isChecked());assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));assert.deepEqual(errors,[]);
 console.log('PASS: browser/database clock mismatch, authoritative expense/payment defaults, clear future-date validation, GST 12% on ₹5,400, paid cash recording and mobile layout.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
