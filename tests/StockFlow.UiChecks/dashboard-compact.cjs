// Dashboard geometry with actual Razor/CSS and synthetic data; no shop writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),phase=process.argv[2]||'after',out=path.join(root,'.artifacts/dashboard-compact',phase);
const sizes=[[1920,1080],[1920,900],[1440,900],[1366,768],[1366,600],[1280,720],[1280,600],[1024,768],[1024,600],[768,1024],[640,600],[375,812],[375,600],[320,568]];
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true}),results=[];try{
const page=await browser.newPage({reducedMotion:'reduce',deviceScaleFactor:1});
await page.route('**/*',route=>{const u=new URL(route.request().url());
 if(u.pathname==='/')return route.fulfill({path:path.join(root,'tests/bin/phase13-ui-fixtures/Admin-dashboard.html'),contentType:'text/html'});
 if(u.pathname.endsWith('.js'))return route.fulfill({body:'',contentType:'text/javascript'});
 if(phase==='before'&&/\/css\/dashboard-business(?:\.[a-z0-9]{10})?\.css$/.test(u.pathname))return route.fulfill({path:path.join(root,'.artifacts/dashboard-compact/before.css'),contentType:'text/css'});
 const file=path.resolve(assets,'.'+u.pathname.replace(/\.[a-z0-9]{10}(?=\.(css|png|svg)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
});
for(const [width,height]of sizes)for(const variant of ['four-actions','all-authorized','large-values']){
 await page.setViewportSize({width,height});await page.goto('http://stockflow.test/');
 // Store profiles can expose four or six actions. Keep all routes in production;
 // this fixture variant represents a store without service/warranty workflows.
 if(variant!=='all-authorized')await page.locator('.retail-daily-actions a[href="/Service/Create"],.retail-daily-actions a[href="/Warranty/Lookup"]').evaluateAll(nodes=>nodes.forEach(n=>n.remove()));
 if(variant==='large-values')await page.evaluate(()=>{document.querySelector('.sf-hero h1 em').textContent='Srinivasan Krishnamurthy Store Administrator';for(const n of document.querySelectorAll('.sf-hero-stat strong'))n.firstChild.textContent='12,34,56,78,901 ';});
 await page.waitForTimeout(100);
 const m=await page.evaluate(()=>{
  const r=n=>{const b=n.getBoundingClientRect();return {left:b.left,right:b.right,top:b.top,bottom:b.bottom,width:b.width,height:b.height};},one=s=>document.querySelector(s),visible=n=>n.getClientRects().length&&getComputedStyle(n).visibility!=='hidden';
  const hero=one('.sf-hero'),banner=r(hero),stats=[...hero.querySelectorAll('.sf-hero-stat')],note=one('.sf-hero-note'),quick=one('.retail-daily-actions'),overview=one('.retail-overview-title');
  const text=[...hero.querySelectorAll('.sf-eyebrow,h1,.sf-hero-content>p,.sf-hero-note,.sf-hero-stat small,.sf-hero-stat strong')].map(n=>{
   const range=document.createRange();range.selectNodeContents(n);const rects=[r(n),...range.getClientRects()],left=Math.min(...rects.map(b=>b.left)),right=Math.max(...rects.map(b=>b.right)),top=Math.min(...rects.map(b=>b.top)),bottom=Math.max(...rects.map(b=>b.bottom));
   // Font ink may exceed its line box with visible overflow; verify its actual
   // range fits inside the clipping ancestor, rather than demanding hidden ink.
   return {text:n.textContent.trim(),visible:!!visible(n),box:{left,right,top,bottom,width:right-left,height:bottom-top},dimensions:[n.clientWidth,n.scrollWidth,n.clientHeight,n.scrollHeight],fits:n.scrollWidth<=n.clientWidth+1&&(n.scrollHeight<=n.clientHeight+1||getComputedStyle(n).overflowY==='visible')};
  });
  const blocks=[...hero.querySelectorAll('.sf-eyebrow,h1,.sf-hero-content>p,.sf-hero-note,.sf-hero-stat')].map(r);
  const actions=[...quick.querySelectorAll('a')].map(n=>({href:n.getAttribute('href'),box:r(n),fits:n.scrollWidth<=n.clientWidth+1&&n.scrollHeight<=n.clientHeight+1}));
  const img=one('.sf-hero-photo');
  return {banner,sloganVisible:!!visible(note),text,blocks,stats:stats.map(r),actions,quick:r(quick),overview:r(overview),overviewCards:r(overview.nextElementSibling),bannerToQuick:r(quick).top-banner.bottom,quickToOverview:r(overview).top-r(quick).bottom,overviewToCards:r(overview.nextElementSibling).top-r(overview).bottom,image:{loaded:img.complete&&img.naturalWidth>0,fit:getComputedStyle(img).objectFit},overflow:document.documentElement.scrollWidth>innerWidth+1};
 });
 if(phase!=='before'){
  assert(!m.overflow,'No page overflow');assert(m.image.loaded&&m.image.fit==='cover','Original image loaded without stretching');
  assert.equal(m.stats.length,4);assert(m.sloganVisible,'Slogan stays visible');
  for(const n of m.text){assert(n.visible,'Foreground visible: '+n.text);assert(n.fits,'Foreground not clipped: '+JSON.stringify(n));assert(n.box.left>=m.banner.left-1&&n.box.right<=m.banner.right+1&&n.box.top>=m.banner.top-1&&n.box.bottom<=m.banner.bottom+1,'Foreground inside banner: '+n.text);}
  for(let i=0;i<m.blocks.length;i++)for(let j=i+1;j<m.blocks.length;j++){const a=m.blocks[i],b=m.blocks[j],overlap=Math.min(a.right,b.right)-Math.max(a.left,b.left)>1&&Math.min(a.bottom,b.bottom)-Math.max(a.top,b.top)>1;assert(!overlap,'Foreground regions do not overlap '+i+'/'+j);}
  assert(Math.max(...m.stats.map(n=>n.height))-Math.min(...m.stats.map(n=>n.height))<1,'Equal statistic heights');
  assert.equal(m.bannerToQuick,12);assert(m.quickToOverview>=12&&m.quickToOverview<=16);assert(m.overviewToCards>=10&&m.overviewToCards<=12);
  for(const a of m.actions){assert(a.box.height>=70&&a.fits,'Readable, fully clickable quick action');}
  for(const href of ['/Sales/Create','/Purchases/Create','/Inventory','/CashClosing'])assert(m.actions.some(n=>n.href===href),'Preserved action: '+href);
  if(width>=1280&&variant!=='large-values'){assert(m.stats.every(n=>Math.abs(n.top-m.stats[0].top)<1),'Four stats per row');if(variant==='four-actions')assert(m.actions.every(n=>Math.abs(n.box.top-m.actions[0].box.top)<1),'Four actions per row');}
 }
 if(variant==='four-actions'&&[[1920,1080],[1366,768],[1280,600],[768,1024],[375,812]].some(([w,h])=>w===width&&h===height))await page.screenshot({path:path.join(out,`${width}x${height}.png`),fullPage:true});
 if(variant==='four-actions'&&width===375&&height===812)await page.screenshot({path:path.join(out,'375x812-viewport.png')});
 if(phase!=='before'&&variant==='large-values'&&width===375&&height===812)await page.screenshot({path:path.join(out,'long-content-375.png'),fullPage:true});
 results.push({viewport:[width,height],variant,...m});
}
fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(phase==='before'?`CAPTURE baseline: ${results.length} dashboard layouts at ${sizes.length} viewports.`:`PASS ${phase}: ${results.length} dashboard layouts; ${sizes.length} viewports, all content, slogan, stats, action routes, spacing and large-content growth.`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
