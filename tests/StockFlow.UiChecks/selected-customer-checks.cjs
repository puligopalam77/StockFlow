const assert=require('node:assert/strict'),path=require('path');
module.exports=async(page,out)=>{
 let record={customerId:10,customerName:'Ranga',mobileNumber:'9019725212'};
 await page.route('**/api/customers/search?**',r=>r.fulfill({contentType:'application/json',body:JSON.stringify([record])}));
 const search=page.locator('#customer-search'),add=page.getByRole('button',{name:'+ Add Customer',exact:true});
 async function select(){await search.fill('customer');await page.locator('#customer-results [role=option]').waitFor();await search.press('Enter');}
 async function measure(){
  const m=await page.locator('#customer-current').evaluate(n=>{const r=x=>x.getBoundingClientRect().toJSON(),name=n.querySelector('#customer-name'),pill=n.querySelector('#customer-mobile-pill'),mobile=n.querySelector('#customer-mobile'),clear=n.querySelector('#customer-clear'),add=document.querySelector('.billing-customer-row>.btn');return{field:r(n),name:r(name),pill:r(pill),mobile:r(mobile),clear:r(clear),add:r(add),radius:getComputedStyle(n).borderRadius,nameFont:getComputedStyle(name).fontSize,addFont:getComputedStyle(add).fontSize,mobileFont:getComputedStyle(mobile).fontSize,pillColor:getComputedStyle(pill).backgroundColor,phoneColor:getComputedStyle(pill.querySelector('svg')).color,phoneWidth:r(pill.querySelector('svg')).width,ellipsis:getComputedStyle(name).textOverflow,clipped:name.scrollWidth>name.clientWidth,viewport:innerWidth,scroll:n.scrollWidth,client:n.clientWidth,pageWidth:document.documentElement.scrollWidth,overflow:document.documentElement.scrollWidth>innerWidth||n.scrollWidth>n.clientWidth};});
  assert.equal(m.field.height,36);assert.equal(m.add.height,36);assert.equal(m.radius,'8px');assert.equal(m.pill.height,28);assert.equal(m.phoneWidth,14);assert.equal(m.nameFont,'13px');assert.equal(m.addFont,'13px');assert.equal(m.mobileFont,'13px');assert.equal(m.pillColor,'rgb(252, 231, 243)');assert.equal(m.phoneColor,'rgb(236, 0, 140)');assert.equal(m.ellipsis,'ellipsis');
  assert(Math.abs(m.name.y+m.name.height/2-m.pill.y-m.pill.height/2)<1,'Name and phone share a center line');assert(m.name.right<=m.pill.x&&m.pill.right<=m.clear.x);assert(m.clear.width>=32&&m.clear.height>=32);assert(m.mobile.width>=70,'Full mobile number remains readable');assert(!m.overflow,JSON.stringify(m));
  if(Math.abs(m.field.y-m.add.y)<1)assert.equal(m.field.y,m.add.y,'Field and button vertically align');else assert(m.add.y>=m.field.bottom,'Narrow layout wraps without overlap');
  return m;
 }
 await select();assert.equal(await page.locator('#customer-mobile').textContent(),record.mobileNumber);assert.equal(await page.locator('.billing-customer-symbol').count(),0);assert.equal(await page.locator('#customer-clear').getAttribute('aria-label'),'Remove selected customer');
 assert.equal((await page.locator('#customer-clear svg').boundingBox()).width,16);
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[430,800],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await measure();if(width===1366||width===320)await page.screenshot({path:path.join(out,'single-line-customer-'+width+'.png')});
 }
 await page.locator('#customer-clear').click();assert.equal(await search.evaluate(n=>n===document.activeElement),true);assert.equal((await search.boundingBox()).height,36);assert.equal((await add.boundingBox()).height,36);assert.equal(await page.locator('#sale-customer').inputValue(),'');
 record={...record,customerName:'A very long registered customer name that must truncate without covering the phone number or remove action'};
 await select();assert((await measure()).clipped,'Long name truncates');assert.equal(await page.locator('#customer-mobile').textContent(),record.mobileNumber);
 await add.click();assert(await page.locator('#customer-quick-add-dialog').evaluate(n=>n.open));await page.locator('#quick-customer-cancel').click();
 await page.locator('#customer-clear').click();record={...record,customerName:'Name only customer',mobileNumber:null};await select();assert(await page.locator('#customer-mobile-pill').isHidden());
 console.log('PASS: 36px selected/search/Add controls, single-line 28px phone pill, 14px phone icon, long-name ellipsis, readable number, removal/search and existing Add Customer modal at nine sizes. Controlled HTTP.');
};
