const assert=require('node:assert/strict'),crypto=require('node:crypto');
module.exports=async(page,drafts,failures={})=>{
 await page.waitForTimeout(1100);assert.equal(drafts.size,0,'Opening untouched New Sale must not create a draft');
 await page.locator('#show-held').click();await page.locator('.billing-saved-empty').waitFor();assert.equal(drafts.size,0,'Opening Saved Bills must not create an empty draft');await page.locator('#saved-bills-close').click();await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open);
 const ids=[];
 for(let i=0;i<29;i++){const id=crypto.randomUUID();ids.push(id);drafts.set(id,{draftStatus:i%2?'HELD':'ACTIVE',revision:1,leaseUntil:null,editorId:crypto.randomUUID(),label:'Saved customer '+i+' / 9876543210',payload:{draftId:id,customerId:10,customerLabel:'Saved customer '+i+' / 9876543210',cart:[],estimatedTotal:0}});}
 try{
  for(const [width,height] of [[1366,768],[1280,600],[1024,768],[390,844],[320,568]]){
   await page.setViewportSize({width,height});await page.locator('#show-held').click();await page.locator('.billing-saved-card').first().waitFor();
   const rect=await page.locator('#saved-bills-drawer').boundingBox();assert(rect.width<=540&&rect.width<=width);if(width>=1024)assert(rect.width>=480);
   assert(await page.locator('#saved-bills-drawer').evaluate(d=>{const list=d.querySelector('#saved-bills-list'),f=d.querySelector('footer');return d.scrollWidth<=d.clientWidth&&list.scrollHeight>list.clientHeight&&list.getBoundingClientRect().bottom<=f.getBoundingClientRect().top+1&&f.getBoundingClientRect().bottom<=innerHeight+1;}),'Single list scroll and unobscured footer '+width);
   if(width>=1024){const row=await page.locator('.billing-saved-card').first().boundingBox();console.log('Saved Bill row',width,row.height);assert(row.height>=100&&row.height<=115,'Desktop rows target 100–115px');}
   const hint=page.locator('[data-saved-discard-tooltip]').first();assert.equal(await hint.evaluate(el=>getComputedStyle(el).cursor),'pointer','Discard uses hand cursor');await hint.hover();await page.locator('#saved-bills-drawer .tooltip').waitFor({state:'visible'});assert.equal(await page.locator('#saved-bills-drawer .tooltip-inner').innerText(),'Discard Bill');
   await hint.focus();assert(await page.locator('#saved-bills-drawer .tooltip').isVisible(),'Discard tooltip available to keyboard focus');await page.mouse.move(0,0);await page.locator('#saved-bills-close').focus();
   await page.locator('#saved-bills-close').click();await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open);
  }
  await page.locator('#show-held').click();await page.locator('[data-saved-filter="DRAFTS"]').click();await page.waitForFunction(()=>document.querySelector('#saved-bills-count').textContent==='29 Bills'&&document.querySelector('[data-saved-filter="DRAFTS"]').getAttribute('aria-pressed')==='true');
  assert.equal(await page.locator('.billing-saved-card').count(),15);
  await page.locator('[data-saved-filter="HELD"]').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===14);
  const allowedHint=page.locator('.billing-saved-actions button[data-saved-discard-tooltip]').first();await allowedHint.focus();await page.locator('#saved-bills-drawer .tooltip').waitFor({state:'visible'});assert.equal(await page.locator('#saved-bills-drawer .tooltip-inner').innerText(),'Discard Bill');
  await page.locator('[data-saved-filter="ALL"]').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===25);
  await page.locator('#saved-bills-next').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===4);
  await page.locator('#saved-bills-search').fill('9876543210');await page.waitForFunction(()=>document.querySelector('#saved-bills-page').textContent==='Page 1 of 2');
  await page.locator('#saved-bills-search').fill('nonexistent-person');await page.locator('.billing-saved-empty').waitFor();assert.equal(await page.locator('.billing-saved-card').count(),0);
  await page.getByRole('button',{name:'Clear Search',exact:true}).click();await page.locator('[data-saved-filter="HELD"]').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===14);
  await page.locator('.billing-saved-actions button[aria-label^="Discard"]:not(:disabled)').first().click();await page.locator('#sf-confirm-cancel').click();await page.waitForFunction(()=>!document.querySelector('#sf-confirm-dialog').open);assert.equal(drafts.get(ids[1]).draftStatus,'HELD','Cancel preserves held bill');
  await page.locator('.billing-saved-actions button[aria-label^="Discard"]:not(:disabled)').first().click();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===13);assert.equal(drafts.get(ids[1]).draftStatus,'DISCARDED','Confirm discards the selected synthetic bill only');
  await page.locator('[data-saved-filter="DRAFTS"]').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===15);
  const active=page.locator('.billing-saved-actions button[aria-label^="Discard"]').first();assert(await active.isEnabled(),'Expired Active drafts can be discarded');
  await active.click();await page.locator('#sf-confirm-cancel').click();await page.waitForFunction(()=>!document.querySelector('#sf-confirm-dialog').open);assert.equal(drafts.get(ids[0]).draftStatus,'ACTIVE');
  // A live session can claim a row after it was listed. Server recovery must refuse it.
  drafts.get(ids[0]).leaseUntil=Date.now()+120000;
  await active.click();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelector('#saved-bills-feedback').textContent.includes('being edited'));assert.equal(drafts.get(ids[0]).draftStatus,'ACTIVE');
  drafts.get(ids[0]).leaseUntil=null;
  await active.click();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===14);assert.equal(drafts.get(ids[0]).draftStatus,'DISCARDED');
  // Lost responses must retry the exact recovery/delete operation, not mutate twice.
  failures.loseRecoverResponse=true;
  await page.locator('.billing-saved-actions button[aria-label^="Discard"]').first().click();await page.locator('#sf-confirm-accept').click();await page.getByRole('button',{name:'Retry Discard',exact:true}).waitFor();
  failures.loseDeleteResponse=true;
  await page.getByRole('button',{name:'Retry Discard',exact:true}).click();await page.getByRole('button',{name:'Retry Discard',exact:true}).waitFor();
  await page.getByRole('button',{name:'Retry Discard',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===13);assert.equal(drafts.get(ids[2]).revision,3,'Recovery plus discard happen once across retries');
  await page.locator('#saved-bills-close').click();await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open);
  await page.locator('#customer-search').fill('Counter');await page.locator('#customer-results [role=option]').first().click();await page.locator('#sale-scan').fill('fixture');await page.locator('#sale-scan').press('Enter');await page.waitForFunction(()=>document.querySelector('#draft-status').textContent==='Draft saved');
  const currentId=[...drafts.keys()].find(id=>!ids.includes(id));
  await page.locator('#show-held').click();await page.locator('[data-saved-filter="DRAFTS"]').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===14);const current=structuredClone(drafts.get(currentId));
  await page.locator('.billing-saved-actions button[aria-label^="Discard"]').first().click();await page.locator('#sf-confirm-accept').click();await page.waitForFunction(()=>document.querySelectorAll('.billing-saved-card').length===13);
  assert.deepEqual(drafts.get(currentId),current,'Discard another draft preserves current payload, revision and live editor');assert.equal(await page.locator('#sale-customer').inputValue(),'10');
  await page.locator('#saved-bills-close').click();await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open);
  await page.locator('#customer-clear').click();await page.waitForFunction(()=>document.querySelector('#draft-status').textContent==='Draft saved');assert.equal(drafts.get(currentId).payload.customerId,null,'Current draft still saves with its original lease after deleting another bill');
  drafts.delete(currentId);await page.evaluate(()=>localStorage.clear());await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 }finally{for(const id of ids)drafts.delete(id);await page.setViewportSize({width:1366,height:768});await page.locator('#sale-scan').focus();}
 console.log('PASS Saved Bills: untouched/open drawer creates no draft, compact rows at five sizes, single scroll/footer, filters/counts/search/paging, discard confirmation/cancellation. APIs mocked.');
};
