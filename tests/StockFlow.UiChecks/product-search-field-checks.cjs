const assert=require('node:assert/strict'),path=require('path'),fs=require('fs');
module.exports=async(page,out)=>{
 const input=page.locator('#sale-scan'),row=page.locator('.billing-scan-row:has(#sale-scan)'),camera=row.locator(':scope>.sf-camera-trigger');
 const measurements=[];
 assert.equal(await page.locator('#scan-sale').count(),0);await camera.waitFor();
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[430,800],[375,667],[320,640]]){
  await page.setViewportSize({width,height});
  const m=await row.evaluate(n=>{const input=n.querySelector('#sale-scan'),field=input.parentElement,icon=n.querySelector('.billing-product-search-icon'),r=x=>x.getBoundingClientRect().toJSON(),style=getComputedStyle(input),s=getComputedStyle(icon);const controls=['#customer-search','.billing-customer-row>.btn','#sale-scan','.sf-camera-trigger','#quick-add-item'].map(selector=>{const el=document.querySelector(selector),css=getComputedStyle(el);return{selector,height:r(el).height,width:r(el).width,minHeight:css.minHeight,padding:css.padding,lineHeight:css.lineHeight,font:css.fontSize,boxSizing:css.boxSizing};});const labels=['#billing-customer-card>label','.billing-search-heading>h2'].map(selector=>{const css=getComputedStyle(document.querySelector(selector));return{selector,font:css.fontSize,lineHeight:css.lineHeight};});return{controls,labels,input:r(input),camera:r(n.querySelector('.sf-camera-trigger')),quick:r(n.querySelector('#quick-add-item')),icon:r(icon),pointer:s.pointerEvents,color:s.color,barcode:getComputedStyle(field,'::before').width,cameraIcon:r(n.querySelector('.sf-camera-trigger svg')).width,font:style.fontSize,cameraFont:getComputedStyle(n.querySelector('.sf-camera-trigger')).fontSize,quickFont:getComputedStyle(n.querySelector('#quick-add-item')).fontSize,padding:getComputedStyle(n.closest('.billing-lookup-card')).paddingTop,sectionGap:parseFloat(getComputedStyle(document.querySelector('#billing-customer-card')).marginBottom),zoom:visualViewport.scale,right:parseFloat(style.paddingRight),left:parseFloat(style.paddingLeft),overflow:document.documentElement.scrollWidth>innerWidth+1};});
  measurements.push({width,height,...m});
  for(const control of m.controls){assert.equal(control.height,36,control.selector+' rendered height at '+width);assert.equal(control.minHeight,'36px');assert.equal(control.font,'13px');assert.equal(control.boxSizing,'border-box');assert.equal(control.lineHeight,'18px');}
  for(const label of m.labels)assert.equal(label.font,'13px',label.selector);
  assert.equal(m.input.height,36);assert.equal(m.camera.height,36);assert.equal(m.quick.height,36);assert.equal(m.icon.width,15);assert.equal(m.barcode,'16px');assert.equal(m.camera.width,36);assert.equal(m.cameraIcon,16);assert.equal(m.font,'13px');assert.equal(m.quickFont,'13px');assert.equal(m.padding,'14px');assert.equal(m.sectionGap,12);assert.equal(m.zoom,1);assert.equal(m.pointer,'none');assert.equal(m.color,'rgb(100, 116, 139)');assert(Math.abs(m.input.right-m.icon.right-12)<1);assert(Math.abs(m.input.y+m.input.height/2-m.icon.y-m.icon.height/2)<1);assert(m.right>=34&&m.left>=34);assert(!m.overflow);
  if(width>=1280){assert.equal(m.input.y,m.camera.y);assert.equal(m.input.y,m.quick.y);}else assert(m.quick.y>=m.input.y&&m.camera.y>=m.input.y);
  await input.click({position:{x:m.input.width-20,y:23}});assert(await input.evaluate(n=>document.activeElement===n),'Decorative search icon passes clicks to input');
  if(width===1920||width===1366||width===320)await page.screenshot({path:path.join(out,'inline-product-search-'+width+'.png')});
 }
 fs.writeFileSync(path.join(out,'compact-lookup-measurements.json'),JSON.stringify(measurements,null,2));
 await page.setViewportSize({width:1366,height:768});
 let requests=0;page.on('request',r=>{if(new URL(r.url()).pathname==='/api/products/variants')requests++;});
 await input.fill('Cab');await page.locator('#product-results button').first().waitFor();assert.equal(requests,1,'Typing performs one debounced search');
 await input.press('ArrowDown');await input.press('Enter');await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1);assert.equal(requests,1,'Keyboard selection does not duplicate search');
 await input.fill('CABLE-RED');await input.press('Enter');await page.waitForFunction(()=>document.querySelector('.billing-quantity input')?.value==='2');
 await camera.click();await page.locator('.sf-camera-dialog').waitFor();await page.getByRole('button',{name:'Cancel',exact:true}).click();assert.equal(await page.locator('.sf-camera-dialog').count(),0);
 await page.locator('#quick-add-item').click();assert(await page.locator('.billing-manual-modal').evaluate(n=>n.open));await page.locator('#manual-cancel').click();
 console.log('PASS: removed search button, decorative 15px search/16px barcode icons, 36px field/camera/Quick Add controls at nine sizes, single debounced search, keyboard addition, exact barcode scan, camera dialog and Quick Add. Controlled HTTP.');
};
