// Loopback TestServer only: synthetic records, database unavailable, writes blocked.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const out=path.resolve(__dirname,'../bin/action-icons');fs.mkdirSync(out,{recursive:true});
const base='http://127.0.0.1:55863';
const sizes=[[1920,1080],[1366,768],[1280,720],[1280,650],[1024,768],[768,1024],[390,844],[375,667],[1536,864],[1280,600]];
const routes=['/Categories','/Brands','/Products','/Suppliers','/Customers','/Admin/Users','/Admin/Roles','/Sales','/Sales/Create','/Purchases','/Inventory','/Reports/Activity'];
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});const results=[];
try{
 const p=await browser.newPage();const errors=[];p.on('pageerror',e=>errors.push(e.message));p.setDefaultTimeout(7000);
 for(const [width,height]of sizes){await p.setViewportSize({width,height});
  for(const route of routes){const response=await p.goto(base+route);assert.equal(response.status(),200,route);await p.waitForTimeout(80);
   const result=await p.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth,
    actions:[...document.querySelectorAll('.sf-action-icon')].filter(n=>n.getBoundingClientRect().width).map(n=>{const r=n.getBoundingClientRect(),s=getComputedStyle(n),f=getComputedStyle(n,'::before'),i=n.querySelector('svg');return {label:n.getAttribute('aria-label'),size:n.dataset.actionSize,width:r.width,height:r.height,face:parseFloat(f.width),radius:f.borderRadius,icon:i?.getBoundingClientRect().width,color:s.color};}),
    excluded:document.querySelectorAll('.sf-sidebar .sf-action-icon,.sf-topbar .sf-action-icon,.sf-help-panel .sf-action-icon,.sf-help-open.sf-action-icon,[role=tab].sf-action-icon').length}));
   assert(!result.overflow,route+' document overflow at '+width);assert.equal(result.excluded,0);
   for(const a of result.actions){const size=a.size==='standard'?32:a.size==='touch'?44:32;assert(a.label);assert.equal(a.width,size);assert.equal(a.height,size);assert.equal(a.face,size);assert.equal(a.radius,'6px');assert(['rgb(16, 29, 56)','rgb(176, 42, 55)'].includes(a.color),route+' '+JSON.stringify(a));if(a.icon)assert.equal(a.icon,a.size==='standard'?16:a.size==='touch'?18:16);}
   results.push({route,width,height,actions:result.actions.length});
   if(route==='/Categories'&&[1280,390].includes(width)&&height!==600)await p.screenshot({path:path.join(out,`category-${width}x${height}.png`),fullPage:true});
  }
 }
 // In-place enhancement preserves listeners, DOM identity, href and loading state.
 await p.setViewportSize({width:1280,height:650});await p.goto(base+'/Categories');
 await p.evaluate(()=>{const wrap=document.createElement('section');wrap.id='action-component-test';wrap.innerHTML=`<div class="table-responsive"><table><tr><td><button id="icon-test-edit" title="Edit test row"><svg viewBox="0 0 24 24"><path d="M4 20L20 4" stroke="currentColor"/></svg></button><button id="icon-test-disabled" disabled aria-busy="true" aria-label="Saving test row"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg><span class="visually-hidden">Saving</span></button><a href="#retained-destination" id="icon-test-link" aria-label="View test row"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg></a></td></tr></table></div><button id="icon-test-primary">Save product <svg width="22" height="22"></svg></button><button id="icon-test-standard" aria-label="Standard test"><svg></svg></button><button id="icon-test-touch" aria-label="Touch test"><svg></svg></button>`;
  document.querySelector('main').prepend(wrap);window.retainedButton=wrap.querySelector('#icon-test-edit');window.clicks=0;retainedButton.addEventListener('click',()=>window.clicks++);wrap.querySelector('#icon-test-disabled').addEventListener('click',()=>window.clicks+=100);
  StockFlowActionIcons.enhance(wrap.querySelector('#icon-test-standard'),{size:'standard'});StockFlowActionIcons.enhance(wrap.querySelector('#icon-test-touch'),{size:'touch'});
 });await p.waitForFunction(()=>document.querySelector('#icon-test-edit').classList.contains('sf-action-icon'));
 const action=p.locator('#icon-test-edit');await action.scrollIntoViewIfNeeded();await action.focus();
 await p.getByRole('tooltip',{name:'Edit test row',exact:true}).waitFor();
 assert(await action.evaluate(n=>getComputedStyle(n).outlineStyle!=='none'));await action.press('Enter');await action.press('Space');assert.equal(await p.evaluate(()=>clicks),2);
 assert(await action.evaluate(n=>n===retainedButton));assert.equal(await p.locator('#icon-test-link').getAttribute('href'),'#retained-destination');
 assert.equal(await p.locator('#icon-test-disabled').getAttribute('aria-busy'),'true');await p.locator('#icon-test-disabled').evaluate(n=>n.click());assert.equal(await p.evaluate(()=>clicks),2);assert(await p.locator('#icon-test-disabled').isDisabled());
 for(const state of ['hidden','d-none','inline']){await action.evaluate((n,state)=>{if(state==='hidden')n.hidden=true;else if(state==='inline')n.style.display='none';else n.classList.add(state);},state);assert(!await action.isVisible(),'Preserve '+state+' visibility');await action.evaluate(n=>{n.hidden=false;n.classList.remove('d-none');n.style.removeProperty('display');});}
 assert.equal(await p.locator('#icon-test-primary.sf-action-icon').count(),0);
 await p.mouse.move(0,0);await p.waitForTimeout(200);assert.equal(await action.evaluate(n=>getComputedStyle(n,'::before').backgroundColor),'rgba(0, 0, 0, 0)');
 await action.hover();await p.waitForTimeout(220);assert.equal(await action.evaluate(n=>getComputedStyle(n,'::before').backgroundColor),'rgb(241, 244, 250)');assert.equal(await action.evaluate(n=>getComputedStyle(n).color),'rgb(16, 29, 56)');
 await action.evaluate(n=>n.setAttribute('aria-label','Updated action label'));await action.focus();await action.dispatchEvent('pointerenter');await p.getByRole('tooltip',{name:'Updated action label',exact:true}).waitFor();
 assert(await p.locator('#sf-action-tooltip').evaluate(n=>{const r=n.getBoundingClientRect();return r.x>=0&&r.y>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}));await action.press('Escape');assert.equal(await p.locator('#sf-action-tooltip').count(),0);
 assert.equal((await p.locator('#icon-test-standard').boundingBox()).width,32);assert.equal((await p.locator('#icon-test-touch').boundingBox()).width,44);
 const gaps=await p.locator('#action-component-test td > .sf-action-group').evaluate(n=>[...n.children].slice(1).map((a,i)=>a.getBoundingClientRect().left-n.children[i].getBoundingClientRect().right));assert(gaps.every(x=>x>=4&&x<=4.1),'exact 4px gaps');
 await p.evaluate(()=>{const n=document.createElement('button');n.type='button';n.id='icon-later';n.setAttribute('aria-label','Later row action');n.innerHTML='<svg viewBox="0 0 24 24"></svg>';document.querySelector('#action-component-test td').append(n);});
 await p.waitForFunction(()=>document.querySelector('#icon-later').parentElement.classList.contains('sf-action-group'));assert.equal(await p.locator('#action-component-test td > .sf-action-group').count(),1);assert.equal(await p.locator('#action-component-test td .sf-action-group .sf-action-group').count(),0);assert(await action.evaluate(n=>n===retainedButton));await action.click();assert.equal(await p.evaluate(()=>clicks),3);
 // Real confirmation dismissal remains safe and the modal close uses standard size.
 await p.evaluate(()=>{window.confirmResult=null;StockFlowDialog.confirm('Discard test changes?').then(v=>window.confirmResult=v);});await p.locator('#sf-confirm-dialog').waitFor({state:'visible'});
 assert.equal((await p.locator('#sf-confirm-dialog .sf-action-icon').boundingBox()).width,32);await p.keyboard.press('Escape');await p.waitForFunction(()=>confirmResult===false);
 await p.screenshot({path:path.join(out,'component-variants.png'),fullPage:true});
 // Coarse-pointer targets reserve space in normal table layout, rather than overlap.
 const touch=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true});const t=await touch.newPage();await t.goto(base+'/Categories');await t.locator('.category-icon-action.sf-action-icon').first().waitFor();
 const targets=await t.locator('.category-group-row .category-icon-action').evaluateAll(nodes=>nodes.map(n=>{const r=n.getBoundingClientRect();return {x:r.x,right:r.right,w:r.width,h:r.height,face:parseFloat(getComputedStyle(n,'::before').width)};}));
 assert(targets.length>=3);targets.forEach(n=>{assert.equal(n.w,44);assert.equal(n.h,44);assert.equal(n.face,32);});for(let i=1;i<targets.length;i++)assert(targets[i].x-targets[i-1].right>=4);
 for(const n of await t.locator('.category-group-row .category-icon-action').all()){await n.scrollIntoViewIfNeeded();assert(await n.evaluate(n=>{const r=n.getBoundingClientRect();return n.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}));}
 await t.screenshot({path:path.join(out,'touch-390.png'),fullPage:true});
 await t.goto(base+'/Products');await t.locator('#master-rows .sf-action-group').waitFor();const productTouch=await t.locator('#master-rows tr').first().evaluate(row=>{const cell=row.lastElementChild,c=cell.getBoundingClientRect(),g=cell.querySelector('.sf-action-group').getBoundingClientRect(),a=[...cell.querySelectorAll('.sf-action-icon')].map(n=>n.getBoundingClientRect());return {within:g.left>=c.left&&g.right<=c.right,widths:a.map(r=>r.width),gap:a[1].left-a[0].right};});assert.deepEqual(productTouch.widths,[44,44]);assert(productTouch.within);assert.equal(productTouch.gap,4);await touch.close();
 assert.deepEqual(errors,[]);fs.writeFileSync(path.join(out,'layout-results.json'),JSON.stringify(results,null,2));console.log(`PASS: ${results.length} module/viewport layouts; dynamic actions, exact dimensions/gaps, neutral/danger semantic colors, focus/tooltips, Enter/Space, retained handlers/href/loading/disabled state, standard/touch variants, Escape confirmation, 44px non-overlapping touch targets.`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
