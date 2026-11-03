// Uses real Razor fixtures and mocked APIs. No application database access.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/configuration-ui-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try {
 const page=await browser.newPage({reducedMotion:'reduce',viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const products=[{productId:1,productName:'Cotton Shirt',categoryId:1,hasVariants:true,isSerialTracked:false,trackingType:'QUANTITY',taxPercentage:5,warrantyMonths:12,warrantyProvider:'MANUFACTURER',isActive:true},{productId:2,productName:'Battery',categoryId:1,hasVariants:false,isSerialTracked:true,trackingType:'SERIAL',taxPercentage:18,warrantyMonths:36,isActive:true}];
 let variants=[{variantId:11,productId:1,productName:'Cotton Shirt / DEFAULT',variantName:'DEFAULT',isDefault:true,sku:'SHIRT-DEFAULT',isSerialTracked:false,purchasePrice:100,sellingPrice:200,taxPercentage:5,warrantyMonths:12,minimumStockLevel:2,attributes:{},barcodes:['001234'],barcode:'001234',barcodeSource:'MANUFACTURER',isActive:true,currentStock:4},{variantId:21,productId:2,productName:'Battery / DEFAULT',variantName:'DEFAULT',isDefault:true,sku:'BATTERY',isSerialTracked:true,purchasePrice:500,sellingPrice:600,taxPercentage:18,warrantyMonths:36,minimumStockLevel:0,attributes:{},barcodes:[],isActive:true,currentStock:1}];
 let productBody,variantBody,fail=false,writes=0;
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  const json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
  if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}]});
  if(p==='/api/products'){
   if(req.method()==='POST'){productBody=req.postDataJSON();return json({...productBody,productId:1});}return json(products);
  }
  if(p==='/api/products/1')return json(products[0]);
  if(p==='/api/categories')return json([{categoryId:1,categoryName:'Shirts',categoryPath:"Clothing / Men's Wear / Shirts"}]);
  if(p==='/api/brands')return json([]);
  if(p.startsWith('/api/products/variants') && /\/(print|reprint)$/.test(p))return json({});
  if(p.startsWith('/api/products/variants')){
   if(req.method()==='GET')return json(variants.filter(v=>v.productId===Number(url.searchParams.get('productId'))));
   if(p.endsWith('/barcode'))return json({barcode:'SFV00000011'});
   writes++;assert.ok(req.headers()['x-csrf-token']);variantBody=req.postDataJSON();
   if(fail)return json({message:'SKU already exists.'},409);
   const id=req.method()==='POST'?12:Number(p.split('/').pop());variants=variants.filter(v=>v.variantId!==id);variants.push({...variantBody,variantId:id,productName:'Cotton Shirt / '+variantBody.variantName,currentStock:0,barcode:variantBody.primaryBarcode||'SFV00000012'});return json({variantId:id});
  }
  if(p==='/api/scan')return json({productId:2,productUnitId:25,productName:'Battery',serialNumber:'SERIAL-25'});
  if(p.startsWith('/api/'))return json([]);
  const fixture=path.join(fixtures,p.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(fixture))return route.fulfill({path:fixture,contentType:'text/html'});
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/Products');await page.locator('#master-rows tr').first().waitFor();await page.locator('#add-record').click();await page.locator('#purchase-registration[open]').waitFor();
 for(const id of ['sku','barcode','purchasePrice','sellingPrice','minimumStockLevel','isSerialTracked'])assert.equal(await page.locator('#master-form #'+id).count(),0);
 assert.equal(await page.locator('#quick-category option[value="1"]').innerText(), "Clothing / Men's Wear / Shirts");
 await page.locator('#quick-product-name').fill('Cotton Shirt');await page.locator('#quick-category').selectOption('1');await page.locator('#quick-description').fill('Cotton clothing');await page.locator('#quick-has-variants').check();await page.locator('#quick-variant-name').fill('DEFAULT');
 await page.locator('#quick-warranty').fill('12');await page.locator('#quick-provider').selectOption('DEALER');
 await page.locator('#quick-tracking').selectOption('QUANTITY');await page.locator('#quick-sku').fill('SHIRT-DEFAULT');await page.locator('#purchase-registration-save').click();await page.getByText('Record saved.',{exact:true}).waitFor();assert.equal(new URL(page.url()).pathname,'/Products');await page.goto('http://stockflow.test/Products/Variants?productId=1');
 assert.equal(productBody.description,'Cotton clothing');assert.equal(productBody.trackingType,'QUANTITY');assert.equal(productBody.warrantyProvider,'DEALER');assert.equal(productBody.hasVariants,true);
 await page.locator('#variant-rows button').first().waitFor();assert.equal(await page.locator('.configuration-submenu a[href="/Products/Variants"]').count(),0);
 assert.equal(await page.locator('#variant-editor').isVisible(),false);await page.locator('#variant-new').click();
 assert.equal(await page.locator('#variant-tax').inputValue(),'5');assert.equal(await page.locator('#variant-warranty').inputValue(),'12');assert.equal(await page.locator('#variant-tracking').isDisabled(),true);
 await page.locator('#variant-name').fill('Black / M');await page.locator('#variant-sku').fill('SH-BLK-M');await page.locator('#variant-purchase').fill('400');await page.locator('#variant-sale').fill('699');await page.locator('#variant-location-minimum').fill('15');
 await page.locator('#variant-source').selectOption('MANUFACTURER');await page.locator('#variant-primary').fill('000987654');
 await page.locator('#attribute-add').click();await page.getByRole('textbox',{name:'Attribute name',exact:true}).fill('Size');await page.getByRole('textbox',{name:'Attribute value',exact:true}).fill('M');
 await page.locator('#attribute-add').click();await page.getByRole('textbox',{name:'Attribute name',exact:true}).last().fill('size');await page.getByRole('textbox',{name:'Attribute value',exact:true}).last().fill('L');
 await page.getByRole('button',{name:'Save Variant',exact:true}).click();await page.getByText('Use distinct attribute names with one value each.').waitFor();assert.equal(writes,0);await page.getByRole('button',{name:'Remove attribute'}).last().click();
 fail=true;await page.getByRole('button',{name:'Save Variant',exact:true}).click();await page.locator('#variant-editor-message').getByText('SKU already exists.').waitFor();assert.equal(await page.locator('#variant-editor').evaluate(d=>d.open),true);
 fail=false;await page.getByRole('button',{name:'Save Variant',exact:true}).click();await page.locator('#variant-editor').waitFor({state:'hidden'});await page.getByText('Size / model saved.',{exact:true}).waitFor();
 assert.equal(variantBody.primaryBarcode,'000987654');assert.equal(variantBody.attributes.Size,'M');assert.equal(variantBody.locationMinimumStockLevel,15);assert.equal(variantBody.applyLocationMinimumStock,true);
 await page.locator('#variant-search').fill('SH-BLK-M');assert.equal(await page.locator('#variant-rows tr').count(),1);await page.getByRole('button',{name:'Edit',exact:true}).click();assert.equal(await page.locator('#variant-primary').inputValue(),'000987654');await page.locator('#variant-cancel').click();
 await page.getByRole('button',{name:'Labels',exact:true}).click();await page.locator('#label-dialog[open]').waitFor();await page.locator('#label-count').fill('3');await page.locator('#label-refresh').click();await page.waitForFunction(()=>document.querySelectorAll('.stock-label svg').length===3);assert.equal(await page.locator('.stock-label svg').count(),3);await page.locator('[data-close-label]').first().click();
 await page.locator('#variant-search').fill('');await page.locator('#variant-product').selectOption('2');await page.locator('#variant-rows').getByText('BATTERY',{exact:true}).waitFor();assert.equal(await page.locator('#variant-new').isDisabled(),true);assert.equal(await page.getByRole('button',{name:'Labels',exact:true}).count(),0);
 await page.getByRole('button',{name:'Edit',exact:true}).click();assert.equal(await page.locator('#variant-tracking').inputValue(),'true');await page.keyboard.press('Escape');
 await page.getByRole('button',{name:'Unit label',exact:true}).click();await page.locator('#unit-serial').fill('SERIAL-25');await page.getByRole('button',{name:'Save & Preview',exact:true}).click();await page.locator('#label-dialog[open]').waitFor();assert.equal(await page.locator('.stock-label svg').count(),1);await page.locator('[data-close-label]').first().click();
 await page.locator('#variant-product').selectOption('1');await page.locator('#variant-rows').getByText('SH-BLK-M',{exact:true}).waitFor();
 for(const width of [1440,390]){await page.setViewportSize({width,height:1000});await page.locator('#variant-new').click();await page.locator('#variant-editor[open]').waitFor();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);const box=await page.locator('#variant-editor').boundingBox();assert.ok(box.x>=0&&box.x+box.width<=width);await page.keyboard.press('Escape');}
 await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:path.join(fixtures,'variant-list.png')});await page.locator('#variant-new').click();await page.screenshot({path:path.join(fixtures,'variant-popup.png')});
 assert.deepEqual(errors,[]);console.log('PASS: inline product save, legacy variant compatibility, variant defaults/list/search, duplicate attributes, failed save, barcode leading zeros, location minimum, edit, label copies, serialized units and responsive dialogs.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});



