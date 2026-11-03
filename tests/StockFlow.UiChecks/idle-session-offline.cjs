const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const errors=[];
  async function setup(absoluteMilliseconds=28800000) {
   const context=await browser.newContext(),state={activity:0,expire:0,deny:false};
   await context.route('**/*',async route=>{
    const req=route.request(),url=new URL(req.url()),p=url.pathname;
    const json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
    if(p==='/Products')return route.fulfill({body:fs.readFileSync(path.join(root,'tests/bin/idle-session-ui-fixtures/Products.html'),'utf8').replace(/data-absolute-remaining="\d+"/,`data-absolute-remaining="${absoluteMilliseconds}"`),contentType:'text/html'});
    if(p==='/Account/Login')return route.fulfill({contentType:'text/html',body:'<p>Sign in after inactivity</p>'});
    if(p==='/account/session/activity') {
     state.activity++;assert.equal(req.method(),'POST');assert(req.headers()['x-csrf-token']);
     assert(req.postDataJSON().ageMilliseconds>=0);
     return json({remainingMilliseconds:900000-req.postDataJSON().ageMilliseconds,absoluteRemainingMilliseconds:absoluteMilliseconds},state.deny?401:200);
    }
    if(p==='/account/session/expire'){state.expire++;return route.fulfill({status:204});}
    if(p==='/api/branches/context')return json({locations:[],locationId:1,storeId:1});
    if(p.startsWith('/api/'))return json([]);
    const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
    if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
    return route.fulfill({status:404,body:''});
   });
   const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
   await page.clock.install({time:new Date('2026-10-06T10:00:00Z')});
   await page.goto('http://stockflow.test/Products');
   await page.locator('#master-rows').getByText('No matching records.').waitFor();
   return {context,page,state};
  }
  {
   const {context,page,state}=await setup();
   await page.clock.fastForward(14*60000);assert(await page.locator('#idle-session-warning').isVisible());
   assert.equal(state.activity,0,'background loading does not send activity');
   await page.clock.fastForward(59999);assert.equal(new URL(page.url()).pathname,'/Products');
   await page.clock.fastForward(1001);await page.waitForURL('**/Account/Login?reason=idle');
   assert.equal(state.expire,1,'inactivity invokes server logout');await context.close();
  }
  {
   const {context,page,state}=await setup();
   await page.clock.fastForward(14*60000);await Promise.all([page.waitForResponse('**/account/session/activity'),page.mouse.move(100,100)]);
   assert(!(await page.locator('#idle-session-warning').isVisible()));
   await page.clock.fastForward(14*60000);assert.equal(new URL(page.url()).pathname,'/Products','mouse movement resets 15 minutes');
   await page.locator('#master-search').fill('Working');await page.clock.runFor(300);
   await page.clock.fastForward(14*60000);assert.equal(new URL(page.url()).pathname,'/Products','typing resets 15 minutes');
   await page.locator('#idle-session-continue').focus();await page.keyboard.press('Enter');await page.clock.runFor(300);
   await page.clock.fastForward(14*60000);assert.equal(new URL(page.url()).pathname,'/Products','Stay signed in resets 15 minutes');
   assert(state.activity>=3);await context.close();
  }
  {
   const {context,page,state}=await setup();
   const second=await context.newPage();second.on('pageerror',e=>errors.push(e.message));
   await second.goto('http://stockflow.test/Products');await second.locator('#master-rows').getByText('No matching records.').waitFor();
   await page.clock.fastForward(14*60000);await Promise.all([second.waitForResponse('**/account/session/activity'),second.mouse.move(100,100)]);
   await page.clock.fastForward(2*60000);assert.equal(new URL(page.url()).pathname,'/Products','activity in one tab keeps the other signed in');
   await page.clock.fastForward(14*60000);await page.waitForURL('**/Account/Login?reason=idle');await second.waitForURL('**/Account/Login?reason=idle');
   assert(state.expire>=1 && state.expire<=2,'simultaneous tab timeouts safely sign out the shared session');await context.close();
  }
  {
   const {context,page,state}=await setup();
   await page.clock.setSystemTime(new Date('2026-10-06T10:16:00Z'));
   await page.mouse.move(200,150);await page.waitForURL('**/Account/Login?reason=idle');
   assert.equal(state.activity,0,'first input after sleep cannot revive an expired session');await context.close();
  }
  {
   const {context,page,state}=await setup();state.deny=true;
   await page.mouse.move(100,100);await page.waitForURL('**/Account/Login?reason=ended');
   assert.equal(state.activity,1,'server rejection returns to login');await context.close();
  }
  {
   const {context,page,state}=await setup(120000);
   await page.clock.fastForward(60000);assert(await page.locator('#idle-session-warning').isVisible());
   assert(await page.locator('#idle-session-continue').isHidden(),'maximum lifetime cannot be extended by Stay signed in');
   await Promise.all([page.waitForResponse('**/account/session/activity'),page.mouse.move(100,100)]);
   await page.clock.fastForward(60000);await page.waitForURL('**/Account/Login?reason=absolute');
   assert.equal(state.expire,1,'active input does not extend absolute lifetime');await context.close();
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: idle/absolute logout, warnings, pointer/typing/continue reset, no background keepalive, shared tabs, sleep expiry and server rejection.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
