const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:1000},reducedMotion:'reduce'});page.setDefaultTimeout(10000);
 const errors=[],requests=[],writes=[],reservations=require('./sequential-sku-mock.cjs')();let fail=false;
 page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/*',route=>{const request=route.request(),p=new URL(request.url()).pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Products')return route.fulfill({contentType:'text/html',body:'<h1>Products</h1>'});if(p==='/Products/Create')return route.fulfill({path:path.join(root,'tests/bin/product-review-fixtures/Products.html'),contentType:'text/html'});
  if(p==='/api/products/identifier-reservations'){const body=request.postDataJSON();requests.push(body);if(fail)return route.fulfill({status:409,contentType:'application/json',body:JSON.stringify({message:'The active shop changed. Return to the original shop.'})});return json(reservations.reserve(body));}
  if(p==='/api/categories')return json([{categoryId:1,categoryName:'Brake Parts',skuPrefix:'BRK',isActive:true,isEffectivelyActive:true,parentCategoryId:null}]);
  if(p==='/api/categories/schema')return json({available:true,attributes:[],categories:[]});
  if(p==='/api/products'&&request.method()==='POST'){const body=request.postDataJSON();writes.push(body);return json({...body,productId:99,isActive:true});}
  if(p==='/api/products/variants')return json([]);
  if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[]});if(p==='/api/notifications')return json({unread:0,items:[]});if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 await page.goto('http://localhost:5220/Products/Create');await page.locator('#purchase-registration-fields:not([disabled])').waitFor();
 assert.equal(requests.length,0);assert.equal(await page.locator('#quick-sku').inputValue(),'');
 await page.locator('#registration-tab-variants').click();assert.equal(requests.length,0);assert(await page.getByText('2 required fields need attention.',{exact:true}).isVisible());
 await page.locator('#quick-product-name').fill('Brake pad set');await page.locator('#quick-category-level-0-toggle').click();await page.locator('#quick-category-level-0-panel').getByRole('button',{name:'Brake Parts',exact:true}).click();
 fail=true;await page.locator('#registration-next').click();await page.getByText('The active shop changed. Return to the original shop.',{exact:true}).waitFor();assert.equal(await page.locator('#quick-product-name').inputValue(),'Brake pad set');fail=false;
 await page.locator('#registration-next').click();await page.locator('#registration-panel-variants:not([hidden])').waitFor();
 const code=await page.locator('#quick-sku').inputValue();assert.equal(code,'TST-0001');assert.equal(await page.locator('#quick-default-sku').inputValue(),code);assert(requests.every(r=>r.businessProfileCode==='BIKE_ACCESSORIES'));
 const count=requests.length;await page.locator('#registration-back').click();await page.locator('#registration-next').click();await page.locator('#registration-panel-variants:not([hidden])').waitFor();assert.equal(requests.length,count);assert.equal(await page.locator('#quick-sku').inputValue(),code);
 await page.locator('#quick-generate-sku').click();await page.waitForFunction(()=>!document.getElementById('quick-generate-sku').disabled);assert.equal(await page.locator('#quick-sku').inputValue(),code);
 await page.locator('#quick-has-variants').check();await page.getByLabel('Select variant attribute',{exact:true}).click();await page.locator('.variant-attribute-results').getByRole('button',{name:'Size',exact:true}).click();await page.getByRole('button',{name:'+ Add Attribute',exact:true}).click();
 for(const value of ['M','L']){await page.getByLabel('New size option',{exact:true}).fill(value);await page.getByLabel('New size option',{exact:true}).press('Enter');}
 await page.getByRole('button',{name:'Generate Variants',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.variant-preview-row').length===2);
 assert.deepEqual(await page.locator('.variant-identifier').evaluateAll(list=>list.map(input=>input.value)),[code+'-01',code+'-02']);
 await page.getByLabel('M SKU',{exact:true}).fill('MANUAL-BRAKE-M');await page.getByRole('button',{name:'Generate Variants',exact:true}).click();assert.equal(await page.getByLabel('M SKU',{exact:true}).inputValue(),'MANUAL-BRAKE-M');
 await page.getByLabel('New size option',{exact:true}).fill('XL');await page.getByLabel('New size option',{exact:true}).press('Enter');await page.getByRole('button',{name:'Generate Variants',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.variant-preview-row').length===3);assert.equal(await page.getByLabel('XL SKU',{exact:true}).inputValue(),code+'-03');
 await page.locator('#registration-next').click();await page.locator('#quick-purchase').fill('100');await page.locator('#quick-sale').fill('200');await page.locator('#registration-next').click();await page.locator('#quick-final-review>summary').click();await page.getByText('MANUAL-BRAKE-M, '+code+'-02, '+code+'-03',{exact:true}).waitFor();
 for(const width of [768,390,320]){await page.setViewportSize({width,height:850});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));}
 await page.locator('#purchase-registration-save').click();await page.waitForURL('**/Products');assert.equal(await page.getByText('Record saved.',{exact:true}).count(),0);assert.equal(writes.length,1);assert.equal(writes[0].parentProductCode,code);assert.deepEqual(writes[0].variants.map(v=>v.sku),['MANUAL-BRAKE-M',code+'-02',code+'-03']);assert.deepEqual(errors,[]);
 console.log('PASS: browser uses reserved backend identifiers, retries retain drafts, navigation keeps codes, simple SKU matches parent, numeric variant suffixes/manual overrides persist, four-step save and mobile layouts work. API mocked.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1});
