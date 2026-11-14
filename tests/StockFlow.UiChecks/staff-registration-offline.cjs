const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {selectValue}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/role-management-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),out=path.join(root,'.artifacts/staff-registration');
fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({hasTouch:true}),errors=[],writes=[],queries=[];
  page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(10000);
  let delegated=false,stale=false;
  const roles=[{roleId:2,roleName:'Counter Staff',staffAccountType:'EMPLOYEE',isActive:true},{roleId:3,roleName:'Shop Supervisor',staffAccountType:'SHOP_ADMIN',isActive:true}];
  const permissions=[{moduleId:1,moduleCode:'DASHBOARD',moduleName:'Home',actions:['VIEW']},{moduleId:2,moduleCode:'SALES',moduleName:'Sales',actions:['VIEW','CREATE']}];
  const stores=[{storeId:1,storeName:'Main shop'},{storeId:2,storeName:'Second shop'}];
  const users=[{userId:10,fullName:'Ravi Kumar',username:'ravi',email:'ravi@example.test',mobileNumber:'9876543210',accountType:'EMPLOYEE',roleId:2,roleName:'Counter Staff',assignedStores:'Main shop',isActive:true,mustChangePassword:true,securityVersion:4,storeAccess:{storeIds:[1],defaultStoreId:1,warehouseLocationIds:[]},permissions}];
  await page.route('**/*',async route=>{
   const req=route.request(),u=new URL(req.url()),p=u.pathname,json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
   if(p==='/Admin/Users')return route.fulfill({path:path.join(fixtures,'Users.html'),contentType:'text/html'});
   if(p==='/api/admin/users/registration-options')return json({businessName:'Synthetic business',canManageStaff:true,canCreateShopAdmin:!delegated,canUseAdvancedAccess:!delegated,roles:delegated?roles.slice(0,1):roles,stores:delegated?stores.slice(0,1):stores,warehouses:delegated?[]:[{locationId:8,locationName:'Central warehouse'}]});
   if(p==='/api/admin/users'&&req.method()==='GET'){queries.push(u.searchParams);return json(users.filter(x=>(!u.searchParams.get('term')||[x.fullName,x.username,x.mobileNumber,x.email].join(' ').includes(u.searchParams.get('term')))&&(!u.searchParams.get('accountType')||x.accountType===u.searchParams.get('accountType'))));}
   if(p==='/api/admin/users/username-suggestion')return json({username:'new.supervisor1'});
   if(p==='/api/admin/users/10'&&req.method()==='GET')return json(users[0]);
   if((p==='/api/admin/users'||p==='/api/admin/users/10')&&req.method()!=='GET'){
    const body=req.postDataJSON();assert(req.headers()['x-csrf-token']);writes.push(body);
    if(stale)return json({message:'This user changed. Reload before saving.'},400);
    return json({id:10});
   }
   if(p==='/api/admin/users/10/reset-password'||p==='/api/admin/users/10/status'){writes.push(req.postDataJSON());return route.fulfill({status:204});}
   if(/^\/api\/admin\/roles\/\d+\/permissions$/.test(p))return json(permissions);
   if(p==='/api/branches/context')return json({storeId:1,locationId:1,canReviewStores:true,locations:[{storeId:1,locationId:1,storeName:'Main shop',locationName:'Main stock'}]});
   if(p==='/api/notifications')return json({unread:0,items:[]});if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  const ready=()=>page.waitForFunction(()=>!document.querySelector('#admin-users').inert&&document.querySelector('#user-rows tr'));
  for(const [width,height]of [[1920,1080],[1366,768],[1024,768],[768,1024],[844,390],[390,844],[320,640]]){
   await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Admin/Users');await ready();
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'List fits '+width);
   assert(await page.locator('#user-filter-panel').isHidden(),'Filters start collapsed');
   assert.equal(await page.locator('#user-search button[type="submit"]').count(),0,'Search has no submit button');
   await page.screenshot({path:path.join(out,'staff-list-'+width+'.png'),fullPage:true});
   await page.locator('#toggle-user-filters').tap();assert.equal(await page.locator('#toggle-user-filters').getAttribute('aria-expanded'),'true');
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Expanded filters fit '+width);
   if(width>=1024){const tops=await page.locator('.staff-filter-grid>div').evaluateAll(items=>items.map(e=>Math.round(e.getBoundingClientRect().top)));assert(tops.every(top=>top===tops[0]),'Desktop filters share one row');}
   await page.locator('#toggle-user-filters').tap();
   await page.locator('#new-user').tap();await page.locator('#user-editor:not([hidden])').waitFor();
   assert(await page.evaluate(()=>Math.abs(document.querySelector('#user-editor').getBoundingClientRect().width-document.querySelector('#admin-users').getBoundingClientRect().width)<2),'Form uses available workspace width at '+width);
   if(width===1366){
    const originalWidth=await page.locator('#admin-users').evaluate(e=>e.getBoundingClientRect().width);
    await page.locator('#sidebar-toggle').click();await page.waitForFunction(old=>document.querySelector('#admin-users').getBoundingClientRect().width>old+100,originalWidth);
    assert(await page.evaluate(()=>Math.abs(document.querySelector('#user-editor').getBoundingClientRect().width-document.querySelector('#admin-users').getBoundingClientRect().width)<2),'Form expands when sidebar collapses');
    await page.locator('#sidebar-toggle').click();await page.waitForFunction(old=>Math.abs(document.querySelector('#admin-users').getBoundingClientRect().width-old)<2,originalWidth);
   }
   assert.equal(await page.locator('#business-name').inputValue(),'Synthetic business');assert(await page.locator('#business-name').getAttribute('readonly')!==null);
   assert(!await page.locator('#advanced-store-access').evaluate(e=>e.open));assert(await page.locator('#multi-store-default').isHidden());assert(await page.locator('#must-change').isChecked());
   assert.equal(await page.locator('#role option[value="3"]').count(),0);
   await selectValue(page.locator('#account-type'),'SHOP_ADMIN');await page.waitForFunction(()=>!document.querySelector('#admin-users').inert);assert.equal(await page.locator('#role option[value="2"]').count(),0);
   await selectValue(page.locator('#role'),'3');await page.waitForFunction(()=>!document.querySelector('#admin-users').inert);
   await selectValue(page.locator('#primary-store'),'1');assert.match(await page.locator('#staff-access-summary').textContent(),/Main shop.*Shop Supervisor.*3 actions/);
   await page.getByText('Effective permissions',{exact:true}).tap();assert.match(await page.locator('#staff-permission-summary').textContent(),/Sales: View, Create/);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Editor fits '+width);
   await page.screenshot({path:path.join(out,'staff-'+width+'.png'),fullPage:true});
  }
  await page.locator('#full-name').fill('New Supervisor');await page.locator('#full-name').blur();await page.waitForFunction(()=>document.querySelector('#username').value==='new.supervisor1');
  await page.locator('#username').fill('new.supervisor');await page.locator('#full-name').fill('Changed Name');await page.locator('#full-name').blur();assert.equal(await page.locator('#username').inputValue(),'new.supervisor','Manual username is preserved');
  await page.locator('#active').uncheck();assert.equal(await page.locator('#staff-status-label').textContent(),'Inactive');await page.locator('#active').check();await page.locator('#mobile').fill('9876543210');await page.locator('#email').fill('new.supervisor@example.test');await page.locator('#password').fill('SyntheticPassword123!');await page.locator('#confirm-password').fill('SyntheticPassword123!');
  await page.locator('#save-user').click();await page.locator('#user-list-view:not([hidden])').waitFor();assert.equal(writes.at(-1).accountType,'SHOP_ADMIN');assert.deepEqual(writes.at(-1).storeAccess,{storeIds:[1],warehouseLocationIds:[],defaultStoreId:1});assert.deepEqual(writes.at(-1).permissions,[]);
  await page.getByText('View access',{exact:true}).click();await page.waitForFunction(()=>document.querySelector('.staff-list-access').textContent.includes('Sales: View, Create'));
  await page.getByRole('button',{name:'View ravi',exact:true}).click();await page.locator('#user-editor:not([hidden])').waitFor();assert(await page.locator('#save-user').isHidden());assert(await page.locator('#full-name').isDisabled());assert.match(await page.locator('#staff-permission-summary').textContent(),/Create/);await page.locator('#cancel-user').click();
  await page.getByRole('button',{name:'Edit ravi',exact:true}).click();await page.locator('#user-editor:not([hidden])').waitFor();await selectValue(page.locator('#primary-store'),'2');
  stale=true;await page.locator('#save-user').click();await page.locator('#admin-message').filter({hasText:'Reload before saving'}).waitFor();assert.equal(await page.locator('#primary-store').inputValue(),'2');stale=false;
  await page.locator('#save-user').click();await page.locator('#user-list-view:not([hidden])').waitFor();assert.deepEqual(writes.at(-1).storeAccess.storeIds,[2]);assert.equal(writes.at(-1).expectedVersion,4);
  await Promise.all([page.waitForResponse(r=>r.url().includes('/api/admin/users?')&&new URL(r.url()).searchParams.get('term')==='ravi@example.test'),page.locator('#search-term').fill('ravi@example.test')]);
  assert.equal(queries.at(-1).get('term'),'ravi@example.test','Typing searches without a Search button');
  await page.locator('#toggle-user-filters').click();
  await Promise.all([page.waitForResponse(r=>r.url().includes('/api/admin/users?')&&new URL(r.url()).searchParams.get('accountType')==='EMPLOYEE'),selectValue(page.locator('#filter-account-type'),'EMPLOYEE')]);
  assert.equal(queries.at(-1).get('accountType'),'EMPLOYEE');assert.match(await page.locator('#user-filter-count').textContent(),/1/);
  await Promise.all([page.waitForResponse(r=>r.url().includes('/api/admin/users?')&&!new URL(r.url()).searchParams.has('accountType')&&!new URL(r.url()).searchParams.get('term')),page.locator('#clear-users').click()]);assert(await page.locator('#clear-users').isHidden());
  await page.getByRole('button',{name:'Reset password',exact:true}).click();await page.locator('#reset-form:not([hidden])').waitFor();await page.locator('#reset-password').fill('ResetPassword123!');await page.locator('#reset-confirm').fill('ResetPassword123!');await page.locator('#reset-form button[type="submit"],#reset-form button:not([type])').click();await page.locator('#user-list-view:not([hidden])').waitFor();assert.equal(writes.at(-1).mustChangePassword,true);
  delegated=true;await page.goto('http://stockflow.test/Admin/Users');await ready();await page.locator('#new-user').click();await page.locator('#user-editor:not([hidden])').waitFor();assert(await page.locator('#advanced-store-access').isHidden());assert(await page.locator('#account-type option[value="SHOP_ADMIN"]').evaluate(e=>e.disabled));assert.equal(await page.locator('#primary-store option[value="2"]').count(),0);
  assert.deepEqual(errors,[]);console.log('PASS: Staff registration and list at seven viewports, touch controls, trusted role filtering, saved permission summary, single-shop payload, view/edit, stale save retention, filters, password reset and delegated UI.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
