// Render real Razor pages with mocked APIs; never connects to the application database.
const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'../..'),staticRoot=path.join(root,'src/StockFlow.Web/wwwroot');
const {chromium}=require(path.join(__dirname,'bin/Debug/net10.0/.playwright/package'));
const fixtures=path.join(root,'tests/bin/variant-ui-fixtures'),base='http://localhost:51997';
const barcodeWorkflow=process.argv.includes('--barcode-workflow');
const variants=[{productId:1,variantId:11,productName:'Shirt / Black M',variantName:'Black M',sku:'M',sellingPrice:120,purchasePrice:50,taxPercentage:5,currentStock:10,isActive:true,attributes:{Size:'M'},barcode:'SFV00000011'},
{productId:1,variantId:12,productName:'Shirt / Black L',variantName:'Black L',sku:'L',sellingPrice:150,purchasePrice:60,taxPercentage:12,currentStock:20,isActive:true,attributes:{Size:'L'},manufacturerBarcode:'0123456789012'}];
const purchase={purchaseId:1,purchaseNumber:'PUR-1',supplierName:'Supplier',supplierId:1,purchaseDate:'2026-09-30',status:'DRAFT',items:[{...variants[0],purchaseItemId:1,quantity:10,receivedQuantity:0,serialNumbers:[],lineTotal:525}],subtotal:500,taxAmount:25,discountAmount:0,totalAmount:525};
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));let saved,receipt,quote;const labelPosts=[];let itemLabels=barcodeWorkflow?[]:[1,2,3].map(n=>({labelId:n,intakeId:8,itemNumber:n,variantId:11,productName:"Shirt / Black M",internalBarcode:"SFI8X"+n,quantity:1,sellingPrice:120}));
 await page.addInitScript(()=>window.print=()=>window.printedCodes=[...document.querySelectorAll("#variant-labels svg")].map(node=>node.getAttribute("aria-label")));
 await page.route('**/*',async route=>{
  const request=route.request(),url=new URL(request.url()),p=url.pathname;
  const json=data=>route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  if(p==='/api/branches/context')return json({canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}],locationId:1,storeId:1});
  if(p.includes('/item-labels')){if(request.method()==='POST'){labelPosts.push('generate');if(barcodeWorkflow)itemLabels=[1,2,3].map(n=>({labelId:n,itemNumber:n,variantId:11,productName:'Shirt / Black M',internalBarcode:'SFL'+n,quantity:1,sellingPrice:120}));}return json(p.includes('/12/')?[]:itemLabels);}
  if(/\/variants\/(11|12)\/(reprint|print)$/.test(p)){labelPosts.push(request.postDataJSON());return json({});}
  if(p==='/api/products/variants'){if(request.method()==='POST'){saved=request.postDataJSON();return json({variantId:13});}return json(variants);}
  if(p==='/api/products')return json([{productId:1,productName:'Shirt',hasVariants:true,isActive:true}]);
  if(p==='/api/suppliers')return json([{supplierId:1,supplierName:'Supplier'}]);
  if(p==='/api/purchases/1')return json(purchase);
  if(p==='/api/purchases/1/receipts'){if(request.method()==='POST')receipt=request.postDataJSON();return json(request.method()==='POST'?{receiptId:1}:[]);}
  if(p==='/api/scan')return json({...variants[['L','0123456789012'].includes(url.searchParams.get('code'))?1:0],isAvailable:true,matchType:'PRODUCT_BARCODE'});
  if(p==='/api/sales/quote'){quote=request.postDataJSON();return json({subtotal:0,taxAmount:0,discountAmount:0,totalAmount:0,items:quote.items.map(i=>({taxPercentage:5,lineTotal:0}))});}
  if(p.startsWith('/api/'))return json([]);
  const fixture=path.join(fixtures,p.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(fixture))return route.fulfill({contentType:'text/html',path:fixture});
  const file=path.resolve(staticRoot,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  if(file.startsWith(staticRoot+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file,contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':undefined});
  return route.fulfill({status:404,body:''});
 });
 await page.goto(base+'/Products/Variants');await page.locator('#variant-rows tr').first().waitFor();
 if(process.argv.includes('--barcode-sale-scan')){
  await page.goto(base+'/Sales/Create');
  for(const code of ['SFL1','SFL1','0123456789012']){
   await page.locator('#sale-scan').fill(code);await page.locator('#scan-sale').click();await page.waitForTimeout(350);
  }
  assert.equal(await page.locator('#sale-cart tr').count(),2);
  assert.equal(quote.items[0].variantId,11);assert.equal(quote.items[0].quantity,2);
  assert.equal(quote.items[1].variantId,12);assert.equal(quote.items[1].quantity,1);
  assert.deepEqual(errors,[]);console.log('PASS: New Sale accepts generated and manufacturer codes, repeated scans increase quantity, and distinct variants stay on separate bill lines. Mock APIs only.');return;
 }
 await page.getByRole('button',{name:'Labels',exact:true}).first().click();
 if(barcodeWorkflow){
  await page.waitForSelector('#label-dialog[open]');
  assert.equal(labelPosts.length,0,'opening does not generate barcodes');
  assert.equal(await page.locator('#variant-labels svg').count(),0);
  assert(await page.locator('#label-generate').isVisible());
  assert(await page.locator('#label-print').isHidden());
  await page.evaluate(()=>{document.querySelector('#label-generate').click();document.querySelector('#label-generate').click();});
  await page.waitForFunction(()=>document.querySelectorAll('#variant-labels svg').length===3);
  assert.equal(labelPosts.filter(x=>x==='generate').length,1,'repeated clicks issue one generation request');
  assert.deepEqual(await page.locator('#variant-labels svg').evaluateAll(nodes=>nodes.map(n=>n.getAttribute('aria-label'))),['SFL1','SFL2','SFL3']);
  assert((await page.locator('#label-item-list').textContent()).includes('Existing stock'),'legacy stock caption does not claim a receipt');
  await page.locator('#label-select-none').click();await page.getByRole('checkbox',{name:'Select SFL2',exact:true}).check();
  await page.locator('#label-print').click();await page.waitForFunction(()=>window.printedCodes?.length===1);
  assert.deepEqual(labelPosts.at(-1),{labelIds:[2]});
  await page.locator('[data-close-label]').first().click();await page.getByRole('button',{name:'Reprint',exact:true}).first().click();
  await page.locator('#label-print').click();await page.waitForFunction(()=>window.printedCodes?.length===3);
  assert.equal(labelPosts.filter(x=>x==='generate').length,1,'reprint never replaces codes');
  await page.locator('[data-close-label]').first().click();await page.getByRole('button',{name:'Labels',exact:true}).nth(1).click();
  await page.waitForFunction(()=>document.querySelector('#label-source').value==='existing');
  assert(await page.locator('#label-generate').isHidden());
  assert((await page.locator('#label-help').textContent()).includes('0123456789012'));
  await page.locator('#label-count').fill('2');await page.locator('#label-print').click();
  await page.waitForFunction(()=>window.printedCodes?.length===2);
  assert.deepEqual(await page.evaluate(()=>window.printedCodes),['0123456789012','0123456789012']);
  assert.deepEqual(labelPosts.at(-1),{existingBarcode:'0123456789012',copies:2});
  await page.locator('#label-source').selectOption('unique');
  assert(await page.locator('#label-generate').isVisible());assert(await page.locator('#label-print').isHidden());
  assert.deepEqual(errors,[]);console.log('PASS: explicit unique generation for existing stock, repeat-click protection, specific/all reprinting, supplied manufacturer barcode display/printing, and source selection. Mock APIs only.');return;
 }
 await page.waitForFunction(()=>document.querySelectorAll('.stock-label svg').length===3);
 const codes=await page.locator('#variant-labels svg').evaluateAll(nodes=>nodes.map(n=>n.getAttribute('aria-label')));assert.equal(new Set(codes).size,3);
 assert(await page.locator('#label-copy-controls').isHidden());
 await page.locator('#label-select-none').click();assert(await page.locator('#label-print').isHidden());
 await page.getByRole('checkbox',{name:'Select SFI8X2',exact:true}).check();await page.locator('#label-print').click();
 await page.waitForFunction(()=>window.printedCodes?.length===1);assert.deepEqual(await page.evaluate(()=>window.printedCodes),['SFI8X2']);assert.deepEqual(labelPosts.at(-1),{labelIds:[2]});
 await page.locator('[data-close-label]').first().click();await page.getByRole('button',{name:'Reprint',exact:true}).first().click();
 await page.locator('#label-select-all').click();await page.locator('#label-print').click();await page.waitForFunction(()=>window.printedCodes?.length===3);
 assert.deepEqual(await page.evaluate(()=>window.printedCodes),codes);assert.equal(labelPosts.filter(x=>x==='generate').length,0,'opening and reprinting read saved labels without regenerating');
 if(process.argv.includes('--labels-only')){assert.deepEqual(errors,[]);console.log('PASS: three distinct item barcodes, specific selection, select all, stable reprinting, and saved label IDs submitted for printing. Mock APIs only.');return;}
 await page.locator('[data-close-label]').first().click();await page.locator('#variant-new').click();await page.locator('#variant-name').fill('White M');await page.locator('#variant-sku').fill('WHITE-M');await page.locator('#attribute-add').click();await page.getByRole('textbox',{name:'Attribute name',exact:true}).fill('Size');await page.getByRole('textbox',{name:'Attribute value',exact:true}).fill('M');await page.locator('#attribute-add').click();await page.getByRole('textbox',{name:'Attribute name',exact:true}).last().fill('Color');await page.getByRole('textbox',{name:'Attribute value',exact:true}).last().fill('White');await page.getByRole('button',{name:'Save Variant',exact:true}).click();await page.waitForTimeout(100);assert.equal(saved.attributes.Size,'M');assert.equal(saved.productId,1);
 await page.goto(base+'/Purchases/Create');await page.locator('#purchase-fields:not([disabled])').waitFor();
 const select=page.locator('#purchase-items select').first();await select.selectOption('12');assert.equal(await page.getByRole('spinbutton',{name:'Purchase cost *',exact:true}).inputValue(),'60');
 await page.goto(base+'/Sales/Create');
 for(const value of ['M','M','L']){await page.locator('#sale-scan').fill(value);await page.locator('#scan-sale').click();await page.waitForTimeout(350);}
 assert.equal(await page.locator('#sale-cart tr').count(),2);assert.equal(quote.items[0].variantId,11);assert.equal(quote.items[0].quantity,2);assert.equal(quote.items[1].variantId,12);
 await page.goto(base+'/Purchases/Details/1');await page.locator('#receipt-form:visible').waitFor();await page.locator('#receive-1').fill('4');await page.locator('#receipt-submit').click();await page.waitForTimeout(150);assert.equal(receipt.items[0].quantity,4);assert.ok(receipt.requestId);
 assert.deepEqual(errors,[]);console.log('PASS: variant editor, attributes, Code 128 labels, purchase prices, repeated scans, separate variant cart lines, and partial receipt submission.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});

