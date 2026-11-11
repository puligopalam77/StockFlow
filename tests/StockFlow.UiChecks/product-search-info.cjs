// Presentation-only touch checks; full search/billing workflows use fast-billing-offline.cjs.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 // Reduced CSS viewport plus increased DPR models desktop browser zoom;
 // this is zoom-equivalent emulation, not a browser-toolbar zoom setting.
 const cases=[];for(const zoom of [1,1.25,1.5])for(const [width,height] of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844]])cases.push({width:Math.floor(width/zoom),height:Math.floor(height/zoom),zoom,touch:false});
 for(const width of [390,768])cases.push({width,height:844,zoom:1,touch:true});
 for(const {width,height,zoom,touch} of cases){
  const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:zoom,hasTouch:touch,isMobile:touch}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{const u=new URL(route.request().url());if(route.request().method()!=='GET')return route.fulfill({status:405});if(u.pathname==='/Sales/Create')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),'utf8')});if(u.pathname.endsWith('.js')&&!/\/(stockflow-shell|billing-layout|billing-customer|bootstrap\.bundle\.min)(\.[a-z0-9]{10})?\.js$/.test(u.pathname))return route.fulfill({contentType:'text/javascript',body:''});const file=path.join(root,'src/StockFlow.Web/wwwroot',u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});});
  await page.goto('http://stockflow.test/Sales/Create');
  // The business initializer is intentionally excluded; enable controls only for this isolated UI check.
  await page.locator('#sale-editor').evaluate(n=>n.disabled=false);
  const info=page.locator('#sale-search-info'),tip=page.locator('.billing-search-tooltip');await info.scrollIntoViewIfNeeded();
  const original=await info.getAttribute('data-bs-title'),before=await page.locator('#sale-scan').boundingBox();
  const box=await info.boundingBox();assert.equal(box.width,16);
  const gap=await info.evaluate(n=>{const group=n.closest('.billing-search-heading'),label=group.querySelector('h2').getBoundingClientRect(),icon=n.getBoundingClientRect();return {css:getComputedStyle(group).gap,actual:icon.left-label.right,center:Math.abs(icon.y+icon.height/2-label.y-label.height/2)};});assert.equal(gap.css,'5px');assert(Math.abs(gap.actual-5)<1&&gap.center<1);
  if(touch){assert.equal(await info.evaluate(n=>getComputedStyle(n,'::before').right),'-14px');await page.touchscreen.tap(box.x+box.width+12,box.y+box.height/2);}else await info.hover();await tip.waitFor({state:'visible'});
  assert(await tip.evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}));
  assert.equal(await tip.locator('.tooltip-inner').textContent(),original);assert.equal(await tip.evaluate(n=>getComputedStyle(n).pointerEvents),'none');
  const size=await tip.boundingBox();assert(size.width<=321&&size.width<=width-15,JSON.stringify({width,zoom,size}));if(width>=340)assert(Math.abs(size.width-320)<1);
  if(height-box.y-box.height>=size.height+8)assert((await tip.getAttribute('data-popper-placement')).startsWith('bottom'));
  assert.deepEqual(await page.locator('#sale-scan').boundingBox(),before,'Help introduces no field layout change');
  if(touch)await page.locator('#sale-scan').tap();else await page.locator('#sale-scan').click();await tip.waitFor({state:'hidden'});assert(await page.locator('#sale-scan').evaluate(n=>document.activeElement===n));
  if(!touch){
   await info.focus();await tip.waitFor({state:'visible'});await page.keyboard.press('Escape');await tip.waitFor({state:'hidden'});
   await info.click();await tip.waitFor({state:'visible'});await page.keyboard.press('Escape');await tip.waitFor({state:'hidden'});await info.evaluate(n=>n.blur());await page.mouse.move(0,0);
   // Force a near-edge anchor to verify Popper flips/clamps rather than clipping.
   await info.evaluate(n=>{n.style.position='fixed';n.style.top=(innerHeight-28)+'px';n.style.left=(innerWidth-28)+'px';});await info.hover();await tip.waitFor({state:'visible'});
   const edge=await tip.evaluate(n=>{const r=n.getBoundingClientRect();return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:innerWidth,height:innerHeight,placement:n.dataset.popperPlacement};});assert(edge.left>=0&&edge.top>=0&&edge.right<=edge.width&&edge.bottom<=edge.height,JSON.stringify({zoom,...edge}));assert(!edge.placement.startsWith('bottom'));await page.keyboard.press('Escape');
  }
  assert.deepEqual(errors,[]);await page.close();
 }
 console.log('PASS: seven desktop/tablet/mobile sizes at 100% and 125%/150% zoom-equivalent viewport/DPR settings; 5px icon gap, unchanged content/field bounds, 320px responsive width, bottom preference, edge collision fallback, hover/focus/click/Escape and two 44px touch-target checks.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
