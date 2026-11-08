// Current Razor/script via loopback TestServer; synthetic users and blocked writes.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
(async()=>{const b=await chromium.launch({channel:'msedge',headless:true});try{
 const p=await b.newPage({viewport:{width:1280,height:650}}),errors=[],writes=[];p.on('pageerror',e=>errors.push(e.message));p.setDefaultTimeout(8000);
 const user={userId:2,fullName:'Synthetic Staff',username:'synthetic.staff',roleId:2,roleName:'Sales',email:'staff@example.test',mobileNumber:'9876543210',isActive:true,securityVersion:1,permissions:[],storeAccess:{storeIds:[1],warehouseLocationIds:[],defaultStoreId:1},assignedStores:'Main store'};
 await p.route('**/api/admin/**',route=>{const r=route.request(),url=new URL(r.url()).pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});if(r.method()!=='GET'){writes.push(url);return route.fulfill({status:405,body:''});}
  if(url==='/api/admin/users')return json([user]);if(url==='/api/admin/users/2')return json(user);
  if(url==='/api/admin/roles')return json([{roleId:2,roleName:'Sales',isActive:true}]);if(url==='/api/admin/store-options')return json({stores:[{storeId:1,storeName:'Main store'}],warehouses:[]});return json([]);
 });
 await p.goto('http://127.0.0.1:55863/Admin/Users');await p.locator('#user-rows .sf-action-icon').first().waitFor();
 const actions=p.locator('#user-rows .sf-action-icon');assert.equal(await actions.getByText('View / edit').count(),0);assert.equal(await p.getByRole('button',{name:'View / edit',exact:true}).count(),1,'No duplicate edit control');
 for(const n of await actions.all()){assert.equal((await n.boundingBox()).width,32);assert.equal(await n.locator('svg').evaluate(n=>n.getBoundingClientRect().width),16);}
 await p.getByRole('button',{name:'Deactivate',exact:true}).click();await p.locator('#sf-confirm-dialog').waitFor({state:'visible'});await p.keyboard.press('Escape');await p.locator('#sf-confirm-dialog').waitFor({state:'hidden'});assert.deepEqual(writes,[]);
 await p.getByRole('button',{name:'View / edit',exact:true}).click();await p.locator('#user-editor').waitFor({state:'visible'});assert.equal(await p.locator('#full-name').inputValue(),'Synthetic Staff');
 await p.goto('http://127.0.0.1:55863/Admin/Users');await p.getByRole('button',{name:'Reset password',exact:true}).click();await p.locator('#reset-form').waitFor({state:'visible'});assert((await p.locator('#reset-title').textContent()).includes(user.username));assert.deepEqual(writes,[]);
 await p.goto('http://127.0.0.1:55863/Admin/Users');await p.locator('#user-rows .sf-action-icon').first().waitFor();await p.screenshot({path:path.resolve(__dirname,'../bin/action-icons/staff-actions.png'),fullPage:true});assert.deepEqual(errors,[]);
 console.log('PASS: staff row icon dimensions, unique edit action, existing Edit and Reset Password handlers, status confirmation/Escape, no mutations.');
}finally{await b.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
