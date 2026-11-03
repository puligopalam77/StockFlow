const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/category-attributes-fixtures');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({reducedMotion:'reduce',viewport:{width:1440,height:1000}});page.setDefaultTimeout(9000);
  let profile='CLOTHING',schema,registered=null,fail=false,writes=0;
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const chooseCategory=async value=>{await page.locator('#quick-category-toggle').click();await page.locator('#quick-category').selectOption(value);};
  const chooseVariantOptions=async (name,values)=>{const select=page.locator('select[aria-label="'+name+' options"]');await select.evaluate(e=>e.closest('details').open=true);await select.selectOption(values);assert(await select.evaluate(e=>!e.closest('details').open),name+' dropdown closes after selection');};
  const setup=()=>{schema={available:true,version:1,lockedCategoryIds:[],attributes:[
   {code:'size',label:'Size',type:'SELECT',options:profile==='FOOTWEAR'?['6','7','8']:['S','M','L']},
   {code:'color',label:'Color',type:'SELECT',options:['Black','Blue']},
   {code:'material',label:'Material',type:'TEXT',options:[]},
   {code:'weight',label:'Weight',type:'NUMBER',options:[],minimum:0,maximum:10}
  ],categories:[{categoryId:1,fields:[{code:'size',required:true,variantAxis:true},{code:'color',required:true,variantAxis:true},{code:'material',required:true,variantAxis:false},{code:'weight',required:false,variantAxis:false}]}]};registered=null;};
  await page.route('**/*',async route=>{
   const req=route.request(),p=new URL(req.url()).pathname;const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/Products'||p==='/Categories/Attributes')return route.fulfill({path:path.join(fixtures,(p==='/Products'?'':'Settings-')+profile+'.html'),contentType:'text/html'});
   if(p==='/api/categories/schema'){
    if(req.method()==='GET')return json(schema);
    const doc=req.postDataJSON();assert(req.headers()['x-csrf-token']);schema={...schema,...doc,version:schema.version+1};return json(schema);
   }
   if(p==='/api/categories')return json([{categoryId:1,categoryName:'Configured items',categoryPath:'Configured items'},{categoryId:2,categoryName:'Other items',categoryPath:'Other items'}]);
   if(p==='/api/brands')return json([]);
   if(p==='/api/products/variants')return json(registered?.variants.map((v,i)=>({...v,variantId:i+1,productId:1,isActive:true}))||[]);
   if(p==='/api/products'||p==='/api/products/1'){
    if(req.method()==='GET')return json(p.endsWith('/1')?registered:(registered?[registered]:[]));
    writes++;if(fail)return json({message:'SKU already exists.'},400);
    registered={...req.postDataJSON(),productId:1,isActive:true};schema.lockedCategoryIds=[1];return json(registered);
   }
   if(p==='/api/notifications')return json({unread:0,items:[]});
   if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[]});
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
   return route.fulfill({status:404,body:''});
  });
  for(profile of ['CLOTHING','FOOTWEAR','BIKE_ACCESSORIES']){
   setup();await page.goto('http://stockflow.test/Products');await page.locator('#add-record').click();await page.waitForFunction(()=>!document.querySelector('#purchase-registration-fields').disabled);
   await page.locator('#quick-product-name').fill('Configured item');await chooseCategory('1');await page.locator('#quick-sku').fill('ITEM');
   await page.locator('#registration-tab-variants').click();assert.equal(await page.locator('#quick-specifications select').count(),2);assert(await page.getByLabel('Material *',{exact:true}).isVisible());
   await page.getByLabel('Material *',{exact:true}).fill('Cotton');await page.getByLabel('Weight',{exact:true}).fill('2');
   await page.locator('#registration-tab-basic').click();await chooseCategory('2');await page.locator('#registration-tab-media').click();assert(await page.getByLabel('Specification template',{exact:true}).isVisible());
   await page.locator('#registration-tab-basic').click();await chooseCategory('1');await page.locator('#registration-tab-variants').click();assert.equal(await page.getByLabel('Material *',{exact:true}).inputValue(),'Cotton','category switching retains values');
   await page.locator('#quick-has-variants').check();assert(await page.locator('#quick-variant-fields').isHidden());
   const sizes=profile==='FOOTWEAR'?['6','7']:['S','M'];
   await chooseVariantOptions('Size',sizes);await chooseVariantOptions('Color',['Black','Blue']);
   await page.locator('#registration-tab-pricing').click();await page.locator('#quick-purchase').fill('100');await page.locator('#quick-sale').fill('200');await page.locator('#registration-tab-variants').click();await page.getByRole('button',{name:'Generate Variants',exact:true}).click();
   assert.equal(await page.locator('#quick-combinations tbody tr').count(),4);
   await page.locator('#quick-combinations tbody input[type=checkbox]').last().uncheck();
   const firstSku=page.locator('#quick-combinations tbody tr').first().locator('input[aria-label$=" SKU"]'),secondSku=page.locator('#quick-combinations tbody tr').nth(1).locator('input[aria-label$=" SKU"]');
   await secondSku.fill(await firstSku.inputValue());await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration-message').waitFor({state:'visible'});
   assert((await page.locator('#purchase-registration-message').innerText()).includes('unique SKU'));assert.equal(await page.locator('#quick-combinations tbody tr').count(),4,'preview survives validation error');
   await page.locator('#registration-tab-variants').click();await secondSku.fill('ITEM-02');await page.getByRole('button',{name:'Edit variant',exact:false}).first().click();await page.locator('#quick-combinations tbody tr').first().locator('input[aria-label$=" selling price"]').fill('250');
   fail=true;await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click();await page.getByText('SKU already exists.',{exact:true}).waitFor();assert.equal(await page.locator('#quick-combinations tbody input[type=checkbox]:checked').count(),3,'failed save retains selected subset');fail=false;
   for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'page overflow '+width);assert(await page.locator('#purchase-registration').evaluate(e=>e.scrollWidth<=e.clientWidth),'dialog overflow '+width);}
   await page.setViewportSize({width:1440,height:1000});await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration').waitFor({state:'hidden'});
   assert.equal(registered.variants.length,3);assert.equal(registered.variants[0].sellingPrice,250);assert(registered.variants.every(v=>v.attributes.material==='Cotton'&&v.attributes.size&&v.attributes.color));assert.equal(registered.trackingType,'QUANTITY');
   await page.getByRole('button',{name:'Edit',exact:true}).click();await page.locator('#editor[open]').waitFor();assert.equal(await page.locator('#product-configurations fieldset').count(),3);assert(await page.locator('#categoryId').isDisabled());assert(await page.locator('#product-configurations [data-attribute-code="size"]').first().isDisabled());await page.locator('#editor').evaluate(e=>e.close());
   await page.goto('http://stockflow.test/Categories/Attributes');await page.waitForFunction(()=>!document.querySelector('#attribute-fields').disabled);
   assert(await page.locator('#attribute-definitions fieldset').first().evaluate(e=>e.disabled));assert((await page.locator('#attribute-categories').innerText()).includes('locked'));
   console.log('PASS: '+profile+' typed registration, category retention, selected combinations, prices, failed-save retention and saved identity guards.');
  }
  setup();schema={available:true,version:0,attributes:[],categories:[],lockedCategoryIds:[]};profile='FOOTWEAR';
  await page.goto('http://stockflow.test/Categories/Attributes');await page.waitForFunction(()=>!document.querySelector('#attribute-fields').disabled);await page.locator('#attribute-suggestions').click();assert.equal(await page.locator('#attribute-definitions fieldset').count(),3);
  await page.locator('#attribute-categories details').first().locator('summary').click();const map=page.locator('#attribute-categories fieldset').first();await map.getByLabel('Footwear size (UK)',{exact:true}).check();await map.getByLabel('Variant axis',{exact:true}).first().check();
  await page.locator('#attribute-save').click();await page.waitForFunction(()=>document.querySelector('#attribute-status').textContent.includes('saved'));assert.equal(schema.categories[0].fields[0].variantAxis,true);assert.equal(schema.categories[0].fields[0].required,true);
  for(const width of [1440,768,390,320]){await page.setViewportSize({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'settings overflow '+width);}
  setup();schema.attributes[0].options=Array.from({length:50},(_,i)=>'Size '+(i+1));schema.attributes[1].options=['Black','Blue','Red','White','Green'];
  await page.setViewportSize({width:1440,height:1000});await page.goto('http://stockflow.test/Products');await page.locator('#add-record').click();await page.waitForFunction(()=>!document.querySelector('#purchase-registration-fields').disabled);
  await page.locator('#quick-product-name').fill('Boundary product');await chooseCategory('1');await page.locator('#quick-sku').fill('LIMIT');await page.locator('#registration-tab-variants').click();await page.getByLabel('Material *',{exact:true}).fill('Cotton');await page.locator('#quick-has-variants').check();
  await chooseVariantOptions('Size',schema.attributes[0].options);await chooseVariantOptions('Color',schema.attributes[1].options);await page.getByRole('button',{name:'Generate Variants',exact:true}).click();assert((await page.locator('#quick-combinations').innerText()).includes('up to 200'));
  await chooseVariantOptions('Size',schema.attributes[0].options.slice(0,31));await chooseVariantOptions('Color',['Black']);await page.getByRole('button',{name:'Generate Variants',exact:true}).click();
  assert.equal(await page.locator('#quick-combinations tbody input[type=checkbox]:checked').count(),0);await page.locator('#quick-combinations tbody input[type=checkbox]').evaluateAll(inputs=>inputs.forEach(input=>{input.checked=true;input.dispatchEvent(new Event('change'));}));
  await page.locator('#registration-tab-media').click();await page.locator('#purchase-registration-save').click();await page.locator('#purchase-registration-message').waitFor({state:'visible'});assert((await page.locator('#purchase-registration-message').innerText()).includes('1 and 30'));
  assert.deepEqual(errors,[]);assert.equal(writes,6);console.log('PASS: reusable setup, profile suggestions, mappings, required axes, save, responsive layouts and preview/selection limits. Mock APIs only.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
