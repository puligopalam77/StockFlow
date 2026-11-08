// CSS/layout checks against real Razor. Navigation behavior/permissions are covered
// separately by navigation-offline.cjs and --navigation-checks. No shop DB access.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const phase=process.argv[2]||'after',out=path.join(root,'.artifacts/sidebar-density',phase);
const liveAssets=process.env.SIDEBAR_LIVE_ASSETS;
const dashboard=process.env.SIDEBAR_DASHBOARD==='1';
const fixture=process.env.SIDEBAR_FIXTURE||'Products';
const sizes=[[1920,1080],[1536,864],[1366,768],[1280,720],[1280,600],[1024,768],[768,1024],[390,844],[320,568]];
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const p=await browser.newPage({reducedMotion:'reduce',deviceScaleFactor:1}),results=[];
 await p.route('**/*',async route=>{
  const url=new URL(route.request().url());
  if(url.pathname==='/Products')return route.fulfill({path:path.join(root,dashboard?'tests/bin/phase13-ui-fixtures/Admin-dashboard.html':`tests/bin/navigation-ui-fixtures/${fixture}.html`),contentType:'text/html'});
  if(url.pathname.endsWith('.js'))return route.fulfill({body:'',contentType:'text/javascript'});
  if(phase==='before'&&/\/css\/shop-navigation(?:\.[a-z0-9]{10})?\.css$/.test(url.pathname))return route.fulfill({path:path.join(root,'.artifacts/sidebar-density/before.css'),contentType:'text/css'});
  if(liveAssets&&url.pathname.endsWith('.css'))return route.fulfill({response:await route.fetch({url:liveAssets+url.pathname.replace(/\.[a-z0-9]{10}(?=\.css$)/,'')})});
  const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
 for(const [width,height]of sizes){await p.setViewportSize({width,height});await p.goto('http://stockflow.test/Products');
  if(width<=991)await p.evaluate(()=>document.body.classList.add('sf-nav-open'));
  await p.waitForTimeout(280);
  if(dashboard){
   const sale=p.locator('.retail-daily-actions a[href="/Sales/Create"]');
   assert.equal(await sale.locator('strong').innerText(),'Make a bill');
   assert.equal(await sale.locator('small').innerText(),'Create a sale for your customer');
   for(const section of ['.sf-hero','.retail-daily-actions','#stock-trends'])assert.equal(await p.locator(section).count(),1,'Existing dashboard section retained: '+section);
   assert(await p.locator('.sf-kpi-grid').count()>=1,'Existing inventory/business summary retained');
  }
  const metrics=await p.evaluate(()=>{
   const box=s=>document.querySelector(s).getBoundingClientRect(),sidebar=box('.sf-sidebar'),header=box('.sf-sidebar-brand'),nav=box('.sf-shop-nav'),footer=box('.sf-sidebar-bottom');
   const rows=[...document.querySelectorAll('.sf-shop-nav .sf-nav-link')].filter(n=>n.getClientRects().length).map(n=>{const r=n.getBoundingClientRect(),icon=n.querySelector(':scope>.sf-icon'),i=icon?.getBoundingClientRect(),label=n.querySelector(':scope>span:not(.sf-nav-chevron)');return {height:r.height,font:getComputedStyle(n).fontSize,icon:i?.width,iconLeft:i?.left,labelLeft:label?.getBoundingClientRect().left};});
   const style=getComputedStyle(document.querySelector('.sf-sidebar')),toggle=document.querySelector('#sidebar-toggle'),t=toggle.getBoundingClientRect();
   return {width:sidebar.width,minWidth:style.minWidth,maxWidth:style.maxWidth,workspaceLeft:box('.sf-workspace').left,topbarLeft:box('.sf-topbar').left,headerHeight:header.height,headerBottom:header.bottom,toggle:{width:t.width,height:t.height,background:getComputedStyle(toggle).backgroundColor},footerFont:getComputedStyle(document.querySelector('.sf-sidebar-bottom')).fontSize,navTop:nav.top,navBottom:nav.bottom,footerTop:footer.top,footerBottom:footer.bottom,overflow:document.documentElement.scrollWidth>innerWidth+1,rows};
  });
  assert(!metrics.overflow,width+' no page overflow');
  assert(metrics.navTop>=metrics.headerBottom-1&&metrics.navBottom<=metrics.footerTop+1,'Header/footer reserve menu space');
  assert.equal(metrics.footerBottom,height,'Status footer stays visible');
  if(phase!=='before'){
   assert.equal(metrics.width,width>=992?230:265,'Shared width and existing mobile drawer');
   assert.equal(metrics.workspaceLeft,width>=992?metrics.width:0,'Workspace follows shared width');
   assert.equal(metrics.topbarLeft,metrics.workspaceLeft,'Header follows workspace');
   if(width>=992){assert.equal(metrics.minWidth,'230px');assert.equal(metrics.maxWidth,'230px');assert.equal(metrics.headerHeight,72,'Compact brand header');}
   assert.equal(metrics.toggle.width,44);assert.equal(metrics.toggle.height,44);assert.equal(metrics.toggle.background,'rgba(0, 0, 0, 0)');assert.equal(metrics.footerFont,'12px');
   assert(metrics.rows.length>5);for(const row of metrics.rows){assert(row.height>=44&&row.height<=46,'Compact accessible parent row');assert.equal(row.font,'14px');if(row.icon)assert.equal(row.icon,18);}
   const labels=metrics.rows.filter(r=>r.icon).map(r=>r.labelLeft);assert(Math.max(...labels)-Math.min(...labels)<1,'Aligned parent labels');
  }
  await p.screenshot({path:path.join(out,`${width}x${height}.png`)});
  // Expand all levels to exercise independent scrolling at every viewport height.
  await p.evaluate(()=>document.querySelectorAll('.sf-shop-nav details').forEach(n=>n.open=true));
  if(phase!=='before'){
   const expanded=await p.locator('.sf-shop-nav').evaluate(nav=>{
    const box=nav.getBoundingClientRect(),links=[...nav.querySelectorAll('a,summary')];
    const indents=[...nav.querySelectorAll('.sf-nav-children')].map(n=>{const group=n.parentElement.getBoundingClientRect(),child=n.firstElementChild?.getBoundingClientRect();return child?child.left-group.left:null;}).filter(n=>n!==null);
    const fit=links.every(n=>{const r=n.getBoundingClientRect();return r.left>=box.left&&r.right<=box.right+1&&n.scrollWidth<=n.clientWidth+1;});
    const arrows=[...nav.querySelectorAll('.sf-nav-chevron')].map(n=>n.getBoundingClientRect().right);
    return {fit,indents,arrowSpread:Math.max(...arrows)-Math.min(...arrows)};
   });
   assert(expanded.fit,'All expanded links/active backgrounds fit without clipping');
   for(const indent of expanded.indents)assert(indent>=24&&indent<=28,'Consistent 24-28px submenu indentation: '+indent);
   assert(expanded.arrowSpread<=3,'All levels align arrows near the right edge');
   metrics.expanded=expanded;
   const wrapping=await p.locator('.sf-shop-nav').evaluate(nav=>{
    const parent=nav.querySelector(':scope > details > summary'),label=parent.querySelector('span:not(.sf-nav-chevron)'),text=label.textContent;
    label.textContent='Customer relationships and inventory management with a longer translated label';
    const r=label.getBoundingClientRect(),arrow=parent.querySelector('.sf-nav-chevron').getBoundingClientRect(),icon=parent.querySelector('.sf-icon').getBoundingClientRect();
    const top=r.height>20&&r.left>=icon.right&&r.right<=arrow.left&&parent.scrollWidth<=parent.clientWidth+1;
    label.textContent=text;
    const child=nav.querySelector('.sf-catalog-nav a'),old=child.textContent;child.textContent='A longer translated catalog description that wraps naturally';
    const nested=child.getBoundingClientRect().height>40&&child.scrollWidth<=child.clientWidth+1;child.textContent=old;
    return top&&nested;
   });
   assert(wrapping,'Long parent/child labels wrap without clipping or arrow/icon overlap');
   if(fixture==='NewSale')metrics.billing=await p.locator('.billing-layout').evaluate(n=>({containerWidth:n.parentElement.getBoundingClientRect().width,columns:getComputedStyle(n).gridTemplateColumns}));
  }
  if(phase!=='before'&&width>=992){
   const children=await p.locator('.sf-shop-nav .sf-nav-child').evaluateAll(nodes=>nodes.map(n=>({height:n.getBoundingClientRect().height,font:getComputedStyle(n).fontSize})));
   for(const child of children){assert(child.height>=36&&child.height<=40,'Compact expanded submenu');assert.equal(child.font,'13px');}
  }
  await p.locator('.sf-shop-nav').evaluate(n=>n.scrollTop=0);
  const navBox=await p.locator('.sf-shop-nav').boundingBox(),pageScroll=await p.evaluate(()=>scrollY);
  await p.mouse.move(navBox.x+30,navBox.y+navBox.height/2);await p.mouse.wheel(0,240);
  await p.waitForFunction(()=>document.querySelector('.sf-shop-nav').scrollTop>0);
  assert.equal(await p.evaluate(()=>scrollY),pageScroll,'Wheel scrolling stays in navigation');
  const scroll=await p.locator('.sf-shop-nav').evaluate(n=>{const before=scrollY;n.scrollTop=n.scrollHeight;return {moved:n.scrollTop>0,bodyUnchanged:scrollY===before,overflow:getComputedStyle(n).overflowY};});
  assert(scroll.moved&&scroll.bodyUnchanged&&scroll.overflow==='auto','Expanded navigation scrolls independently');
  const last=p.locator('.sf-shop-nav a').last();await last.scrollIntoViewIfNeeded();await last.focus();
  assert(await last.evaluate(n=>{const r=n.getBoundingClientRect(),nav=n.closest('.sf-shop-nav').getBoundingClientRect();return r.top>=nav.top-1&&r.bottom<=nav.bottom+1&&getComputedStyle(n).outlineStyle!=='none';}),'Last menu item reachable with visible focus');
  if(width<=991)assert(await last.evaluate(n=>n.getBoundingClientRect().height>=44),'Mobile submenu touch target');
  await p.screenshot({path:path.join(out,`${width}x${height}-expanded.png`)});
  if(width>=992){await p.evaluate(()=>document.body.classList.add('sf-nav-collapsed'));await p.waitForTimeout(280);assert.equal(await p.locator('.sf-workspace').evaluate(n=>n.getBoundingClientRect().left),0,'Existing collapsed workspace uses full width');}
  else if(dashboard){await p.evaluate(()=>document.body.classList.remove('sf-nav-open'));await p.waitForTimeout(280);assert(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Closed mobile dashboard has no horizontal overflow');await p.screenshot({path:path.join(out,`${width}x${height}-closed.png`)});}
  results.push({viewport:[width,height],...metrics});
 }
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(`PASS ${phase}: ${sizes.length} sidebar viewports, dimensions/alignment, workspace offset, fixed header/footer, expanded scrolling, keyboard reachability and collapsed width.`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
