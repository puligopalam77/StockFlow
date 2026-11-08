// Rendered Razor + actual scripts/CSS through the read-only responsive audit host.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const origin='http://127.0.0.1:55863',out=path.resolve(__dirname,'../../.artifacts/compact-typography');
const sizes=[[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[390,844]];
const routes=['/','/Sales','/Sales/Create','/Sales/Details/1','/Quotations','/Quotations/Create','/Purchases','/Purchases/Create?mode=order','/Purchases/Details/1','/Inventory','/Inventory/LowStock','/Transfers','/Products','/Products/create','/Products/edit?id=1','/Categories','/Brands','/Labels','/Expenses','/Expenses/Create','/Expenses/Report','/Reports','/Reports/ProductSales','/Suppliers','/Customers','/Settings/Company','/Settings/BusinessProfile','/Admin/Users','/Admin/Roles','/Admin/Stores','/Admin/Gst'];
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true}),results=[],failures=[];
try{const queue=routes.filter(route=>!process.env.TYPOGRAPHY_ROUTE_FILTER||new RegExp(process.env.TYPOGRAPHY_ROUTE_FILTER).test(route)).flatMap(route=>sizes.map(size=>({route,size})));
await Promise.all(Array.from({length:2},async()=>{const page=await browser.newPage({reducedMotion:'reduce'});page.setDefaultTimeout(30000);
while(queue.length){const {route,size:[width,height]}=queue.shift();try{
 await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});await page.setViewportSize({width,height});
 const response=await page.goto(origin+route,{waitUntil:'domcontentloaded'});assert.equal(response.status(),200,'Page renders');
 if(/^\/Products\/(create|edit)/.test(route))await page.waitForFunction(()=>document.querySelector('#master-data')?.dataset.screenReady==='true');
 if(route==='/Sales/Create')await page.waitForSelector('#billing-summary');
 await page.waitForTimeout(150);
 const measured=await page.evaluate(()=>{
  const main=document.querySelector('main'),visible=n=>n.getClientRects().length&&!n.closest('dialog:not([open]),details:not([open])')&&getComputedStyle(n).visibility!=='hidden';
  const titles=[...main.querySelectorAll('h1')].filter(n=>visible(n)&&!n.closest('.sf-hero,.invoice-paper,.sf-document-brand,.sf-document-heading,.sf-report-brand')).map(n=>({text:n.textContent.trim(),font:getComputedStyle(n).fontSize,weight:+getComputedStyle(n).fontWeight,line:getComputedStyle(n).lineHeight}));
  const buttons=[...main.querySelectorAll(':is(.sf-page-heading,.sf-list-caption,.master-screen-heading,.admin-heading,.reports-header,.crm-heading,.company-page-heading,.quote-heading,.quote-list-heading,.configuration-heading) .btn')].filter(visible).map(n=>({id:n.id,text:n.textContent.trim(),height:n.getBoundingClientRect().height,font:getComputedStyle(n).fontSize,overflow:n.scrollWidth>n.clientWidth+1}));
  const pos=['#customer-search','#sale-scan','#complete-sale'].map(s=>{const n=document.querySelector(s);return n?{selector:s,height:n.getBoundingClientRect().height,font:getComputedStyle(n).fontSize}:null;}).filter(Boolean);
  return {titles,buttons,pos,overflow:document.documentElement.scrollWidth>innerWidth+1,sidebar:document.querySelector('.sf-sidebar').getBoundingClientRect().width,contentLeft:document.querySelector('.sf-workspace').getBoundingClientRect().left,breadcrumb:[...main.querySelectorAll('.sf-breadcrumb')].filter(visible).map(n=>getComputedStyle(n).fontSize)};
 });
 assert(!measured.overflow,'No horizontal page overflow');
 for(const t of measured.titles){assert.equal(t.font,'22px','Page title: '+t.text);assert(t.weight>=600&&t.weight<=700,'Title weight: '+JSON.stringify(t));}
 for(const b of measured.buttons){assert(b.height>=(width<=767?44:36)-.5,'Header action target: '+JSON.stringify(b));if(width>=768)assert(b.height<=40.5,'Compact header action: '+JSON.stringify(b));assert(!b.overflow,'Unclipped action: '+JSON.stringify(b));assert.equal(b.font,'13px');}
 for(const font of measured.breadcrumb)assert.equal(font,'12px','Breadcrumb font');
 if(width>=992){assert.equal(measured.sidebar,230);assert.equal(measured.contentLeft,230);}else assert.equal(measured.contentLeft,0);
 for(const control of measured.pos)assert(control.height>=(control.selector==='#complete-sale'?44:width<=767?44:38)-.5,'Comfortable POS control: '+JSON.stringify(control));
 if((width===1366||width===390)&&['/Sales/Create','/Expenses/Create','/Products/create','/Purchases/Details/1','/Reports'].includes(route))await page.screenshot({path:path.join(out,route.replace(/[^a-z0-9]/gi,'_')+'-'+width+'.png'),fullPage:true});
 results.push({route,width,...measured});
}catch(error){failures.push({route,width,error:error.message});console.error({route,width,error:error.message});}}
await page.close();}));
fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({results,failures},null,2));for(const failure of failures)console.error(failure);assert.equal(failures.length,0,'All typography/layout checks');
console.log(`PASS: compact titles/header actions, breadcrumbs, POS exceptions and overflow on ${new Set(results.map(r=>r.route)).size} routes at ${sizes.length} sizes (${results.length} layouts).`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
