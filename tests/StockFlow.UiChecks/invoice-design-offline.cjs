const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/invoice-design-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1100}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/Sales/Invoice/3')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(output,'Invoice.html'),'utf8')});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:JSON.stringify(url.pathname==='/api/branches/context'?{locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Counter'}],locationId:1,storeId:1}:[])});
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales/Invoice/3');
  const customer=await page.locator('.invoice-customer').textContent();
  for(const text of ['Rajesh Kumar','9876543210','45 MG Road','36ABCDE1234F1Z5'])assert(customer.includes(text));
  await page.locator('.invoice-paper').screenshot({path:path.join(output,'invoice-customer-preview.png')});
  await page.emulateMedia({media:'print'});
  assert.equal(await page.locator('.invoice-identification').evaluate(n=>getComputedStyle(n).color),'rgb(255, 255, 255)');
  await page.pdf({path:path.join(output,'invoice-customer-a4.pdf'),format:'A4',printBackground:true});
  await page.emulateMedia({media:'screen'});
  for(const width of [768,390,320]){
   await page.setViewportSize({width,height:1100});
   await page.waitForFunction(()=>innerWidth>991 || parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft)===0);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no page overflow at '+width);
   await page.locator('.invoice-paper').screenshot({path:path.join(output,'invoice-'+width+'.png')});
  }
  await page.locator('#invoice-print-layout').selectOption('thermal');await page.emulateMedia({media:'print'});
  assert(await page.locator('.invoice-paper').evaluate(n=>n.getBoundingClientRect().width<=281));
  assert((await page.locator('.invoice-customer').textContent()).includes('36ABCDE1234F1Z5'));
  await page.pdf({path:path.join(output,'invoice-customer-80mm.pdf'),width:'80mm',height:'600mm',printBackground:true});
  assert.deepEqual(errors,[]);
  console.log('PASS: saved customer details, branded A4 preview/PDF, 768/390/320 layouts and 80 mm receipt.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
