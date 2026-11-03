const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 let categories=[{categoryId:1,categoryName:'Battery',categoryPath:'Battery',parentCategoryId:null,categoryLevel:1,isActive:true},{categoryId:2,categoryName:'Inverter',categoryPath:'Inverter',parentCategoryId:null,categoryLevel:1,isActive:true}],brands=[],saved,categoryWrites=0;
 await page.route('**/*',async route=>{
  const request=route.request(),p=new URL(request.url()).pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Brands')return route.fulfill({body:fs.readFileSync(path.join(root,'tests/bin/brand-registration-fixtures/Brands.html'),'utf8').replace('data-catalog-registration-enhancements="true"','data-catalog-registration-enhancements="false"'),contentType:'text/html'});
  if(p==='/api/brands/registration-options')return json({detailsAvailable:false});
  if(p==='/api/categories'||p==='/api/brands/categories'){
   if(request.method()==='POST'){assert(request.headers()['x-csrf-token']);categoryWrites++;const body=request.postDataJSON(),category={categoryId:3,categoryName:body.categoryName};categories.push(category);return json(category,201);}
   return json(categories);
  }
  if(p==='/api/brands'||p==='/api/brands/1'){
   if(request.method()==='GET')return json(p.endsWith('/1')?brands[0]:brands);
   saved=request.postDataJSON();brands=[{...saved,brandId:1,brandCode:'BRD0001',isActive:true}];return json(brands[0],201);
  }
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/Brands');await page.getByText('No matching brands.').waitFor();await page.locator('#add-record').click();await page.locator('#editor[open]').waitFor();
 await page.locator('#brandName').fill('Shared brand');await page.locator('#master-form [type=submit]').click();await page.getByText('Select at least one category for this brand.',{exact:true}).waitFor();assert(!saved);
 await page.locator('#brand-all-categories').uncheck();await page.locator('[data-brand-root="1"]').check();await page.locator('[data-brand-root="2"]').check();
 assert.equal(await page.locator('#brand-new-category').count(),0);assert(await page.locator('#brand-logo').isDisabled());
 await page.locator('#master-form [type=submit]').click();await page.getByText('Record saved.',{exact:true}).waitFor();assert.deepEqual(saved.categoryIds,[1,2]);
 await page.getByRole('button',{name:'Edit',exact:true}).click();await page.locator('#editor[open]').waitFor();assert.equal(await page.locator('#brand-category-choices input:checked').count(),2);
 assert.deepEqual(errors,[]);console.log('PASS: legacy brand category assignments, mandatory selection, pre-058 metadata gating, edit reload and mobile layout.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
