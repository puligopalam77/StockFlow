const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const context=await browser.newContext(),page=await context.newPage(),errors=[];page.on('pageerror',error=>errors.push(error.message));
 let fail=true,writes=0;
 const now='2026-10-06T10:00:00Z';
 let sessions=[{sessionId:'A'.repeat(32),device:'Mozilla Windows Edg/100',ipAddress:'127.0.0.1',createdAt:now,lastActivityAt:now,absoluteExpiresAt:'2026-10-06T18:00:00Z',isCurrent:true},
  {sessionId:'B'.repeat(32),device:'Mozilla Android Chrome/100',ipAddress:'127.0.0.2',createdAt:now,lastActivityAt:now,absoluteExpiresAt:'2026-10-06T18:00:00Z',isCurrent:false}];
 await context.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Account/Sessions')return route.fulfill({path:path.join(root,'tests/bin/idle-session-ui-fixtures/Sessions.html'),contentType:'text/html'});
  if(p==='/Account/Login')return route.fulfill({contentType:'text/html',body:'<p>Sign in</p>'});
  if(p==='/account/session/devices')return json(sessions);
  if(p==='/account/session/revoke'){
   writes++;assert(req.headers()['x-csrf-token']);assert.equal(req.method(),'POST');
   if(fail)return json({},500);
   sessions=sessions.filter(item=>item.sessionId!==req.postDataJSON().sessionId);return route.fulfill({status:204});
  }
  if(p==='/account/session/activity')return json({remainingMilliseconds:900000,absoluteRemainingMilliseconds:28800000});
  if(p==='/api/branches/context')return json({locations:[],locationId:1,storeId:1});
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/Account/Sessions');await page.locator('#sessions-rows tr').nth(1).waitFor();
 assert(await page.getByText('Edge on Windows (this device)',{exact:true}).isVisible());assert(await page.getByText('Chrome on Android',{exact:true}).isVisible());
 for(const width of [1440,390,320]){
  await page.setViewportSize({width,height:900});await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const overflow=await page.evaluate(()=>[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth && !e.closest('.table-responsive')).slice(0,8).map(e=>[e.tagName,e.className,e.getBoundingClientRect().right]));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`sessions page overflow at ${width}: `+JSON.stringify(overflow));
 }
 const other=()=>page.getByRole('button',{name:'Sign out Chrome on Android',exact:true});
 await other().click();await page.locator('#session-revoke-cancel').click();assert.equal(writes,0,'cancel does not revoke a session');
 await other().click();await page.locator('#session-revoke-confirm').click();await page.getByText('Unable to update sessions. Please try again.',{exact:true}).waitFor();
 assert.equal(await page.locator('#sessions-rows tr').count(),2,'failed revocation retains listed devices');
 fail=false;await other().click();await page.locator('#session-revoke-confirm').click();await page.getByText('Session signed out.',{exact:true}).waitFor();
 await page.waitForFunction(()=>document.querySelectorAll('#sessions-rows tr').length===1);assert.equal(writes,2);
 await page.getByRole('button',{name:'Sign out Edge on Windows (this device)',exact:true}).click();await page.locator('#session-revoke-confirm').click();await page.waitForURL('**/Account/Login?reason=ended');
 assert.deepEqual(errors,[]);console.log('PASS: device listing, current device, confirmation/cancel, failed revoke retry, remote/self revocation and 1440/390/320 layouts.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
