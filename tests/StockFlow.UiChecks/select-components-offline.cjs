const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const screenshots=path.join(root,'tests/bin/dropdown-ui');fs.mkdirSync(screenshots,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1280,height:600},deviceScaleFactor:1.5});
  page.setDefaultTimeout(6000);const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{
   const p=new URL(route.request().url()).pathname;
   if(/^\/(?:Suppliers|Products|Purchases|Sales|Inventory|Reports|OtherModule)\/Create$/.test(p))return route.fulfill({contentType:'text/html',body:
    '<!doctype html><html><head><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/css/select.css"></head><body style="padding:16px"><form id="form"><label for="entity">Supplier</label><select id="entity" name="supplierId" data-sf-select="search"><option value="">Not specified</option>'+Array.from({length:14},(_,i)=>'<option value="'+(100+i)+'"'+(i===3?' selected':'')+(i===6?' disabled':'')+' data-search="SUP'+i+'">Supplier '+i+'</option>').join('')+'</select><details id="details"><summary>Required details</summary><label for="required">GST registration</label><select id="required" name="registrationId" required data-sf-select="search"><option value="">Choose registration</option><option value="29">Karnataka registration</option><option value="33">Tamil Nadu registration</option></select></details><label>Status<select id="short" name="status"><option value="ACTIVE">Active</option><option value="INACTIVE">Inactive</option></select></label><label>Authorized shops<select id="multiple" name="shops" multiple><option value="1" selected>Main</option><option value="2">Branch</option></select></label><label for="locked">Locked</label><select id="locked" disabled data-sf-select="search"><option value="old" selected>Saved inactive record</option></select><button id="save">Save</button><button id="reset" type="reset">Reset</button></form><div style="overflow:hidden;height:100px"><dialog id="dialog"><form method="dialog"><label for="modal">Location</label><select id="modal" data-sf-select="search"><option value="">Not specified</option><option value="48">Stock room</option></select><button>Close dialog</button></form></dialog></div><button id="outside">Outside</button><script src="/js/common/form-validation.js"></script><script src="/js/common/select.js"></script><script src="/js/common/select-migrations.js"></script><script>window.changes=0;window.posts=[];entity.addEventListener("change",()=>changes++);form.addEventListener("submit",event=>{event.preventDefault();posts.push(Object.fromEntries(new FormData(form)));});</script></body></html>'});
   const file=path.resolve(assets,'.'+p);
   return file.startsWith(assets+path.sep)&&fs.existsSync(file)?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Suppliers/Create');
  const trigger=page.locator('#entity-search'),search=page.locator('#entity-panel input[type=search]'),list=page.locator('#entity-options');
  await trigger.waitFor();assert.equal(await trigger.inputValue(),'Supplier 3');
  await trigger.press('Space');assert.equal(await trigger.getAttribute('aria-expanded'),'true');await search.press('Escape');
  assert.equal(await page.locator('#short').evaluate(e=>!!e.sfSelect),true,'Short status uses shared dropdown');
  assert.equal(await page.locator('#multiple').evaluate(e=>!!e.sfSelect),false,'Existing multi-select semantics remain native');
  await trigger.click();await search.fill(' sUpPlIeR 12 ');
  assert.equal(await list.evaluate(e=>getComputedStyle(e).scrollbarWidth),'thin','Shared scrollbar stays thin and scrollable');
  assert(await list.evaluate(e=>e.scrollHeight>e.clientHeight||getComputedStyle(e).overflowY==='auto'));
  assert.equal(await page.locator('#entity').inputValue(),'103','Searching never changes selected ID');
  await list.getByRole('option',{name:'Supplier 12',exact:true}).hover();assert.equal(await page.evaluate(()=>changes),0);
  assert.equal(await list.getByRole('option',{name:'Supplier 12',exact:true}).evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(255, 240, 247)');
  await list.getByRole('option',{name:'Supplier 12',exact:true}).click();
  assert.equal(await page.locator('#entity').inputValue(),'112');assert.equal(await trigger.inputValue(),'Supplier 12');
  assert.equal(await page.evaluate(()=>changes),1,'Exactly one existing change event');
  assert.equal(await trigger.evaluate(e=>e===document.activeElement),true);
  await trigger.click();assert.equal(await list.getByRole('option',{name:'Supplier 12',exact:true}).getAttribute('aria-selected'),'true');
  assert((await list.getByRole('option',{name:'Supplier 12',exact:true}).evaluate(e=>getComputedStyle(e,'::after').content)).includes('✓'),await list.getByRole('option',{name:'Supplier 12',exact:true}).evaluate(e=>({content:getComputedStyle(e,'::after').content,html:e.outerHTML,styles:[...document.styleSheets].map(s=>s.href)})));
  await search.fill('no such supplier');assert((await list.textContent()).includes('No matching results'));
  await search.press('Escape');assert.equal(await page.locator('#entity').inputValue(),'112');
  await trigger.click();await search.fill('SUP4');await search.press('ArrowDown');await search.press('Enter');
  assert.equal(await page.locator('#entity').inputValue(),'104','Searches supported aliases and keeps exact IDs');
  await trigger.click();await search.fill('Supplier 2');
  await search.evaluate(e=>e.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',isComposing:true,bubbles:true})));
  assert.equal(await page.locator('#entity').inputValue(),'104','IME confirmation does not select prematurely');await search.press('Escape');
  await trigger.click();await list.getByRole('option',{name:'Not specified',exact:true}).click();
  assert.equal(await page.locator('#entity').inputValue(),'');
  assert.equal(await trigger.inputValue(),'','Unselected trigger does not display Not specified as a saved choice');
  assert.equal(await trigger.getAttribute('placeholder'),'Select Supplier');
  assert.equal(await page.locator('#entity option[value=""]').textContent(),'Not specified','Optional clear stays inside original options');
  await trigger.press('Enter');await search.press('End');await search.press('Enter');assert.equal(await page.locator('#entity').inputValue(),'113');
  await trigger.click();await search.fill('Supplier 6');assert(await list.getByRole('option',{name:'Supplier 6',exact:true}).isDisabled());await search.press('Escape');
  await page.locator('#save').click();assert.equal(await page.evaluate(()=>posts.length),0);
  assert(await page.locator('#details').getAttribute('open')!==null,'Required field reveals accordion');
  assert.equal(await page.locator('#required-search').getAttribute('aria-invalid'),'true');
  assert.equal(await page.locator('.sf-validation-summary').count(),0);
  await page.locator('#required-search').click();assert.equal(await page.locator('#required-options [role=option]').count(),2,'Required selector cannot clear');
  await page.locator('#required-options').getByRole('option',{name:'Karnataka registration'}).click();
  await page.locator('#save').click();assert.equal((await page.evaluate(()=>posts.at(-1))).registrationId,'29');
  assert(!(await page.evaluate(()=>posts.at(-1))).hasOwnProperty('entity-search'),'Transient search is never posted');
  await page.evaluate(()=>{entity.add(new Option('Newly authorized supplier','850'));entity.value='850';});
  await page.waitForFunction(()=>document.getElementById('entity-search').value==='Newly authorized supplier');
  assert(await page.locator('#locked-search').isDisabled());
  await page.evaluate(()=>entity.setAttribute('readonly',''));await trigger.waitFor({state:'visible'});assert.equal(await trigger.getAttribute('aria-disabled'),'true');
  await trigger.evaluate(e=>e.click());assert.equal(await trigger.getAttribute('aria-expanded'),'false');await page.evaluate(()=>entity.removeAttribute('readonly'));
  await trigger.click();await page.locator('#outside').click();assert.equal(await trigger.getAttribute('aria-expanded'),'false');
  await page.locator('#reset').click();await page.waitForFunction(()=>document.getElementById('entity-search').value==='Supplier 3');
  await page.evaluate(()=>document.getElementById('dialog').showModal());
  await page.locator('#modal-search').click();await page.locator('#modal-options').getByRole('option',{name:'Stock room'}).click();
  assert.equal(await page.locator('#modal').inputValue(),'48');assert.equal(await page.locator('#modal-search').evaluate(e=>e===document.activeElement),true);
  await page.locator('#dialog').getByRole('button',{name:'Close dialog'}).click();
  for(const [width,height]of [[1920,1080],[1366,768],[1280,600],[1280,650],[1280,720],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
   await page.setViewportSize({width,height});await trigger.scrollIntoViewIfNeeded();await trigger.click();
   const box=await page.locator('#entity-panel').boundingBox();assert(box.x>=0&&box.y>=0&&box.x+box.width<=width+1&&box.y+box.height<=height+1,'Popover stays in viewport '+width);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal overflow '+width);
   if(width===1280||width===320)await page.screenshot({path:path.join(screenshots,'shared-select-'+width+'x'+height+'.png')});
   await search.press('Home');await search.press('ArrowDown');await search.press('ArrowUp');await search.press('Escape');
  }
  for(const [module,phase]of [['Suppliers','supplier-address'],['Products','catalog'],['Purchases','purchasing'],['Sales','sales-expenses'],['Inventory','inventory'],['Reports','remaining']]){
   await page.goto('http://stockflow.test/'+module+'/Create');
   await page.evaluate(()=>{
    for(const id of ['authorized-list','variant-list','product-category','product-brand','part-product','bulk-product','serial-unit','productId','variantId','quickProductId']){const select=document.createElement('select');select.id=id;select.setAttribute('aria-label',id);for(let i=0;i<12;i++)select.add(new Option('Authorized option '+i,String(200+i)));document.getElementById('form').append(select);}
   });
   await page.waitForFunction(()=>document.getElementById('authorized-list').sfSelect);
   assert.equal(await page.locator('#authorized-list').getAttribute('data-sf-select-phase'),phase);
   assert.equal(await page.locator('#variant-list').evaluate(e=>!!e.sfSelect),true,'Local variant select uses shared dropdown in '+module);
   for(const id of ['product-category','product-brand'])assert.equal(await page.locator('#'+id).evaluate(e=>!!e.sfSelect),true,'Eligible category/brand filter uses existing shared component in '+module);
   for(const id of ['part-product','bulk-product','serial-unit','productId','variantId','quickProductId'])assert.equal(await page.locator('#'+id).evaluate(e=>!!e.sfSelect),true,'Local select uses shared dropdown: '+id);
   await page.locator('#authorized-list-search').click();await page.locator('#authorized-list-query').fill('option 9');
   await page.locator('#authorized-list-options').getByRole('option',{name:'Authorized option 9',exact:true}).click();
   assert.equal(await page.locator('#authorized-list').inputValue(),'209','Module selection preserves authorized ID in '+module);
  }
  await page.evaluate(()=>document.getElementById('entity').sfSelect.wrapper.style.cssText='position:fixed;bottom:12px;left:16px;right:16px;width:auto;z-index:1');
  await trigger.click();const flipped=await page.locator('#entity-panel').boundingBox(),anchor=await trigger.boundingBox();
  assert(flipped.y>=0&&flipped.y+flipped.height<=anchor.y+1,'Popover flips above a bottom-edge trigger');
  await search.press('Tab');assert.equal(await trigger.getAttribute('aria-expanded'),'false','Tab closes without trapping keyboard focus');
  await page.evaluate(()=>{
   const select=document.createElement('select');select.id='filter-default';select.setAttribute('aria-label','Supplier');select.add(new Option('All Suppliers',''));select.add(new Option('One supplier','1'));document.getElementById('form').append(select);StockFlowSelect.create(select);
  });
  assert.equal(await page.locator('#filter-default-search').getAttribute('placeholder'),'All Suppliers','Meaningful filter defaults remain explicit');
  await page.emulateMedia({forcedColors:'active'});assert.equal(await list.evaluate(e=>getComputedStyle(e).scrollbarWidth),'auto','High contrast keeps the system scrollbar');await page.emulateMedia({forcedColors:'none'});
  await page.goto('http://stockflow.test/OtherModule/Create');
  await page.evaluate(()=>{
   for(const count of [0,1,8,501]){
    const label=document.createElement('label'),source=document.createElement('select');source.id='count-'+count;label.htmlFor=source.id;label.textContent='Count '+count;
    for(let i=0;i<count;i++)source.add(new Option('Local option '+i,String(i)));
    document.getElementById('form').append(label,source);
   }
  });
  await page.waitForFunction(()=>document.getElementById('count-501').sfSelect);
  for(const count of [0,1,8,501])assert(await page.locator('#count-'+count).evaluate(e=>!!e.sfSelect),'All list lengths use shared UI');
  assert.equal(await page.locator('#count-8').getAttribute('data-sf-select-phase'),'shared','Unknown routes covered');
  await page.locator('#count-501-search').click();await page.locator('#count-501-query').fill('Local option 500');await page.getByRole('option',{name:'Local option 500',exact:true}).click();
  assert.equal(await page.locator('#count-501').inputValue(),'500','Large list searches original options without truncating values');
  await page.locator('#count-0-search').click();await page.locator('#count-0-options .sf-select-empty').waitFor();await page.keyboard.press('Escape');
  await page.evaluate(()=>document.getElementById('count-0').add(new Option('Loaded later','late')));
  await page.locator('#count-0-search').click();await page.getByRole('option',{name:'Loaded later',exact:true}).click();assert.equal(await page.locator('#count-0').inputValue(),'late');
  assert.deepEqual(errors,[]);console.log('PASS shared selectors: all single-select sizes/routes including short/empty/501-option lists, dynamic options, local product/variant selects, preserved multi-select semantics, approved design, validation, keyboard/IME, exact IDs/events, reset, disabled/readonly, modal focus, ten viewports at DPR 1.5, upward positioning and six module phases. No application database writes.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
