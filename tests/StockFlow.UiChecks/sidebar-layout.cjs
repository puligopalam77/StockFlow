// Real Razor navigation + shared shell; no application/database writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const out=path.resolve(__dirname,'../../.artifacts/sidebar-compact');fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true}),results=[],errors=[];try{
 const page=await browser.newPage({reducedMotion:'reduce'});page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*.js',r=>/\/stockflow-shell(\.[a-z0-9]{10})?\.js$/.test(new URL(r.request().url()).pathname)?r.continue():r.fulfill({contentType:'text/javascript',body:''}));
 const measure=()=>page.evaluate(()=>{
  const side=document.querySelector('.sf-sidebar'),nav=side.querySelector('.sf-shop-nav'),workspace=document.querySelector('.sf-workspace'),header=document.querySelector('.sf-topbar');
  const links=[...nav.querySelectorAll('.sf-nav-link,.sf-nav-child')].filter(n=>n.getBoundingClientRect().width>0);
  return {token:getComputedStyle(document.documentElement).getPropertyValue('--sf-sidebar').trim(),side:side.getBoundingClientRect().width,offset:workspace.getBoundingClientRect().x,workspaceWidth:workspace.getBoundingClientRect().width,headerHeight:header.getBoundingClientRect().height,headerX:header.getBoundingClientRect().x,clientWidth:document.documentElement.clientWidth,bodyWidth:document.body.getBoundingClientRect().width,pageHeight:document.documentElement.scrollHeight,overflow:document.documentElement.scrollWidth>innerWidth+1||nav.scrollWidth>nav.clientWidth+1,badRows:links.filter(n=>n.scrollWidth>n.clientWidth+1||n.scrollHeight>n.clientHeight+1).map(n=>n.textContent.trim()),badArrows:links.filter(n=>{const c=n.querySelector('.sf-nav-chevron');if(!c)return false;const r=n.getBoundingClientRect(),a=c.getBoundingClientRect();return Math.abs(a.right-r.right+parseFloat(getComputedStyle(n).paddingRight))>1||Math.abs(a.width-14)>1;}).map(n=>n.textContent.trim())};
 });
 const check=(m,baseline)=>{assert.equal(m.side,230);assert.equal(m.token,'230px');assert.equal(m.offset,230);assert.equal(m.headerX,230);assert.equal(m.headerHeight,50);assert(!m.overflow,JSON.stringify(m));assert.deepEqual(m.badRows,[]);assert.deepEqual(m.badArrows,[]);if(baseline){assert.equal(m.workspaceWidth,baseline.workspaceWidth);assert.equal(m.pageHeight,baseline.pageHeight);}};
 for(const route of ['/','/Sales/Create','/Inventory','/Purchases','/Customers','/Reports/Activity','/Settings/Company?section=business'])for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768]]){
  await page.setViewportSize({width,height});await page.goto('http://127.0.0.1:55863'+route);
  await page.locator('.sf-shop-nav details').evaluateAll(ns=>ns.forEach(n=>n.open=false));
  const baseline=await measure();check(baseline);const groups=page.locator('.sf-shop-nav>details'),states=[];
  for(let i=0;i<await groups.count();i++){
   const group=groups.nth(i);await group.evaluate(n=>{n.open=true;for(const child of n.querySelectorAll('details'))child.open=true;});
   check(await measure(),baseline);states.push(await group.getAttribute('data-nav-group'));
   await group.evaluate(n=>n.open=false);
  }
  await page.locator('.sf-shop-nav details').evaluateAll(ns=>ns.forEach(n=>n.open=true));check(await measure(),baseline);
  const long=page.locator('.sf-shop-nav .sf-nav-child[href]').last(),old=await long.textContent();await long.evaluate(n=>n.textContent='Stock valuation and inventory movement reconciliation report');
  assert((await long.boundingBox()).height>36,'Long submenu label wraps');check(await measure(),baseline);await long.evaluate((n,text)=>n.textContent=text,old);
  const openCount=await page.locator('.sf-shop-nav details[open]').count();await page.locator('#sidebar-toggle').click();await page.waitForFunction(()=>document.querySelector('.sf-workspace').getBoundingClientRect().x===0);
  const collapsed=await measure();assert.equal(collapsed.offset,0);assert.equal(collapsed.workspaceWidth,collapsed.bodyWidth);assert.equal(collapsed.headerX,0);assert.equal(collapsed.side,230);assert(!collapsed.overflow);assert(await page.locator('.sf-sidebar').evaluate(n=>n.inert));
  await page.locator('#sidebar-toggle').click();await page.waitForFunction(()=>document.querySelector('.sf-workspace').getBoundingClientRect().x===230);check(await measure(),baseline);assert.equal(await page.locator('.sf-shop-nav details[open]').count(),openCount);
  if(route==='/'&&width===1366){
   for(let i=0;i<await groups.count();i++){const summary=groups.nth(i).locator(':scope>summary');await summary.click();assert.equal(await groups.nth(i).evaluate(n=>n.open),false);await summary.press('Enter');assert.equal(await groups.nth(i).evaluate(n=>n.open),true);}
   await page.locator('.sf-sidebar').screenshot({path:path.join(out,'expanded-1366.png')});
  }
  results.push({route,width,height,groups:states,...baseline});
 }
 for(const width of [768,390,320]){
  await page.setViewportSize({width,height:844});await page.goto('http://127.0.0.1:55863/');await page.locator('#sidebar-toggle').click();await page.waitForFunction(()=>document.body.classList.contains('sf-nav-open'));
  assert.equal((await measure()).offset,0);assert((await measure()).side<=width-32);await page.keyboard.press('Escape');assert(!(await page.locator('body').evaluate(n=>n.classList.contains('sf-nav-open'))));
 }
 assert.deepEqual(errors,[]);fs.writeFileSync(path.join(out,'measurements.json'),JSON.stringify(results,null,2));
 console.log('PASS: '+results.length+' page/viewport combinations; every top-level submenu alone/all open, 230px width/offset, long-label wrapping, aligned chevrons, no overflow/content shift, collapse/restore, mouse/keyboard toggles and three mobile drawers.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
