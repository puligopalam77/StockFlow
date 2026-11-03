const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),output=path.join(root,'tests/bin/billing-ui-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1280,height:650}}),errors=[],posts=[];page.on('pageerror',e=>errors.push(e.message));let fail=false,gate=null;
 await page.route('**/*',async route=>{const req=route.request(),u=new URL(req.url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(u.pathname==='/Settings/Company')return route.fulfill({path:path.join(output,'BillingSettings.html'),contentType:'text/html'});
  if(u.pathname==='/api/settings/billing-policy'){
   if(req.method()==='GET')return json({discountLimitPercent:10,canManage:true,shopScoped:true,version:1,fastBillingEnabled:false,requireRoutineReason:true});
   posts.push(req.postDataJSON());if(gate)await gate;if(fail)return route.fulfill({status:500,contentType:'application/json',body:'{"message":"Policy update failed; retry."}'});return route.fulfill({status:204,body:''});
  }
  if(u.pathname==='/api/notifications')return json({unread:0,items:[]});
  if(u.pathname.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://localhost/Settings/Company');await page.waitForFunction(()=>!document.querySelector('#discount-limit-percent').disabled);assert.equal(await page.locator('#discount-limit-percent').inputValue(),'10');
 for(const invalid of ['-1','101','1.001']){await page.locator('#discount-limit-percent').fill(invalid);await page.locator('#save-billing-policy').click();assert.equal(posts.length,0,'Invalid policy does not post');}
 await page.locator('#fast-billing-enabled').check();assert.equal(await page.locator('#require-routine-reason').isChecked(),false);await page.locator('#discount-limit-percent').fill('15');fail=true;await page.locator('#save-billing-policy').click();await page.waitForFunction(()=>document.querySelector('#billing-policy-message').textContent.includes('failed'));assert.equal(await page.locator('#discount-limit-percent').inputValue(),'15');
 fail=false;let release;gate=new Promise(r=>release=r);await page.locator('#save-billing-policy').click();await page.waitForFunction(()=>document.querySelector('#save-billing-policy').disabled);await page.evaluate(()=>document.querySelector('#billing-policy-form').requestSubmit());assert.equal(posts.length,2,'Pending policy request cannot duplicate');release();await page.waitForFunction(()=>document.querySelector('#billing-policy-message').textContent.includes('saved'));assert.equal(posts.at(-1).discountLimitPercent,15);assert.equal(posts.at(-1).fastBillingEnabled,true);assert.equal(posts.at(-1).requireRoutineReason,false);assert.equal(posts.at(-1).requireExceptionalReason,true);
 await page.locator('#reload-billing-policy').click();await page.waitForFunction(()=>document.querySelector('#discount-limit-percent').value==='10');
 for(const [width,height] of [[1280,650],[390,844]]){await page.setViewportSize({width,height});await page.locator('#billing-policy-settings').scrollIntoViewIfNeeded();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await page.screenshot({path:path.join(output,'billing-policy-settings-'+width+'.png'),fullPage:true});}
 assert.deepEqual(errors,[]);console.log('PASS: separate authorized settings form, policy validation, failure retention, pending duplicate prevention, saved reload and responsive settings.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
