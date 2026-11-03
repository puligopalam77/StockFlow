// Rendered Razor and mocked API responses. Never contacts PostgreSQL.
const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'../..'),staticRoot=path.join(root,'src/StockFlow.Web/wwwroot');
const {chromium}=require(path.join(__dirname,'bin/Debug/net10.0/.playwright/package'));
const fixtures=path.join(root,'tests/bin/security-ui-fixtures'),base='http://localhost:51997';
const fields=['canView','canCreate','canEdit','canDelete','canApprove','canExport'];
const modules=['Products','Customers','Sales','User management','Security','Dashboard'].map((moduleName,i)=>({moduleId:i+1,moduleName,moduleCode:moduleName.toUpperCase().replaceAll(' ','_'),supportedActions:moduleName==='Dashboard'?['VIEW']:moduleName==='Security'?['VIEW','MANAGE']:['VIEW','CREATE','EDIT','DEACTIVATE'],actions:[],...Object.fromEntries(fields.map(f=>[f,false]))}));
let saved,reset,roleSaved,permissionSaved,logoutBody=null,failUpdate=false;
const user={userId:2,fullName:'Example User',username:'example',roleId:2,roleName:'Sales',email:'staff@example.com',mobileNumber:'9876543210',requireTwoFactor:true,isActive:true,mustChangePassword:true,securityVersion:1,permissions:modules,storeAccess:{storeIds:[1],warehouseLocationIds:[],defaultStoreId:1},assignedStores:'Main store'};
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({reducedMotion:'reduce'}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async route=>{
   const request=route.request(),url=new URL(request.url()),p=url.pathname;
   if(p==='/Account/Logout' && request.method()==='POST'){logoutBody=request.postData();return route.fulfill({contentType:'text/html',body:'<h1>Signed out</h1>'});}
   const json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
   if(p==='/Account/ChangePassword' && request.method()==='POST')return json({message:'Current password is incorrect.'},400);
   if(p==='/api/admin/modules')return json(modules);
   if(p==='/api/admin/store-options')return json({stores:[{storeId:1,storeName:'Main store'},{storeId:2,storeName:'Second store'}],warehouses:[{locationId:3,locationName:'Central warehouse'}]});if(p==='/api/admin/roles')return json([{roleId:1,roleName:'Admin',isActive:true},{roleId:2,roleName:'Sales',isActive:true}]);
   if(/^\/api\/admin\/roles\/\d+\/permissions$/.test(p)){if(request.method()==='PUT'){roleSaved=request.postDataJSON();return route.fulfill({status:204});}return json(modules);}
   if(p==='/api/admin/users/2/reset-password'){reset=request.postDataJSON();return route.fulfill({status:204});}
   if(p==='/api/admin/users/2/permissions'){permissionSaved=request.postDataJSON();return route.fulfill({status:204});}
   if(p==='/api/admin/users/2'){if(request.method()==='PUT'){if(failUpdate)return json({message:'Account could not be saved.'},400);saved=request.postDataJSON();return json({id:2});}return json(user);}
   if(p==='/api/admin/users'){if(request.method()==='POST'){saved=request.postDataJSON();return json({id:3});}return json([user]);}
   if(p==='/api/admin/auditlogs')return json([{auditLogId:1,username:'admin',actionName:'USER_CREATE',moduleName:'Administration',recordId:2,createdAt:'2026-09-29T09:00:00',newValues:'{"FullName":"<script>alert(1)</script>"}'}]);
   if(p.startsWith('/Admin/'))return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,p.split('/').pop()+'.html'),'utf8')});
   const file=path.resolve(staticRoot,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   if(file.startsWith(staticRoot+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file,contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':undefined});
   return route.fulfill({status:404,body:''});
  });
  await page.goto(base+'/Admin/Users');await page.locator('#user-rows .admin-row-action').first().waitFor();
  assert.equal(await page.locator('.sf-nav a[href="/Account/ChangePassword"]').count(),0);
  await page.getByLabel('My profile',{exact:true}).click();
  await page.getByRole('region',{name:'Personal profile'}).waitFor();
  assert(await page.locator('#profile-details-dialog').isHidden());
  await page.locator('#open-profile-details').click();await page.locator('#profile-details-dialog').waitFor();
  assert((await page.locator('#profile-details-dialog').innerText()).includes('person@example.test'));
  assert((await page.locator('#profile-details-dialog').innerText()).includes('9876543210'));
  assert(page.url().endsWith('/Admin/Users'));
  await page.keyboard.press('Escape');assert(await page.locator('#profile-details-dialog').isHidden());
  await page.getByLabel('My profile',{exact:true}).click();await page.locator('#open-change-password').click();await page.locator('#change-password-dialog').waitFor();
  await page.locator('#profile-current-password').fill('OldPassword-1');await page.locator('#profile-new-password').fill('NewPassword-2');await page.locator('#profile-confirm-password').fill('Mismatch-password-3');
  await page.locator('#profile-password-form button[type="submit"]').click();await page.getByText('Passwords do not match.',{exact:true}).waitFor();
  await page.locator('#profile-confirm-password').fill('NewPassword-2');await page.locator('#profile-password-form button[type="submit"]').click();await page.getByText('Current password is incorrect.',{exact:true}).waitFor();assert(page.url().endsWith('/Admin/Users'));
  await page.setViewportSize({width:320,height:700});const passwordBox=await page.locator('#change-password-dialog').boundingBox();assert(passwordBox.x>=0&&passwordBox.x+passwordBox.width<=320);
  await page.locator('#change-password-dialog button[data-close-account]').last().click();await page.waitForFunction(()=>document.getElementById('profile-current-password').value==='');
  assert.equal(await page.locator('#logout-confirmation form[method="post"] input[name="__RequestVerificationToken"]').count(),1);  await page.setViewportSize({width:320,height:700});await page.getByLabel('My profile',{exact:true}).click();
  const profileBox=await page.locator('.sf-profile-panel').boundingBox();assert(profileBox.x>=0&&profileBox.x+profileBox.width<=320);
    await page.locator('#profile-logout').click();await page.locator('#logout-confirmation').waitFor();
  assert.equal(logoutBody,null);await page.locator('#logout-cancel').click();assert(await page.locator('#logout-confirmation').isHidden());
  await page.getByLabel('My profile',{exact:true}).click();await page.locator('#profile-logout').click();await page.keyboard.press('Escape');assert(await page.locator('#logout-confirmation').isHidden());
  assert.equal(await page.locator('.sf-nav a[href="/Admin/Users"]').count(),1);
  for(const width of [1440,390]){await page.setViewportSize({width,height:1000});await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+2);}
  await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:path.join(fixtures,'user-list.png'),fullPage:true});
  await page.locator('#new-user').click();await page.locator('#user-list-view').waitFor({state:'hidden'});
  assert.equal(await page.locator('#permission-matrix, #two-factor-policy').count(),0);
  await page.locator('#full-name').fill('New Colleague');await page.locator('#username').fill('new.colleague');await page.locator('#role').selectOption('2');
  await page.locator('#email').fill('new@example.com');await page.locator('#mobile').fill('9876543210');await page.locator('#password').fill('Abcd123!');await page.locator('#confirm-password').fill('Abcd123!');
  await page.locator('#primary-store').selectOption('1');await page.locator('#save-user').click();await page.waitForFunction(()=>document.getElementById('user-editor').hidden);
  assert.equal(saved.username,'new.colleague');assert.equal(saved.requireLoginEmailOtp,undefined);assert.equal(saved.useRoleTemplate,true);assert.equal(saved.email,'new@example.com');assert.equal(saved.mobileNumber,'9876543210');assert.deepEqual(saved.storeAccess,{storeIds:[1],warehouseLocationIds:[],defaultStoreId:1});assert.equal(await page.locator('#password').inputValue(),'');
  await page.getByRole('button',{name:'View / edit',exact:true}).first().click();await page.locator('#user-editor').waitFor();failUpdate=true;await page.locator('#save-user').click();await page.getByText('Account could not be saved.',{exact:true}).waitFor();assert(await page.locator('#user-editor').isVisible());assert.equal(await page.locator('#email').inputValue(),'staff@example.com');failUpdate=false;await page.locator('#save-user').click();await page.waitForFunction(()=>document.getElementById('user-editor').hidden);assert.equal(saved.useRoleTemplate,true);assert.equal(saved.expectedVersion,1);
  await page.getByRole('button',{name:'Reset password',exact:true}).first().click();await page.locator('#reset-password').fill('Changed passphrase 123');await page.locator('#reset-confirm').fill('Changed passphrase 123');await page.locator('#reset-form button[type="submit"], #reset-form button:not([type])').click();
  await page.waitForFunction(()=>document.getElementById('reset-form').hidden);assert.equal(reset.expectedVersion,1);assert.equal(reset.mustChangePassword,true);assert.equal(await page.locator('#reset-password').inputValue(),'');
  await page.goto(base+'/Admin/Roles');await page.getByLabel('Find module permissions').fill('Products');await page.getByLabel('Products: CREATE',{exact:true}).check();await page.locator('#save-role').click();await page.getByText('Template saved.',{exact:false}).waitFor();assert(roleSaved[0].actions.includes('CREATE'));
  await page.goto(base+'/Admin/AuditLogs');await page.getByText('View changes',{exact:true}).click();assert((await page.locator('pre').innerText()).includes('<script>alert(1)</script>'));assert.equal(await page.locator('pre script').count(),0);
  for(const width of [1440,768,390,320]){await page.setViewportSize({width,height:1000});await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+2,null,{timeout:3000});}
    await page.getByLabel('My profile',{exact:true}).click();await page.locator('#profile-logout').click();await page.locator('.sf-dialog-confirm').click();await page.getByText('Signed out',{exact:true}).waitFor();assert(logoutBody.includes('__RequestVerificationToken='));
  assert.deepEqual(errors,[]);console.log('PASS: administration matrix, create payload, password reset, template save, audit escaping and responsive layout (mock APIs).');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});


