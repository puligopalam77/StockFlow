// Rendered Razor fixtures + real shared shell/select/language scripts; HTTP is synthetic.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const live=process.env.STOCKFLOW_HEADER_LIVE,base=live||'http://stockflow.test';
const touch=process.env.STOCKFLOW_HEADER_TOUCH==='1';
const routes={dashboard:'/',billing:'/Sales/Create',sales:'/Sales',purchases:'/Purchases',inventory:'/Inventory',customers:'/Customers',reports:'/Reports',expenses:'/Expenses',settings:'/Settings/Company'};
const screenUrl=screen=>base+(live?routes[screen]:'/check/'+screen);
const screens={dashboard:'phase13-ui-fixtures/Admin-dashboard.html',billing:'billing-ui-fixtures/Billing.html',sales:'list-screen-fixtures/Sales.html',purchases:'list-screen-fixtures/Purchases.html',inventory:'list-screen-fixtures/Inventory.html',customers:'list-screen-fixtures/Customers.html',reports:'list-screen-fixtures/Reports-Activity.html',expenses:'expense-list-fixtures/Index.html',settings:'navigation-ui-fixtures/_Settings_Company_section_business.html'};
const layoutMeasurements=[];
const sizes=touch?[[768,1024],[375,667]]:[[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[375,667],[320,568]];
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({reducedMotion:'reduce',hasTouch:touch}),errors=[],writes=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{const req=route.request(),p=new URL(req.url()).pathname;
  if(p.startsWith('/check/'))return route.fulfill({contentType:'text/html',path:path.join(root,'tests/bin',screens[p.split('/').at(-1)])});
  if(live&&Object.values(routes).includes(p))return route.continue();
  if(p==='/Notifications')return route.fulfill({contentType:'text/html',body:'<h1>Notifications fixture</h1>'});
  if(p==='/api/screenlanguages'){if(req.method()==='PUT')writes.push(req.postDataJSON());return route.fulfill({contentType:'application/json',body:req.method()==='GET'?'[]':'{}'});}
  if(p==='/api/notifications')return route.fulfill({contentType:'application/json',body:'{"unread":3,"items":[]}'});
  const clean=p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,'');
  if(clean.endsWith('.js')&&!/\/(stockflow-shell|language-selector|notifications|action-icons|select|select-migrations|language|screen-language-catalog)\.js$/.test(clean))return route.fulfill({contentType:'text/javascript',body:''});
  if(live)return route.continue();
  const file=path.resolve(assets,'.'+clean);return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
 for(const screen of touch?['billing']:Object.keys(screens))for(const [width,height] of sizes){
  await page.setViewportSize({width,height});await page.goto(screenUrl(screen));await page.locator('.sf-language-control .sf-select-trigger').waitFor();
  for(const collapsed of [false,true]){
   await page.evaluate(collapsed=>{document.body.classList.toggle('sf-nav-collapsed',collapsed);document.body.classList.remove('sf-nav-open');},collapsed);await page.waitForTimeout(280);
   const m=await page.locator('.sf-topbar').evaluate(n=>{const rect=n=>{const r=n.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};const button=n.querySelector('#sidebar-toggle'),lang=n.querySelector('.sf-language-control .sf-select-trigger'),bell=n.querySelector('#notification-bell'),profile=n.querySelector('.sf-profile-trigger');return {sidebar:rect(document.querySelector('.sf-sidebar')),workspace:rect(document.querySelector('.sf-workspace')),sidebarOverflow:document.querySelector('.sf-sidebar').scrollWidth>document.querySelector('.sf-sidebar').clientWidth+1,pageOverflow:document.documentElement.scrollWidth>innerWidth+1,header:rect(n),computedHeight:getComputedStyle(n).height,minHeight:getComputedStyle(n).minHeight,maxHeight:getComputedStyle(n).maxHeight,button:rect(button),lang:rect(lang),bell:rect(bell),profile:rect(profile),avatar:rect(profile.querySelector('.sf-admin-icon')),menuIcon:rect(button.querySelector('svg')),bellIcon:rect(bell.querySelector('svg')),font:getComputedStyle(lang).fontSize,profileFont:getComputedStyle(profile.querySelector('.sf-username')).fontSize,position:getComputedStyle(n).position,main:rect(document.querySelector('.sf-main')),overflow:n.scrollWidth>n.clientWidth+1,viewport:document.documentElement.clientWidth};});
   assert.equal(m.header.height,50,JSON.stringify({screen,width,collapsed,m}));assert.equal(m.computedHeight,'50px');assert.equal(m.minHeight,'50px');assert.equal(m.maxHeight,'50px');assert.equal(m.position,'sticky');assert.equal(m.avatar.width,32);assert.equal(m.avatar.height,32);assert.equal(m.menuIcon.width,18);assert.equal(m.bellIcon.width,18);assert.equal(m.lang.height,32);assert.equal(m.profileFont,'14px');if(width>=992){assert.equal(m.lang.width,130);assert.equal(m.font,'14px');}
   assert(m.button.width>=40&&m.button.height>=40&&m.bell.width>=40&&m.bell.height>=40);assert(!m.overflow);
   if(touch)for(const target of [m.button,m.bell,m.profile])assert(target.width>=44&&target.height>=44,'Touch target remains at least 44px');
   for(const c of [m.button,m.lang,m.bell,m.profile])assert(c.x>=m.header.x&&c.right<=m.viewport+1&&c.y>=0&&c.bottom<=50&&Math.abs(c.y+c.height/2-25)<1,JSON.stringify({screen,width,collapsed,c,m}));
   if(width>=992){assert.equal(m.sidebar.width,230);assert.equal(m.workspace.x,collapsed?0:230);}else assert.equal(m.workspace.x,0);assert(!m.sidebarOverflow&&!m.pageOverflow);layoutMeasurements.push({screen,width,height,collapsed,...m});
   assert(m.lang.x>=m.button.right&&m.bell.x>=m.lang.right&&m.profile.x>=m.bell.right,'Header controls do not overlap');assert(m.main.y>=50,'Content follows header in document flow');
  }
  // Popup geometry and opening must not move the page at any tested size.
  const anchor=page.locator('.sf-language-control .sf-select-trigger');
  const before=await page.locator('.sf-main').boundingBox();const scrollBefore=await page.evaluate(()=>[scrollX,scrollY]);await anchor.click();
  const box=await page.locator('.sf-language-control .sf-select-panel').boundingBox(),field=await anchor.boundingBox();const clientWidth=await page.evaluate(()=>document.documentElement.clientWidth);
  assert.equal(box.width,170);assert(box.height<=240&&box.x>=8&&box.x+box.width<=clientWidth&&box.y+box.height<=height);assert(Math.abs(box.x+box.width-Math.min(clientWidth-8,Math.max(178,field.x+field.width)))<1,'Right alignment with viewport clamping');
  assert.deepEqual(await page.locator('.sf-main').boundingBox(),before);assert.deepEqual(await page.evaluate(()=>[scrollX,scrollY]),scrollBefore);await page.keyboard.press('Escape');
  await page.evaluate(()=>document.body.classList.remove('sf-nav-collapsed'));await page.locator('#sidebar-toggle').click();
  assert(await page.locator('body').evaluate(n=>matchMedia('(max-width:991px)').matches?n.classList.contains('sf-nav-open'):n.classList.contains('sf-nav-collapsed')));
  if(width<=991)await page.keyboard.press('Escape');else await page.locator('#sidebar-toggle').click();
  await page.locator('.sf-profile-trigger').click();const panel=await page.locator('.sf-profile-panel').evaluate(n=>{const r=n.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom};});assert(panel.left>=0&&panel.right<=width&&panel.top>=50&&panel.bottom<=height,JSON.stringify({screen,width,height,panel}));await page.keyboard.press('Escape');assert.equal(await page.locator('#profile-menu').evaluate(n=>n.open),false);
  await page.evaluate(()=>{const spacer=document.createElement('div');spacer.style.height='1800px';document.querySelector('.sf-main').append(spacer);scrollTo({top:500,behavior:'instant'});});assert.equal((await page.locator('.sf-topbar').boundingBox()).y,0);await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));
  if(screen==='billing'&&[1366,375].includes(width)){fs.mkdirSync(path.join(root,'.artifacts/compact-topbar'),{recursive:true});await page.screenshot({path:path.join(root,`.artifacts/compact-topbar/billing-${width}.png`)});}
 }
 // Exercise the existing profile/logout actions and native notifications route.
 await page.setViewportSize({width:1366,height:768});await page.goto(screenUrl('billing'));await page.locator('.sf-profile-trigger').click();await page.locator('#profile-logout').click();await page.locator('#logout-confirmation').waitFor({state:'visible'});await page.locator('#logout-cancel').click();assert.equal(await page.locator('#logout-confirmation').evaluate(n=>n.open),false);
 assert.equal(await page.locator('#notification-count').textContent(),'3');await page.locator('#notification-bell').click();await page.waitForURL('**/Notifications');
 // Verify the shared language picker still dispatches to the existing save handler.
 await page.goto(screenUrl('billing'));await page.locator('.sf-language-control .sf-select-trigger').waitFor();
 if(await page.locator('#language-choice').isEnabled()){
  const trigger=page.locator('.sf-language-control .sf-select-trigger'),options=page.locator('.sf-language-control [role=option]');
  await trigger.click();
  const menu=page.locator('.sf-language-control .sf-select-panel'),search=menu.locator('.sf-select-query'),list=menu.locator('.sf-select-options');
  const dimensions=await menu.evaluate(n=>{const r=n.getBoundingClientRect(),t=document.querySelector('.sf-language-control .sf-select-trigger').getBoundingClientRect();return {width:r.width,height:r.height,left:r.left,right:r.right,top:r.top,bottom:r.bottom,triggerBottom:t.bottom,triggerRight:t.right,scroll:n.querySelector('.sf-select-options').scrollHeight>n.querySelector('.sf-select-options').clientHeight};});
  assert.equal(await options.first().evaluate(n=>getComputedStyle(n,'::after').position),'static');assert(dimensions.width===170);assert(Math.abs(dimensions.right-dimensions.triggerRight)<1);assert(dimensions.height<=240&&dimensions.top===dimensions.triggerBottom+4&&dimensions.scroll);assert.equal((await search.boundingBox()).height,34);assert.equal((await options.first().boundingBox()).height,34);assert.equal(await options.count(),await page.locator('#language-choice option').count());
  await search.press('End');assert(await list.evaluate(n=>n.scrollTop>0));await search.press('Home');assert.equal(await list.evaluate(n=>n.scrollTop),0);
  await options.nth(1).hover();
  const highlighted=await options.nth(1).evaluate(n=>({background:getComputedStyle(n).backgroundColor,color:getComputedStyle(n).color}));
  assert.equal(await options.first().evaluate(n=>getComputedStyle(n,'::after').color),'rgb(255, 0, 128)');assert.equal(highlighted.background,'rgb(241, 244, 249)');assert.equal(highlighted.color,'rgb(20, 35, 63)');assert.equal(await options.first().evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 244, 249)');assert.equal(await options.first().evaluate(n=>getComputedStyle(n).color),'rgb(20, 35, 63)');
  // The selected row/checkmark and keyboard-active row also stay readable.
  await options.first().hover();assert.equal(await options.first().evaluate(n=>getComputedStyle(n,'::after').color),'rgb(255, 0, 128)');
  await page.mouse.move(0,0);await trigger.press('ArrowDown');
  const active=page.locator('.sf-language-control .sf-select-option.sf-select-active');
  assert.equal(await active.evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 244, 249)');assert.equal(await active.evaluate(n=>getComputedStyle(n).color),'rgb(20, 35, 63)');
  await search.fill('Tamil');assert.equal(await options.count(),1);await search.press('ArrowDown');await search.press('Enter');await page.waitForFunction(()=>!document.querySelector('#language-choice').disabled);assert.equal(await page.locator('#language-choice').inputValue(),'ta');assert(writes.some(w=>w.language==='ta'&&w.screen==='application'));
  await trigger.click();assert.equal(await options.count(),await page.locator('#language-choice option').count());await page.keyboard.press('Escape');assert.equal(await trigger.getAttribute('aria-expanded'),'false');
 }
 fs.mkdirSync(path.join(root,'.artifacts/sidebar-language'),{recursive:true});fs.writeFileSync(path.join(root,'.artifacts/sidebar-language',touch?'touch.json':'desktop.json'),JSON.stringify(layoutMeasurements,null,2));
 assert.deepEqual(errors,[]);console.log('PASS: computed height/min-height/max-height 50px and compact controls on '+(touch?'touch tablet/mobile with 44px action targets (four layouts)':'nine screens, eight sizes and both sidebar states (144 layouts)')+'; no header overflow/overlap, sticky scrolling, sidebar toggle/Escape, bounded profile menus, logout cancellation, notifications route/count and shared language interaction. '+(live?'Running read-only Razor application at '+live+'; controlled header API responses.':'Rendered fixtures; mock HTTP only.'));
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
