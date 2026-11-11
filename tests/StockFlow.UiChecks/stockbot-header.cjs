// Read-only Razor server, real header/dropdown/chat scripts, mocked header APIs.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});let checks=0;const measurements=[];const out=path.resolve(__dirname,'../../.artifacts/header-sizing');fs.mkdirSync(out,{recursive:true});
try {for(const touch of [false,true]){
 const page=await browser.newPage({hasTouch:touch,reducedMotion:'reduce'});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{const req=route.request(),p=new URL(req.url()).pathname;
  if(p==='/api/screenlanguages')return route.fulfill({contentType:'application/json',body:'[]'});
  if(p==='/api/notifications')return route.fulfill({contentType:'application/json',body:'{"unread":3,"items":[]}'});
  if(req.method()!=='GET')return route.fulfill({status:405});
  if(p.endsWith('.js')&&!/\/(stockflow-shell|language-selector|notifications|select|select-migrations|language|screen-language-catalog|help-panel|responsive-shell)(\.[a-z0-9]{10})?\.js$/.test(p))return route.fulfill({contentType:'text/javascript',body:''});
  return route.continue();
 });
 await page.goto('http://127.0.0.1:55863/Sales/Create');await page.locator('.sf-language-control .sf-select-trigger').waitFor();
 for(const [width,height] of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844],[320,568]]){
  await page.setViewportSize({width,height});await page.waitForTimeout(80);
  for(const collapsed of [false,true]){
   await page.evaluate(c=>{document.body.classList.toggle('sf-nav-collapsed',c);document.body.classList.remove('sf-nav-open');},collapsed);await page.waitForTimeout(80);
   const m=await page.evaluate(()=>{const rect=n=>n.getBoundingClientRect().toJSON(),bot=document.querySelector('#help-open'),img=bot.querySelector('.sf-stockbot-avatar');return {header:rect(document.querySelector('.sf-topbar')),language:rect(document.querySelector('.sf-language-control .sf-select-trigger')),languageStyle:(()=>{const s=getComputedStyle(document.querySelector('.sf-language-control .sf-select-trigger'));return {font:s.fontSize,padding:s.padding,minHeight:s.minHeight,arrow:s.backgroundSize,radius:s.borderRadius};})(),bell:rect(document.querySelector('#notification-bell')),bellIcon:rect(document.querySelector('#notification-bell svg')),menu:rect(document.querySelector('#sidebar-toggle')),menuIcon:rect(document.querySelector('#sidebar-toggle svg')),brand:rect(document.querySelector('.sf-sidebar-brand')),logo:rect(document.querySelector('.sf-sidebar .sf-brand-mark')),languageHit:rect(document.querySelector('.sf-language-control .sf-select')),profileFont:getComputedStyle(document.querySelector('.sf-username')).fontSize,profileChevron:getComputedStyle(document.querySelector('.sf-profile-chevron')).width,profileArtwork:rect(document.querySelector('.sf-admin-icon .sf-icon')),profileIcon:rect(document.querySelector('.sf-admin-icon')),controls:[...document.querySelectorAll('#sidebar-toggle,.sf-language-control,#notification-bell,#help-open,#profile-menu')].map(rect),bot:rect(bot),image:rect(img),fit:getComputedStyle(img).objectFit,hand:rect(bot.querySelector('.sf-stockbot-hand')),expression:rect(bot.querySelector('.sf-stockbot-expression')),overflow:document.documentElement.scrollWidth>innerWidth+1,headerOverflow:document.querySelector('.sf-topbar').scrollWidth>document.querySelector('.sf-topbar').clientWidth+1};});
   assert.equal(await page.evaluate(()=>devicePixelRatio),1);assert.equal(m.header.height,50);assert.equal(m.language.height,32);assert.equal(m.profileIcon.width,32);assert.equal(m.profileIcon.height,32);assert.equal(m.profileArtwork.width,18);assert.equal(m.profileArtwork.height,18);assert.equal(m.bot.width,touch?44:40);assert.equal(m.bot.height,touch?44:40);assert.equal(m.image.width,26);assert.equal(m.image.height,26);assert.equal(m.hand.width,26);for(const layer of [m.hand,m.expression]){assert.equal(layer.width,26);assert.equal(layer.x,m.image.x);assert.equal(layer.y,m.image.y);}assert.equal(m.fit,'contain');assert(!m.overflow&&!m.headerOverflow,JSON.stringify({width,touch,m}));
   for(let i=0;i<m.controls.length;i++){const c=m.controls[i];assert(c.left>=m.header.left&&c.right<=width+1&&c.top>=0&&c.bottom<=50,JSON.stringify({width,touch,c}));if(i)assert(c.left>=m.controls[i-1].right-1,'No overlapping header controls');}
   assert.equal(m.languageStyle.font,'14px');assert.equal(m.languageStyle.minHeight,'32px');assert.equal(m.languageStyle.arrow,'12px 12px');assert.equal(m.languageStyle.radius,'8px');if(width>=992){assert.equal(m.language.width,130);assert.equal(m.brand.height,72);assert.equal(m.logo.width,44);assert.equal(m.logo.height,44);}assert.equal(m.languageHit.height,touch?44:40);
   assert.equal(m.bell.width,touch?44:40);assert.equal(m.bell.height,touch?44:40);assert.equal(m.bellIcon.width,18);assert.equal(m.menuIcon.width,18);assert.equal(m.menu.width,touch?44:40);assert.equal(m.profileFont,'14px');assert.equal(m.profileChevron,'14px');
   for(const c of m.controls)assert(Math.abs(c.y+c.height/2-25)<1,'Controls vertically centered '+JSON.stringify({width,touch,c,header:m.header}));
   if(width>=380){const centers=await page.locator('.sf-profile-trigger').evaluate(n=>{const icon=n.querySelector('.sf-admin-icon').getBoundingClientRect(),svg=n.querySelector('.sf-profile-chevron svg'),path=svg.querySelector('path'),b=path.getBBox(),point=new DOMPoint(b.x+b.width/2,b.y+b.height/2).matrixTransform(path.getScreenCTM());return {iconY:icon.y+icon.height/2,arrowY:point.y};});assert(Math.abs(centers.iconY+1.5-centers.arrowY)<0.1,'Arrow uses optical alignment with profile text');}
   measurements.push({width,height,touch,collapsed,...m});checks++;
  }
  if([1366,320].includes(width)&&!touch)await page.screenshot({path:path.join(out,'header-'+width+'.png')});
  await page.evaluate(()=>document.body.classList.remove('sf-nav-collapsed'));
  await page.locator('#sidebar-toggle').click();assert(await page.locator('body').evaluate(n=>matchMedia('(max-width:991px)').matches?n.classList.contains('sf-nav-open'):n.classList.contains('sf-nav-collapsed')));
  if(width<=991)await page.keyboard.press('Escape');else await page.locator('#sidebar-toggle').click();
  if(width===1366&&!touch)await page.locator('.sf-profile-trigger').screenshot({path:path.join(out,'profile-centered.png')});
  await page.locator('.sf-profile-trigger').click();assert(await page.locator('#profile-menu').evaluate(n=>n.open));
  assert(await page.locator('.sf-profile-panel').evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=document.querySelector('.sf-topbar').getBoundingClientRect().bottom&&r.bottom<=innerHeight;}));
  await page.keyboard.press('Escape');assert(!(await page.locator('#profile-menu').evaluate(n=>n.open)));
  const language=page.locator('.sf-language-control .sf-select-trigger');const frame=await page.locator('.sf-language-control .sf-select').boundingBox();await page.mouse.click(frame.x+frame.width/2,frame.y+1);assert.equal(await language.getAttribute('aria-expanded'),'true');await page.keyboard.press('Escape');assert.equal(await language.getAttribute('aria-expanded'),'false');
  await page.locator('#help-open').focus();await page.keyboard.press('Tab');await page.locator('#help-open').focus();
  const tooltip=await page.locator('.sf-stockbot-tooltip').evaluate(n=>{const r=n.getBoundingClientRect();return {visible:getComputedStyle(n).visibility,bottom:r.bottom,top:r.top,left:r.left,right:r.right};});assert.equal(tooltip.visible,'visible');const bot=await page.locator('#help-open').boundingBox();assert(tooltip.left>=0&&tooltip.right<=width&&tooltip.top>=bot.y+bot.height&&tooltip.bottom<=height);
  await page.keyboard.press('Enter');assert(await page.locator('#help-panel').evaluate(n=>n.open));await page.locator('#help-close').click();
 }
 // Clicking the larger frame must not bypass a disabled language choice.
 await page.locator('#language-choice').evaluate(n=>{n.sfSelect.close();n.disabled=true;});
 const lockedFrame=await page.locator('.sf-language-control .sf-select').boundingBox();await page.mouse.click(lockedFrame.x+lockedFrame.width/2,lockedFrame.y+1);
 assert.equal(await page.locator('.sf-language-control .sf-select-trigger').getAttribute('aria-expanded'),'false');await page.locator('#language-choice').evaluate(n=>n.disabled=false);
 // Larger root font settings must not let children expand or overflow the header.
 for(const width of [1366,768,320])for(const fontSize of [20,24]){
  await page.setViewportSize({width,height:844});await page.evaluate(size=>{document.documentElement.style.fontSize=size+'px';document.body.classList.remove('sf-nav-collapsed');},fontSize);await page.waitForTimeout(300);
  const m=await page.locator('.sf-topbar').evaluate(n=>{const h=n.getBoundingClientRect();return {height:h.height,overflow:n.scrollWidth>n.clientWidth+1,controls:[...n.querySelectorAll('#sidebar-toggle,.sf-language-control,#notification-bell,#help-open,#profile-menu')].map(c=>c.getBoundingClientRect().toJSON())};});
  assert.equal(m.height,50);assert(!m.overflow);for(let i=0;i<m.controls.length;i++){const c=m.controls[i];assert(c.left>=0&&c.right<=width+1&&c.top>=0&&c.bottom<=m.height);if(i)assert(c.left>=m.controls[i-1].right-1);}checks++;
 }
 await page.evaluate(()=>document.documentElement.style.removeProperty('font-size'));
 assert.deepEqual(errors,[]);await page.close();
}fs.writeFileSync(path.join(out,'measurements.json'),JSON.stringify(measurements,null,2));console.log('PASS: '+checks+' header layouts with real shared dropdown scripts, desktop/touch targets, 100% zoom, measured 50px header, bounded tooltip and keyboard chat access.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
