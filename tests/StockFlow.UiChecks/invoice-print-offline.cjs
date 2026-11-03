const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/invoice-design-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const context=await browser.newContext({viewport:{width:1366,height:768}});
  await context.route('**/*',async route=>{
   const url=new URL(route.request().url()),match=url.pathname.match(/^\/Sales\/Invoice\/([5-8])$/);
   if(match)return route.fulfill({path:path.join(fixtures,`Invoice-${match[1]}.html`),contentType:'text/html'});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  const page=await context.newPage();
  const sizes=[[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]];
  for(const id of [5,6,7,8]){
   await page.goto(`http://stockflow.test/Sales/Invoice/${id}`);
   const original=await page.locator('.invoice-totals .grand-total').allTextContents();
   for(const format of ['a4','thermal']){
    await page.locator('#invoice-print-layout').selectOption(format);
    for(const [width,height] of sizes){
     await page.setViewportSize({width,height});
     assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${id}/${format}/${width}: page overflow`);
     assert.deepEqual(await page.locator('.invoice-totals .grand-total').allTextContents(),original,'format must retain saved totals');
    }
    await page.setViewportSize({width:1280,height:900});
    await page.emulateMedia({media:'print'});
    assert(await page.locator('.invoice-actions').isHidden());
    for(const selector of ['.sf-help-launcher','.sf-help-panel','.sf-topbar','.sf-sidebar']){
     for(const el of await page.locator(selector).all())assert(await el.isHidden(),`${selector} excluded from print`);
    }
    assert(await page.locator('.invoice-brand-footer').isVisible(),'brand footer retained');
    assert(await page.locator('.invoice-items tbody tr').count()===(id===8?70:id===6?2:1));
    const moneyCells=await page.locator('.invoice-paper .amount:visible').evaluateAll(nodes=>nodes.filter(n=>n.scrollWidth>n.clientWidth+1).map(n=>n.textContent));
    assert.deepEqual(moneyCells,[],`monetary clipping: ${id}/${format}: ${moneyCells.join(', ')}`);
    const paperHeight=await page.locator('.invoice-paper').evaluate(el=>el.getBoundingClientRect().height);
    if(id===5&&format==='thermal'){
     assert(paperHeight<600,`one-item receipt unexpectedly long: ${paperHeight}px`);
     assert.equal(await page.locator('.invoice-serial').count(),0);
     assert.equal(await page.locator('.invoice-warranties').count(),0);
     assert(await page.locator('.invoice-quantity-rate').isVisible());
     console.log(`Simple 80mm receipt content: ${Math.round(paperHeight)} CSS px / ${(paperHeight/96*25.4).toFixed(1)} mm.`);
    }
    if(id!==8||format==='a4'){
     const options=format==='a4'?{format:'A4',preferCSSPageSize:true}:{width:'80mm',height:`${Math.ceil(paperHeight/96*25.4+7)}mm`};
     const pdf=await page.pdf({path:path.join(fixtures,`invoice-${id}-${format}.pdf`),...options,printBackground:true});
     const pages=(pdf.toString('latin1').match(/\/Type\s*\/Page\b/g)||[]).length;
     if(id===8)assert(pages>1,'70-line A4 fixture must span multiple pages');
     if(id===5&&format==='thermal')assert.equal(pages,1,'simple receipt must fit the measured roll length');
    }
    await page.emulateMedia({media:'screen'});
   }
  }
  await page.goto('http://stockflow.test/Sales/Invoice/5');await page.locator('#invoice-print-layout').selectOption('thermal');await page.setViewportSize({width:1024,height:768});
  await page.screenshot({path:path.join(fixtures,'receipt-80mm.png'),fullPage:true});
  await page.goto('http://stockflow.test/Sales/Invoice/6');await page.screenshot({path:path.join(fixtures,'invoice-a4.png'),fullPage:true});
  const scaled=await browser.newContext({viewport:{width:1280,height:720},deviceScaleFactor:1.5});
  const zoom=await scaled.newPage();await zoom.setContent(fs.readFileSync(path.join(fixtures,'Invoice-5.html'),'utf8'));
  // Test browser zoom separately from Windows DPI or physical printer acceptance.
  await zoom.addStyleTag({content:fs.readFileSync(path.join(assets,'css/invoice.css'),'utf8')});await zoom.evaluate(()=>{document.body.style.zoom='1.5';document.body.classList.add('invoice-thermal');});
  assert(await zoom.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await scaled.close();
  console.log('PASS: A4/80mm saved amounts, compact receipt, serial/manual/warranty/credit/refunds, 70-line A4, ten viewports, simulated 150% zoom and private print chrome. Physical printers/Windows DPI not verified; 58mm unsupported.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
