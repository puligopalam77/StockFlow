const assert=require('node:assert/strict');
module.exports=async page=>{
 const input=page.locator('#sale-scan'),host=page.locator('#product-results'),status=page.locator('#product-result-status');
 const product={productId:1,variantId:1,productName:'Cable / Red',sku:'CABLE-RED',sellingPrice:100,mrp:120,taxPercentage:18,currentStock:3,isActive:true,isAvailable:true,isSerialTracked:false};
 const held=new Map(),responses=[];let queries=0;
 await page.route('**/api/products/variants?**',async route=>{
  const term=new URL(route.request().url()).searchParams.get('term');queries++;
  if(term.startsWith('slow'))await new Promise(resolve=>held.set(term,resolve));
  try{await route.fulfill(term==='slow-failure'?{status:500,json:{message:'Old search failed'}}:{json:term==='none'?[]:Array.from({length:7},(_,i)=>({...product,productName:term+' '+i,sku:term+'-'+i}))});responses.push(term);}catch(error){if(!page.isClosed())throw error;}
 });
 await page.route('**/api/scan?**',route=>new URL(route.request().url()).searchParams.get('code')==='fallback'?route.fulfill({status:404,json:{message:'No exact barcode'}}):route.fallback());
 async function lookup(term){await input.fill(term);await page.waitForFunction(t=>document.querySelector('#product-result-status').textContent.includes('7 matching')&&!document.querySelector('#product-results').hidden&&document.querySelector('#product-results strong').textContent.startsWith(t),term);}
 async function closed(text=''){
  assert.equal(await input.inputValue(),text);assert(await host.isHidden());assert.equal(await host.locator('.billing-result').count(),0);assert.equal(await status.textContent(),'');assert.equal(await input.getAttribute('aria-expanded'),'false');
 }
 async function startSlow(term){await input.fill(term);await page.waitForRequest(r=>new URL(r.url()).pathname==='/api/products/variants'&&new URL(r.url()).searchParams.get('term')===term);}
 async function release(term){held.get(term)();await new Promise(resolve=>{const check=()=>responses.includes(term)?resolve():setTimeout(check,10);check();});await page.waitForTimeout(40);}
 await closed();await lookup('helmet');await input.press('ArrowDown');assert.equal(await host.locator('.highlighted').count(),1);
 await input.fill('');await closed();await input.fill('   ');await closed('   ');
 await startSlow('slow-cleared');await input.fill('');await closed();await release('slow-cleared');await closed();
 await startSlow('slow-old');await lookup('new');await release('slow-old');assert((await host.locator('strong').first().textContent()).startsWith('new'));
 await startSlow('slow-failure');await input.fill('');await release('slow-failure');await closed();
 await lookup('escape');await input.press('Escape');await closed('escape');
 await lookup('outside');await page.locator('.sf-page-heading h1').click();await closed('outside');
 const before=queries;await input.fill('cancelled-before-debounce');await page.locator('.sf-page-heading h1').click();await page.waitForTimeout(350);assert.equal(queries,before);await closed('cancelled-before-debounce');
 await startSlow('slow-escape');await input.press('Escape');await release('slow-escape');await closed('slow-escape');
 await startSlow('slow-outside');await page.locator('.sf-page-heading h1').click();await release('slow-outside');await closed('slow-outside');
 await lookup('selected');await input.press('ArrowDown');await input.press('Enter');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1);await closed();
 await lookup('mouse');await host.locator('button').first().click();await page.waitForFunction(()=>document.querySelector('.billing-quantity input').value==='2');await closed();
 await input.fill('CABLE-RED');await input.press('Enter');await page.waitForFunction(()=>document.querySelector('.billing-quantity input').value==='3');await closed();
 // A failed exact barcode retains its query for the existing text-search fallback.
 await input.fill('fallback');await input.press('Enter');await page.waitForFunction(()=>document.querySelector('#product-result-status').textContent.includes('7 matching'));assert.equal(await input.inputValue(),'fallback');await input.fill('');await closed();
 await input.fill('none');await page.waitForFunction(()=>document.querySelector('#product-result-status').textContent.includes('No products'));await input.fill('');await closed();
 // Starting a fresh bill and restoring the held bill reset transient lookup state.
 await page.locator('#customer-search').fill('Counter');await page.locator('#customer-results [role=option]').first().waitFor();await page.locator('#customer-search').press('Enter');
 await page.locator('#hold-bill').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===0&&!document.querySelector('#sale-editor').disabled);await closed();
 await lookup('before-restore');await page.locator('#show-held').click();await page.locator('.billing-saved-card').getByRole('button',{name:/Resume Bill/}).first().click();
 await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open&&document.querySelectorAll('#sale-cart tr').length===1);await closed();assert.equal(await page.locator('.billing-quantity input').inputValue(),'3');
 await page.locator('#billing-workspace').evaluate(n=>n.dataset.canScan='false');await input.fill('search-only');await input.press('Enter');await page.waitForFunction(()=>document.querySelector('#product-result-status').textContent.includes('7 matching'));assert.equal(await input.inputValue(),'search-only');
 await page.locator('#billing-workspace').evaluate(n=>n.dataset.canScan='true');
 await lookup('before-clear-bill');await page.locator('#clear-bill-items').click();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===0);await closed();
 console.log('PASS product-search state: immediate clear, whitespace, delayed/out-of-order/error responses, Escape/outside cancellation, debounce cancellation, mouse/keyboard Add, repeated barcode scan, text fallback, new bill and restored draft; bill quantities preserved.');
};
