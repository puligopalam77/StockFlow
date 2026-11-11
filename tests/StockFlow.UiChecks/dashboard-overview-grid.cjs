// Actual Razor/CSS and sidebar toggle; frozen HTTP prevents application writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/phase13-ui-fixtures'),out=path.join(root,'.artifacts/overview-grid');
const sizes=[[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844]];
const expectedColumns=width=>width>=960?4:width>=720?3:width>=480?2:1;
async function routes(context,role='Admin'){
 await context.route('**/*',route=>{const req=route.request(),u=new URL(req.url());
  if(req.method()!=='GET')return route.fulfill({status:405});
  if(u.pathname==='/')return route.fulfill({path:path.join(fixtures,role+'-dashboard.html'),contentType:'text/html'});
  if(u.pathname.endsWith('.js')&&!/\/stockflow-shell(?:\.[a-z0-9]{10})?\.js$/.test(u.pathname))return route.fulfill({body:'',contentType:'text/javascript'});
  const file=path.resolve(assets,'.'+u.pathname.replace(/\.[a-z0-9]{10}(?=\.(css|js|png|svg)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
 });
}
async function measure(page){return page.locator('.sf-overview-grid').evaluate(grid=>{
 const box=n=>{const r=n.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height};};
 return {viewport:[innerWidth,innerHeight],zoom:devicePixelRatio,grid:box(grid),columns:getComputedStyle(grid).gridTemplateColumns.split(' ').length,overflow:document.documentElement.scrollWidth>innerWidth+1,cards:[...grid.querySelectorAll('.sf-kpi')].map(n=>({box:box(n),href:n.getAttribute('href'),icon:box(n.querySelector('.sf-kpi-icon')),iconColor:getComputedStyle(n.querySelector('.sf-kpi-icon')).backgroundImage,text:[...n.querySelectorAll('h2,strong,p')].map(t=>{const range=document.createRange();range.selectNodeContents(t);return {text:t.textContent.trim(),box:box(t),ink:box(range),clipped:t.scrollWidth>t.clientWidth+1||(t.scrollHeight>t.clientHeight+1&&getComputedStyle(t).overflowY!=='visible')};})}))};
 });}
function verify(m,count){
 assert.equal(m.columns,expectedColumns(m.grid.width));assert.equal(m.cards.length,count);assert(!m.overflow,'No horizontal page overflow');
 for(const [i,c] of m.cards.entries()){
  assert(c.box.height>=108&&c.icon.width===42&&c.icon.height===42,'Compact readable card '+i);
  assert(c.box.left>=m.grid.left-1&&c.box.right<=m.grid.right+1);
  for(const t of c.text){assert(!t.clipped,'Unclipped text: '+t.text);assert(t.box.left>=c.icon.right+8&&t.box.right<=c.box.right-10,'Text clears icon and card edges');assert(t.ink.top>=c.box.top&&t.ink.bottom<=c.box.bottom&&t.ink.right<=c.box.right-10,'Visible text ink stays inside card');}
  if(i%m.columns)assert(Math.abs(c.box.height-m.cards[i-i%m.columns].box.height)<1,'Equal heights within each row');
  if(i>=m.columns)assert(Math.abs(c.box.top-m.cards[i-m.columns].box.bottom-12)<1,'Continuous rows without section gaps');
 }
}
(async()=>{fs.mkdirSync(out,{recursive:true});const results=[],browser=await chromium.launch({channel:'msedge',headless:true});try {
 const context=await browser.newContext({reducedMotion:'reduce'});await routes(context);const page=await context.newPage();page.setDefaultTimeout(30000);const errors=[];page.on('pageerror',e=>errors.push(e.message));
 let baseline;
 for(const [width,height] of sizes){await page.setViewportSize({width,height});await page.goto('http://stockflow.test/');
  const initial=await measure(page);verify(initial,13);const values=initial.cards.map(c=>({href:c.href,text:c.text.map(t=>t.text),color:c.iconColor}));baseline??=values;assert.equal(new Set(values.map(c=>c.text[0])).size,values.length,'No duplicate cards');assert.deepEqual(values,baseline,'Values, labels, descriptions, links and icon colors unchanged');
  for(const label of ['Total Products','Stock received','Stock leaving the shop','Current Stock','Total Suppliers',"Today's Sales",'Monthly Sales','Customer Outstanding Dues','Overdue Payments','Low Stock'])assert(values.some(c=>c.text[0]===label),'Required card: '+label);
  results.push({mode:width>=992?'expanded':'drawer-closed',...initial});
  if([1920,1366,390].includes(width)){await page.locator('.retail-overview-title').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,`overview-${width}-expanded.png`)});}
  await page.locator('#sidebar-toggle').click();await page.waitForTimeout(300);const toggled=await measure(page);verify(toggled,13);results.push({mode:width>=992?'collapsed':'drawer-open',...toggled});
  if(width>=992){assert(toggled.grid.width>initial.grid.width,'Collapsed sidebar releases content width');await page.locator('.retail-overview-title').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,`overview-${width}-collapsed.png`)});}
  await page.locator(width>=992?'#sidebar-toggle':'#sidebar-close').click();await page.evaluate(()=>document.querySelectorAll('.sf-nav details').forEach(n=>n.open=true));const nested=await measure(page);verify(nested,13);assert.equal(nested.grid.width,initial.grid.width,'Nested menus do not widen sidebar');results.push({mode:'nested',...nested});
  await page.evaluate(()=>{for(const n of document.querySelectorAll('.sf-overview-grid strong'))n.textContent='₹12,34,56,78,901.50';});const large=await measure(page);verify(large,13);results.push({mode:'large-values',...large});
 }
 // Test exact content-width boundaries independently of viewport/sidebar width.
 await page.setViewportSize({width:1920,height:1080});await page.goto('http://stockflow.test/');
 for(const width of [479,480,719,720,959,960]){await page.locator('.sf-dashboard-main').evaluate((n,w)=>n.style.width=(w+32)+'px',width);const m=await measure(page);verify(m,13);assert.equal(m.grid.width,width);results.push({mode:'boundary',...m});}
 await context.close();const service=await browser.newContext();await routes(service,'Service');const servicePage=await service.newPage();await servicePage.goto('http://stockflow.test/');const permitted=await measure(servicePage);verify(permitted,9);assert(!permitted.cards.some(c=>["Today's Sales",'Monthly Sales','Customer Outstanding Dues','Total Suppliers'].includes(c.text[0])),'Role-restricted cards remain unavailable');await service.close();assert.deepEqual(errors,[]);
}finally{await browser.close();}
 // Use real browser tab zoom, not CSS transforms or device-scale emulation.
 const ext=path.join(out,'zoom-extension');fs.mkdirSync(ext,{recursive:true});fs.writeFileSync(path.join(ext,'manifest.json'),JSON.stringify({manifest_version:3,name:'StockFlow local zoom check',version:'1.0',permissions:['tabs'],background:{service_worker:'worker.js'}}));fs.writeFileSync(path.join(ext,'worker.js'),'chrome.runtime.onInstalled.addListener(()=>{});');
 for(const [width,height] of sizes.slice(0,5)){
  const context=await chromium.launchPersistentContext('',{channel:'msedge',headless:true,viewport:null,ignoreDefaultArgs:['--disable-extensions'],args:[`--window-size=${width},${height}`,'--disable-extensions-except='+ext,'--load-extension='+ext]});
  try{await routes(context);const worker=context.serviceWorkers()[0]||await context.waitForEvent('serviceworker'),page=await context.newPage();await page.goto('http://stockflow.test/');await worker.evaluate(async()=>{for(const tab of await chrome.tabs.query({}))if(tab.url?.startsWith('http://stockflow.test/'))await chrome.tabs.setZoom(tab.id,1.25);});await page.waitForFunction(()=>devicePixelRatio===1.25);
   const desktop=await page.evaluate(()=>innerWidth>=992);for(const mode of (desktop?['expanded','collapsed']:['drawer-closed','drawer-open'])){const m=await measure(page);verify(m,13);results.push({mode:'zoom125-'+mode,window:[width,height],...m});await page.locator('.retail-overview-title').scrollIntoViewIfNeeded();const cdp=await context.newCDPSession(page),shot=await cdp.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});fs.writeFileSync(path.join(out,`zoom125-${width}-${mode}.png`),Buffer.from(shot.data,'base64'));await cdp.detach();await page.locator(!desktop&&mode==='drawer-open'?'#sidebar-close':'#sidebar-toggle').click();await page.waitForTimeout(300);}
  }finally{await context.close();}
 }
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log('PASS: '+results.length+' Overview layouts; seven requested viewports, expanded/collapsed/drawer/nested menus, resizing, large values, exact 480/720/960px content boundaries, unchanged card contents/links/colors, role restrictions and actual 125% zoom.');
})().catch(e=>{console.error(e);process.exitCode=1;});
