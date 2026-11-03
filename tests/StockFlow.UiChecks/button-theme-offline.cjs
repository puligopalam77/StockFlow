// Check the shared action colors against actual rendered list-page styles.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/list-screen-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:900}});
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname,fixture=path.join(fixtures,p.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(fixture))return route.fulfill({path:fixture,contentType:'text/html'});if(p.endsWith('.js'))return route.fulfill({contentType:'text/javascript',body:''});const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});});
 const charcoal='rgb(23, 27, 34)',pink='rgb(255, 0, 127)';
 for(const file of ['Sales','Purchases','StockInflow','StockOutflow','Inventory','Inventory-LowStock','Products','Products-Variants','Categories','Brands','Suppliers','Customers','Admin-Users','Admin-AuditLogs','Admin-Stores','Service','Service-Pending','Warranty-Lookup','Warranty-Claims','Warranty-Expiring','Transfers','Account-Sessions','Search','Reports-Stores','Reports-Activity','Reports-ProductSales'].map(name=>name+'.html')){
  await page.goto('http://stockflow.test/'+file.replace(/\.html$/,'').replaceAll('-','/'));
  assert.equal(await page.locator('link[href*="button-theme."]').count(),1,file+' loads shared theme');
  await page.evaluate(()=>{const host=document.createElement('div');host.id='button-theme-check';host.innerHTML='<button class="btn btn-primary">Primary</button><button class="btn btn-outline-secondary">Secondary</button><a href="#" class="btn btn-dark">Action link</a><button class="btn btn-danger">Destructive</button><button type="submit">Submit</button><button class="btn" disabled>Disabled</button><button role="tab">Tab</button>';document.querySelector('.sf-main').prepend(host);});
  const actions=page.locator('#button-theme-check :is(.btn,button[type=submit])');
  for(const color of await actions.evaluateAll(nodes=>nodes.map(n=>getComputedStyle(n).backgroundColor)))assert.equal(color,charcoal,file+' default');
  const button=actions.first();await button.hover();await page.waitForTimeout(200);assert.equal(await button.evaluate(n=>getComputedStyle(n).backgroundColor),pink,file+' hover');await page.mouse.move(0,0);await page.waitForTimeout(200);assert.equal(await button.evaluate(n=>getComputedStyle(n).backgroundColor),charcoal,file+' hover-out');
  await actions.last().hover({force:true});assert.equal(await actions.last().evaluate(n=>getComputedStyle(n).backgroundColor),charcoal,file+' disabled');
  await button.focus();await page.keyboard.press('Tab');const focused=actions.nth(1);assert(await focused.evaluate(n=>n.matches(':focus-visible')&&parseFloat(getComputedStyle(n).outlineWidth)>=2),file+' keyboard focus');
  assert.notEqual(await page.locator('#button-theme-check [role=tab]').evaluate(n=>getComputedStyle(n).backgroundColor),charcoal,file+' tab unchanged');
 }
 await page.setContent('<body class="sf-auth-body"><button type="submit" class="sf-signin">Sign in</button></body>');await page.addStyleTag({path:path.join(assets,'css/button-theme.css')});await page.waitForTimeout(200);assert.equal(await page.locator('.sf-signin').evaluate(n=>getComputedStyle(n).backgroundColor),charcoal);await page.locator('.sf-signin').hover();await page.waitForTimeout(200);assert.equal(await page.locator('.sf-signin').evaluate(n=>getComputedStyle(n).backgroundColor),pink);
 console.log('PASS: charcoal defaults, pink hover, hover-out, disabled and keyboard focus across list-screen styles; navigation/tab styling excluded; sign-in action included. No APIs or database accessed.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
