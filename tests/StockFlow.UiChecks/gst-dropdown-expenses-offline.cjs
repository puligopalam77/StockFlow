// Real rendered Expenses form: custom presentation retains native IDs and events.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const options={businessDate:'2026-11-09',categories:[{id:'11111111-1111-4111-8111-111111111111',name:'Shop rent'}],payees:[{id:'22222222-2222-4222-8222-222222222222',name:'Supplier'}],permissions:{CREATE:true,EDIT:true,POST:true,PAY:true},taxRules:[0,5,12,18,28].map((rate,i)=>({id:100+i,name:'GST '+rate+'%',rate,treatment:rate?'TAXABLE':'NIL',supported:true,effectiveFrom:'2000-01-01'}))};
 await page.route('**/*',route=>{
  const req=route.request(),u=new URL(req.url()),json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  if(u.pathname==='/Expenses/Create')return route.fulfill({path:path.join(root,'tests/bin/expense-list-fixtures/Create.html'),contentType:'text/html'});
  if(u.pathname==='/api/expenses/options')return json(options);
  if(u.pathname==='/api/expenses/tax-preview'){
   const input=req.postDataJSON(),rule=options.taxRules.find(r=>r.id===input.gst.ruleId),base=Math.round(input.amount/(1+rule.rate/100)*100)/100;
   return json({...input.gst,rate:rule.rate,ruleName:rule.name,treatment:rule.treatment,total:input.amount,taxableAmount:base,gstAmount:Math.round((input.amount-base)*100)/100,itcClaimed:false,reviewStatus:'NEEDS_REVIEW'});
  }
  if(u.pathname.startsWith('/api/'))return json(u.pathname==='/api/notifications'?{unread:0,items:[]}:[]);
  const file=path.join(assets,u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 await page.goto('https://stockflow.test/Expenses/Create');await page.waitForFunction(()=>document.querySelector('#expense-state').textContent!=='Loading...').catch(async e=>{console.error({errors,message:await page.locator('#expense-message').textContent()});throw e;});
 await selectValue(page.locator('#expense-category'),options.categories[0].id);await selectValue(page.locator('#expense-payee'),options.payees[0].id);
 await page.locator('#expense-amount').fill('1180');await page.locator('#expense-description').fill('Synthetic GST dropdown check');
 await page.locator('label[for=expense-gst-includes]').click();
 const rate=page.locator('#expense-gst-rule');assert.equal(await rate.evaluate(n=>!!n.sfSelect),true,'Shared GST presentation is active');
 for(const value of ['100','101','102','103','104']){await selectValue(rate,value);await page.waitForFunction(()=>!document.querySelector('#expense-summary-gst').hidden);assert.equal(await rate.inputValue(),value);}
 await selectValue(rate,'103');await page.waitForFunction(()=>document.querySelector('#expense-summary-gst-rate').textContent==='18%');
 assert((await page.locator('#expense-summary-gst-amount').textContent()).includes('180.00'));
 await rate.evaluate(n=>{window.gstEvents={input:0,change:0};for(const type of ['input','change'])n.addEventListener(type,()=>window.gstEvents[type]++);});
 const trigger=page.locator('#expense-gst-rule-search'),query=page.locator('#expense-gst-rule-query');
 await trigger.press('Enter');await query.fill('12%');await query.press('ArrowDown');await query.press('Enter');
 assert.equal(await rate.inputValue(),'102');assert.deepEqual(await page.evaluate(()=>gstEvents),{input:1,change:1});
 await trigger.press('Enter');await query.press('Escape');assert.equal(await rate.inputValue(),'102');
 assert.equal(await trigger.getAttribute('aria-expanded'),'false');
 await trigger.click();const chosen=page.locator('#expense-gst-rule-options [aria-selected=true]');
 assert.equal(await chosen.getAttribute('data-value'),'102');
 assert.equal(await chosen.evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 244, 249)');
 assert.equal(await chosen.evaluate(n=>getComputedStyle(n,'::after').color),'rgb(255, 0, 128)');
 await query.press('Tab');assert.equal(await trigger.getAttribute('aria-expanded'),'false');
 assert.equal(await page.evaluate(()=>document.activeElement.id),'expense-gst-gstin','Tab continues to the next expense field');
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[768,1024],[390,844],[320,640]]){
  await page.setViewportSize({width,height});await page.waitForTimeout(300);await rate.focus();assert(await page.locator('#expense-gst-rule-search').isVisible());
  const rect=await page.locator('#expense-gst-rule-search').boundingBox();assert(rect.x>=0&&rect.x+rect.width<=width+1,'GST rate stays inside viewport at '+width);
  await trigger.click();const panel=page.locator('#expense-gst-rule-panel'),box=await panel.boundingBox();
  assert(box.height<=240&&box.x>=0&&box.x+box.width<=width+1&&box.y>=0&&box.y+box.height<=height+1);
  if(width===1366||width===390)await page.screenshot({path:path.join(root,'tests/bin/dropdown-ui/expense-gst-'+width+'.png'),fullPage:true});
  await query.press('Escape');
 }
 await page.locator('label[for=expense-gst-no]').click();assert(await page.locator('#expense-gst-section').isHidden());
 assert.deepEqual(errors,[]);console.log('PASS Expenses: shared GST picker, exact input/change events, keyboard/Tab/Escape, light hover/selection and pink check, five original rate IDs, 18% inclusive preview, No GST and seven responsive sizes. Mocked HTTP; live posting not tested.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
