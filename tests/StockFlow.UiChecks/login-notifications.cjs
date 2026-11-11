// Real Razor, toast service and idle-session script; controlled HTTP, no DB writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/password-page-fixtures'),out=path.join(root,'.artifacts/login-notifications');
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true});try {
 const context=await browser.newContext(),page=await context.newPage(),errors=[],posts=[];let failure='invalid',expires=0;
 page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(e.message));await page.clock.install();
 await context.route('**/*',route=>{const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(p==='/Account/Login'){
   let file='login.html';if(req.method()==='POST'){posts.push(new URLSearchParams(req.postData()));file='login-'+failure+'.html';}
   else if(url.searchParams.get('reason')==='absolute'||url.searchParams.get('reason')==='idle')file='login-session.html';
   return route.fulfill({contentType:'text/html',path:path.join(fixtures,file)});
  }
  if(p==='/expiring')return route.fulfill({contentType:'text/html',body:`<html><body><p>Private shop screen</p><div id="idle-session" data-session="toast-expiry-check" data-timeout="28800000" data-remaining="${url.searchParams.get('duration')||10}" data-absolute-remaining="${url.searchParams.get('duration')||10}"></div><div id="idle-session-warning" hidden><span id="idle-session-warning-text"></span><button id="idle-session-continue">Stay signed in</button></div><form action="/Account/Logout" method="post"><button>Sign out</button></form><script src="/js/idle-session.js"></script></body></html>`});
  if(p==='/account/session/expire'){expires++;return route.fulfill({status:204});}
  if(p==='/Account/Logout')return route.fulfill({status:302,headers:{location:'/Account/Login'}});
  if(p==='/Account/ForgotPassword')return route.fulfill({contentType:'text/html',path:path.join(fixtures,'forgot.html')});
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
 const sessionToast=()=>page.locator('.sf-toast-info');
 await page.goto('http://stockflow.test/expiring');await page.clock.runFor(1100);await page.waitForURL('**/Account/Login');
 assert.equal(expires,1);await sessionToast().waitFor();assert.equal(await sessionToast().locator('p').textContent(),'Your session has expired. Please sign in again.');
 assert.equal(new URL(page.url()).searchParams.has('reason'),false);assert.equal(await page.locator('.sf-login-form .alert,.sf-login-content>.alert').count(),0);
 assert.equal(await page.locator('#sf-notifications').count(),1);assert.equal(await sessionToast().locator('button').evaluate(n=>getComputedStyle(n).color),'rgb(255, 255, 255)');await page.clock.runFor(5000);assert(await sessionToast().isVisible());await page.clock.runFor(600);assert.equal(await sessionToast().count(),0);
 await page.reload();assert.equal(await page.locator('.sf-toast').count(),0);
 await page.goto('http://stockflow.test/Account/Login?reason=idle&ReturnUrl=%2FSales%2FCreate%3Fa%3D1#sign-in');await sessionToast().waitFor();
 assert.equal(new URL(page.url()).searchParams.get('ReturnUrl'),'/Sales/Create?a=1');assert.equal(new URL(page.url()).hash,'#sign-in');
 await page.addScriptTag({path:path.join(assets,'js/login-notifications.js')});assert.equal(await sessionToast().count(),1,'Adapter is idempotent');
 await sessionToast().getByRole('button',{name:/Dismiss/}).click();assert.equal(await sessionToast().count(),0);await page.reload();assert.equal(await page.locator('.sf-toast').count(),0);
 for(const suffix of ['', '?reason=logout']){await page.goto('http://stockflow.test/Account/Login'+suffix);assert.equal(await page.locator('.sf-toast').count(),0,'Normal and first login are quiet');}
 // Another tab's normal logout closes this session without an expiry notification.
 await page.evaluate(()=>localStorage.clear());await page.goto('http://stockflow.test/expiring?duration=60000');
 const other=await context.newPage();await other.goto('http://stockflow.test/expiring?duration=60000');await other.locator('form button').click();await other.waitForURL('**/Account/Login');await page.clock.runFor(1000);await page.waitForURL('**/Account/Login');assert.equal(await page.locator('.sf-toast').count(),0);await other.close();
 // Keep native submit, CSRF, Remember Me, field validation and safe server errors.
 await page.locator('.sf-signin').click();assert.equal(posts.length,0);assert(await page.locator('[data-valmsg-for="Input.Username"],.sf-field-validation').first().isVisible());
 await page.locator('#Input_Username').fill('cashier');await page.locator('#Input_Password').fill('private-test-password');await page.locator('#Input_RememberMe').check();await page.locator('.sf-signin').click();await page.locator('.sf-toast-error').waitFor();
 assert.equal(posts.length,1);assert(posts[0].get('__RequestVerificationToken'));assert.deepEqual(posts[0].getAll('Input.RememberMe'),['true','false']);assert.match(await page.locator('.sf-toast-error').textContent(),/Invalid username or password/);assert.equal(await page.locator('#Input_Password').inputValue(),'');
 failure='unavailable';await page.locator('#Input_Username').fill('cashier');await page.locator('#Input_Password').fill('private-test-password');await page.locator('.sf-signin').click();await page.locator('.sf-toast-error').waitFor();assert.match(await page.locator('.sf-toast-error').textContent(),/temporarily unavailable/);assert(!((await page.locator('body').textContent()).includes('private-provider-detail')));
 await page.goto('http://stockflow.test/Account/Login');await context.setOffline(true);await page.locator('.sf-toast-error').waitFor();await page.evaluate(()=>dispatchEvent(new Event('offline')));assert.equal(await page.locator('.sf-toast-error').count(),1);
 await page.locator('#Input_Username').fill('cashier');await page.locator('#Input_Password').fill('password');const before=posts.length;await page.locator('.sf-signin').click();assert.equal(posts.length,before);assert.equal(await page.locator('#Input_Username').inputValue(),'cashier');await context.setOffline(false);assert.equal(await page.locator('.sf-toast-error').count(),0);
 await page.goto('http://stockflow.test/Account/Login?passwordChanged=True');await page.locator('.sf-toast-success').waitFor();await page.reload();assert.equal(await page.locator('.sf-toast').count(),0);
 for(const [width,height] of [[1920,1080],[1366,768],[1280,720],[1067,465],[768,1024],[375,667],[320,568]]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Account/Login?reason=absolute');await sessionToast().waitFor();
  const r=await page.locator('#sf-notifications').evaluate(n=>{const r=n.getBoundingClientRect(),fields=[...document.querySelectorAll('.sf-input-wrap,.sf-signin')].map(n=>n.getBoundingClientRect());return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,overflow:document.documentElement.scrollWidth>innerWidth,overlap:fields.some(f=>r.left<f.right&&r.right>f.left&&r.top<f.bottom&&r.bottom>f.top)};});
  assert(r.left>=0&&r.right<=width+1&&r.top>=0&&!r.overflow&&!r.overlap,JSON.stringify({width,height,...r}));assert.equal(await page.locator('#sf-notifications').count(),1);
  if([1366,375].includes(width))await page.screenshot({path:path.join(out,`session-toast-${width}.png`),fullPage:true});
 }
 assert.deepEqual(errors,[]);console.log('PASS: real expiry redirect, 5.5s dismissal, manual close, refresh-once, safe ReturnUrl, first login/normal logout (including other tabs), invalid credentials/service failures, required validation, native CSRF/Remember Me POST, offline guard/dedup/recovery, password-change notice and seven responsive viewports.');
 await context.close();
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
