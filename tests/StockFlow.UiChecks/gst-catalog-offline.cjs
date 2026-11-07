// Rendered Razor + real local scripts/styles; APIs are mocked separately from the database checks.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),screens=path.join(root,'.artifacts/gst-catalog-responsive');fs.mkdirSync(screens,{recursive:true});
const tax={taxConfigId:1,productId:1,storeId:1,ruleId:1,hsnSac:'610910',uqc:'NOS',classification:'GOODS',treatment:'TAXABLE',taxRate:5,cessRate:0,reverseCharge:false,effectiveFrom:'2026-01-01',reviewReference:'Synthetic classification evidence'};
const rule={ruleId:1,ruleName:'Reviewed synthetic rule',description:'Synthetic reference only',tax,reviewStatus:'REVIEWED',status:'ACTIVE',productsAssigned:51};
const row={productId:1,productName:'Synthetic product',productCode:'P-001',sku:'P-001',category:'Subcategory',parentCategory:'Parent',productHsn:'610910',...tax,ruleName:rule.ruleName,mappingStatus:'Configured',mappingSource:'Product default',variantCount:2,overrideCount:0};
const settings={available:true,catalogAvailable:true,today:'2026-11-01',company:{defaultTaxMode:'EXCLUSIVE'},businesses:[{businessId:1,legalName:'Synthetic business',address:'Synthetic address',stateCode:'27',pinCode:'400001'}],registrations:[{registrationId:1,businessId:1,gstin:'27AAPFU0939F1ZV',registrationType:'REGULAR',stateCode:'27',effectiveFrom:'2026-01-01',status:'ACTIVE',address:'Synthetic address'}],mappings:[{mappingId:1,registrationId:1,businessProfileCode:'ELECTRICAL',storeId:1,effectiveFrom:'2026-01-01',status:'ACTIVE'}],taxes:[],products:[],shops:[{storeId:1,storeName:'Main store'}],history:[]};
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
 const page=await browser.newPage(),errors=[],writes=[],queries=[],uploads=[];let readonly=false,operationId='2674e879-9c2f-474f-aaee-4c6f8b228d15',committed=false,importFailure=false,commits=0;
 page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(12000);
 function operation(){return {operationId,status:committed?'COMPLETED':'PREVIEW',total:51,scopeLabel:'Main store / ELECTRICAL',counts:committed?{UPDATED:50,REVIEW:1}:{READY:50,REVIEW:1},selectionSummary:JSON.stringify({products:51,hsnValues:2,existingOverrides:1}),rows:[{row:2,productId:1,productName:row.productName,status:committed?'UPDATED':'READY',message:'Synthetic preview',beforeValue:'[]',proposedValue:JSON.stringify(tax)}]};}
 await page.route('**/*',async route=>{
  const req=route.request(),u=new URL(req.url()),p=u.pathname,json=b=>route.fulfill({contentType:'application/json',body:JSON.stringify(b)});queries.push(p+u.search);
  if(p==='/Admin/Gst'){let html=fs.readFileSync(path.join(root,'tests/bin/gst-ui-fixtures/settings.html'),'utf8');if(readonly)html=html.replace('data-edit="true"','data-edit="false"');return route.fulfill({contentType:'text/html',body:html});}
  if(p==='/api/gst/settings/states')return json({'27':'Maharashtra'});
  if(p==='/api/gst/settings')return json(settings);
  if(p==='/api/gst/settings/readiness')return json({ready:true,canActivate:true,checks:[]});
  if(p==='/api/gst/settings/catalog/options')return json({categories:[{id:1,name:'Parent'},{id:2,name:'Subcategory',parentId:1},{id:3,name:'Other parent'},{id:4,name:'Other child',parentId:3}],brands:[{id:1,name:'Brand'}],permissions:Object.fromEntries(['EDIT','APPROVE_TAX','OVERRIDE_TAX','BULK_TAX','IMPORT_TAX','EXPORT_TAX'].map(k=>[k,!readonly])),pricingScope:'Existing business-profile billing defaults.'});
  if(p==='/api/gst/settings/catalog')return json({total:51,rows:[row],summary:{total:51,configured:50,needsReview:1,missing:0}});
  if(p==='/api/gst/settings/catalog/rules')return json({total:1,rows:[rule]});
  if(p==='/api/gst/settings/catalog/history')return json({versions:[tax],audit:[]});
  if(p==='/api/gst/settings/catalog/preview'){writes.push(req.postDataJSON());committed=false;return json(operation());}
  if(p==='/api/gst/settings/catalog/import'){
   uploads.push(req.postDataBuffer());if(importFailure)return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:'The uploaded spreadsheet is malformed or unsupported.'})});committed=false;return json(operation());
  }
  if(p==='/api/gst/settings/catalog/commit'){commits++;committed=true;return json(operation());}
  if(p==='/api/gst/settings/catalog/template'||p==='/api/gst/settings/catalog/export')return route.fulfill({contentType:'text/csv',body:'ProductId,RuleId\r\n1,1\r\n'});
  if(p==='/api/gst/settings/catalog/operations')return json({total:0,rows:[]});
  if(p.startsWith('/api/gst/settings/catalog/operations/'))return json(operation());
  if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Stock'}],locationId:1,storeId:1});
  if(p==='/api/notifications')return json({unread:0,items:[]});if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 async function mapping(wizard=false){await page.goto('http://stockflow.test/Admin/Gst');if(wizard){await page.getByRole('button',{name:'Continue GST Setup',exact:true}).click();await page.locator('#gst-stepper').getByRole('button',{name:/Tax Configuration/}).click();}else await page.getByRole('button',{name:'Manage Tax Configuration',exact:true}).click();await page.locator('.gst-mapping-table').waitFor();}
 async function closeModal(){await page.locator('.gst-tax-dialog[open]>header button').click();if(await page.locator('#sf-confirm-modal[open]').count())await page.locator('#sf-confirm-accept').click();}
 for(const [width,height]of [[1920,1080],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await mapping(true);assert.equal(await page.locator('#gst-stepper button').count(),5);assert(await page.getByRole('button',{name:'Save & Continue',exact:true}).isVisible());
  assert.equal(await page.locator('.gst-mapping-table th').count(),6);assert(!await page.locator('.gst-selection-bar').isVisible());assert.equal(await page.getByRole('button',{name:'Apply Filters',exact:true}).count(),0);
  assert((await page.locator('.gst-configuration-status').textContent()).includes('50 of 51 configured'));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'Mapping overflow '+width);await page.screenshot({path:path.join(screens,'mapping-'+width+'x'+height+'.png'),fullPage:true});
  const size=await page.getByRole('button',{name:'Edit Mapping',exact:true}).evaluate(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height,svg:e.querySelector('svg').getBoundingClientRect().width}));assert.equal(size.width,26);assert.equal(size.height,26);assert(size.svg>=13&&size.svg<=14);
  await page.getByRole('button',{name:'Import',exact:true}).click();await page.getByRole('button',{name:'Browse Files',exact:true}).waitFor();assert(await page.getByRole('button',{name:'Validate File',exact:true}).isDisabled());
  const bounds=await page.locator('.gst-import-dialog').boundingBox(),foot=await page.locator('.gst-import-dialog>footer').boundingBox();assert(bounds.x>=0&&bounds.x+bounds.width<=width+1&&bounds.y>=0&&bounds.y+bounds.height<=height+1,'Import fits '+width+'x'+height);assert(foot.y+foot.height<=height+1,'Import footer reachable '+width+'x'+height);
  await page.screenshot({path:path.join(screens,'import-'+width+'x'+height+'.png'),fullPage:true});await closeModal();
  await page.getByRole('button',{name:'Tax Rules',exact:true}).click();await page.getByRole('button',{name:'+ Add Tax Rule',exact:true}).click();await page.locator('.gst-tax-dialog').waitFor({state:'visible'});assert(await page.locator('.gst-tax-dialog').evaluate(e=>e.getBoundingClientRect().width<=innerWidth));await closeModal();
  await page.getByRole('button',{name:'Billing Defaults',exact:true}).click();await page.getByText('Existing business-profile billing defaults.',{exact:true}).waitFor();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'Billing overflow '+width);
  console.log('PASS: compact mapping/import/rules/billing '+width+'x'+height);
 }
 await page.setViewportSize({width:1280,height:650});await mapping();
 await page.getByRole('button',{name:'View Tax Details',exact:true}).click();await page.locator('.gst-tax-details-table').waitFor();assert((await page.locator('.gst-tax-details-table').textContent()).includes('Pricing Behavior'));await closeModal();
 await page.locator('input[name=Search]').fill('helmet');await page.waitForTimeout(400);assert(queries.some(q=>q.includes('Search=helmet')),'Search debounces to server');
 await page.locator('select[name=ParentCategoryId]').selectOption('1');await page.waitForTimeout(150);assert.deepEqual(await page.locator('select[name=CategoryId] option').allTextContents(),['All subcategories','Subcategory']);
 await page.getByRole('button',{name:'Reset',exact:true}).click();assert.equal(await page.locator('input[name=Search]').inputValue(),'');await page.waitForTimeout(150);
 await page.getByRole('checkbox',{name:'Select current page',exact:true}).check();await page.getByRole('button',{name:'Select all 51 matching filtered products',exact:true}).click();await page.locator('#sf-confirm-accept').click();
 await page.getByText('51 products / variants selected across all matching pages',{exact:true}).waitFor();await page.getByRole('button',{name:'Assign Tax',exact:true}).first().click();await page.locator('dialog[open] select[name=ruleId]').selectOption('1');await page.getByRole('button',{name:'Preview Changes',exact:true}).click();await page.getByRole('button',{name:'Confirm Tax Assignment',exact:true}).waitFor();assert.equal(writes[0].allMatching,true);assert.equal(writes[0].targets.length,0);
 await page.getByLabel('I reviewed the classification, conditions and applicability to these products.',{exact:true}).check();await page.getByRole('button',{name:'Confirm Tax Assignment',exact:true}).click();await page.locator('#sf-confirm-accept').click();await page.getByRole('heading',{name:'Tax Assignment Results',exact:true}).waitFor();await closeModal();
 await page.getByRole('button',{name:'Import',exact:true}).click();
 const download=page.waitForEvent('download');await page.getByRole('button',{name:'Download CSV Template (.csv)',exact:true}).click();assert((await download).suggestedFilename().endsWith('.csv'));assert(queries.some(q=>q.includes('/catalog/template?')&&q.includes('format=csv')));
 const csv={name:'tax-mapping.csv',mimeType:'text/csv',buffer:Buffer.from('ProductId,RuleId\r\n1,1\r\n')};
 const chooserEvent=page.waitForEvent('filechooser');await page.getByRole('button',{name:'Browse Files',exact:true}).click();await (await chooserEvent).setFiles(csv);assert((await page.locator('.gst-selected-file').textContent()).includes('tax-mapping.csv'));
 assert(!await page.getByRole('button',{name:'Validate File',exact:true}).isDisabled());await page.getByRole('button',{name:'Remove File',exact:true}).click();assert(await page.getByRole('button',{name:'Validate File',exact:true}).isDisabled());
 await page.locator('#gst-tax-import-file').setInputFiles({name:'bad.txt',mimeType:'text/plain',buffer:Buffer.from('bad')});await page.getByText('Choose a CSV or XLSX file.',{exact:true}).waitFor();assert(await page.getByRole('button',{name:'Validate File',exact:true}).isDisabled());
 await page.locator('#gst-tax-import-file').setInputFiles({name:'empty.csv',mimeType:'text/csv',buffer:Buffer.alloc(0)});await page.getByText('The selected file is empty.',{exact:true}).waitFor();
 const data=await page.evaluateHandle(()=>{const transfer=new DataTransfer();transfer.items.add(new File(['ProductId,RuleId\r\n1,1\r\n'],'dropped.csv',{type:'text/csv'}));return transfer;});await page.locator('.gst-file-drop').dispatchEvent('drop',{dataTransfer:data});assert((await page.locator('.gst-selected-file').textContent()).includes('dropped.csv'));
 const changeEvent=page.waitForEvent('filechooser');await page.getByRole('button',{name:'Change File',exact:true}).click();await (await changeEvent).setFiles(csv);
 importFailure=true;await page.getByRole('button',{name:'Validate File',exact:true}).click();await page.locator('.gst-import-dialog .gst-catalog-inline-error').filter({hasText:'malformed'}).waitFor();assert.equal(commits,1,'Validation never commits mappings');importFailure=false;
 await page.getByRole('button',{name:'Validate File',exact:true}).click();await page.getByRole('heading',{name:'Import Preview',exact:true}).waitFor();assert(uploads.at(-1).includes(Buffer.from('ProductId,RuleId')),'Actual multipart file contents reach import');assert.equal(commits,1);
 await page.locator('select[name=preview-status]').selectOption('invalid');await page.waitForTimeout(150);assert(queries.some(q=>q.includes('status=invalid')));
 await page.getByLabel('I reviewed the classification, conditions and applicability to these products.',{exact:true}).check();await page.getByRole('button',{name:'Confirm Import',exact:true}).click();await page.locator('#sf-confirm-accept').click();await page.getByRole('heading',{name:'Tax Mapping Import Results',exact:true}).waitFor();await page.getByRole('link',{name:'Download Result Report',exact:true}).waitFor();assert.equal(commits,2);await closeModal();
 await page.locator('.gst-more-menu>summary').click();await page.getByRole('button',{name:'Export Tax Mapping',exact:true}).click();await page.locator('select[name=scope]').selectOption('all');await page.locator('select[name=format]').selectOption('csv');const exportDownload=page.waitForEvent('download');await page.getByRole('button',{name:'Download Export',exact:true}).click();await exportDownload;
 readonly=true;await mapping();assert.equal(await page.getByRole('button',{name:'Assign Tax',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:'Import',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:'Edit Mapping',exact:true}).count(),0);
 assert.deepEqual(errors,[]);console.log('PASS: native chooser, CSV download, change/remove/drop, invalid/empty files, server errors, preview before commit, results, export, filtering, selection and read-only access.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
