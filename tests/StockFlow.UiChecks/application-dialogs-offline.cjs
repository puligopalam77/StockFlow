const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1200,height:900}});page.setDefaultTimeout(Number(process.env.STOCKFLOW_UI_TIMEOUT_MS||6000));const errors=[],native=[];page.on('pageerror',e=>errors.push(e.message));page.on('dialog',async d=>{native.push(d.type());await d.dismiss();});
 await page.route('**/*',route=>{const p=new URL(route.request().url()).pathname;if(p==='/next')return route.fulfill({body:'<h1>Products</h1>',contentType:'text/html'});if(p==='/edit')return route.fulfill({contentType:'text/html',body:`<html><head><link rel="stylesheet" href="/css/stockflow.css"><link rel="stylesheet" href="/css/dialog-standard.css"></head><body class="sf-app"><h1>Edit Product</h1><form id="draft"><label for="name">Name</label><input id="name" value="Helmet"><button type="submit">Save</button><button type="button" id="cancel">Cancel</button></form><button id="remove" type="button">Remove variant</button><div id="variant">Unsaved variant details</div><a href="/next" id="back">Back to Products</a><script src="/js/confirmation-dialog.js"></script><script src="/js/unsaved-changes.js"></script><script>
 document.getElementById('remove').onclick=async()=>{if(await StockFlowDialog.confirm('Remove this unsaved variant and its entered details?'))document.getElementById('variant').remove();};
 document.getElementById('draft').onsubmit=event=>{event.preventDefault();StockFlowUnsaved.markSaved(event.target);};
 document.getElementById('cancel').onclick=()=>{location.href='/next';};
 </script></body></html>`});const file=path.resolve(assets,'.'+p);return file.startsWith(assets+path.sep)&&fs.existsSync(file)?route.fulfill({path:file}):route.fulfill({status:404});});
 await page.goto('http://localhost:5220/edit');await page.locator('#remove').click();assert(await page.locator('#sf-confirm-description').getByText('Remove this unsaved variant and its entered details?',{exact:true}).isVisible());assert.equal(await page.locator('#sf-confirm-cancel').evaluate(n=>n===document.activeElement),true);await page.keyboard.press('Escape');assert(await page.locator('#variant').isVisible());assert.equal(await page.evaluate(()=>document.activeElement.id),'remove');
 await page.locator('#remove').click();await page.locator('#sf-confirm-accept').click();assert.equal(await page.locator('#variant').count(),0);
 await page.locator('#name').fill('Helmet draft');await page.locator('#back').click();await page.locator('#sf-confirm-dialog[open]').waitFor();assert.equal(await page.locator('#sf-confirm-cancel').textContent(),'Continue Editing');assert.equal(await page.locator('#sf-confirm-accept').textContent(),'Discard Changes');await page.locator('#sf-confirm-cancel').click();assert(page.url().endsWith('/edit'));assert.equal(await page.locator('#name').inputValue(),'Helmet draft');
 await page.locator('#cancel').click();await page.locator('#sf-confirm-dialog[open]').waitFor();await page.setViewportSize({width:320,height:640});assert(await page.locator('#sf-confirm-dialog').evaluate(n=>n.getBoundingClientRect().width<=innerWidth));fs.mkdirSync(path.join(root,'tests/bin/application-dialogs'),{recursive:true});await page.screenshot({path:path.join(root,'tests/bin/application-dialogs/discard-mobile.png')});await page.locator('#sf-confirm-accept').click();await page.waitForURL('**/next');
 await page.goto('http://localhost:5220/edit');await page.locator('#name').fill('Saved Helmet');await page.getByRole('button',{name:'Save',exact:true}).click();await page.locator('#back').click();await page.waitForURL('**/next');
 await page.goto('http://localhost:5220/edit');await page.locator('#name').fill('Temporary');await page.locator('#name').fill('Helmet');await page.locator('#back').click();await page.waitForURL('**/next');assert.deepEqual(native,[]);assert.deepEqual(errors,[]);
 // Exercise the discard refinement with the full shared button theme loaded.
 await page.goto('http://localhost:5220/edit');
 await page.addStyleTag({path:path.join(assets,'lib/bootstrap/dist/css/bootstrap.min.css')});
 await page.addStyleTag({path:path.join(assets,'css/button-theme.css')});
 await page.addStyleTag({path:path.join(assets,'css/responsive-foundations.css')});
 await page.addStyleTag({path:path.join(assets,'css/action-icons.css')});
 await page.addStyleTag({path:path.join(assets,'css/dialog-standard.css')});
 await page.addScriptTag({path:path.join(assets,'js/action-icons.js')});
 await page.evaluate(()=>{document.body.classList.add('sf-app');});
 await page.setViewportSize({width:1200,height:900});
 await page.locator('#name').fill('Keep my edits');await page.locator('#back').click();
 await page.locator('.sf-confirm-close.sf-action-icon').waitFor();
 for(const [width,height] of [[1366,768],[390,844],[320,640]]){
  await page.setViewportSize({width,height});
  const close=await page.locator('#sf-confirm-dialog').evaluate(dialog=>{const c=dialog.querySelector('.sf-confirm-close'),r=c.getBoundingClientRect(),d=dialog.getBoundingClientRect(),icon=dialog.querySelector('.sf-confirm-symbol').getBoundingClientRect();return {position:getComputedStyle(c).position,right:d.right-r.right,top:r.top-d.top,inside:r.left>=d.left&&r.right<=d.right&&r.top>=d.top&&r.bottom<=d.bottom,overlapsIcon:r.left<icon.right&&r.right>icon.left&&r.top<icon.bottom&&r.bottom>icon.top};});
  assert.equal(close.position,'absolute');assert(close.inside&&close.right>=0&&close.right<=16&&close.top>=0&&close.top<=16);assert.equal(close.overlapsIcon,false,'Close button must stay clear of the warning icon');
 }
 await page.setViewportSize({width:1200,height:900});
 assert.equal(await page.locator('#sf-confirm-dialog').getAttribute('data-kind'),'discard');
 assert.match(await page.locator('#sf-confirm-description').textContent(),/Edit Product/);
 const layout=await page.locator('#sf-confirm-dialog').evaluate(n=>{const icon=n.querySelector('.sf-confirm-symbol'),r=icon.getBoundingClientRect(),d=n.getBoundingClientRect();return {width:r.width,height:r.height,center:Math.abs(r.x+r.width/2-d.x-d.width/2),align:getComputedStyle(n.querySelector('h2')).textAlign};});
 assert.equal(layout.width,44);assert.equal(layout.height,44);assert(layout.center<1);assert.equal(layout.align,'center');
 for(const id of ['#sf-confirm-cancel','#sf-confirm-accept']){
  const base=id==='#sf-confirm-cancel'?'rgb(23, 27, 34)':'rgb(255, 255, 255)',hover=id==='#sf-confirm-cancel'?'rgb(48, 54, 64)':'rgb(248, 215, 218)',ink=id==='#sf-confirm-cancel'?'rgb(255, 255, 255)':'rgb(176, 42, 55)';
  const b=page.locator(id);await page.mouse.move(0,0);await page.waitForTimeout(220);
  assert.deepEqual(await b.evaluate(n=>[getComputedStyle(n).backgroundColor,getComputedStyle(n).color]),[base,ink]);
  await b.hover();await page.waitForTimeout(220);
  assert.deepEqual(await b.evaluate(n=>[getComputedStyle(n).backgroundColor,getComputedStyle(n).color]),[hover,ink]);
  await b.evaluate(n=>n.disabled=true);await page.waitForTimeout(220);
  assert.equal(await b.evaluate(n=>getComputedStyle(n).backgroundColor),base);await b.evaluate(n=>{n.disabled=false;n.setAttribute('aria-busy','true');});await page.waitForTimeout(220);assert.equal(await b.evaluate(n=>getComputedStyle(n).backgroundColor),base);await b.evaluate(n=>n.removeAttribute('aria-busy'));
 }
 const heights=await page.locator('.sf-confirm-actions .btn').evaluateAll(ns=>ns.map(n=>n.getBoundingClientRect().height));assert.equal(heights[0],heights[1]);
 await page.keyboard.press('Tab');assert(await page.evaluate(()=>getComputedStyle(document.activeElement).outlineStyle!=='none'));
 await page.mouse.click(5,5);assert(await page.locator('#sf-confirm-dialog').evaluate(n=>n.open));
 await page.locator('.sf-confirm-close').click();assert.equal(await page.locator('#name').inputValue(),'Keep my edits');assert(page.url().endsWith('/edit'));
 await page.locator('#back').click();await page.keyboard.press('Escape');assert.equal(await page.locator('#name').inputValue(),'Keep my edits');assert(page.url().endsWith('/edit'));
 await page.evaluate(()=>{document.querySelector('#draft').dataset.unsavedContext='New Purchase';});await page.locator('#back').click();assert.match(await page.locator('#sf-confirm-description').textContent(),/New Purchase/);
 await page.setViewportSize({width:320,height:640});assert(await page.locator('#sf-confirm-dialog').evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&n.scrollWidth===n.clientWidth;}));
 fs.mkdirSync(path.join(root,'tests/bin/discard-dialog'),{recursive:true});await page.screenshot({path:path.join(root,'tests/bin/discard-dialog/mobile.png')});
 await page.setViewportSize({width:1200,height:900});await page.mouse.move(0,0);await page.waitForTimeout(220);await page.screenshot({path:path.join(root,'tests/bin/discard-dialog/desktop.png')});
 await page.locator('#sf-confirm-accept').click();await page.waitForURL('**/next');
 assert.deepEqual(native,[]);assert.deepEqual(errors,[]);
 await page.goto('http://localhost:5220/edit');await page.locator('#name').fill('Retain on Enter');await page.locator('#back').click();await page.keyboard.press('Enter');assert(page.url().endsWith('/edit'));assert.equal(await page.locator('#name').inputValue(),'Retain on Enter');await page.locator('#back').click();for(const key of ['Tab','Tab','Tab','Tab','Tab','Tab','Shift+Tab','Shift+Tab','Shift+Tab','Shift+Tab','Shift+Tab','Shift+Tab']){await page.keyboard.press(key);assert(await page.evaluate(()=>document.querySelector('#sf-confirm-dialog').contains(document.activeElement)),'Native modal traps keyboard focus');}await page.keyboard.press('Escape');assert.equal(await page.locator('#name').inputValue(),'Retain on Enter');
 console.log('PASS: removal, discard navigation, cancel/X/Escape preservation, focus, save/revert bypass, contextual copy, 44px centered icon, safe charcoal primary/danger outline buttons, disabled states, backdrop protection, mobile bounds and zero browser alerts.');
 }finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1});
