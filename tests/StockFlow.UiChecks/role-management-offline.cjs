const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {selectValue}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/role-management-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),out=path.join(root,'.artifacts/role-management');fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[],writes=[];page.setDefaultTimeout(10000);page.on('pageerror',e=>errors.push(e.message));
 const catalog=JSON.parse(fs.readFileSync(path.join(fixtures,'Modules.json'),'utf8')),blank=()=>catalog.map(m=>({...m,actions:[]})),template=()=>catalog.map(m=>({...m,actions:m.moduleCode==='SALES'?['VIEW','CREATE']:m.moduleCode==='DASHBOARD'?['VIEW']:[]}));
 const roles=[{roleId:1,roleName:'Admin',isActive:true,assignedUsers:1,isProtected:true,version:'a'},{roleId:2,roleName:'Cashier',description:'Counter billing',isActive:true,assignedUsers:8,requiresTwoFactor:true,version:'b'},{roleId:3,roleName:'Inventory Staff',isActive:false,assignedUsers:0,version:'c'}];let reject=false,limitedGrants=false;
 await page.route('**/*',async r=>{const req=r.request(),u=new URL(req.url()),p=u.pathname,json=(body,status=200)=>r.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p.startsWith('/Admin/Roles')){const name=p.includes('/Create')?'Create':p.includes('/Edit/')?'Edit':p.includes('/View/')?'View':'List';let html=fs.readFileSync(path.join(fixtures,name+'.html'),'utf8');if(limitedGrants)html=html.replace(/data-grants="[^"]*"/,'data-grants="{&quot;INVENTORY&quot;:[&quot;VIEW&quot;]}"');return r.fulfill({body:html,contentType:'text/html'});}
  if(p==='/Admin/Users')return r.fulfill({path:path.join(fixtures,'Users.html'),contentType:'text/html'});
  if(p==='/api/admin/roles/directory'||p==='/api/admin/roles')return json(roles);
  if(p==='/api/admin/roles/templates')return json([{templateKey:'custom-role',templateName:'Custom Role',isActive:true,isSystem:true,version:'system-v1'}]);
  if(p==='/api/admin/roles/templates/custom-role')return json({template:{templateKey:'custom-role',templateName:'Custom Role',isActive:true,isSystem:true,version:'system-v1'},permissions:blank(),omittedActions:0});
  if(p==='/api/admin/modules')return json(blank());
  if(/\/roles\/\d+\/details$/.test(p))return json({role:roles.find(v=>v.roleId===Number(p.split('/').at(-2))),permissions:template()});
  if(/\/roles\/\d+\/permissions$/.test(p))return json(template());
  if(p==='/api/admin/roles/save'){assert(req.headers()['x-csrf-token']);writes.push(req.postDataJSON());return reject?json({message:'Role changed. Reload before saving.'},409):json({roleId:4});}
  if(/\/roles\/\d+\/status$/.test(p)){writes.push(req.postDataJSON());return r.fulfill({status:204});}
  if(p==='/api/admin/store-options')return json({stores:[{storeId:1,storeName:'Main store'}],warehouses:[]});
  if(p==='/api/admin/users/registration-options')return json({businessName:'Test business',canManageStaff:true,canCreateShopAdmin:true,canUseAdvancedAccess:true,roles:roles.filter(r=>r.isActive&&!r.isProtected).map(r=>({...r,staffAccountType:'EMPLOYEE'})),stores:[{storeId:1,storeName:'Main store'}],warehouses:[]});
  if(p==='/api/admin/users')return json([]);
  if(p==='/api/branches/context')return json({storeId:1,locationId:1,canReviewStores:true,locations:[{storeId:1,locationId:1,storeName:'Main store',locationName:'Main stock'}]});
  if(p==='/api/notifications')return json({unread:0,items:[]});if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?r.fulfill({path:file}):r.fulfill({status:404,body:''});
 });
 const ready=async()=>{await page.locator('#role-editor-form:not([hidden])').waitFor();if(await page.locator('#role-template-field').isVisible()){await selectValue(page.locator('#role-template'),'custom-role');await page.waitForFunction(()=>!document.querySelector('#roles-workspace').inert&&!document.querySelector('.role-permission-card').hidden);}};
 for(const [width,height]of [[1536,1024],[1366,768],[1024,768],[768,1024],[390,844],[320,640]]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Admin/Roles');await page.waitForFunction(()=>document.querySelector('#roles-total').textContent==='3');assert.equal(await page.locator('#role-list tr').count(),3);assert.equal(await page.locator('#roles-active').textContent(),'2');assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await page.getByRole('link',{name:'+ Add Role',exact:true}).click();await ready();assert.equal(await page.locator('#role-editor-form .role-card').count(),3);assert.equal(await page.locator('#role-authenticator,.role-authenticator').count(),0);assert(await page.locator('.permission-group > summary input[data-module-group]').count()>0);assert.equal(await page.locator('#role-permission-matrix [data-module]').evaluateAll(items=>new Set(items.map(e=>e.dataset.module)).size),catalog.length);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:path.join(out,'create-'+width+'.png'),fullPage:true});
 }
 await page.goto('http://stockflow.test/Admin/Roles');await page.locator('#role-list tr').first().waitFor();await page.locator('#role-search').fill('cashier');assert.equal(await page.locator('#role-list tr').count(),1);assert(await page.locator('#role-list .role-status-action').isDisabled());await page.locator('#role-filter-reset').click();await selectValue(page.locator('#role-filter-status'),'INACTIVE');assert.equal(await page.locator('#role-list tr').count(),1);
 await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();await page.getByRole('button',{name:'Expand All',exact:true}).click();assert.equal(await page.locator('.permission-group:not([open])').count(),0);await page.getByRole('button',{name:'Collapse All',exact:true}).click();assert.equal(await page.locator('.permission-group[open]').count(),0);await page.locator('#role-name').fill('New Cashier');
 const inventory=catalog.find(m=>m.moduleCode==='STOCK_ADJUSTMENT'),inventoryGroup='Inventory',group=page.getByRole('checkbox',{name:'Select all screens in '+inventoryGroup,exact:true});
 await group.check();
 const inventorySection=page.locator('.permission-group[data-group="Inventory"]');
 assert.equal(await inventorySection.locator('input[data-action]:not([disabled]):not(:checked)').count(),0,'Module selection enables its grantable screens and actions including Product Catalog');
 assert(await inventorySection.getByText('Product Catalog',{exact:true}).count());
 await page.locator('[data-module="STOCK_ADJUSTMENT"] [data-action="ADJUST"]').uncheck();assert(await group.evaluate(e=>e.indeterminate),'Individual action changes make parent selection partial');await group.check();
 await page.locator('[data-module="STOCK_ADJUSTMENT"] [data-action="VIEW"]').uncheck();assert(await group.evaluate(e=>e.indeterminate),'Individual screen changes make parent selection partial');
 await group.check();await group.uncheck();assert.equal(await inventorySection.locator('input[data-action]:checked').count(),0,'Clearing a module clears its child permissions');
 await page.locator('.role-copy-advanced > summary').click();await selectValue(page.locator('#role-copy'),'2');await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelector('#role-permission-count').textContent==='3');
 if(!await page.locator('[data-module="STOCK_ADJUSTMENT"]').evaluate(e=>e.closest('.permission-group').open))await page.locator('.permission-group > summary').filter({hasText:inventoryGroup}).click();await page.locator('[data-module="STOCK_ADJUSTMENT"] [data-action="VIEW"]').check();await page.locator('[data-module="STOCK_ADJUSTMENT"] [data-action="ADJUST"]').check();assert.equal(await page.locator('#role-permission-count').textContent(),'5');
 await page.getByLabel('Find module permissions').fill('Sales');assert(await page.locator('.permission-group > summary input[data-module-group]').count()>0);await page.getByLabel('Find module permissions').fill('');
 reject=true;await page.locator('#role-save').click();await page.locator('#role-message').filter({hasText:'Reload before saving'}).waitFor();assert.equal(await page.locator('#role-name').inputValue(),'New Cashier');reject=false;await page.locator('#role-save').click();await page.waitForURL('**/Admin/Roles');assert.equal(writes.at(-1).permissions.length,catalog.length);assert(writes.at(-1).permissions.find(m=>m.moduleCode==='STOCK_ADJUSTMENT').actions.includes('ADJUST'));
 await page.goto('http://stockflow.test/Admin/Roles/Edit/2');await ready();assert.match(await page.locator('#role-impact').textContent(),/8 users/);await page.locator('#role-description').fill('Updated counter role');await page.locator('#role-save').click();await page.locator('#sf-confirm-accept').click();await page.waitForURL('**/Admin/Roles');assert.equal(writes.at(-1).confirmAssignedUsers,true);assert.equal(writes.at(-1).expectedVersion,'b');assert.equal(writes.at(-1).requiresTwoFactor,true,'Removing the policy control preserves the existing policy');
 await page.goto('http://stockflow.test/Admin/Roles/View/2');await ready();assert(await page.locator('#role-save').isHidden());assert(await page.locator('#role-name').isDisabled());assert.equal(await page.locator('#role-permission-matrix input[data-action]:not([disabled])').count(),0);assert.equal(await page.locator('.permission-group > summary input[type="checkbox"]:not([disabled])').count(),0);

 await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();
 const paths=await page.evaluate(catalog=>StockFlowRolePermissionEditor.navigationEntries(catalog),catalog);
 const names=[...new Set(paths.map(e=>e.group))];for(const expected of ['Home','Purchases','Sales','Inventory','Expenses','Cash in Hand','Reports','Customers','Settings'])assert(names.includes(expected),'Navigation name: '+expected);
 for(const old of ['Dashboard','Receive Stock','Sell / Billing','Operating Expenses','Setup','Customer Relationships','GST & Tax Reports'])assert(!names.includes(old),'Old permission group removed: '+old);
 assert(paths.some(e=>e.group==='Inventory'&&e.subgroup==='Product Catalog'&&e.title==='Products'));
 assert(paths.some(e=>e.group==='Settings'&&e.subgroup==='Staff & Access'&&e.title==='Roles & Permissions'));
 const labels=await page.evaluate(()=>[...document.querySelectorAll('.sf-shop-nav a[href]')].map(a=>({route:a.getAttribute('href'),label:a.querySelector('.sf-nav-label')?.textContent.trim()||a.textContent.trim()})));
 for(const entry of paths){const match=labels.find(l=>l.route===entry.route);if(match)assert.equal(entry.title,match.label,'Screen caption matches sidebar: '+entry.route);}
 await page.getByRole('button',{name:'Expand All',exact:true}).click();
 const company=route=>page.locator('.permission-module[data-route="'+route+'"]');
 await company('/Settings/Company?section=business').locator('[data-action="VIEW"]').check();
 assert(await company('/Settings/Company?section=billing').locator('[data-action="VIEW"]').isChecked(),'Business and invoice shared access stays synchronized');
 await company('/Settings/Company?section=billing').locator('[data-action="EDIT"]').check();
 assert(await company('/Settings/Company?section=business').locator('[data-action="EDIT"]').isChecked(),'Shared edit actions stay synchronized');
 await company('/Settings/BusinessProfile').locator('[data-action="VIEW"]').uncheck();
 assert.equal(await company('/Settings/Company?section=business').locator('input:checked').count(),0,'Clearing a shared screen retains VIEW dependencies');
 await company('/Sales/Create').locator('[data-action="VIEW"]').check();
 assert(await company('/Sales').locator('[data-action="CREATE"]').isChecked(),'New Sale selects its existing CREATE permission');
 await company('/Sales').locator('[data-action="CREATE"]').uncheck();
 assert(!await company('/Sales/Create').locator('[data-action="VIEW"]').isChecked(),'New Sale requires CREATE as well as VIEW');
 limitedGrants=true;await page.goto('http://stockflow.test/Admin/Roles/Create');await ready();await page.locator('#role-name').fill('Limited inventory reader');
 await page.getByRole('checkbox',{name:'Select all screens in Inventory',exact:true}).check();
 assert.equal(await page.locator('#role-permission-count').textContent(),'1','Bulk selection cannot add undelegated actions');
 assert.equal(await page.locator('.permission-group[data-group="Inventory"] input[data-action]:not([data-action="VIEW"]):checked').count(),0);
 assert(await page.locator('#role-editor-form').evaluate(f=>f.checkValidity()),JSON.stringify(await page.locator('#role-editor-form').evaluate(f=>[...f.elements].filter(e=>e.willValidate&&!e.validity.valid).map(e=>({id:e.id,value:e.value,message:e.validationMessage})))));
 await page.locator('#role-save').click();await page.waitForURL('**/Admin/Roles');
 assert.deepEqual(writes.at(-1).permissions.filter(r=>r.actions.length).map(r=>[r.moduleCode,r.actions]),[['INVENTORY',['VIEW']]],'Save preserves one canonical row and grant bounds');limitedGrants=false;
 await page.goto('http://stockflow.test/Admin/Users');await page.locator('#new-user').click();assert.equal(await page.locator('#permission-matrix').count(),0);assert.equal(await page.locator('#user-editor .permission-editor').count(),0);await selectValue(page.locator('#role'),'2');assert.match(await page.locator('#selected-role-help').textContent(),/automatically/);assert.equal(await page.locator('#role option[value="1"]').count(),0,'Protected owner role is excluded from new user selection');
 assert.deepEqual(errors,[]);console.log('PASS: Roles list and full-page Create/Edit/View at six sizes, sidebar names and hierarchy, linked screens, grant-limited bulk selection, catalog reuse, filters, copying, dependencies, counts, conflict retention, assigned-user confirmation, readonly view and role-only user registration.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
