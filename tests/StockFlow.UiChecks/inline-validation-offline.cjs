// Shared validation and real notification scripts; isolated HTML, no database writes.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const assets=path.resolve(__dirname,'../../src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1280,height:720}}),errors=[];
 page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname;
  if(p==='/test')return route.fulfill({contentType:'text/html',body:`<!doctype html><html><head><link rel="stylesheet" href="/css/stockflow.css"></head><body>
   <form id="form"><label for="name">Name</label><input id="name" required minlength="2">
   <details><summary>Optional contact</summary><label for="email">Email</label><input id="email" type="email"></details>
   <button type="submit">Save</button><button type="reset">Reset</button></form>
   <form id="server-form"><label for="server-name">Saved name</label><input id="server-name" name="ServerName" value="Rejected name"><span class="field-validation-error" data-valmsg-for="ServerName">Choose a different name.</span></form>
   <script src="/js/notifications-ui.js"></script><script src="/js/common/form-validation.js"></script>
   <script>window.writes=0;document.getElementById('form').addEventListener('submit',event=>{event.preventDefault();writes++;});</script></body></html>`});
  const file=path.resolve(assets,'.'+p);return file.startsWith(assets+path.sep)&&fs.existsSync(file)?route.fulfill({path:file}):route.fulfill({status:404});
 });
 await page.goto('http://stockflow.test/test');
 assert.equal(await page.locator('#server-name').getAttribute('aria-invalid'),'true');
 assert(await page.locator('#server-name-validation-error').isVisible());
 assert.equal(await page.locator('#server-form [data-valmsg-for]').count(),1,'Server field feedback is reused');
 await page.locator('#server-name').fill('Corrected name');
 assert(await page.locator('#server-name-validation-error').isHidden());
 await page.evaluate(()=>document.getElementById('server-form').remove());
 for(const type of ['success','error','warning','info']){
  await page.evaluate(type=>StockFlowNotify.show(type,'Operation '+type,{key:'operation',duration:0}),type);
  await page.getByRole('button',{name:'Save',exact:true}).click();
  assert(await page.locator('#name-validation-error').isVisible());
  assert.equal(await page.locator('#name').getAttribute('aria-invalid'),'true');
  assert.equal(await page.locator(':focus').getAttribute('id'),'name');
  assert.equal(await page.locator('.sf-validation-summary').count(),0);
  assert.equal(await page.locator('.sf-toast').count(),1);
  assert.equal(await page.locator('.sf-toast-copy>p').textContent(),'Operation '+type);
  assert.equal(await page.evaluate(()=>writes),0);
 }
 await page.locator('#name').fill('Retailer');await page.locator('#name').press('Tab');
 await page.locator('#email').evaluate(input=>input.value='bad');
 await page.getByRole('button',{name:'Save',exact:true}).click();
 assert(await page.locator('#email').isVisible());assert(await page.locator('#email-validation-error').isVisible());
 await page.waitForFunction(()=>document.activeElement.id==='email');assert.equal(await page.evaluate(()=>writes),0);
 await page.locator('#email').fill('shop@example.com');await page.getByRole('button',{name:'Save',exact:true}).click();
 assert.equal(await page.evaluate(()=>writes),1);assert.equal(await page.locator('[aria-invalid=true]').count(),0);
 await page.evaluate(()=>{const field=document.createElement('input');field.id='dynamic';field.required=true;document.getElementById('form').append(field);});
 await page.getByRole('button',{name:'Save',exact:true}).click();assert(await page.locator('#dynamic-validation-error').isVisible());
 assert.equal(await page.evaluate(()=>writes),1);assert.equal(await page.locator('.sf-validation-summary').count(),0);
 await page.getByRole('button',{name:'Reset',exact:true}).click();assert.equal(await page.locator('[aria-invalid=true]').count(),0);
 assert.equal(await page.locator('.sf-toast').count(),1,'Reset preserves operation notification');
 await page.setViewportSize({width:320,height:640});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 assert.deepEqual(errors,[]);console.log('PASS: shared inline-only validation, blocked/valid saves, dynamic fields, first-error focus, collapsed details, reset, mobile and unchanged success/error/warning/info flags.');
 }finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
