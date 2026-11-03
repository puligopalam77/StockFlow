// Actual master-screen Razor fixtures and production CSS/scripts. APIs are mocked;
// wheel events (not scrollTo alone) verify that the user can scroll long pages.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:560},reducedMotion:'reduce'});page.setDefaultTimeout(10000);const errors=[];page.on('pageerror',error=>errors.push(error.message));
 const categories=[{categoryId:1,categoryCode:'CAT0001',categoryName:'Test category',categoryPath:'Test category',isActive:true,isEffectivelyActive:true,parentCategoryId:null,displayOrder:0}];
 const records={suppliers:{supplierId:1,supplierName:'Test supplier',supplierCode:'SUP0001',countryCode:'IN',paymentTermsDays:30,isActive:true},customers:{customerId:1,customerName:'Test customer',isActive:true},categories:categories[0],brands:{brandId:1,brandName:'Test brand',brandCode:'BRD0001',categoryIds:[1],aliases:[],isActive:true},products:{productId:1,productName:'Test product',categoryId:1,unitOfMeasure:'PIECE',hasVariants:false,isActive:true,trackingType:'QUANTITY',warrantyMonths:0,taxPercentage:0}};
 await page.route('**/*',route=>{const request=route.request(),p=new URL(request.url()).pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  const screen=p.match(/^\/(Products|Categories|Brands|Suppliers|Customers)\/(Create|Edit|View)$/i);
  if(screen){const module=screen[1][0].toUpperCase()+screen[1].slice(1).toLowerCase(),mode=screen[2][0].toUpperCase()+screen[2].slice(1).toLowerCase();return route.fulfill({path:path.join(root,'tests/bin/master-screen-fixtures',module+'-'+mode+'.html'),contentType:'text/html'});}
  if(p==='/api/suppliers/options')return json({countries:[{code:'IN',name:'India'}]});
  if(p==='/api/categories/schema')return json({available:true,categoryDetailsAvailable:true,attributes:[],categories:[]});
  if(p==='/api/categories/sku-prefix-suggestion')return json({available:false});
  if(p==='/api/brands/categories')return json(categories);if(p==='/api/brands/registration-options')return json({detailsAvailable:false});
  if(p==='/api/products/variants')return json([]);
  if(p==='/api/branches/context')return json({locationId:1,storeId:1,locations:[]});if(p==='/api/notifications')return json({unread:0,items:[]});
  const api=p.match(/^\/api\/(products|categories|brands|suppliers|customers)(?:\/(\d+))?$/);if(api)return json(api[2]?records[api[1]]:[records[api[1]]]);if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 async function wheelToBottom(){await page.mouse.move(12,(await page.viewportSize()).height-80);for(let i=0;i<8;i++)await page.mouse.wheel(0,1000);await page.waitForFunction(()=>scrollY>0);await page.waitForFunction(()=>innerHeight+scrollY>=document.documentElement.scrollHeight-3);}
 for(const width of [1366,390]){await page.setViewportSize({width,height:560});for(const module of ['Suppliers','Categories','Brands','Customers','Products'])for(const mode of ['Edit','Create','View']){
   await page.goto('http://localhost:5220/'+module+'/'+mode+(mode==='Create'?'':'?id=1'));await page.locator('#master-data[data-screen-ready=true]').waitFor({state:'attached'});
   await page.locator('.master-screen-form').first().waitFor();assert(!await page.locator('.master-screen-form').first().evaluate(el=>el.matches(':modal')),module+' '+mode+' stays a full-page form');
   assert.notEqual(await page.evaluate(()=>getComputedStyle(document.body).overflowY),'hidden',module+' '+mode+' permits page scrolling');
   // Supplier's actual fields must be reachable without synthetic content.
   if(module==='Suppliers'){assert(await page.evaluate(()=>document.documentElement.scrollHeight>innerHeight));await wheelToBottom();const footer=page.locator('#master-form .configuration-dialog-actions');assert(await footer.isVisible());const bounds=await footer.boundingBox();assert(bounds.y<560&&bounds.y+bounds.height>0,'Supplier footer reachable by wheel');}
   // Even initially short forms must grow when expanded sections/data are added.
   await page.evaluate(()=>{const longSection=document.createElement('section');longSection.id='scroll-test-extra';longSection.style.height='1000px';longSection.textContent='Expanded content fixture';document.querySelector('.master-screen-form form').append(longSection);const marker=document.createElement('p');marker.id='scroll-test-bottom';marker.textContent='End of long form';document.querySelector('.master-screen-form form').append(marker);});
   await wheelToBottom();assert((await page.locator('#scroll-test-bottom').boundingBox()).y<560,module+' '+mode+' bottom is reachable');
   console.log('PASS: '+module+' '+mode+' wheel scroll and bottom access at '+width+'px');
 }}
 // A real popup still locks the background and scrolls its own long content.
 await page.goto('http://localhost:5220/Suppliers/Edit?id=1');await page.locator('#master-data[data-screen-ready=true]').waitFor({state:'attached'});
 await page.evaluate(()=>{const dialog=document.getElementById('deactivate-dialog'),content=document.createElement('div');content.style.height='1200px';content.textContent='Long popup content';dialog.prepend(content);dialog.showModal();});
 assert.equal(await page.evaluate(()=>getComputedStyle(document.body).overflowY),'hidden');const dialog=page.locator('#deactivate-dialog'),box=await dialog.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.wheel(0,2000);await page.waitForFunction(()=>document.getElementById('deactivate-dialog').scrollTop>0);await page.evaluate(()=>document.getElementById('deactivate-dialog').close());assert.notEqual(await page.evaluate(()=>getComputedStyle(document.body).overflowY),'hidden');
 assert.deepEqual(errors,[]);console.log('PASS: native popup scrolls internally, locks background only while modal, and releases scrolling on close. No API writes.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1});
