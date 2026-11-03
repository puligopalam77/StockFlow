// Real Razor fixtures and browser scripts; all APIs are mocked, no application DB access.
const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'../..'),web=path.join(root,'src/StockFlow.Web/wwwroot');
const {chromium}=require(path.join(__dirname,'bin/Debug/net10.0/.playwright/package'));
const fixtures=path.join(root,'tests/bin/variant-ui-fixtures'),base='http://localhost:51997';
const locations=[{locationId:1,storeId:1,storeName:'Main',locationName:'Stock',locationType:'SALES_FLOOR'},{locationId:2,storeId:2,storeName:'Branch',locationName:'Stock',locationType:'SALES_FLOOR'}];
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));let selected=1,created,store,assignment;
 let transfer={transferId:1,transferNumber:'TRF-1',fromLocationId:1,toLocationId:2,fromLocation:'Main',toLocation:'Branch',status:'DRAFT',items:[{transferItemId:1,variantId:1,productName:'Shirt',requestedQuantity:3,dispatchedQuantity:0,receivedQuantity:0,damagedQuantity:0,lostQuantity:0,isSerialTracked:false,units:[]}],operations:[]};
 await page.route('**/*',async route=>{
  const r=route.request(),p=new URL(r.url()).pathname,json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  if(r.method()!=='GET')assert.equal(r.headers()['x-stockflow-location'],String(selected));
  if(p==='/api/branches/context'){if(r.method()==='POST'){selected=r.postDataJSON().locationId;return route.fulfill({status:204});}return json({isOwner:true,canReviewStores:true,locationId:selected,storeId:selected,locations});}
  if(p==='/api/branches/stores'){if(r.method()==='POST'){store=r.postDataJSON();return json({storeId:3});}return json([{storeId:1,storeName:'Main'},{storeId:2,storeName:'Branch'}]);}
  if(p==='/api/branches/users/2'){if(r.method()==='PUT'){assignment=r.postDataJSON();return route.fulfill({status:204});}return json({storeIds:[1],warehouseLocationIds:[],defaultStoreId:1});}
  if(p==='/api/transfers/locations')return json(locations);
  if(p==='/api/transfers/variants')return json([{variantId:1,productName:'Shirt',variantName:'M',sku:'SHIRT-M',isSerialTracked:false}]);
  if(p==='/api/transfers'){if(r.method()==='POST'){created=r.postDataJSON();return json({transferId:1});}return json([transfer]);}
  if(p==='/api/transfers/1')return json(transfer);
  if(p.startsWith('/api/transfers/1/')){const action=p.split('/').pop(),body=r.postDataJSON();assert.ok(body.requestId);transfer.status={request:'REQUESTED',approve:'APPROVED',dispatch:'DISPATCHED',receive:'RECEIVED'}[action];if(action==='dispatch')transfer.items[0].dispatchedQuantity=3;if(action==='receive')transfer.items[0].receivedQuantity=3;return json({transferId:1});}
  if(p==='/api/branches/comparison')return json([{productName:'Shirt',variantName:'M',storeName:'Main',locationName:'Stock',locationType:'SALES_FLOOR',currentStock:7}]);
  if(p==='/api/admin/users')return json([{userId:2,fullName:'Employee',username:'employee'}]);
  if(p.startsWith('/api/'))return json([]);
  const fixture=path.join(fixtures,p.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(fixture))return route.fulfill({contentType:'text/html',body:fs.readFileSync(fixture,'utf8').replace('name="stockflow-location" content="1"',`name="stockflow-location" content="${selected}"`).replace('stockflow.transfer.1.1.1',`stockflow.transfer.1.1.${selected}`)});
  const file=path.resolve(web,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(web+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file,contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':undefined});
  return route.fulfill({status:404,body:''});
 });
 await page.goto(base+'/Admin/Stores');await page.locator('#location-store option[value="2"]').waitFor({state:'attached'});
 await page.locator('#store-form [name=storeCode]').fill('THIRD');await page.locator('#store-form [name=storeName]').fill('Third');await page.locator('#store-form button').click();await page.waitForFunction(()=>document.getElementById('branch-message').textContent.includes('Store created'));assert.equal(store.storeCode,'THIRD');
 await page.locator('#find-access-users').click();await page.locator('#access-user').selectOption('2');await page.locator('#load-access').click();await page.locator('#access-form:visible').waitFor();await page.locator('#access-stores input[value="2"]').check();await page.locator('#default-store').selectOption('2');await page.locator('#access-form button').click();await page.waitForFunction(()=>document.getElementById('branch-message').textContent.includes('Access saved'));assert.deepEqual(assignment.storeIds,[1,2]);
 await page.goto(base+'/Transfers');await page.locator('summary').filter({hasText:'Create transfer'}).click();await page.locator('#transfer-find').click();await page.locator('#transfer-variant option[value="1"]').waitFor({state:'attached'});await page.locator('#transfer-variant').selectOption('1');await page.locator('#transfer-to').selectOption('2');await page.locator('#transfer-quantity').fill('3');await page.locator('#transfer-add').click();await page.locator('#transfer-create .btn-primary').click();await page.locator('#transfer-detail:visible').waitFor();assert.equal(created.items[0].quantity,3);
 for(const [action,state] of [['request','Requested'],['approve','Approved'],['dispatch','Dispatched']]){await page.locator(`[data-action="${action}"]`).click();await page.waitForFunction(state=>document.getElementById('transfer-title').textContent.endsWith(state),state);}
 await page.locator('#business-location').selectOption('2:2');await page.waitForFunction(()=>document.querySelector('meta[name="stockflow-location"]').content==='2');await page.goto(base+'/Transfers?id=1');await page.locator('[data-quantity]').fill('3');await page.locator('[data-action="receive"]').click();await page.waitForFunction(()=>document.getElementById('transfer-title').textContent.endsWith('Received'));
 await page.goto(base+'/Reports/Stores');await page.locator('#comparison-rows tr').waitFor();assert.ok((await page.locator('#comparison-rows').textContent()).includes('Shirt'));
 await page.locator('#business-location').selectOption('1:1');await page.waitForFunction(()=>document.querySelector('meta[name="stockflow-location"]').content==='1');assert.equal(selected,1);
 assert.deepEqual(errors,[]);console.log('PASS store creation, assignments, context switching, transfer lifecycle and comparison UI.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
