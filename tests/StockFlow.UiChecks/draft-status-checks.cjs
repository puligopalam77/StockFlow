const assert=require('node:assert/strict'),path=require('path');
module.exports=async(page,out)=>{
 let mode='delay',count=0,releaseFirst,releaseSecond;
 const first=new Promise(r=>releaseFirst=r),second=new Promise(r=>releaseSecond=r);
 await page.route('**/api/sales/billing/drafts/**',async route=>{
  if(route.request().method()==='PUT'){
   count++;
   if(mode==='fail')return route.fulfill({status:500,contentType:'application/json',body:JSON.stringify({message:'Synthetic draft save failed'})});
   if(mode==='delay'&&count<=2)await(count===1?first:second);
  }
  return route.fallback();
 });
 const status=page.locator('#draft-status');
 const success=()=>page.waitForFunction(()=>document.querySelector('#draft-status').dataset.saved==='true');
 const note=value=>page.locator('#sale-notes').evaluate((n,value)=>{n.value=value;n.dispatchEvent(new Event('input',{bubbles:true}));},value);
 try{
  assert.notEqual(await status.getAttribute('data-saved'),'true','No initial success');
  await page.locator('#sale-scan').fill('CABLE-RED');await page.locator('#sale-scan').press('Enter');
  await page.waitForFunction(()=>document.querySelector('#draft-status').textContent==='Saving active draft...');
  assert.equal(await status.getAttribute('data-saved'),'false');
  await note('Latest draft note');releaseFirst();
  await page.waitForFunction(()=>document.querySelector('#draft-status').textContent==='Saving active draft...');
  assert.equal(await status.getAttribute('data-saved'),'false','An older acknowledgement cannot mark newer edits saved');
  mode='normal';releaseSecond();await success();assert.equal(await status.textContent(),'Draft saved');
  for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[430,800],[375,667],[320,640]]){
   await page.setViewportSize({width,height});
   const m=await page.locator('#billing-header-actions').evaluate(n=>{const r=x=>x.getBoundingClientRect().toJSON(),s=n.querySelector('#draft-status');return{status:r(s),hold:r(n.querySelector('#hold-bill')),saved:r(n.querySelector('#show-held')),font:getComputedStyle(s).fontSize,gap:getComputedStyle(s).gap,icon:getComputedStyle(s,'::before').width,overflow:document.documentElement.scrollWidth>innerWidth};});
   if(width>=1280){const topbar=await page.locator('.sf-topbar').boundingBox(),summary=await page.locator('#billing-summary').boundingBox();assert(m.status.y-topbar.y-topbar.height<=8,'Status sits at the content top edge');assert(summary.y>=m.saved.bottom&&summary.y-m.saved.bottom<=12,'Separate summary follows the buttons');}
   assert.equal(m.hold.y,m.saved.y);assert.equal(m.status.bottom+4,m.hold.y);assert(Math.abs(m.status.right-m.saved.right)<1,'Right aligned '+JSON.stringify({width,...m}));assert.equal(m.font,'12px');assert.equal(m.gap,'4px');assert.equal(m.icon,'14px');assert(!m.overflow);assert.equal(m.hold.height,width>700?32:44);assert.equal(m.saved.height,width>700?32:44);
   if(width===1366){
    const heightBefore=(await page.locator('.sf-page-heading').boundingBox()).height;
    const oldLayout=await page.addStyleTag({content:'#draft-status{order:0!important;font-size:10px!important;line-height:15px!important}#billing-header-actions{row-gap:8px!important}'});
    await status.evaluate(n=>n.textContent='Active draft saved in Saved Bills');
    assert(heightBefore<=(await page.locator('.sf-page-heading').boundingBox()).height,'Header does not exceed the previous two-row status layout');
    await oldLayout.evaluate(n=>n.remove());await status.evaluate(n=>n.textContent='Draft saved');await success();
    await page.screenshot({path:path.join(out,'draft-saved-header.png')});
   }
  }
  mode='fail';await note('Failed save note');await page.waitForFunction(()=>document.querySelector('#draft-status').textContent.includes('Synthetic draft save failed'));
  assert.equal(await status.getAttribute('data-saved'),'false');assert.equal(await status.evaluate(n=>getComputedStyle(n,'::before').content),'none');
  mode='normal';await note('Retry latest note');await success();
  await page.locator('#show-held').click();assert(await page.locator('#saved-bills-drawer').evaluate(n=>n.open));await page.locator('#saved-bills-close').click();
  console.log('PASS: pending/failure/older-ack states have no success icon, latest persistence shows Draft saved, nine responsive header layouts, 14px icon, 12px text, 4px spacing and Saved Bills action. Controlled HTTP.');
 }finally{releaseFirst();releaseSecond();}
};
