// Real shared Razor header and scripts; account writes are blocked.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const out=path.resolve(__dirname,'../../.artifacts/profile-menu');fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});const results=[],errors=[],writes=[];try{
 const page=await browser.newPage({deviceScaleFactor:1});page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',r=>{const req=r.request(),p=new URL(req.url()).pathname;
  if(req.method()!=='GET'){writes.push(p);return r.fulfill({status:405});}
  if(['/Account/Sessions','/Account/TwoFactor'].includes(p))return r.fulfill({contentType:'text/html',body:'<h1>Account route fixture</h1>'});
  if(p==='/api/screenlanguages')return r.fulfill({contentType:'application/json',body:'[]'});
  if(p==='/api/notifications')return r.fulfill({contentType:'application/json',body:'{"unread":3,"items":[]}'});
  if(p.endsWith('.js')&&!/\/(stockflow-shell|help-panel|responsive-shell|language-selector|notifications|action-icons|select|select-migrations|language|screen-language-catalog)(\.[a-z0-9]{10})?\.js$/.test(p))return r.fulfill({contentType:'text/javascript',body:''});return r.continue();
 });
 const header=()=>page.locator('.sf-topbar').evaluate(n=>Object.fromEntries(['.sf-menu-toggle','.sf-language-control .sf-select-trigger','.sf-notification-bell','#help-open','.sf-admin-icon','.sf-profile-trigger'].map(q=>[q,n.querySelector(q).getBoundingClientRect().toJSON()]).concat([['height',n.getBoundingClientRect().height]])));
 for(const route of ['/','/Sales/Create'])for(const [width,height] of [[1280,720],[1366,768],[1536,864],[1920,1080],[768,1024],[390,844],[320,568],[1280,240],[390,240]]){
  await page.setViewportSize({width,height});await page.goto('http://127.0.0.1:55863'+route);await page.locator('.sf-language-control .sf-select-trigger').waitFor();
  const before=await header(),main=await page.locator('.sf-main').boundingBox(),scroll=await page.evaluate(()=>scrollY);assert.equal(before.height,50);assert.equal(await page.evaluate(()=>devicePixelRatio),1);
  const trigger=page.locator('.sf-profile-trigger'),menu=page.locator('.sf-profile-panel');await trigger.click();
  const m=await menu.evaluate(n=>{const r=n.getBoundingClientRect(),s=getComputedStyle(n);return {rect:r.toJSON(),padding:s.padding,radius:s.borderRadius,scrolling:n.scrollHeight>n.clientHeight,overflow:s.overflowY,rows:[...n.children].map(e=>({text:e.textContent.trim().replace(/\s+/g,' ').replace(/\s*\u203a$/,'').trim(),height:e.getBoundingClientRect().height,font:getComputedStyle(e).fontSize})),icons:[...n.querySelectorAll('svg')].map(e=>e.getBoundingClientRect().width)};});
  const clientWidth=await page.evaluate(()=>document.documentElement.clientWidth),t=await trigger.boundingBox();assert(m.rect.left>=0&&m.rect.right<=clientWidth&&m.rect.top>=50&&m.rect.bottom<=height,JSON.stringify({route,width,height,m}));
  assert(m.rect.width>=180&&m.rect.width<280&&m.rect.width<=clientWidth-24,'Menu fits its labels without excess width');assert(await menu.evaluate(n=>[...n.querySelectorAll('button')].every(b=>{const label=b.querySelector('span'),arrow=b.querySelector('span:last-child');return Math.abs(arrow.getBoundingClientRect().left-label.getBoundingClientRect().right-10)<1;})),'Arrows follow labels with a 10px gap');if(width>=700){assert(Math.abs(m.rect.right-(t.x+t.width))<1,'Right aligned');assert.equal(m.rect.top-(t.y+t.height),6,'Trigger gap');}assert.equal(m.padding,'8px');assert.equal(m.radius,'10px');assert.equal(m.overflow,'auto');
  assert.equal(await trigger.evaluate(n=>getComputedStyle(n).borderColor),'rgb(255, 179, 209)');assert.equal(m.rows[0].height,40);assert.equal(await trigger.evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(255, 240, 246)');assert.equal(await trigger.evaluate(n=>getComputedStyle(n).gap),'8px');
  assert.deepEqual(m.rows.map(x=>x.text),['My profile','Devices signed in','Profile Details','Change password','Phone sign-in verification','Logout']);for(const row of m.rows.slice(1)){assert(row.height>=40&&row.height<=44);assert.equal(row.font,'13px');}assert(m.icons.every(x=>x===16));assert.equal(m.scrolling,height===240);
  assert.deepEqual(await header(),before);assert.deepEqual(await page.locator('.sf-main').boundingBox(),main);assert.equal(await page.evaluate(()=>scrollY),scroll);assert(!(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)));
  await page.keyboard.press('Escape');assert(!(await page.locator('#profile-menu').evaluate(n=>n.open)));assert(await page.evaluate(w=>document.activeElement===(w<=991?document.querySelector('#sidebar-toggle'):document.querySelector('.sf-profile-trigger')),width),'Existing Escape focus behavior');
  await trigger.press('Enter');await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.getAttribute('href')),'/Account/Sessions');assert(await page.locator('.sf-profile-panel a').first().evaluate(n=>getComputedStyle(n).outlineStyle==='solid'));
  for(let i=0;i<4;i++)await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.id),'profile-logout');const logout=await page.locator('#profile-logout').boundingBox();assert(logout.y>=m.rect.top&&logout.y+logout.height<=m.rect.bottom);
  await page.keyboard.press('Escape');await trigger.click();await page.locator('.sf-main').click({position:{x:5,y:5}});assert(!(await page.locator('#profile-menu').evaluate(n=>n.open)));
  await trigger.click();await page.locator('#sidebar-toggle').focus();assert(!(await page.locator('#profile-menu').evaluate(n=>n.open)));
  if(height>=568){await trigger.click();const row=page.locator('#open-profile-details');await row.hover();const colors=await row.evaluate(n=>({bg:getComputedStyle(n).backgroundColor,text:getComputedStyle(n).color}));assert.deepEqual(colors,{bg:'rgb(241, 244, 249)',text:'rgb(20, 35, 63)'});if(width===1366&&route==='/')await menu.screenshot({path:path.join(out,'menu-1366.png')});await page.keyboard.press('Escape');}
  results.push({route,width,height,...m});
 }
 // Exercise existing handlers without signing out or changing an account.
 for(const width of [1366,320]){
  await page.setViewportSize({width,height:768});await page.goto('http://127.0.0.1:55863/');
  for(const [button,dialog] of [['#open-profile-details','#profile-details-dialog'],['#open-change-password','#change-password-dialog']]){
   await page.locator('.sf-profile-trigger').click();await page.locator(button).click();assert(await page.locator(dialog).evaluate(n=>n.open));assert(!(await page.locator('#profile-menu').evaluate(n=>n.open)));await page.keyboard.press('Escape');assert(await page.locator('.sf-profile-trigger').evaluate(n=>n===document.activeElement));
  }
  await page.locator('.sf-profile-trigger').click();await page.locator('#open-change-password').click();await page.locator('#profile-current-password').fill('Synthetic-current');await page.locator('#profile-new-password').fill('Synthetic-new-123');await page.locator('#profile-confirm-password').fill('Synthetic-other-123');await page.locator('#profile-password-form button[type=submit]').click();assert.equal(await page.locator('#profile-password-message').textContent(),'Passwords do not match.');await page.keyboard.press('Escape');await page.waitForFunction(()=>document.querySelector('#profile-new-password').value==='');assert(!(await page.locator('#change-password-dialog').evaluate(n=>n.open)));
  await page.locator('.sf-profile-trigger').click();await page.locator('#profile-logout').click();assert(await page.locator('#logout-confirmation').evaluate(n=>n.open));await page.locator('#logout-cancel').click();assert(await page.locator('.sf-profile-trigger').evaluate(n=>n===document.activeElement));
  for(const href of ['/Account/Sessions','/Account/TwoFactor']){await page.locator('.sf-profile-trigger').click();await page.locator('.sf-profile-panel a[href="'+href+'"]').click();assert.equal(new URL(page.url()).pathname,href);await page.goto('http://127.0.0.1:55863/');}
 }
 assert.deepEqual(errors,[]);assert.deepEqual(writes,[]);fs.writeFileSync(path.join(out,'measurements.json'),JSON.stringify(results,null,2));console.log('PASS: '+results.length+' profile layouts, compact rows, bounded scrolling, unchanged header/content, menu order, keyboard/outside/focus dismissal, account routes, dialogs, password mismatch and logout cancellation. No account writes.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
