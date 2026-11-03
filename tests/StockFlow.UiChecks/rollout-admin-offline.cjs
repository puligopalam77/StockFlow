const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),output=path.join(root,'tests/bin/security-ui-fixtures'),catalog=JSON.parse(fs.readFileSync(path.join(output,'Modules.json'),'utf8'));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];let saved,templateSaved,permissionSaved,editing=false;
 const template=()=>catalog.map(p=>({...p,actions:{DASHBOARD:['VIEW'],SCAN:['VIEW','SCAN'],SALES:['VIEW','CREATE'],INVOICES:['VIEW','PRINT'],CUSTOMERS:['VIEW','CREATE'],INVENTORY:['VIEW'],REPORTS:['VIEW']}[p.moduleCode]||[]}));
 const target={userId:2,fullName:'Pilot cashier',username:'pilot.cashier',roleId:2,roleName:'Sales',isActive:true,securityVersion:1,mustChangePassword:false,permissions:template(),storeAccess:{storeIds:[1,2],warehouseLocationIds:[3],defaultStoreId:1}};
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{const req=route.request(),url=new URL(req.url()),p=url.pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(['/Admin/Users','/Admin/Roles'].includes(p))return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(output,p.split('/').at(-1)+'.html'),'utf8')});
  if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Counter'}],locationId:1,storeId:1});
  if(p==='/api/admin/modules')return json(catalog);
  if(p==='/api/admin/roles')return json([{roleId:1,roleName:'Admin',isActive:true},{roleId:2,roleName:'Sales',isActive:true},{roleId:3,roleName:'Storekeeper',isActive:true}]);
  if(p.endsWith('/permissions')&&p.startsWith('/api/admin/roles')){if(req.method()==='PUT'){templateSaved=req.postDataJSON();return route.fulfill({status:204});}return json(template());}
  if(p==='/api/admin/store-options')return json({stores:[{storeId:1,storeName:'Main store'},{storeId:2,storeName:'Second store'}],warehouses:[{locationId:3,locationName:'Central warehouse'}]});
  if(p==='/api/admin/users/2/permissions'){permissionSaved=req.postDataJSON();return route.fulfill({status:204});}
  if(p==='/api/admin/users/2')return json(target);
  if(p==='/api/admin/users'){if(req.method()==='POST'){saved=req.postDataJSON();return json({id:3});}return json([target]);}
  const file=path.join(root,'src/StockFlow.Web/wwwroot',p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/Admin/Users');await page.locator('#new-user').click();await page.waitForSelector('#primary-store option[value="1"]',{state:'attached'});
 assert.equal(await page.locator('#role').inputValue(),'','role must be chosen explicitly');assert.equal(await page.locator('.permission-group[open]').count(),0,'groups start collapsed');assert.equal(await page.locator('[data-action]:checked').count(),0,'no blanket grants');
 assert.equal(await page.locator('.permission-module').count(),catalog.length);assert.equal(await page.locator('[data-module^="TRANSFER_"]').count(),0,'retired transfer modules absent');
 await page.locator('#role').selectOption('2');await page.waitForFunction(()=>document.querySelector('#selected-role-help').textContent.includes('Template loaded'));await page.locator('#primary-store').selectOption('1');
 await page.getByLabel('Find module permissions').fill('send goods');assert.equal(await page.locator('[data-module="STOCK_TRANSFER"]').count(),1);
 await page.getByLabel('STOCK_TRANSFER: DISPATCH',{exact:true}).check();assert(await page.getByLabel('STOCK_TRANSFER: VIEW',{exact:true}).isChecked());assert(!(await page.getByLabel('STOCK_TRANSFER: RECEIVE',{exact:true}).isChecked()),'dispatch never grants receipt');
 assert.equal(await page.locator('[data-module="STOCK_TRANSFER"] [data-action="DISPATCH"]').locator('..').evaluate(n=>n.classList.contains('sensitive')),true);
 await page.getByLabel('STOCK_TRANSFER: VIEW',{exact:true}).uncheck();assert(!(await page.getByLabel('STOCK_TRANSFER: DISPATCH',{exact:true}).isChecked()));
 await page.getByLabel('STOCK_TRANSFER: DISPATCH',{exact:true}).check();await page.getByLabel('Find module permissions').fill('correct stock');assert.equal(await page.locator('[data-module="STOCK_ADJUSTMENT"]').count(),1);await page.getByLabel('STOCK_ADJUSTMENT: ADJUST',{exact:true}).check();
 await page.getByLabel('Find module permissions').fill('inventory');assert.equal(await page.locator('[data-module="INVENTORY"] [data-action="APPROVE"]').count(),0,'inventory adjustment is a separate module');
 await page.getByLabel('Find module permissions').fill('');await page.getByLabel('Assigned only',{exact:true}).check();assert((await page.locator('.permission-module').count())<catalog.length);await page.getByLabel('Assigned only',{exact:true}).uncheck();
 await page.locator('.permission-workflows summary').click();assert((await page.locator('.permission-workflows').textContent()).includes('Bill with scanner: Ready'));
 for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});await page.waitForFunction(()=>innerWidth>991||(parseFloat(getComputedStyle(document.querySelector('.sf-workspace')).marginLeft)===0&&document.querySelector('.sf-sidebar').getBoundingClientRect().right<=0));assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:path.join(output,`module-assignment-${width}.png`),fullPage:true});}
 await page.locator('#full-name').fill('Pilot colleague');await page.locator('#username').fill('pilot.colleague');await page.locator('#password').fill('Example pilot passphrase 123');await page.locator('#confirm-password').fill('Example pilot passphrase 123');await page.locator('#save-user').click();await page.waitForFunction(()=>document.querySelector('#user-editor').hidden);
 assert.equal(saved.permissions.length,catalog.length);assert.deepEqual(saved.storeAccess,{storeIds:[1],warehouseLocationIds:[],defaultStoreId:1});assert(saved.permissions.find(p=>p.moduleCode==='STOCK_TRANSFER').actions.includes('DISPATCH'));assert(saved.permissions.find(p=>p.moduleCode==='STOCK_ADJUSTMENT').actions.includes('ADJUST'));
 await page.getByRole('button',{name:'View / edit',exact:true}).first().click();await page.waitForFunction(()=>!document.querySelector('#user-editor').hidden&&!document.querySelector('#admin-users').inert);assert.equal(await page.locator('#primary-store').inputValue(),'1');assert((await page.locator('#store-assignment-help').textContent()).includes('2 stores and 1 warehouses'));assert.equal(await page.locator('#initial-password').isVisible(),false);
 await page.locator('#save-user-permissions').click();await page.waitForFunction(()=>document.querySelector('#user-editor').hidden);assert.equal(permissionSaved.permissions.length,catalog.length);assert(!permissionSaved.storeAccess,'permissions-only save does not replace assignments');
 await page.goto('http://stockflow.test/Admin/Roles');await page.getByLabel('Find module permissions').fill('reports');await page.getByLabel('REPORTS: VIEW',{exact:true}).uncheck();await page.locator('#save-role').click();await page.waitForFunction(()=>document.querySelector('#admin-message').textContent.includes('Template saved'));assert.deepEqual(templateSaved.find(p=>p.moduleCode==='REPORTS').actions,[]);assert.deepEqual(errors,[]);
 console.log('PASS full latest catalog, collapsed/searchable groups, role choice, workflow checks, serial/transfer actions, assigned shop, preserved advanced access, complete payload and mobile layouts');
 }finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
