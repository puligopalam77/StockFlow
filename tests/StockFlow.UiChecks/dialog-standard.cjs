// Actual Razor dialogs, styles and scripts; audit host rejects all mutations.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const origin='http://127.0.0.1:55863',out=path.resolve(__dirname,'../../.artifacts/dialog-standard');
const routes=['/','/Sales/Create','/Purchases/Create?mode=order','/Purchases/Details/1','/Receiving','/Products','/Products/Variants?productId=1','/Categories','/Brands','/Suppliers','/Customers','/Expenses/Create','/Labels','/Replenishment','/Admin/Stores','/Admin/Gst','/Account/Sessions','/CashClosing','/Quotations/Create','/Transfers','/Reports','/Admin/Users','/Admin/Roles'];
const sizes=[[1920,1080],[1440,900],[1366,768],[1280,720],[1024,768],[768,1024],[375,812],[1280,480],[683,384],[375,406]];
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true}),results=[],failures=[],unavailable=[];
try {const queue=[...routes];await Promise.all(Array.from({length:2},async()=>{
while(queue.length){const route=queue.shift(),page=await browser.newPage({reducedMotion:'reduce'});page.setDefaultTimeout(6000);try {
 await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});
 const response=await page.goto(origin+route,{waitUntil:'domcontentloaded'});if(response.status()!==200){unavailable.push({route,status:response.status()});continue;}
 await page.waitForTimeout(180);
 const ids=await page.locator('dialog[data-sf-dialog]').evaluateAll(ns=>ns.filter(n=>n.id).map(n=>n.id));
 // Shared confirmation is used by every module, including those with no forms.
 await page.evaluate(()=>{window.StockFlowDialog?.discard('DRF-0020: This unfinished bill will be permanently removed. Completed invoices and stock records will not be affected.',{title:'Discard saved bill?',confirmText:'Discard Bill'});});
 if(await page.locator('#sf-confirm-dialog').count())ids.push('sf-confirm-dialog');
 for(const id of new Set(ids))for(const [width,height] of sizes){try {
  await page.setViewportSize({width,height});
  await page.evaluate(id=>{document.querySelectorAll('dialog[open]').forEach(n=>n.close());const n=document.getElementById(id);n.hidden=false;n.showModal();n.querySelector('#sf-confirm-cancel')?.focus();},id);
  const measured=await page.locator('#'+id).evaluate(n=>{const r=n.getBoundingClientRect(),title=n.querySelector('h2');return {variant:n.dataset.sfDialog,width:r.width,left:r.left,right:r.right,top:r.top,bottom:r.bottom,scroll:n.scrollWidth-n.clientWidth,title:title?getComputedStyle(title).fontSize:null,buttons:[...n.querySelectorAll('.btn')].filter(b=>b.getClientRects().length&&!/^(Close|Cancel resume)/.test(b.getAttribute('aria-label')||'')).map(b=>({text:b.textContent.trim(),height:b.getBoundingClientRect().height,overflow:b.scrollWidth-b.clientWidth}))};});
  assert(measured.left>=0&&measured.right<=width+1&&measured.top>=0&&measured.bottom<=height+1,'Dialog fits viewport');
  assert(measured.scroll<=1,'No horizontal dialog overflow');
  if(width>=768&&['compact','medium','large'].includes(measured.variant))assert.equal(measured.width,{compact:440,medium:520,large:800}[measured.variant]>width-24?width-24:{compact:440,medium:520,large:800}[measured.variant]);
  if(id==='sf-confirm-dialog'&&height>=720&&width>=768)assert(measured.bottom-measured.top<320,'Content-based compact height');
  if(measured.title)assert.equal(measured.title,'18px');
  for(const b of measured.buttons)assert(b.height>=((width<=575)?44:40)-1,'Comfortable action: '+b.text);
  // Last action stays reachable in short/zoom-equivalent viewports.
  const actions=page.locator('#'+id+' button:visible');if(await actions.count()){const last=actions.last();await last.scrollIntoViewIfNeeded();assert(await last.isVisible());}
  if(route==='/'&&id==='sf-confirm-dialog'&&[1366,375,683].includes(width))await page.screenshot({path:path.join(out,`confirmation-${width}x${height}.png`)});
  if(route==='/Categories'&&id==='editor'&&[1366,375].includes(width))await page.screenshot({path:path.join(out,`category-${width}.png`)});
  if(route==='/Expenses/Create'&&id==='expense-master-dialog'&&[1366,375].includes(width))await page.screenshot({path:path.join(out,`payee-category-${width}.png`)});
  results.push({route,id,viewport:[width,height],...measured});
 }catch(e){failures.push({route,id,width,height,error:e.message});}}
 }catch(e){unavailable.push({route,error:e.message});}finally{await page.close();}console.log('Audited '+route);}
}));
fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({results,failures,unavailable},null,2));console.log(JSON.stringify({checks:results.length,dialogs:[...new Set(results.map(r=>r.id))],failures,unavailable},null,2));assert.equal(failures.length,0);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
