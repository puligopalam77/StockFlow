const fs=require('fs'),path=require('path'),assert=require('assert');const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/fast-billing-ui-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{const url=new URL(route.request().url());
  if(url.pathname==='/Sales/Invoice/1')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(output,'invoice-manual.html'),'utf8')});
  if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
  const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 page.setDefaultTimeout(10000);await page.goto('http://stockflow.test/Sales/Invoice/1');
 for(const format of ['a4','thermal']){
  await page.emulateMedia({media:'screen'});await page.locator('#invoice-print-layout').selectOption(format);await page.emulateMedia({media:'print'});
  const text=await page.locator('.invoice-paper').textContent();for(const value of ['Small cord','Manual Item','MRP:','610910','Transient counter item','10%','Item Subtotal','Item Discounts','Invoice Discount','Taxable Amount'])assert(text.includes(value),format+' preserves '+value);
  const pdf=await page.pdf({path:path.join(output,'manual-invoice-'+format+'.pdf'),printBackground:true,preferCSSPageSize:true});assert(pdf.length>1000);
  console.log('PASS '+format+' print media and generated browser PDF with manual line details');
 }
 assert.deepEqual(errors,[]);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
