const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
let partial=fs.readFileSync(path.join(root,'src/StockFlow.Web/Pages/Shared/_ProductRegistrationForm.cshtml'),'utf8');
partial=partial.replace(/^@\{.*?\}\s*/, '').replace('@(catalogOnly ? "Add new product" : "Add new product to this receipt")','Add new product').replace('@(catalogOnly ? "Save product" : "Save product & add to receipt")','Save product').replace('@if(Access.Can("BRANDS")){','').replace('</div>}','</div>');
let html=fs.readFileSync(path.join(root,'tests/bin/configuration-ui-fixtures/Products.html'),'utf8');
const start=html.indexOf('<dialog id="purchase-registration"'),end=html.indexOf('</dialog>',start)+9;
assert(start>0&&end>start);html=html.slice(0,start)+partial+html.slice(end);
html=html.replace('<body ', '<body data-shop-type="battery-geyser" ').replace(/class="sf-app"/,'class="sf-app sf-retail-ui"').replace('</head>','<link rel="stylesheet" href="/css/retail-usability.css"></head>').replace('</body>','<script src="/js/retail-usability.js"></script></body>');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},reducedMotion:'reduce'}),errors=[],writes=[];
  page.on('pageerror',error=>errors.push(error.stack||error.message));
  let products=[],missingGeyser=false;
  const categories=[{categoryId:1,categoryPath:'Battery'},{categoryId:2,categoryPath:'Geyser'},{categoryId:3,categoryPath:'Spare Part'},{categoryId:4,categoryPath:'Television'}];
  await page.route('**/*',async route=>{
   const request=route.request(),p=new URL(request.url()).pathname;
   const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Products')return route.fulfill({contentType:'text/html',body:html});
   if(p==='/api/categories')return json(categories.filter(category=>!missingGeyser||category.categoryId!==2));
   if(p==='/api/brands')return json([{brandId:1,brandName:'Battery brand',categoryIds:[1]},{brandId:2,brandName:'Geyser brand',categoryIds:[2]}]);
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Stock'}]});
   if(p==='/api/products'&&request.method()==='POST'){
    const body=request.postDataJSON();writes.push(body);const row={...body,productId:products.length+1,isActive:true};products.push(row);return json(row,201);
   }
   if(p==='/api/products')return json(products);
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Products');await page.locator('#master-rows').waitFor();
  const language=await page.locator('#language-choice').inputValue();
  await page.locator('#add-record').click();await page.locator('#purchase-registration-fields:not([disabled])').waitFor();
  assert(await page.locator('#quick-sku').inputValue());
  assert.equal(await page.locator('#quick-sku').isVisible(),false,'Generated item code stays under optional fields');
  assert.equal(await page.locator('#quick-category option[value="4"]').count(),0,'Unrelated appliance groups are hidden for new products');
  await page.locator('#quick-kind').selectOption('battery');
  assert.equal(await page.locator('#quick-category').inputValue(),'1');assert.equal(await page.locator('#quick-tracking').inputValue(),'SERIAL');
  await page.locator('#quick-product-name').fill('Exide 150 Ah battery');
  await page.getByLabel('Capacity (Ah)',{exact:true}).fill('150 Ah');await page.getByLabel('Voltage (V)',{exact:true}).fill('12 V');
  await page.locator('#quick-specifications details summary').click();
  await page.getByLabel('Free replacement period (months)',{exact:true}).fill('24 months');
  await page.getByLabel('Part-payment warranty period (months)',{exact:true}).fill('24 months');
  await page.locator('#quick-purchase').fill('5000');await page.locator('#quick-sale').fill('6500');await page.locator('#quick-warranty').fill('48');
  await page.screenshot({path:path.join(root,'tests/bin/battery-product-form.png'),fullPage:true});
  await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration').waitFor({state:'hidden'});
  assert.equal(writes.length,1);assert.equal(writes[0].trackingType,'SERIAL');assert.equal(writes[0].categoryId,1);
  assert.equal(writes[0].variants[0].attributes.Capacity,'150 Ah');assert.equal(writes[0].variants[0].attributes['Free replacement warranty'],'24 months');
  assert.equal(writes[0].variants[0].attributes['Pro-rata warranty'],'24 months');assert.equal(writes[0].warrantyMonths,48);
  assert.equal(writes[0].taxPercentage,0,'No GST rate is guessed');
  await page.locator('#add-record').click();await page.locator('#purchase-registration-fields:not([disabled])').waitFor();
  assert.notEqual(await page.locator('#quick-sku').inputValue(),writes[0].variants[0].sku);
  await page.locator('#quick-kind').selectOption('geyser');assert.equal(await page.locator('#quick-category').inputValue(),'2');
  await page.locator('#quick-product-name').fill('Bajaj 25 L geyser');
  await page.getByLabel('Tank capacity (L)',{exact:true}).fill('25 L');await page.getByLabel('Power (W)',{exact:true}).fill('2000 W');
  await page.locator('#quick-specifications details summary').click();
  await page.getByLabel('Tank warranty (months)',{exact:true}).fill('60 months');
  await page.locator('#quick-purchase').fill('6000');await page.locator('#quick-sale').fill('8000');await page.locator('#quick-warranty').fill('24');
  for(const width of [1440,768,390,320]) {
   await page.setViewportSize({width,height:900});
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   assert(await page.locator('#purchase-registration').evaluate(dialog=>dialog.scrollWidth<=dialog.clientWidth+1),'Product dialog overflow at '+width);
   assert(await page.locator('#quick-purchase').evaluate(input=>parseFloat(getComputedStyle(input).fontSize)>=14));
  }
  await page.setViewportSize({width:1440,height:1000});
  await page.locator('#quick-kind').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(root,'tests/bin/geyser-product-form.png'),fullPage:true});
  await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration').waitFor({state:'hidden'});
  assert.equal(writes.length,2);assert.equal(writes[1].categoryId,2);assert.equal(writes[1].variants[0].attributes['Tank capacity'],'25 L');
  assert.equal(writes[1].variants[0].attributes['Tank warranty'],'60 months');assert.equal(writes[1].warrantyMonths,24,'Tank notes do not extend the overall warranty automatically');
  assert.equal(writes[1].variants[0].attributes.Capacity,undefined,'Battery fields do not leak into the next registration');
  missingGeyser=true;await page.locator('#add-record').click();await page.locator('#purchase-registration-fields:not([disabled])').waitFor();
  await page.locator('#quick-kind').selectOption('geyser');await page.locator('#quick-product-name').fill('Another geyser');
  await page.locator('#purchase-registration-save').click();assert.equal(writes.length,2);
  assert(await page.locator('#quick-category-error').isVisible(),'Missing setup is explained and cannot save with a wrong group');
  assert((await page.locator('#quick-category-help').innerText()).includes('Product groups'));
  assert.equal(await page.locator('#language-choice').inputValue(),language,'Language selection is untouched');
  const retained=await page.evaluate(()=>{
      const host=document.createElement('div'), category=document.createElement('select');
      category.append(new Option('Old appliance model','1')); document.body.append(host,category);
      const editor=ProductSpecifications.create(host,category,{'Existing detail':'Keep this value','Capacity':'150 Ah'});
      editor.setTemplate('geyser'); const result=editor.read(); editor.destroy(); host.remove(); category.remove(); return result;
  });
  assert.equal(retained['Existing detail'],'Keep this value'); assert.equal(retained.Capacity,'150 Ah','Changing a detail template retains saved attributes');
  assert.deepEqual(errors,[]);
  console.log('PASS: battery/geyser fields, catalog grouping, serial defaults, generated codes, separate warranty notes, missing-group validation, mobile forms and unchanged language preference.');
 } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
