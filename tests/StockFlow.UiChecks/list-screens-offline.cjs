const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/list-screen-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000},hasTouch:true});
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   const fixture=path.join(fixtures,url.pathname.slice(1).replaceAll('/','-')+'.html');
   if(fs.existsSync(fixture))return route.fulfill({path:fixture,contentType:'text/html'});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  for(const url of ['/Sales','/Purchases','/StockInflow','/StockOutflow','/Inventory','/Inventory/LowStock','/Products','/Products/Variants','/Categories','/Brands','/Suppliers','/Customers','/Admin/Users','/Admin/AuditLogs','/Admin/Stores','/Service','/Service/Pending','/Warranty/Lookup','/Warranty/Claims','/Warranty/Expiring','/Transfers','/Account/Sessions','/Search','/Reports/Stores','/Reports/Activity','/Reports/ProductSales']){
   await page.goto('http://stockflow.test'+url);
   await page.waitForSelector('.sf-list-caption');
   const result=await page.evaluate(()=>{
    const caption=document.querySelector('.sf-list-caption'),breadcrumb=document.querySelector('.sf-list-screen .sf-breadcrumb');
    const title=caption.matches('h1,h2')?caption:caption.querySelector('h1,h2');
    const table=document.createElement('table');table.className='table';table.innerHTML='<thead><tr><th>Heading</th></tr></thead><tbody><tr><td>Example item</td></tr><tr><td><button type="button" class="btn">View</button></td></tr></tbody>';
    document.querySelector('.sf-list-screen .configuration-content, .sf-list-screen').append(table);
    const cell=getComputedStyle(table.querySelector('td')),header=getComputedStyle(table.querySelector('th'));
    const result={title:getComputedStyle(title).fontSize,breadcrumbAfter:caption.nextElementSibling===breadcrumb,body:cell.fontSize,padding:cell.paddingTop,header:header.fontSize,headerPadding:header.paddingTop,rowHeights:[...table.querySelectorAll('tbody tr')].map(row=>Math.round(row.getBoundingClientRect().height))};
    table.remove();return result;
   });
   const dashboard=url==='/Reports/Activity';
   if(!dashboard){assert.equal(result.rowHeights[0],37,url+' compact plain rows');assert(result.rowHeights[1]>=57,url+' preserves at least 44px touchscreen buttons with cell padding: '+JSON.stringify(result.rowHeights));}
   delete result.rowHeights;
   assert.deepEqual(result,{title:'20px',breadcrumbAfter:true,body:dashboard?'11px':'12px',padding:dashboard?'4px':'6px',header:dashboard?'11px':'12px',headerPadding:dashboard?'4px':'6px'},url);
   if(url==='/Sales')await page.screenshot({path:path.join(fixtures,'sales-compact-desktop.png'),fullPage:true});
   for(const width of [768,390,320]){
    await page.setViewportSize({width,height:1000});await page.waitForTimeout(250);
    assert.equal(await page.locator('.sf-list-caption').evaluate(c=>getComputedStyle(c.matches('h1,h2')?c:c.querySelector('h1,h2')).fontSize),'20px',url+' mobile heading');
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),url+' mobile overflow '+width+' '+JSON.stringify(await page.evaluate(()=>[...document.querySelectorAll('main *')].filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,8).map(e=>({tag:e.tagName,id:e.id,cls:e.className,width:e.getBoundingClientRect().width})))));
    const inputs=await page.locator('input.form-control,select.form-select,textarea.form-control').evaluateAll(elements=>elements.filter(e=>e.getBoundingClientRect().width>0).map(e=>({font:parseFloat(getComputedStyle(e).fontSize),height:e.getBoundingClientRect().height})));
    assert(inputs.every(e=>e.font>=16&&e.height>=36),url+' touch inputs stay readable without focus zoom');
    assert(await page.locator('#sidebar-toggle').evaluate(e=>e.getBoundingClientRect().width>=44&&e.getBoundingClientRect().height>=44),url+' navigation touch target');
    await page.setViewportSize({width,height:450});
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),url+' fits with reduced keyboard space');
   }
   await page.setViewportSize({width:1440,height:1000});
  }
  console.log('PASS: 26 screens keep compact desktop rows and captions; tablet/phone touch inputs, navigation targets and reduced-height layouts fit.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
