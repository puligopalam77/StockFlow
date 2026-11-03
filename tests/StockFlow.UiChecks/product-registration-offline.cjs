const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname,'../..'), assets = path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser = await chromium.launch({channel:'msedge',headless:true});
 try {
  const page = await browser.newPage({reducedMotion:"reduce"}); page.setDefaultTimeout(8000); const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  const chooseCategory=async value=>{await page.locator('#quick-category-toggle').click();await page.locator('#quick-category').selectOption(value);};
  const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=','base64');
  let products=[], saved, fail=false, writes=0;
  await page.route('**/*',async route=>{
   const req=route.request(), p=new URL(req.url()).pathname;
   const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Products')return route.fulfill({path:path.join(root,'tests/bin/configuration-ui-fixtures/Products.html'),contentType:'text/html'});
   if(p==='/api/products/1/image')return route.fulfill({contentType:'image/png',body:png});
   if(p==='/api/branches/context')return json({locations:[],storeId:0,locationId:0});
   if(p==='/api/categories/schema')return json({available:false,attributes:[],categories:[],lockedCategoryIds:[]});
   if(p==='/api/categories')return json([{categoryId:1,categoryPath:'Electronics > Inverters'},{categoryId:2,categoryPath:'Electronics > Batteries'},{categoryId:3,categoryPath:'Electronics > Geysers'},{categoryId:4,categoryPath:'Accessories'}]);
   if(p==='/api/brands')return json([{brandId:1,brandName:'Inverter brand',categoryIds:[1]},{brandId:2,brandName:'Battery brand',categoryIds:[2]},{brandId:3,brandName:'Shared brand',categoryIds:[1,2]}]);
   if(p==='/api/products/variants')return json(saved?.variants.map((v,i)=>({...v,variantId:i+10,barcode:v.primaryBarcode || 'SFV00000010',barcodeSource:v.barcodeSource,productId:1})) || []);
   if(p==='/api/products' || p==='/api/products/1'){
    if(req.method()==='GET')return json(p.endsWith('/1')?products[0]:products);
    writes++; if(fail)return json({message:'SKU already exists.'},400);
    saved=req.postDataJSON(); products=[{...saved,productId:1,isActive:true}]; return json(products[0]);
   }
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep) && fs.existsSync(file) && fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Products'); await page.getByText('No matching records.').waitFor();
  assert.equal(await page.locator('.configuration-menu a[href="/Products/Variants"]').count(),0);
  await page.locator('#add-record').click(); await page.locator('#purchase-registration[open]').waitFor();
  assert.equal(await page.locator('#quick-tracking').inputValue(),'SERIAL');
  assert.equal(await page.locator('#quick-code,#quick-code-kind,#quick-aliases,#quick-generate-barcode').count(),0,'Product registration leaves barcode entry to receiving');
  assert.equal(await page.locator('#editor[open]').count(),0,'Setup creation uses the shared New Stock form');
  await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click();
  assert(await page.locator('#quick-product-name-error').isVisible());assert(await page.locator('#quick-category-error').isVisible());assert.equal(writes,0);
  await page.evaluate(()=>StockFlowValidation.validate(document.getElementById('purchase-registration-form'),false));
  assert.equal(await page.locator('#purchase-registration-form .sf-validation-summary:visible').count(),0,'No repeated validation banner');
  assert.equal(await page.locator('#quick-product-name-error').count(),1,'Language/shared revalidation reuses one field error');
  await page.locator('#registration-tab-media').click();
  await page.locator('#quick-image').setInputFiles({name:'inverter.png',mimeType:'image/png',buffer:png});
  await page.locator('#quick-image').locator('..').getByRole('link',{name:'inverter.png',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('#quick-image').parentElement.querySelector('img').naturalWidth>0);
  await page.locator('#purchase-registration').getByRole('button',{name:'Remove image',exact:true}).click();
  assert.equal(await page.locator('#quick-image').evaluate(input=>input.files.length),0);assert(await page.locator('#quick-image').locator('..').locator('img').isHidden());
  await page.locator('#quick-image').setInputFiles({name:'inverter.png',mimeType:'image/png',buffer:png});
  assert.equal(new URL(page.url()).pathname,'/Products','Preview remains in the same screen');
  await page.locator('#registration-tab-basic').click();
  await page.locator('#quick-product-name').fill('Inverter'); await chooseCategory('1');
  assert.equal(await page.getByLabel('Specification template',{exact:true}).inputValue(),'inverter');
  assert.equal(await page.locator('#quick-brand option').count(),3);await page.locator('#quick-brand').selectOption('1');
  await page.locator('#registration-tab-media').click();await page.getByLabel('Inverter rating (VA)',{exact:true}).fill('1100 VA');
  await page.locator('#registration-tab-basic').click();await chooseCategory('2');await page.locator('#registration-tab-media').click();assert(await page.getByLabel('Capacity (Ah)',{exact:true}).isVisible());
  assert.equal(await page.locator('#quick-brand').inputValue(),'','incompatible brand cleared on category change');assert.equal(await page.locator('#quick-brand option[value="1"]').count(),0);assert.equal(await page.locator('#quick-brand option[value="2"]').count(),1);
  await page.locator('#registration-tab-basic').click();await chooseCategory('3');await page.locator('#registration-tab-media').click();assert(await page.getByLabel('Tank capacity (L)',{exact:true}).isVisible());
  await page.locator('#registration-tab-basic').click();await chooseCategory('4');assert.equal(await page.getByLabel('Specification template',{exact:true}).inputValue(),'');
  await chooseCategory('1');assert.equal(await page.getByLabel('Inverter rating (VA)',{exact:true}).inputValue(),'1100 VA','category changes preserve values');
  await page.getByLabel('SKU *',{exact:true}).fill('INV-1100');
  await page.locator('#registration-tab-variants').click();await page.locator('#quick-has-variants').check(); await page.getByLabel('Size / model name *',{exact:true}).fill('1100 VA');
  await page.locator('#registration-tab-media').click();
  await page.getByRole('button',{name:'Add specification',exact:true}).click();
  await page.getByLabel('Specification name',{exact:true}).fill('Rating'); await page.getByLabel('Specification value',{exact:true}).fill('1100 VA');
  await page.locator('#registration-tab-basic').click();await page.locator('#quick-description').fill('Shared registration description');
  await page.locator('#registration-tab-media').click();
  await page.locator('#quick-image-url').fill('https://example.test/inverter.png');
  await page.locator('#registration-tab-basic').click();
  for(const width of [1440,390,320]){
   await page.setViewportSize({width,height:900}); await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`page overflow ${width}`);
   assert(await page.locator('#purchase-registration').evaluate(e=>e.scrollWidth<=e.clientWidth),`dialog overflow ${width}`);
   if(width===1440){
    const columns=await page.locator('#quick-product-name,#quick-category,#quick-brand').evaluateAll(items=>items.map(item=>{const box=item.closest('.col-md-6').getBoundingClientRect();return{x:Math.round(box.x),y:Math.round(box.y),width:Math.round(box.width)}}));
    assert.equal(new Set(columns.map(x=>x.y)).size,1,'first three product fields share one row');
    assert.equal(new Set(columns.map(x=>x.x)).size,3,'product registration uses three desktop columns');
    assert((await page.locator('#purchase-registration').boundingBox()).width<=1080,'shared product dialog width');
   }
   await page.screenshot({path:path.join(root,`tests/bin/configuration-ui-fixtures/electronics-${width}.png`)});
  }
  fail=true; await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click(); await page.getByText('SKU already exists.',{exact:true}).waitFor();
  assert(await page.locator('#purchase-registration').evaluate(e=>e.open)); assert.equal(await page.locator('#quick-sku').inputValue(),'INV-1100');
  fail=false; await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click(); await page.getByText('Record saved.',{exact:true}).waitFor();
  assert.equal(writes,2); assert.equal(saved.variants.length,1); assert.equal(saved.variants[0].attributes.Rating,'1100 VA'); assert(saved.variants.every(v=>v.isSerialTracked));
  assert.equal(saved.description,'Shared registration description');assert.equal(saved.productImageUrl,'https://example.test/inverter.png');
  assert(await page.locator('#master-rows').getByText('Inverter',{exact:true}).isVisible(),'registered product appears immediately in Setup list');
  assert.equal(new URL(page.url()).pathname,'/Products');
  await page.getByRole('button',{name:'Edit',exact:true}).click(); await page.locator('#editor[open]').waitFor();
  assert.equal(await page.locator('#product-configurations fieldset').count(),1);
  await page.waitForFunction(()=>document.querySelector('#productImage').parentElement.querySelector('img').naturalWidth>0);
  assert((await page.locator('#productImage').locator('..').getByRole('link',{name:'inverter.png',exact:true}).getAttribute('href')).endsWith('/api/products/1/image'),'Edit restores the uploaded image');
  assert.equal(await page.locator('#product-configurations fieldset').first().getByLabel('Inverter rating (VA)',{exact:true}).inputValue(),'1100 VA','saved suggested specifications reload');
  await page.locator('#editor').getByRole('button',{name:'Remove image',exact:true}).click();
  await page.getByRole('button',{name:'Add size / model',exact:true}).click();
  const second=page.locator('#product-configurations fieldset').nth(1);
  await second.getByLabel('Size / model name *',{exact:true}).fill('1500 VA'); await second.getByLabel('Item code / SKU *',{exact:true}).fill('INV-1500');
  await page.locator('#hasVariants').click(); assert(await page.locator('#hasVariants').isChecked(),'saved variants cannot be hidden by disabling the option');
  await page.locator('#master-form [type=submit]').click();await page.locator('#editor').waitFor({state:'hidden'});assert.equal(saved.variants.length,2,'existing multi-variant editing remains available');assert.equal(saved.removeProductImage,true,'saved image removal reaches the API');
  await page.setViewportSize({width:1440,height:900});await page.locator('#add-record').click();await page.locator('#purchase-registration[open]').waitFor();
  assert.notEqual(await page.locator('#quick-sku').inputValue(),'INV-1100');assert.equal(await page.locator('#quick-product-name').inputValue(),'','next registration starts empty');
  assert.deepEqual(errors,[]);
  console.log('PASS: shared Setup registration, all-field validation, category brands/specifications, metadata, list refresh, failed-save retention, multi-variant edits and 1440/390/320 layouts.');
 } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
