// Semantic component states against 26 real Razor list-page style combinations.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/list-screen-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:900},reducedMotion:'reduce'});
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname,fixture=path.join(fixtures,p.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(fixture))return route.fulfill({path:fixture,contentType:'text/html'});if(p.endsWith('.js'))return route.fulfill({contentType:'text/javascript',body:''});const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});});
 const white='rgb(255, 255, 255)',pink='rgb(255, 8, 125)',neutral='rgb(241, 244, 250)',ink='rgb(16, 29, 56)';
 const variants=[['btn-primary',pink,'rgb(223, 0, 99)','rgb(198, 0, 90)'],['btn-outline-secondary',white,neutral,'rgb(230, 235, 243)'],['btn-outline-primary',white,neutral,'rgb(230, 235, 243)'],['btn-dark','rgb(23, 27, 34)','rgb(48, 54, 64)','rgb(9, 14, 24)'],['btn-danger','rgb(220, 53, 69)','rgb(176, 42, 55)','rgb(132, 32, 41)'],['btn-outline-danger',white,'rgb(248, 215, 218)','rgb(241, 174, 181)'],['btn-link','rgba(0, 0, 0, 0)',neutral,'rgb(230, 235, 243)']];
 const names=['Sales','Purchases','StockInflow','StockOutflow','Inventory','Inventory-LowStock','Products','Products-Variants','Categories','Brands','Suppliers','Customers','Admin-Users','Admin-AuditLogs','Admin-Stores','Service','Service-Pending','Warranty-Lookup','Warranty-Claims','Warranty-Expiring','Transfers','Account-Sessions','Search','Reports-Stores','Reports-Activity','Reports-ProductSales'];
 for(const name of names){
  await page.goto('http://stockflow.test/'+name.replaceAll('-','/'));
  await page.evaluate(variants=>{const host=document.createElement('div');host.id='button-theme-check';host.innerHTML=variants.map(([c])=>`<button class="btn ${c}">${c}</button>`).join('')+'<button id="native-submit" type="submit">Native</button><button role="tab">Tab</button><button class="sf-select-option">Option</button><input type="file"><input type="checkbox">';document.querySelector('.sf-main').prepend(host);},variants);
  const buttons=page.locator('#button-theme-check .btn');
  for(let i=0;i<variants.length;i++){
   const [cls,base,hover,active]=variants[i],b=buttons.nth(i),color=()=>b.evaluate(n=>getComputedStyle(n).backgroundColor);
   await page.mouse.move(0,0);assert.equal(await color(),base,`${name} ${cls} base`);
   const box=await b.boundingBox();await b.hover();assert.equal(await color(),hover,`${name} ${cls} hover`);
   await page.mouse.down();assert.equal(await color(),active,`${name} ${cls} active`);await page.mouse.up();
   assert.deepEqual(await b.boundingBox(),box,`${name} ${cls} no layout shift`);
   await b.evaluate(n=>n.disabled=true);assert.equal(await color(),base);assert.equal(await b.evaluate(n=>getComputedStyle(n).opacity),'0.55');await b.evaluate(n=>{n.disabled=false;n.setAttribute('aria-busy','true');});assert.equal(await color(),base,'Busy control retains its base color');await b.evaluate(n=>n.removeAttribute('aria-busy'));
   assert.equal(await b.evaluate(n=>getComputedStyle(n).transitionDuration),'0s','Reduced motion');
  }
  await buttons.first().focus();await page.keyboard.press('Tab');assert(await buttons.nth(1).evaluate(n=>n.matches(':focus-visible')&&parseFloat(getComputedStyle(n).outlineWidth)>=2));
  assert.notEqual(await page.locator('#native-submit').evaluate(n=>getComputedStyle(n).backgroundColor),pink,'Native submit is not assigned primary semantics');
  assert.notEqual(await page.locator('#button-theme-check [role=tab]').evaluate(n=>getComputedStyle(n).backgroundColor),pink,'Tab excluded');
 }
 await page.setContent('<body class="sf-auth-body"><button class="sf-signin">Sign in</button></body>');await page.addStyleTag({path:path.join(assets,'css/button-theme.css')});assert.equal(await page.locator('.sf-signin').evaluate(n=>getComputedStyle(n).backgroundColor),pink);await page.locator('.sf-signin').hover();assert.equal(await page.locator('.sf-signin').evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(223, 0, 99)');
 console.log('PASS: seven semantic variants across 26 real list-page style combinations; hover, active, disabled, busy, keyboard focus, reduced motion, stable geometry and control exclusions. No APIs or database accessed.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
