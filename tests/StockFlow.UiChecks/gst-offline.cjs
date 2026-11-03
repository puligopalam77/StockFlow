const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),fixtures=path.join(root,'tests/bin/gst-ui-fixtures'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),screens=path.join(root,'.artifacts/gst-responsive');
fs.mkdirSync(screens,{recursive:true});
const registration={registrationId:1,businessId:1,gstin:'27AAPFU0939F1ZV',registrationType:'REGULAR',stateCode:'27',effectiveFrom:'2026-01-01',effectiveTo:null,status:'ACTIVE',address:'Synthetic street'};
const business={businessId:1,legalName:'Synthetic retailer',pan:'AAPFU0939F',address:'Synthetic street',stateCode:'27',pinCode:'400001'};
const products=[{productId:1,productName:'Synthetic clothing item',hsnCode:'610910',unitOfMeasure:'PIECE',taxPercentage:18}];
const states={'27':'Maharashtra','29':'Karnataka'};
const documents=Array.from({length:11},(_,i)=>({documentId:i+1,registrationId:1,documentNumber:'GST-'+(i+1),documentDate:'2026-11-01',businessProfileCode:'ELECTRICAL',storeId:1,partyName:'Synthetic customer',partyGstin:null,placeOfSupply:'29',taxableValue:100,cgst:0,sgst:0,utgst:0,igst:18,cess:0,totalGst:18,total:118,invoiceDiscount:0,kind:'INVOICE',direction:'SALES',reviewStatus:'PENDING_REVIEW',sourceStatus:'COMPLETED',warnings:['WORKING_REGISTER_PRACTITIONER_REVIEW'],businessSnapshot:JSON.stringify({legal_name:'Synthetic retailer'}),registrationSnapshot:JSON.stringify({gstin:registration.gstin,address:'Synthetic street'}),linesJson:JSON.stringify([{description:'Synthetic goods',hsnSac:'610910',uqc:'NOS',quantity:1,taxRate:18,taxableValue:100,cgst:0,sgst:0,utgst:0,igst:18,cess:0,total:118}])}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[],queries=[],savedNotes=[];let rejectSave=false;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async route=>{const req=route.request(),url=new URL(req.url()),p=url.pathname,json=b=>route.fulfill({contentType:'application/json',body:JSON.stringify(b)});
  if(p==='/Admin/Gst'||p==='/Reports/Gst')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,p.includes('Admin')?'settings.html':'reports.html'),'utf8')});
  if(p==='/api/gst/settings/states')return json(states);
  if(p==='/api/gst/documents'&&req.method()==='POST'){assert(req.headers()['x-csrf-token']);savedNotes.push(req.postDataJSON());return json({id:2});}
  if(p==='/api/gst/settings/businesses'&&req.method()==='POST'){assert(req.headers()['x-csrf-token']);if(rejectSave)return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:'Synthetic backend rejection'})});return json({id:2});}
  if(p==='/api/gst/settings')return json({available:true,company:{companyName:'Synthetic retailer',address:'Synthetic street',pinCode:'400001',state:'Maharashtra',panNumber:'AAPFU0939F'},businesses:[business],registrations:[registration],mappings:[{mappingId:1,registrationId:1,businessProfileCode:'ELECTRICAL',storeId:1,effectiveFrom:'2026-01-01',status:'ACTIVE'}],taxes:[],shops:[{storeId:1,storeName:'Main store'}],products,history:[]});
  if(p==='/api/gst/registrations')return json([registration]);
  if(p==='/api/gst/source-options')return json({products,suppliers:[{supplierId:1,supplierName:'Synthetic supplier'}],purchases:[{purchaseId:1,supplierId:1,purchaseNumber:'PUR-1'}]});
  if(p==='/api/gst/export-history'||p.endsWith('/reviews'))return json([]);
  if(p.startsWith('/api/gst/documents/'))return json(documents[0]);
  if(p==='/api/gst/reports'){queries.push(url.search);return json({gstin:registration.gstin,scopeLabel:'Selected shop/profile — partial registration view',count:11,summary:{taxableSales:1100,outputGst:198,recordedPurchaseGst:0,noteAdjustments:0,pendingReview:11},legacySalesWithoutSnapshots:2,documents:documents.slice(0,10),hsn:[]});}
  if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main',locationName:'Stock'}],locationId:1,storeId:1});
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));if(file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.fulfill({status:404,body:''});
 });
 for(const [width,height]of [[1920,1080],[1366,768],[1280,650],[1024,768],[768,1024],[390,844],[320,640]]){
  await page.setViewportSize({width,height});
  for(const [route,name]of [['/Reports/Gst','reports']]){
   await page.goto('https://stockflow.test'+route);await page.waitForTimeout(200);
   await page.getByRole('button',{name:'View',exact:true}).first().waitFor();
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),`${name} page overflows at ${width}`);
   await page.screenshot({path:path.join(screens,`${name}-${width}x${height}.png`),fullPage:true});
  }
 }
 await page.setViewportSize({width:1280,height:650});
 await page.goto('https://stockflow.test/Reports/Gst');await page.getByRole('button',{name:'View',exact:true}).first().click();await page.locator('#gst-document-detail').waitFor({state:'visible'});assert((await page.locator('#gst-document-detail').innerText()).includes('610910'));
 const size=await page.getByRole('button',{name:'View',exact:true}).first().evaluate(n=>({width:n.getBoundingClientRect().width,height:n.getBoundingClientRect().height,icon:n.querySelector('svg').getBoundingClientRect().width}));assert.equal(size.width,26);assert.equal(size.height,26);assert(size.icon>=13&&size.icon<=14,'Compact GST row action uses shared dimensions');
 await page.getByRole('button',{name:'Close document'}).click();assert(await page.locator('#gst-document-detail').isHidden());
 assert(queries.every(q=>new URLSearchParams(q).get('size')==='10'),'Report defaults to shared ten-row page size');
 documents[0].linesJson=JSON.stringify([{sourceLineId:77,productId:null,isManual:true,description:'Original manual cord',hsnSac:'610910',uqc:'NOS',classification:'GOODS',treatment:'TAXABLE',quantity:1,taxRate:18,taxableValue:100,cgst:0,sgst:0,utgst:0,igst:18,cess:0,total:118}]);
 await page.locator('#gst-record-panel summary').click();await page.locator('#gst-entry-kind').selectOption('CREDIT_NOTE');await page.locator('#gst-entry-parent').fill('1');
 await page.getByRole('button',{name:'Use original invoice lines',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#gst-entry-lines tr')?.dataset.manualSource==='77');
 assert.equal(await page.locator('#gst-entry-lines [name=productId]').inputValue(),'manual');assert(await page.locator('#gst-entry-lines [name=productId]').isDisabled());
 await page.locator('#gst-entry-number').fill('CN-MANUAL-1');await page.locator('#gst-entry-evidence').fill('Original invoice and return');await page.locator('#gst-entry-reason').fill('Accepted manual goods return');
 const invalid=await page.locator('#gst-record-form').evaluate(form=>[...form.elements].filter(e=>e.willValidate&&!e.validity.valid).map(e=>({id:e.id,name:e.name,value:e.value,message:e.validationMessage})));
 assert.deepEqual(invalid,[],'Original manual line form must be valid');
 const recorded=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/gst/documents'&&r.request().method()==='POST');
 await page.locator('#gst-record-form').evaluate(form=>form.requestSubmit());await recorded;
 assert.equal(savedNotes.length,1);assert.equal(savedNotes[0].lines[0].sourceLineId,77);assert.equal(savedNotes[0].lines[0].productId,null);assert.equal(savedNotes[0].lines[0].isManual,true);
 console.log('PASS: manual GST note loads the original snapshot and preserves its non-catalog source identity.');
 assert.deepEqual(errors,[],'No browser runtime errors');console.log('PASS: GST report layouts at seven viewports; contained horizontal tables, document drilldown, compact row actions and shared paging. Mocked browser APIs; not a database test.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
