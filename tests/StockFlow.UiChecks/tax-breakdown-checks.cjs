const assert=require('node:assert/strict'),fs=require('fs'),path=require('path');
const {selectValue}=require('./address-test-helpers.cjs');
module.exports=async(page,out)=>{
 const root=path.resolve(__dirname,'../..'),fixtures=JSON.parse(fs.readFileSync(path.join(root,'tests/bin/tax-breakdown-fixtures/quotes.json'),'utf8'));
 let scenario='mixed',failed=false,delay=0,requests=0;
 await page.route('**/Sales/Create',r=>r.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),'utf8').replace(/data-tax-inclusive="(?:true|false)"/,'data-tax-inclusive="'+(scenario==='inclusive')+'"')}));
 await page.route('**/api/sales/gst-context',r=>r.fulfill({json:{mapped:true,registrationType:'REGULAR',stateCode:'27'}}));
 await page.route('**/api/scan?**',r=>{const index=Number(new URL(r.request().url()).searchParams.get('code').replace('ITEM',''));return r.fulfill({json:{productId:index,variantId:index,productName:'Test product '+index,sku:'ITEM'+index,sellingPrice:scenario==='inclusive'?118:100,taxPercentage:index===1?5:index===2?18:0,currentStock:50,isActive:true,isAvailable:true,isSerialTracked:false}});});
 await page.route('**/api/sales/quote',async r=>{
  requests++;if(delay)await new Promise(resolve=>setTimeout(resolve,delay));if(failed)return r.fulfill({status:400,json:{message:'Synthetic tax quote failure'}});
  const body=r.request().postDataJSON(),fixture=fixtures.find(f=>f.name===scenario&&f.count===body.items.length&&f.discount===body.discountAmount&&f.pos===(body.gstPlaceOfSupplyStateCode||null));
  assert(fixture,'Real calculator fixture exists for '+JSON.stringify(body));return r.fulfill({json:fixture.quote});
 });
 const money=n=>'₹'+n.toFixed(2);
 for(scenario of ['mixed','inclusive']){
  await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.goto('http://stockflow.test/Sales/Create');await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
  const input=page.locator('#sale-scan');for(let i=1;i<=(scenario==='mixed'?3:1);i++){await input.fill('ITEM'+i);await input.press('Enter');await page.waitForFunction(count=>document.querySelectorAll('#sale-cart tr').length===count,i);}
  await page.waitForFunction(()=>document.querySelector('#billing-tax-status').textContent.includes('Select the place of supply'));
  await page.locator('#billing-tax-details>summary').click();assert(!/CGST \d|SGST \d|IGST \d/.test(await page.locator('#billing-tax-rates').textContent()),'No guessed split before place of supply');
  await selectValue(page.locator('#sale-gst-pos'),'27');await page.waitForFunction(()=>document.querySelector('#billing-tax-status').textContent.includes('CGST + SGST'));
  assert.equal(await page.locator('#summary-total').textContent(),scenario==='mixed'?'₹323.00':'₹118.00');
  assert((await page.locator('#billing-tax-rates').textContent()).includes(scenario==='mixed'?'SGST 9%: ₹9.00':'SGST 9%: ₹9.00'));
  if(scenario==='mixed'){assert.equal(await page.locator('#summary-without-gst').textContent(),'₹100.00');assert(await page.locator('#summary-without-gst-row').isVisible());}
  else {assert.equal(await page.locator('#summary-tax-label').textContent(),'GST included (18%)');assert.equal(await page.locator('#summary-taxable').textContent(),'₹100.00');}
  await page.locator('#sale-discount').fill('30');await page.locator('#sale-discount').press('Tab');
  const expected=fixtures.find(f=>f.name===scenario&&f.count===(scenario==='mixed'?3:1)&&f.discount===30&&f.pos==='27').quote;
  await page.waitForFunction(total=>document.querySelector('#summary-total').textContent===total,money(expected.totalAmount));
  assert.equal(await page.locator('#summary-taxable').textContent(),money(expected.taxableAmount));assert.equal(await page.locator('#summary-tax').textContent(),money(expected.taxAmount));
  const before=requests;delay=400;await selectValue(page.locator('#sale-gst-pos'),'29');
  await page.waitForFunction(()=>document.querySelector('#billing-tax-status').textContent.includes('updating'));
  assert.equal(await page.locator('#billing-tax-rates').textContent(),'','Old tax components disappear while quote is pending');
  await page.waitForFunction(()=>document.querySelector('#billing-tax-status').textContent.includes('Inter-state'));delay=0;
  assert.equal(requests,before+1,'One fresh quote for the place-of-supply change');assert(!(await page.locator('#billing-tax-rates').textContent()).includes('CGST'));
  assert.equal(await page.locator('#summary-total').textContent(),money(expected.totalAmount));
  for(const [width,height]of [[1366,768],[1536,864],[1920,1080],[390,844]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(300);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),JSON.stringify(await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,offenders:[...document.querySelectorAll('#billing-workspace *')].filter(n=>n.getBoundingClientRect().right>innerWidth+1).map(n=>({id:n.id,cls:n.className,right:n.getBoundingClientRect().right})).slice(0,8)}))));
   await page.locator('#billing-tax-details').evaluate(n=>n.scrollIntoView({block:'center',behavior:'instant'}));assert(await page.locator('#billing-tax-rates').isVisible());
   if(width===1366)await page.screenshot({path:path.join(out,'tax-breakdown-'+scenario+'.png')});
  }
  failed=true;await selectValue(page.locator('#sale-gst-pos'),'27');await page.waitForFunction(()=>document.querySelector('#billing-tax-status').textContent.includes('unavailable'));
  assert.equal(await page.locator('#billing-tax-rates').textContent(),'');assert(await page.locator('#complete-sale').isDisabled());failed=false;
 }
 await page.route('**/Sales/Invoice/901',r=>r.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/tax-breakdown-fixtures/Invoice.html'),'utf8')}));
 await page.setViewportSize({width:1366,height:768});await page.goto('http://stockflow.test/Sales/Invoice/901');
 assert.equal(await page.locator('.invoice-tax-rate').count(),2);assert((await page.locator('.invoice-tax-breakdown').textContent()).includes('CGST 9%'));
 for(const format of ['a4','thermal']){
  await selectValue(page.locator('#invoice-print-layout'),format);await page.emulateMedia({media:'print'});
  assert(await page.locator('.invoice-tax-breakdown').isVisible(),'Rate details included in '+format+' printing');
  assert(await page.locator('.invoice-tax-rate').evaluateAll(nodes=>nodes.every(n=>n.scrollWidth<=n.clientWidth+1)),'Rate details fit '+format);
  await page.emulateMedia({media:'screen'});
 }
 console.log('PASS tax breakdown UI using real C# quote fixtures: mixed rates, inclusive pricing, discounts, missing place of supply, CGST/SGST -> IGST, pending/error quote states, one refresh per POS change, four viewport sizes and actual Razor invoice rates in A4/thermal print.');
};
