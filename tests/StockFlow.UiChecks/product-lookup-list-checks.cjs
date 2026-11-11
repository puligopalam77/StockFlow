const assert=require('node:assert/strict'),fs=require('fs'),path=require('path');
module.exports=async(page,out)=>{
 const base={sellingPrice:100,mrp:120,taxPercentage:18,currentStock:8,isActive:true,isAvailable:true,isSerialTracked:false,brandName:'Vega',categoryName:'Helmets',minimumStockLevel:1};
 const products=[
  {...base,productId:1,variantId:11,productName:'Retail Helmet / DEFAULT',variantName:'DEFAULT',sku:'HEL-001',hasProductImage:true},
  {...base,productId:1,variantId:12,productName:'Retail Helmet / XS / Black',variantName:'XS / Black',sku:'HEL-001-XS-BLK',hasProductImage:true},
  {...base,productId:2,variantId:21,productName:'Long registered helmet name with extended safety specifications and accessories '.repeat(4),variantName:'XL / Red',sku:'LONG-SKU-XL-RED-123456789',currentStock:1,locationMinimumStockLevel:2,hasProductImage:false},
  {...base,productId:3,variantId:31,productName:'Helmet with missing image file',sku:'HEL-003',hasProductImage:true},
  {...base,productId:4,variantId:41,productName:'Out of stock helmet',sku:'HEL-004',currentStock:0,isAvailable:false},
  {...base,productId:5,variantId:51,productName:'Inactive helmet',sku:'HEL-005',isActive:false,isAvailable:false},
  {...base,productId:6,variantId:61,productName:'Unavailable helmet',sku:'HEL-006',isAvailable:false},
  {...base,productId:7,variantId:71,productName:'Serial tracked helmet',sku:'HEL-007',isSerialTracked:true}
 ];
 const terms=[],quotes=[];let scanRequests=0;
 page.on('request',request=>{const url=new URL(request.url());if(url.pathname==='/api/sales/quote')quotes.push(request.postDataJSON());if(url.pathname==='/api/scan')scanRequests++;});
 await page.route('**/api/products/variants?**',route=>{const term=new URL(route.request().url()).searchParams.get('term');terms.push(term);return route.fulfill({json:term==='none'?[]:term==='XS'?[products[1]]:products});});
 await page.route('**/api/products/1/image',route=>route.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="60" height="40"><rect width="60" height="40" fill="#b7c6da"/><circle cx="30" cy="20" r="15" fill="#203553"/></svg>'}));
 await page.route('**/api/products/3/image',route=>route.fulfill({status:404,body:''}));
 const input=page.locator('#sale-scan'),host=page.locator('#product-results');
 async function lookup(term='helmet'){await input.fill('');await input.fill(term);await page.waitForFunction(count=>!document.querySelector('#product-results').hidden&&document.querySelectorAll('#product-results .billing-result').length===count,term==='none'?0:term==='XS'?1:products.length);}
 await lookup();await page.waitForFunction(()=>!document.querySelectorAll('#product-results .billing-result')[3].querySelector('img'));
 assert.equal(await host.locator('.billing-result').nth(2).locator('img').count(),0,'Missing image uses existing fallback');
 assert.equal(await host.locator('.billing-result').nth(0).locator('img').getAttribute('src'),'/api/products/1/image');
 assert((await host.locator('.billing-result').nth(1).locator('.billing-result-identity small').textContent()).includes('XS / Black'));
 assert((await host.locator('.billing-result').nth(1).locator('.billing-result-identity small').textContent()).includes('HEL-001-XS-BLK'));
 const long=host.locator('.billing-result').nth(2);assert.equal(await long.locator('strong').first().getAttribute('title'),products[2].productName);assert((await long.locator('.billing-stock').textContent()).includes('Low stock'));
 for(const index of [4,5,6])assert(await host.locator('.billing-result').nth(index).locator('button').isDisabled());
 assert.equal(await host.locator('.billing-result').nth(4).locator('.billing-stock').textContent(),'Out of stock');
 const measurements=[];
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[430,800],[375,667],[320,640]])for(const collapsed of [false,true]){
  await page.setViewportSize({width,height});await page.evaluate(c=>document.body.classList.toggle('sf-nav-collapsed',c),collapsed);await page.waitForTimeout(300);
  const m=await host.evaluate(n=>{const r=x=>x.getBoundingClientRect().toJSON();return{host:r(n),clientHeight:n.clientHeight,scrollHeight:n.scrollHeight,zoom:visualViewport.scale,overflow:document.documentElement.scrollWidth>innerWidth+1,scrollX:n.scrollWidth>n.clientWidth+1,rows:[...n.children].map(row=>({row:r(row),image:r(row.querySelector('.billing-result-image')),button:r(row.querySelector('button')),identity:r(row.querySelector('.billing-result-identity')),price:r(row.querySelector('.billing-result-price')),nameFont:getComputedStyle(row.querySelector('.billing-result-identity strong')).fontSize,stockBackground:getComputedStyle(row.querySelector('.billing-stock')).backgroundColor,stock:r(row.querySelector('.billing-stock')),clipped:row.scrollWidth>row.clientWidth+1}))};});
  assert.equal(m.zoom,1);assert(m.host.height<=320&&m.scrollHeight>m.clientHeight);assert(m.clientHeight>=5*60,'Five full product rows fit');assert(!m.overflow&&!m.scrollX,JSON.stringify({width,collapsed,m}));
  for(const row of m.rows){assert.equal(row.row.height,60);assert.equal(row.button.height,32);assert(row.image.width>=32&&row.image.width<=36);assert.equal(row.image.height,row.image.width);assert.equal(row.nameFont,'13px');assert.equal(row.stockBackground,'rgba(0, 0, 0, 0)');assert(!row.clipped);assert(row.identity.right<=row.price.x&&row.price.right<=row.button.x);assert(Math.abs(row.image.y+row.image.height/2-row.button.y-row.button.height/2)<1);assert(row.stock.y>=row.price.y&&row.stock.bottom<=row.row.bottom);}
  measurements.push({width,height,collapsed,...m});
  if(width===1366&&!collapsed){await host.evaluate(n=>n.scrollTop=0);await host.scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,'compact-product-results-1366.png')});}
 }
 await page.setViewportSize({width:1366,height:768});await page.evaluate(()=>document.body.classList.remove('sf-nav-collapsed'));await lookup();
 await input.press('Escape');assert(await host.isHidden());await lookup('XS');assert.equal(await host.locator('.billing-result').count(),1);await input.press('ArrowDown');assert(await host.locator('.billing-result.highlighted').count()===1);const scansBefore=scanRequests;await input.press('Enter');
 await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1);await page.waitForFunction(()=>document.querySelector('#summary-total').textContent.endsWith('118.00'));assert.equal(scanRequests,scansBefore,'Keyboard result selection does not submit another barcode scan');assert.equal(quotes.at(-1)?.items[0]?.variantId,12,'Exact selected variant reaches existing quote API');
 await lookup();await input.press('ArrowDown');await input.press('ArrowDown');await input.press('ArrowUp');await input.press('Enter');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===2);await page.waitForFunction(()=>document.querySelector('#summary-total').textContent.endsWith('236.00'));assert(quotes.at(-1).items.some(i=>i.variantId===11));
 await lookup();await long.locator('button').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===3);await lookup();await long.locator('button').click();await page.waitForFunction(()=>document.querySelector('#sale-message').textContent.includes('Insufficient stock'));assert.equal(await page.locator('#sale-cart tr').count(),3,'Existing stock restriction prevents another low-stock unit');
 await lookup();await host.locator('.billing-result').last().locator('button').click();assert(await page.locator('#unit-picker').isVisible(),'Serial tracked products still require unit selection');assert.equal(await page.locator('#sale-cart tr').count(),3);
 await lookup('none');assert((await page.locator('#product-result-status').textContent()).includes('No products found'));assert(terms.includes('XS')&&terms.includes('none'));
 fs.writeFileSync(path.join(out,'compact-product-results-measurements.json'),JSON.stringify(measurements,null,2));
 console.log('PASS compact product lookup: rendered 60px rows/32px Add/32-36px thumbnails, 320px scroller, nine viewports and both sidebar states; variants, long names, valid/missing/broken images, stock restrictions, keyboard/escape/filtering and serial selection.');
};
