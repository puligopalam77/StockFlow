// Real Razor fixtures and browser interactions; mocked APIs, no database connection.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixtures=path.join(root,'tests/bin/category-hierarchy-fixtures');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.setDefaultTimeout(8000);page.on('pageerror',error=>errors.push(error.message));
 const categories=[{categoryId:1,categoryCode:'CAT0001',categoryName:'Bike Parts & Accessories',parentCategoryId:null,categoryPath:'Bike Parts & Accessories',categoryLevel:1,isLeaf:false,productCount:0,subtreeProductCount:1,isActive:true},
 {categoryId:2,categoryCode:'CAT0002',categoryName:'Helmets',parentCategoryId:1,categoryPath:'Bike Parts & Accessories / Helmets',categoryLevel:2,isLeaf:false,productCount:0,subtreeProductCount:1,isActive:true},
 {categoryId:3,categoryCode:'CAT0003',categoryName:'Full Face Helmets',parentCategoryId:2,categoryPath:'Bike Parts & Accessories / Helmets / Full Face Helmets',categoryLevel:3,isLeaf:true,productCount:1,subtreeProductCount:1,isActive:true}];
 const schema=JSON.parse(fs.readFileSync(path.join(fixtures,'schema.json'),'utf8')),ids=new Map(schema.categories.map((row,index)=>[row.categoryId,index+1]));
 for(const row of [...schema.categories,...schema.effectiveCategories]){row.categoryId=ids.get(row.categoryId);for(const field of row.fields)if(field.definedOnCategoryId)field.definedOnCategoryId=ids.get(field.definedOnCategoryId);}
 schema.lockedCategoryIds=[];let savedCategory,savedSchema;
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname,json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  const files={'/Categories':'Categories.html','/Categories/Attributes':'Attributes.html','/Products':'Products.html'};
  if(files[p])return route.fulfill({contentType:'text/html',path:path.join(fixtures,files[p])});
  if(p==='/api/categories/schema'){if(req.method()==='PUT'){savedSchema=req.postDataJSON();return json({...schema,...savedSchema});}return json(schema);}
  if(p==='/api/categories'){
   if(req.method()==='POST'){savedCategory=req.postDataJSON();const row={...savedCategory,categoryId:4,categoryCode:'CAT0004',categoryLevel:3,categoryPath:categories[1].categoryPath+' / '+savedCategory.categoryName,productCount:0,subtreeProductCount:0,isActive:true,isLeaf:true};categories.push(row);return json(row);}
   const term=(url.searchParams.get('term')||'').toLowerCase();return json(categories.filter(row=>!term||row.categoryPath.toLowerCase().includes(term)));
  }
  if(/^\/api\/categories\/\d+$/.test(p))return json(categories.find(row=>row.categoryId===Number(p.split('/').pop())));
  if(p==='/api/products'||p==='/api/brands')return json([]);
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
  return route.fulfill({status:404,body:''});
 });

 async function chooseParent(id){const name=await page.locator('#categoryName').inputValue(),description=await page.locator('#description').inputValue();if(await page.evaluate(()=>CategoryRegistration.hasChanges()))page.once('dialog',dialog=>dialog.accept());await page.evaluate(id=>CategoryCascades.createUnder(id),id);if(name)await page.locator('#categoryName').fill(name);if(description)await page.locator('#description').fill(description);}
 await page.goto('http://stockflow.test/Categories');await page.locator('#master-rows tr[data-record-id="2"]').waitFor();
 assert.equal(await page.locator('#category-tree').count(),0);assert.deepEqual((await page.locator('#master-head th').allTextContents()).slice(0,2),['Category / Subcategory','Category Code']);
 await page.locator('#add-record').click();await chooseParent(2);await page.locator('[name=categoryName]').fill('Modular Helmets');assert.equal(await page.locator('#category-main-select').inputValue(),'1');assert.equal(await page.locator('#category-sub-select').inputValue(),'2');assert.equal(await page.locator('#category-path-preview').textContent(),'Bike Parts & Accessories / Helmets / Modular Helmets');
 await page.locator('#category-save').click();await page.locator('#editor').waitFor({state:'hidden'});assert.equal(savedCategory.parentCategoryId,2);assert(!('categoryCode' in savedCategory));
 await page.locator('#category-expand-all').click();const full=page.locator('#master-rows tr[data-record-id="3"]');await full.getByRole('button',{name:'Edit',exact:true}).click();
 await page.locator('#editor[open]').waitFor();assert(await page.locator('[name=parentCategoryId]').isDisabled());assert(await page.locator('[name=parentCategoryId] option[value="3"]').isDisabled());await page.locator('#close-editor').click();
 await page.goto('http://stockflow.test/Categories/Attributes');await page.locator('#attribute-categories details').first().waitFor();
 const child=page.locator('#attribute-categories details').filter({has:page.locator('summary',{hasText:'Full Face Helmets'})});await child.locator('summary').click();
 assert((await child.textContent()).includes('Inherited from Bike Parts & Accessories / Helmets'));
 assert(await child.getByRole('checkbox',{name:'Size',exact:true}).isChecked());assert(await child.getByRole('checkbox',{name:'Size',exact:true}).isDisabled());
 await page.locator('#attribute-save').click();await page.waitForFunction(()=>document.querySelector('#attribute-status').textContent.includes('saved'));
 assert.equal(savedSchema.categories.find(row=>row.categoryId===3).fields.length,1,'saving does not copy inherited fields into child assignments');
 await page.goto('http://stockflow.test/Products');await page.locator('#add-record').click();await page.locator('#quick-category-toggle').click();await page.locator('#quick-category').selectOption('3');
 await page.waitForFunction(()=>window.CatalogAttributes?.fields(3).length===4);assert.deepEqual((await page.evaluate(()=>CatalogAttributes.fields(3).map(field=>field.code))).sort(),['color','part_number','size','visor']);
 assert((await page.locator('#quick-category-toggle').textContent()).includes('Bike Parts & Accessories / Helmets / Full Face Helmets'));
 await page.locator('#purchase-registration-close').click();
 await page.goto('http://stockflow.test/Categories');await page.locator('#master-rows tr[data-record-id="2"]').waitFor();
 for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});await page.screenshot({path:path.join(fixtures,'hierarchy-'+width+'.png'),fullPage:true});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no overflow at '+width);}
 assert.deepEqual(errors,[]);console.log('PASS: category row list, cascading parents, live path/level, saved parent identity, protected moves, inherited field sources, direct-only saves, registration fields and responsive widths. Mock APIs only.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
