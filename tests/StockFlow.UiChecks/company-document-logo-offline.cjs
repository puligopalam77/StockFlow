const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/company-profile-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  const documents={'/Sales/Invoice/3':'Invoice','/StockInflow/Details/PURCHASE/1':'Receipt','/Reports/ProductSales':'Report'};
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(documents[url.pathname])return route.fulfill({path:path.join(fixtures,documents[url.pathname]+'.html'),contentType:'text/html'});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.resolve(assets,'.'+url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales/Invoice/3');
  const logo=page.locator('.invoice-business-logo');
  await logo.evaluate(image=>image.decode());
  const source=await logo.getAttribute('src');assert(source.startsWith('data:image/png;base64,'));
  assert.equal(await page.locator('.invoice-footer-logo').getAttribute('src'),source);
  for(const format of ['a4','thermal']){
   await page.locator('#invoice-print-layout').selectOption(format);
   await page.emulateMedia({media:'print'});
   assert(await logo.isVisible());assert(await logo.evaluate(image=>image.naturalWidth>0&&image.getBoundingClientRect().height>0));
   await page.pdf({path:path.join(fixtures,'business-logo-'+format+'.pdf'),...(format==='a4'?{format:'A4'}:{width:'80mm',height:'600mm'}),printBackground:true});
   await page.emulateMedia({media:'screen'});
  }
  for(const width of [390,320]){
   await page.setViewportSize({width,height:1000});
   await page.waitForTimeout(300);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert(await logo.isVisible());
  }
  for(const [url,name] of Object.entries(documents).filter(([,name])=>name!=='Invoice')){
   await page.goto('http://stockflow.test'+url);
   const documentLogo=page.locator('.sf-document-brand img');
   await documentLogo.evaluate(image=>image.decode());assert.equal(await documentLogo.getAttribute('src'),source);
   if(name==='Report')assert(await documentLogo.isHidden(),'Report branding stays out of the working screen');
   await page.emulateMedia({media:'print'});assert(await documentLogo.isVisible());
   assert(await documentLogo.evaluate(image=>image.naturalWidth>0&&image.getBoundingClientRect().height>0));
   await page.emulateMedia({media:'screen'});
  }
  console.log('PASS: saved logo on older invoices, matching footer, A4/80 mm PDFs, mobile layouts and printed receipt/report branding.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
