const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/receiving-ui-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[],posts=[],registrations=[],variantPosts=[],generated=[];
  const shirt={productId:1,variantId:1,productName:'Shirt / Black / M',variantName:'Black / M',sku:'SH-BLK-M',purchasePrice:20,taxPercentage:0,isActive:true,isSerialTracked:false,attributes:{Size:'M',Color:'Black'}};
  const battery={productId:2,variantId:2,productName:'Battery / Model A',sku:'BAT-A',purchasePrice:30,taxPercentage:0,isActive:true,isSerialTracked:true};
  const added={productId:3,variantId:3,productName:'New shirt',sku:'NEW',purchasePrice:25,taxPercentage:0,isActive:true,isSerialTracked:false,isDefault:true};
  const variant={...shirt,variantId:4,productName:'Shirt / Blue / M',sku:'SH-BL-M'};
  let failNext=false;
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const request=route.request(),url=new URL(request.url()),json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
   if(url.pathname==='/Receiving')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(output,'Receiving.html'),'utf8')});
   if(url.pathname==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}],locationId:1,storeId:1});
   if(url.pathname==='/api/suppliers')return json([{supplierId:1,supplierName:'Test supplier'}]);
   if(url.pathname==='/api/categories')return json([{categoryId:1,categoryName:'General'}]);
   if(url.pathname==='/api/products'&&request.method()==='POST'){registrations.push(request.postDataJSON());return json({productId:3});}
   if(url.pathname==='/api/products')return json([{productId:1,productName:'Shirt',hasVariants:true,isSerialTracked:false,taxPercentage:0,warrantyMonths:0}]);
   if(url.pathname==='/api/products/variants'&&request.method()==='POST'){variantPosts.push(request.postDataJSON());return json({variantId:4});}
   if(/^\/api\/products\/variants\/\d+\/barcode$/.test(url.pathname)){generated.push(url.pathname);return json({barcode:'SFV00000003'});}
   if(url.pathname==='/api/products/variants'){const id=url.searchParams.get('productId');return json(id==='3'?[added]:id==='2'?[battery]:id==='1'?[shirt,variant]:[shirt,battery]);}
   if(url.pathname==='/api/scan'){const code=url.searchParams.get('code');if(code==='0000123')return route.fulfill({status:404,contentType:'application/json',body:'{"message":"Product not found"}'});return json(code==='BAT'?battery:shirt);}
   if(url.pathname.startsWith('/api/receiving')){posts.push({url:url.pathname,body:request.postDataJSON()});if(failNext){const status=typeof failNext==='number'?failNext:503;failNext=false;return route.fulfill({status,contentType:'application/json',body:'{"message":"Receipt result uncertain"}'});}return json({intakeId:posts.length,purchaseId:url.pathname.endsWith('opening')?null:12,kind:url.pathname.endsWith('opening')?'OPENING_STOCK':'RECEIPT'});}
   if(url.pathname.startsWith('/api/'))return json([]);
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
  });
  async function start(opening=false){await page.goto('http://stockflow.test/Receiving');await page.waitForFunction(()=>!document.querySelector('#receiving-fields').disabled);if(opening)await page.locator('#intake-kind').selectOption('OPENING_STOCK');else await page.locator('#intake-supplier').selectOption('1');await page.locator('#intake-next').click();}
  async function scan(code){await page.locator('#intake-scan').fill(code);await page.locator('#intake-scan').press('Enter');}
  await start();await page.locator('#intake-find').click();await page.waitForSelector('#intake-variant-filters:not([hidden])');await page.locator('#intake-color-filter').selectOption('Black');assert.equal(await page.locator('#intake-product option').count(),2,'size/color filter narrows available variants');await scan('SHIRT');await page.waitForSelector('[data-variant="1"]');await scan('SHIRT');await page.waitForFunction(()=>document.querySelector('[data-variant="1"] input').value==='2');
  await scan('0000123');await page.waitForFunction(()=>document.querySelector('#intake-registration').open);assert.equal(await page.locator('#intake-new-code').inputValue(),'0000123');
  await page.locator('#intake-new-name').fill('New shirt');await page.locator('#intake-category').selectOption('1');await page.locator('#intake-new-cost').fill('25');await page.locator('#intake-new-price').fill('45');await page.locator('#intake-registration-form button[type="submit"]').click();await page.waitForSelector('[data-variant="3"]');assert.deepEqual(generated,[],'registration retains supplied codes without generating a repeated model barcode');
  assert.equal(registrations[0].barcode,'0000123');assert.equal(registrations[0].purchasePrice,25);
  await page.locator('#intake-add-variant').click();await page.waitForFunction(()=>document.querySelector('#intake-registration').open);await page.locator('#intake-new-name').fill('Blue / M');await page.locator('#intake-new-sku').fill('SH-BL-M');await page.locator('#intake-size').fill('M');await page.locator('#intake-color').fill('Blue');await page.locator('#intake-registration-form button[type="submit"]').click();await page.waitForSelector('[data-variant="4"]');assert.deepEqual(variantPosts[0].attributes,{Size:'M',Color:'Blue'});
  await page.locator('#intake-next').click();assert(await page.locator('#receiving-step-3').isVisible());failNext=true;await page.locator('#intake-save').click();await page.waitForSelector('#receiving-pending:not([hidden])', {timeout:5000}).catch(async error => { console.log({posts, errors, message:await page.locator('#receiving-message').textContent(), invalid:await page.locator(':invalid').evaluateAll(nodes=>nodes.map(n=>({id:n.id,value:n.value,message:n.validationMessage})))}); throw error; });assert(await page.locator('#receiving-fields').evaluate(el=>el.disabled));
  const saved=posts[0].body;await page.reload();await page.waitForSelector('#receiving-pending:not([hidden])');failNext=403;await page.locator('#receiving-retry').click();await page.waitForFunction(()=>!document.querySelector('#receiving-retry').disabled);assert(await page.locator('#receiving-pending').isVisible(),'authorization failure preserves uncertain receipt');await page.locator('#receiving-retry').click();await page.waitForSelector('#intake-success:not([hidden])');assert.deepEqual(posts[1].body,saved,'reload retries exact request');assert.deepEqual(posts[2].body,saved,'permission recovery retries same identifier');assert.equal(posts[2].body.items[0].quantity,2);
  await start(true);assert(await page.locator('#intake-supplier-fields').isHidden());await scan('BAT');await page.waitForSelector('[data-variant="2"]');await page.locator('#intake-next').click();assert((await page.locator('#receiving-message').textContent()).includes('exactly 1 serial'));
  const serialInput=page.locator('[data-variant="2"] input[placeholder="Scan serial and press Enter"]');await serialInput.fill('BAT-SERIAL-01');await serialInput.press('Enter');await page.waitForFunction(()=>document.querySelector('[data-variant="2"] textarea').value==='BAT-SERIAL-01');await page.locator('#intake-next').click();await page.locator('#intake-save').click();await page.waitForSelector('#intake-success:not([hidden])');assert.equal(posts[3].url,'/api/receiving/opening');assert.equal(posts[3].body.supplierId,null);assert.deepEqual(posts[3].body.items[0].serialNumbers,['BAT-SERIAL-01']);
  for(const width of [1440,768,390,320]){await page.setViewportSize({width,height:1000});await start();await scan('SHIRT');await page.waitForSelector('[data-variant="1"]');await page.screenshot({path:path.join(output,`receiving-${width}.png`),fullPage:true});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`overflow at ${width}`);}
  assert.deepEqual(errors,[]);console.log('PASS: guided receipt, repeated scans, unknown barcode, inline variants, retry/reload, serial opening stock and responsive layout.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
