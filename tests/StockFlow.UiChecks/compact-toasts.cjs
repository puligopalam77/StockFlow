// Shared toast presentation against rendered Razor fixtures and real shared assets.
// Business requests are excluded; notification behavior is covered by the existing suites.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const screens={login:'password-page-fixtures/login.html',billing:'billing-ui-fixtures/Billing.html',inventory:'list-screen-fixtures/Inventory.html',purchases:'list-screen-fixtures/Purchases.html',receiving:'list-screen-fixtures/StockInflow.html',expenses:'expense-list-fixtures/Index.html',validation:'list-screen-fixtures/Products.html'};
const sizes=[[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844],[320,568]];
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname;
  if(p.startsWith('/check/')){const file=screens[p.split('/').at(-1)];let html=fs.readFileSync(path.join(root,'tests/bin',file),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');html=html.replace('</body>','<script src="/js/notifications-ui.js"></script><script src="/js/action-icons.js"></script></body>');return route.fulfill({contentType:'text/html',body:html});}
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
 for(const screen of Object.keys(screens))for(const [width,height] of sizes){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/check/'+screen);
  const before=await page.evaluate(()=>document.documentElement.scrollWidth);
  for(const type of ['error','success','warning','info']){
   await page.evaluate(type=>StockFlowNotify.show(type,'Invalid username or password.',{key:'compact-check',duration:0}),type);
   await page.locator('.sf-toast-close.sf-action-icon').waitFor();
   const actual=await page.locator('.sf-toast').evaluate(n=>{const r=n.getBoundingClientRect(),host=n.parentElement.getBoundingClientRect(),icon=n.querySelector('svg'),title=n.querySelector('strong'),message=n.querySelector('p'),close=n.querySelector('button');return {width:r.width,height:r.height,left:host.left,right:host.right,top:host.top,bottom:host.bottom,icon:icon.getBoundingClientRect().width,title:getComputedStyle(title).fontSize,message:getComputedStyle(message).fontSize,color:getComputedStyle(close).color,flag:getComputedStyle(n.querySelector('.sf-toast-flag')).backgroundColor,viewport:document.documentElement.clientWidth,overflow:document.documentElement.scrollWidth,role:n.getAttribute('role'),text:message.textContent};});
   assert(actual.width<=Math.min(360,actual.viewport-32)+1&&actual.width>=Math.min(360,actual.viewport-52)-1,JSON.stringify({screen,width,type,actual}));
   assert(actual.height>=64&&actual.height<=100&&actual.left>=16&&actual.right<=actual.viewport-16&&actual.top===16&&actual.bottom<=height-16,JSON.stringify({screen,width,type,actual}));
   assert.equal(actual.icon,20);assert.equal(actual.title,'14px');assert.equal(actual.message,'13px');assert.equal(actual.color,'rgb(255, 255, 255)');assert.equal(actual.flag,'rgba(0, 0, 0, 0)');assert.equal(actual.text,'Invalid username or password.');assert.equal(actual.role,type==='error'||type==='warning'?'alert':'status');assert.equal(actual.overflow,before,'Toast causes no new horizontal overflow');
   await page.getByRole('button',{name:/Dismiss .* notification/}).click();assert.equal(await page.locator('.sf-toast').count(),0);
  }
  await page.evaluate(()=>{for(const type of ['success','warning','info'])StockFlowNotify.show(type,'Operation completed.',{duration:0});});
  assert.equal(await page.locator('#sf-notifications').count(),1);assert.equal(await page.locator('.sf-toast').count(),3);
  const cards=await page.locator('.sf-toast').evaluateAll(nodes=>nodes.map(n=>{const r=n.getBoundingClientRect();return {top:r.top,bottom:r.bottom};}));assert.equal(Math.round(cards[1].top-cards[0].bottom),10);
  await page.evaluate(()=>{for(const n of document.querySelectorAll('.sf-toast'))StockFlowNotify.dismiss(n.dataset.notificationKey);StockFlowNotify.show('error','Detailed product information: '+('LongUnbrokenProductCode'.repeat(20)),{key:'long',duration:0});});
  await page.getByText('Show details',{exact:true}).click();
  assert.equal(await page.locator('.sf-toast details p').textContent(),'Detailed product information: '+('LongUnbrokenProductCode'.repeat(20)));
  assert(await page.locator('.sf-toast-copy').evaluate(n=>n.scrollWidth<=n.clientWidth+1));
  if(screen==='login'&&[1366,390].includes(width)){await page.evaluate(()=>{StockFlowNotify.dismiss('long');StockFlowNotify.show('error','Invalid username or password.',{duration:0});});fs.mkdirSync(path.join(root,'.artifacts/compact-toasts'),{recursive:true});await page.screenshot({path:path.join(root,`.artifacts/compact-toasts/login-${width}.png`)});}
 }
 const touch=await browser.newPage({viewport:{width:390,height:844},isMobile:true,hasTouch:true});
 await touch.setContent('<meta name="viewport" content="width=device-width,initial-scale=1"><body></body>');
 for(const css of ['stockflow.css','button-theme.css','action-icons.css'])await touch.addStyleTag({path:path.join(assets,'css',css)});
 await touch.addScriptTag({path:path.join(assets,'js/notifications-ui.js')});await touch.addScriptTag({path:path.join(assets,'js/action-icons.js')});
 await touch.evaluate(()=>StockFlowNotify.show('info','Saved successfully.',{duration:0}));await touch.locator('.sf-toast-close.sf-action-icon').waitFor();
 const close=await touch.locator('.sf-toast-close').boundingBox();assert(close.width>=44&&close.height>=44);await touch.locator('.sf-toast-close').tap();assert.equal(await touch.locator('.sf-toast').count(),0);await touch.close();
 assert.deepEqual(errors,[]);console.log('PASS: four toast types across seven rendered screens and eight resolutions (224 checks), exact login error, compact dimensions, colors, readable close icons, manual dismissal, one container, three-toast stacking, long details without overflow and 44px touch dismissal.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
