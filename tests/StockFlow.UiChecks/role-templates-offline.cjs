const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {selectValue}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/role-management-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),out=path.join(root,'.artifacts/role-templates');fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({hasTouch:true}),errors=[],roles=[{roleId:1,roleName:'Admin',isProtected:true,isActive:true,assignedUsers:1,version:'a'}],writes=[];
 page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(10000);
 const catalog=JSON.parse(fs.readFileSync(path.join(fixtures,'Modules.json'),'utf8')),templates=JSON.parse(fs.readFileSync(path.join(fixtures,'Templates.json'),'utf8'));let stale=false,missing=false;
 await page.route('**/*',async r=>{const req=r.request(),u=new URL(req.url()),p=u.pathname,json=(data,status=200)=>r.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
  if(p.startsWith('/Admin/Roles')){const file=p.includes('/Templates')?'Templates':p.includes('/Create')?'Create':'List';return r.fulfill({path:path.join(fixtures,file+'.html'),contentType:'text/html'});}
  if(p==='/Admin/Users')return r.fulfill({path:path.join(fixtures,'Users.html'),contentType:'text/html'});
  if(p==='/api/admin/roles/templates')return missing?json({message:'Role Templates setup is pending. Manually apply database/migrations/097_role_templates.sql after 096, then reload.'},400):json(templates.map(t=>t.template));
  if(p==='/api/admin/roles/templates/save'){
   if(stale)return json({message:'Template changed. Reload before saving.'},409);
   const body=req.postDataJSON();assert(req.headers()['x-csrf-token']);writes.push(body);const key=body.templateKey||'custom-evening',prior=templates.find(t=>t.template.templateKey===key);
   const saved={template:{templateKey:key,templateName:body.templateName,description:body.description,isActive:body.isActive,isSystem:false,version:prior?'v2':'v1'},permissions:body.permissions,omittedActions:0};if(prior)templates.splice(templates.indexOf(prior),1,saved);else templates.push(saved);return json({templateKey:key});
  }
  if(p.startsWith('/api/admin/roles/templates/'))return json(templates.find(t=>t.template.templateKey===decodeURIComponent(p.split('/').at(-1))));
  if(p==='/api/admin/roles/directory'||p==='/api/admin/roles')return json(roles);
  if(p==='/api/admin/roles/save'){const body=req.postDataJSON();writes.push(body);roles.push({roleId:roles.length+1,roleName:body.roleName,isActive:body.isActive,isProtected:false,assignedUsers:0,version:'role-v1'});return json({roleId:roles.length});}
  if(p==='/api/admin/modules')return json(catalog.map(m=>({...m,actions:[]})));
  if(p==='/api/admin/users/registration-options')return json({businessName:'Test business',canManageStaff:true,canCreateShopAdmin:true,canUseAdvancedAccess:true,roles:roles.filter(r=>r.isActive&&!r.isProtected).map(r=>({...r,staffAccountType:'EMPLOYEE'})),stores:[{storeId:1,storeName:'Main store'}],warehouses:[]});
  if(p==='/api/admin/users')return json([]);
  if(p==='/api/notifications')return json({unread:0,items:[]});
  if(p==='/api/admin/store-options')return json({stores:[{storeId:1,storeName:'Main store'}],warehouses:[]});
  if(p==='/api/branches/context')return json({storeId:1,locationId:1,canReviewStores:true,locations:[{storeId:1,locationId:1,storeName:'Main store',locationName:'Main stock'}]});
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?r.fulfill({path:file}):r.fulfill({status:404,body:''});
 });
 const ready=()=>page.locator('#role-editor-form:not([hidden])').waitFor();
 for(const [width,height]of [[1920,1080],[1366,768],[1024,768],[768,1024],[844,390],[390,844],[320,640]]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();assert.equal(await page.locator('#role-template option').count(),9);
  await selectValue(page.locator('#role-template'),'cashier');await page.waitForFunction(()=>document.querySelector('#role-name').value==='Cashier');
  assert.match(await page.locator('#role-description').inputValue(),/Billing/);assert(await page.locator('.role-permission-card').isHidden());
  assert.match(await page.locator('.role-template-module').filter({hasText:'Sales'}).textContent(),/Included/);
  assert.match(await page.locator('.role-template-module').filter({hasText:'Reports'}).textContent(),/Not included/);
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:path.join(out,'cashier-'+width+'.png'),fullPage:true});
  await page.locator('#role-customize').tap();await page.getByRole('button',{name:'Expand All',exact:true}).tap();
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Expanded permissions fit '+width+'px');
  const adjust=page.locator('[data-module="STOCK_ADJUSTMENT"] [data-action="ADJUST"]');assert(await adjust.isChecked());
  for(const group of ['Home','Sales','Inventory','Customers']){const section=page.locator('.permission-group[data-group="'+group+'"]');assert(await section.locator(':scope > summary input').isChecked(),group+' defaults select the entire module');assert.equal(await section.locator('input[type=checkbox]:enabled:not(:checked)').count(),0,group+' includes all sections and actions');}
  await adjust.tap();assert(!await adjust.isChecked());
  const inventory=page.getByRole('checkbox',{name:'Select all screens in Inventory',exact:true});await inventory.tap();assert(await adjust.isChecked(),'Touch module selection restores all child actions');await adjust.tap();
  await page.locator('#role-customize').uncheck();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelector('.role-permission-card').hidden);assert(await adjust.isChecked());
 }
 await page.locator('#role-save').click();await page.waitForURL('**/Admin/Roles');assert.equal(writes.at(-1).templateKey,'cashier');assert.equal(writes.at(-1).customizePermissions,false);assert.equal(writes.at(-1).expectedTemplateVersion,'system-v2');
 await page.getByRole('link',{name:'Role Templates',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.role-template-tile').length===8);
 await page.locator('#template-new').click();await page.locator('#template-form:not([hidden])').waitFor();await page.locator('#template-name').fill('Evening Cashier');await page.locator('#template-description').fill('Evening counter staff');await page.getByRole('checkbox',{name:'Select all screens in Home',exact:true}).check();
 stale=true;await page.locator('#template-save').click();await page.locator('#template-message').filter({hasText:'Reload before saving'}).waitFor();assert.equal(await page.locator('#template-name').inputValue(),'Evening Cashier');stale=false;
 await page.locator('#template-save').click();await page.locator('#template-message').filter({hasText:'Template saved'}).waitFor();assert.equal(await page.locator('.role-template-tile').count(),9);
 await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();assert.equal(await page.locator('#role-template option[value="custom-evening"]').count(),1);await selectValue(page.locator('#role-template'),'custom-evening');await page.waitForFunction(()=>document.querySelector('#role-name').value==='Evening Cashier');await page.locator('#role-save').click();await page.waitForURL('**/Admin/Roles');assert.equal(writes.at(-1).templateKey,'custom-evening');
 await page.goto('http://stockflow.test/Admin/Users');await page.locator('#new-user').click();await page.waitForFunction(()=>[...document.querySelectorAll('#role option')].some(o=>o.textContent.includes('Evening Cashier')));assert.equal(await page.locator('#role option').filter({hasText:'Evening Cashier'}).count(),1);assert.equal(await page.locator('#permission-matrix').count(),0);
 await page.goto('http://stockflow.test/Admin/Roles/Templates');await page.locator('.role-template-tile').filter({hasText:'Evening Cashier'}).getByRole('button').click();await page.locator('#template-active').uncheck();await page.locator('#template-save').click();await page.locator('#template-message').filter({hasText:'Template saved'}).waitFor();
 await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();assert.equal(await page.locator('#role-template option[value="custom-evening"]').count(),0);assert(roles.some(r=>r.roleName==='Evening Cashier'&&r.isActive),'Deactivating a template leaves the saved role active');
 missing=true;await page.goto('http://stockflow.test/Admin/Roles/Create');await page.locator('#role-message').filter({hasText:'097_role_templates.sql'}).waitFor();assert(await page.locator('#role-editor-form').isHidden());
 assert.deepEqual(errors,[]);console.log('PASS: Template defaults, preview/customize, reset, versioned save, configured dropdown, role-to-user flow, inactive filtering, seven desktop/tablet/mobile viewports, touch module selection and missing-migration guidance.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
