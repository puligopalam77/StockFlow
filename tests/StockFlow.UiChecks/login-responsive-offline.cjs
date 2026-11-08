// Real Razor and local assets; synthetic POST responses, no authentication/database writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/password-page-fixtures');
const phase=process.argv[2]||'after',out=path.join(root,process.env.LOGIN_CAPTURE_ROOT||'.artifacts/login-responsive',phase);fs.mkdirSync(out,{recursive:true});
const sizes=[[1920,1080],[1600,900],[1440,900],[1536,864],[1366,768],[1280,800],[1280,720],[1024,768],[834,1194],[820,1180],[768,1024],[430,932],[393,852],[390,844],[375,667],[360,640],[320,568],[1280,650],[1280,600],[1188,530],[1067,465],[1093,614],[911,512],[390,360],[844,390]];
const testedSizes=sizes.filter(([w,h])=>!process.env.LOGIN_CARD_PILOT||['1920x1080','1536x864','1366x768','1280x720','1188x530','1067x465','1024x768','390x844'].includes(w+'x'+h));
const capture=new Set(['1920x1080','1366x768','1280x720','1536x864','1188x530','1024x768','1067x465','768x1024','390x844','320x568']);
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({reducedMotion:'reduce',deviceScaleFactor:1});page.setDefaultTimeout(8000);
 const errors=[],submissions=[],results=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(req.method()==='POST'){const fields=new URLSearchParams(req.postData());submissions.push({...Object.fromEntries(fields),rememberValues:fields.getAll('Input.RememberMe')});return route.fulfill({contentType:'text/html',body:'<p>Submitted</p>'});}
  if(p==='/Account/Login'){const state=url.searchParams.get('state'),name=state==='session'?'login-session.html':state==='invalid'?'login-invalid.html':'login.html';return route.fulfill({contentType:'text/html',path:path.join(fixtures,name)});}
  if(p==='/Account/ForgotPassword')return route.fulfill({contentType:'text/html',path:path.join(fixtures,'forgot.html')});
  if(p==='/Products')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/list-screen-fixtures/Products.html'),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'')});
  if(phase==='before'&&/\/css\/login-responsive(?:\.[a-z0-9]{10})?\.css$/.test(p))return route.fulfill({contentType:'text/css',path:path.join(root,process.env.LOGIN_BASELINE_CSS||'.artifacts/login-responsive/before.css')});
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 async function reachable(selector){const n=page.locator(selector);await n.scrollIntoViewIfNeeded();assert(await n.isVisible(),selector+' visible');assert(await n.evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=-1&&r.right<=innerWidth+1&&r.top>=-1&&r.bottom<=innerHeight+1&&n.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}),selector+' fully reachable');}
 for(const [width,height] of testedSizes){await page.setViewportSize({width,height});let promoBounds;
  for(const state of ['normal','session','invalid','required']){
   await page.goto('http://stockflow.test/Account/Login?state='+state);await page.locator('.sf-warehouse').evaluate(n=>n.decode());
   if(state==='required')await page.locator('.sf-signin').click();
   const layout=await page.evaluate(()=>{
    const r=s=>document.querySelector(s).getBoundingClientRect(),shown=s=>!!document.querySelector(s).getClientRects().length;
    return {width:innerWidth,height:innerHeight,scale:visualViewport.scale,promoHeight:r('.sf-login-visual').height,cardTop:r('.sf-feature-grid').top,cardBottom:r('.sf-feature-grid').bottom,overflow:document.documentElement.scrollWidth>innerWidth+1,scrollHeight:document.documentElement.scrollHeight,buttonBottom:r('.sf-signin').bottom,footerBottom:r('.sf-login-footer').bottom,visual:shown('.sf-login-visual'),cards:shown('.sf-feature-grid'),inputs:[...document.querySelectorAll('.sf-input-wrap input')].map(n=>({height:n.getBoundingClientRect().height,font:parseFloat(getComputedStyle(n).fontSize)}))};
   });
   assert.equal(layout.width,width);assert.equal(layout.height,height);assert.equal(layout.scale,1,'Actual CSS viewport at default browser zoom');
   if(phase!=='before'){
    assert(!layout.overflow,`${width}x${height} ${state}: no horizontal overflow`);
    if(width>=1024&&height>=720){assert(layout.buttonBottom<=height+1,`${width}x${height} ${state}: Sign In above fold (${layout.buttonBottom})`);assert(layout.footerBottom<=height+1,`${width}x${height} ${state}: footer fits (${layout.footerBottom})`);}
    if(width<=991)assert(!layout.visual,'Tablet/mobile prioritize form');else {assert(layout.visual);assert(layout.cards,'All three promotional cards remain available on split layouts');}
    if(layout.cards){
     const currentBounds=[layout.promoHeight,layout.cardTop,layout.cardBottom];promoBounds??=currentBounds;assert.deepEqual(currentBounds,promoBounds,'Promotional layout remains independent of notification/validation height');
     const cards=await page.locator('.sf-feature').evaluateAll(nodes=>nodes.map(n=>{const r=n.getBoundingClientRect(),p=n.querySelector('p'),t=getComputedStyle(p);return {top:r.top,bottom:r.bottom,left:r.left,right:r.right,height:r.height,width:r.width,font:parseFloat(t.fontSize),clipped:n.scrollHeight>n.clientHeight+1||n.scrollWidth>n.clientWidth+1};}));
     assert.equal(cards.length,3);assert(cards.every(n=>!n.clipped&&n.font>=13),'Readable complete card copy');
     assert(cards.every(n=>Math.abs(n.top-cards[0].top)<1&&Math.abs(n.height-cards[0].height)<1&&Math.abs(n.width-cards[0].width)<1),'Equal cards in one horizontal row');
     if(height>=465)assert(cards.every(n=>n.top>=0&&n.bottom<=height+1),width+'x'+height+' cards fit the actual viewport independently of form height');
    }
    assert(layout.inputs.every(x=>x.height>=42&&x.font>=16),'Readable inputs with 44px+ wrapper targets');
    for(const selector of ['#Input_Username','#Input_Password','#password-toggle','.sf-remember','#forgot-password-link','.sf-signin','.sf-login-footer'])await reachable(selector);
    if(layout.visual){await reachable('.sf-handwritten');if(layout.cards){for(let i=1;i<=3;i++)await reachable('.sf-feature:nth-child('+i+')');assert(await page.evaluate(()=>{const t=document.querySelector('.sf-handwritten').getBoundingClientRect(),g=document.querySelector('.sf-feature-grid').getBoundingClientRect(),v=document.querySelector('.sf-login-visual').getBoundingClientRect();return t.bottom<=g.top&&g.bottom<=v.bottom;}),'Complete cards clear of heading');}}
   }
   await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));
   if(capture.has(width+'x'+height)&&state!=='required')await page.screenshot({path:path.join(out,`${width}x${height}-${state}.png`),fullPage:true});
   results.push({state,...layout});
  }
 }
 if(phase!=='before'){
  await page.setViewportSize({width:1067,height:465});await page.goto('http://stockflow.test/Account/Login?state=invalid');
  await reachable('.sf-login-errors');await reachable('.sf-signin');
  assert.equal(submissions.length,0,'Required errors never submit');
  const initialAutocomplete=await page.locator('#Input_Username').getAttribute('autocomplete');
  await page.locator('#Input_Username').fill('example-user');await page.locator('#Input_Password').fill('Synthetic test password!');
  await page.locator('#password-toggle').click();assert.equal(await page.locator('#Input_Password').getAttribute('type'),'text');assert.equal(await page.locator('#password-toggle').getAttribute('aria-pressed'),'true');
  await page.locator('#password-toggle').click();assert.equal(await page.locator('#Input_Password').getAttribute('type'),'password');
  await page.locator('.sf-remember').click();assert(await page.locator('#Input_RememberMe').isChecked());
  assert.equal(await page.locator('#Input_Username').getAttribute('autocomplete'),initialAutocomplete);assert.equal(await page.locator('#Input_Password').getAttribute('autocomplete'),'current-password');
  await page.locator('#Input_Password').press('Enter');await page.getByText('Submitted',{exact:true}).waitFor();assert.equal(submissions.length,1);assert.equal(submissions[0]['Input.Username'],'example-user');assert.deepEqual(submissions[0].rememberValues,['true','false'],'Native checked value precedes the hidden false fallback');
  // Native form sends checked=true first, then the existing hidden=false fallback.
  await page.goto('http://stockflow.test/Account/Login?state=session');assert.equal(await page.locator('[name=ReturnUrl]').inputValue(),'/Expenses');
  await page.locator('#Input_Username').focus();for(let i=0;i<5;i++)await page.keyboard.press('Tab');assert(await page.locator('.sf-signin').evaluate(n=>n===document.activeElement&&n.matches(':focus-visible')));
  await page.locator('#Input_Username').fill('cashier');await page.locator('#forgot-password-link').click();await page.waitForURL('**/Account/ForgotPassword?username=cashier');
  // Even if loaded accidentally, login CSS cannot alter other auth/module views.
  for(const url of ['/Account/ForgotPassword','/Products']){
   await page.goto('http://stockflow.test'+url);await page.evaluate(()=>document.fonts.ready);
   const styles=()=>page.locator('main,main input,main button,main h1').evaluateAll(nodes=>nodes.map(n=>{const s=getComputedStyle(n),r=n.getBoundingClientRect();return {width:r.width,height:r.height,font:s.fontSize,color:s.color,background:s.backgroundColor,padding:s.padding};}));
   const before=await styles();await page.addStyleTag({path:path.join(assets,'css/login-responsive.css')});assert.deepEqual(await styles(),before,url+' is unaffected by login CSS');
  }
  await page.setViewportSize({width:320,height:568});await page.goto('http://stockflow.test/Account/Login?state=invalid');
  await page.locator('.sf-login-errors').evaluate(n=>n.textContent='Sign-in is temporarily unavailable. Please try again shortly or contact your store administrator if the problem continues.');
  await page.evaluate(()=>{const nodes=[...document.querySelectorAll('.sf-login-panel :is(h1,p,label,input,button,.sf-eyebrow,.sf-trust span,.sf-text-button)')];const sizes=nodes.map(n=>parseFloat(getComputedStyle(n).fontSize));nodes.forEach((n,i)=>n.style.fontSize=(sizes[i]*1.5)+'px');});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'150% larger text wraps');for(const s of ['#Input_Username','#Input_Password','.sf-signin','.sf-login-footer'])await reachable(s);
  await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));await page.screenshot({path:path.join(out,'large-text-server-error.png'),fullPage:true});
  for(const dpr of [1.25,1.5]){const context=await browser.newContext({viewport:{width:Math.round(1366/dpr),height:Math.round(768/dpr)},deviceScaleFactor:dpr});const p=await context.newPage();await p.route('**/*',route=>{const url=new URL(route.request().url());if(url.pathname==='/Account/Login')return route.fulfill({path:path.join(fixtures,'login-session.html'),contentType:'text/html'});const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});});await p.goto('http://stockflow.test/Account/Login');assert(await p.evaluate(expected=>devicePixelRatio===expected&&document.documentElement.scrollWidth<=innerWidth+1,dpr));await p.locator('.sf-signin').scrollIntoViewIfNeeded();assert(await p.locator('.sf-signin').isVisible());await context.close();}
  assert.deepEqual(errors,[]);
 }
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(`PASS ${phase}: ${results.length} login layouts at ${testedSizes.length} effective CSS viewports; ${phase==='before'?'baseline captures':'normal/session/server/required errors, readable and reachable controls, complete-or-hidden cards, password/Remember Me/Enter/ReturnUrl/Forgot Password, keyboard, larger text and DPR equivalents'}.`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
