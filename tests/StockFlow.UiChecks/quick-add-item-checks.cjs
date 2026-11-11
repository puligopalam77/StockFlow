const {selectValue}=require('./address-test-helpers.cjs');
// Uses the rendered Razor, real billing scripts and HTTP fixtures in fast-billing-offline.cjs.
const assert=require('node:assert/strict'),path=require('path');
module.exports=async(page,output)=>{
 const dialog=page.locator('.billing-manual-modal');
 await page.route('**/api/sales/manual-tax-rules',route=>route.fulfill({contentType:'application/json',body:JSON.stringify([
  {ruleId:7,ruleName:'Reviewed small goods',hsnSac:'610910',uqc:'NOS',treatment:'TAXABLE',taxRate:5},
  ...[18,0,28,5,12].map((rate,i)=>({ruleId:100+i,ruleName:'TEST ONLY - GST '+rate+'%',hsnSac:'0000',uqc:'NOS',treatment:rate?'TAXABLE':'NIL',taxRate:rate}))
 ])}));
 await page.locator('#quick-add-item').click();
 await page.locator('#manual-name').fill('Loose cord');await page.locator('#manual-quantity').fill('2');
 await page.locator('#manual-price').fill('50');await page.locator('#manual-mrp').fill('60');
 await page.locator('#manual-discount').fill('5');await page.locator('#manual-note').fill('Keep this manual item note');
 const option='7',rule=page.locator('#manual-rule');
 await page.waitForFunction(()=>document.querySelector('#manual-rule').options.length===7);
 assert.equal(await rule.inputValue(),'','Fresh item defaults to No GST');
 assert.equal(await rule.locator('option:checked').textContent(),'No GST');
 await page.waitForFunction(()=>document.querySelector('#manual-rule-search')?.value==='No GST');
 await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 assert((await page.locator('#manual-preview').textContent()).includes('95.00'),'Default No GST adds no tax');
 assert.equal(await rule.evaluate(n=>n.tagName),'SELECT');assert.equal(await rule.evaluate(n=>!!n.sfSelect),true);
 assert.notEqual(await rule.evaluate(n=>getComputedStyle(n.sfSelect.input).backgroundImage),'none','Native dropdown arrow is visible');
 const choices=await rule.locator('option').evaluateAll(ns=>ns.map(n=>({value:n.value,label:n.textContent})));
 assert.equal(choices[0].label,'No GST');assert(choices.every(o=>!o.label.includes('TEST ONLY')));
 const rateById={7:5,100:18,101:0,102:28,103:5,104:12};
 assert.deepEqual(choices.slice(1).map(o=>rateById[o.value]),[0,5,5,12,18,28]);
 assert.deepEqual(choices.slice(1).map(o=>o.value).sort(),Object.keys(rateById).sort(),'All classification IDs retained');
 assert(choices.some(o=>o.label.startsWith('GST 0%')&&o.label.endsWith('NIL')));
 await selectValue(rule,'101');assert.equal(await rule.inputValue(),'101','NIL remains a separate configured classification');
 await selectValue(rule,option);await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 assert((await page.locator('#manual-preview').textContent()).includes('99.75'));
 await page.locator('#manual-cancel').focus();
 await page.waitForFunction(()=>[...document.querySelectorAll('.billing-manual-modal :is(.form-control,.form-select)')].filter(n=>n.getBoundingClientRect().width).every(n=>getComputedStyle(n).borderTopColor==='rgb(203, 213, 225)'));
 const fields=await dialog.locator(':is(.form-control,.form-select):visible').evaluateAll(ns=>ns.map(n=>{const s=getComputedStyle(n);return {id:n.id,bg:s.backgroundColor,border:s.borderTopColor,height:n.getBoundingClientRect().height,disabled:n.disabled,readOnly:n.readOnly};}));
 assert.equal(fields.length,7);for(const f of fields){assert(!f.disabled&&!f.readOnly,f.id);assert.equal(f.bg,'rgb(255, 255, 255)',f.id);assert.equal(f.border,'rgb(203, 213, 225)',f.id);assert(f.height>=40&&f.height<=44,JSON.stringify(f));}
 await page.locator('#manual-name').focus();
 await page.waitForFunction(()=>getComputedStyle(document.querySelector('#manual-name')).borderTopColor==='rgb(255, 0, 128)');
 const focus=await page.locator('#manual-name').evaluate(n=>({border:getComputedStyle(n).borderTopColor,ring:getComputedStyle(n).boxShadow}));
 assert.equal(focus.border,'rgb(255, 0, 128)');assert.notEqual(focus.ring,'none');
 async function verifyRows(){
  const m=await dialog.evaluate(n=>{
   const rect=x=>x.getBoundingClientRect().toJSON(),body=n.querySelector('.billing-manual-body'),primary=n.querySelector('.billing-manual-primary');
   return {available:body.clientWidth-40,grid:rect(primary),gap:parseFloat(getComputedStyle(primary).columnGap),
    fields:['manual-name','manual-quantity','manual-price','manual-mrp','manual-discount','manual-rule','manual-note'].map(id=>rect(n.querySelector('#'+id).sfSelect?.input||n.querySelector('#'+id)))};
  });
  const [name,quantity,price,mrp,discount,gst,notes]=m.fields;
  const helper=await page.locator('#manual-classification').boundingBox();
  assert(Math.abs(helper.x-gst.x)<1&&helper.y>=gst.bottom&&helper.x+helper.width<=gst.right+1,'GST helper stays beneath and within its field');
  if(m.available>=640){
   assert(Math.abs(name.y-quantity.y)<1&&Math.abs(name.y-price.y)<1,'Three primary fields share one row');
   const usable=m.grid.width-2*m.gap;
   for(const [field,ratio]of [[name,.5],[quantity,.2],[price,.3]])assert(Math.abs(field.width-usable*ratio)<1,'50/20/30 proportions after gutters');
   assert(mrp.y>=name.bottom&&discount.y>=price.bottom,'MRP and Discount follow primary fields');
  }else if(m.available>479){
   assert(name.bottom<=quantity.y&&Math.abs(quantity.y-price.y)<1,'Intermediate width: full Name, paired Quantity/Price');
  }else{
   for(let i=1;i<m.fields.length;i++)assert(m.fields[i].y>=m.fields[i-1].bottom,'Narrow container stacks fields in keyboard order');
  }
  if(m.available>479)assert(Math.abs(mrp.y-discount.y)<1&&Math.abs(mrp.width-discount.width)<1,'Second row has equal columns');
  assert(Math.abs(notes.width-m.grid.width)<1,'Notes remains full width');
  if(m.available>=640){assert(Math.abs(gst.y-discount.y)<1&&gst.x>discount.right,'GST sits beside Discount');assert(gst.width>discount.width,'GST has room for classification labels');}
  else{assert(Math.abs(gst.width-m.grid.width)<1,'GST spans narrow rows');assert(gst.y>=Math.max(mrp.bottom,discount.bottom));}
  assert(notes.y>=Math.max(mrp.bottom,discount.bottom,gst.bottom));
  for(const f of m.fields)assert(f.x>=m.grid.x-1&&f.right<=m.grid.right+1,'Fields contained');
 }
 // Container breakpoints must respond even while the desktop viewport stays wide.
 const body=dialog.locator('.billing-manual-body');
 for(const width of [600,440]){await body.evaluate((n,w)=>n.style.maxWidth=w+'px',width);await verifyRows();}
 await body.evaluate(n=>n.style.removeProperty('max-width'));
 await page.locator('#manual-name').focus();
 await page.screenshot({path:path.join(output,'quick-add-three-column.png')});
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1280,600],[1024,768],[768,1024],[640,768],[600,768],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});
  await verifyRows();
  await rule.focus();assert(await page.locator('#manual-rule-search').isVisible());assert.equal(await rule.inputValue(),option);
  // A long server error exercises overflow without changing validation behavior.
  await page.locator('#manual-preview').evaluate(n=>n.textContent='Validation information. '.repeat(35));
  const measure=()=>dialog.evaluate(n=>{const rect=x=>x.getBoundingClientRect().toJSON(),body=n.querySelector('.billing-manual-body'),footer=n.querySelector('footer');return {box:rect(n),body:rect(body),footer:rect(footer),overflow:n.scrollWidth>n.clientWidth+1,outerScroll:n.scrollHeight>n.clientHeight+1,columns:getComputedStyle(n.querySelector('.billing-manual-fields')).gridTemplateColumns.split(' ').length,buttons:[...footer.children].map(b=>({rect:rect(b),font:getComputedStyle(b).fontSize,radius:getComputedStyle(b).borderRadius}))};});
  for(const end of [false,true]){
   await dialog.locator('.billing-manual-body').evaluate((n,end)=>n.scrollTop=end?n.scrollHeight:0,end);
   const m=await measure();assert(m.box.x>=0&&m.box.right<=width+1&&m.box.y>=0&&m.box.bottom<=height+1,JSON.stringify(m));
   assert(!m.overflow&&!m.outerScroll,'Only body scrolls');assert(m.body.bottom<=m.footer.top+1,'Footer does not overlap fields or messages');
   assert.equal(m.columns,1,'Outer grid holds full-width row groups');
   for(const b of m.buttons){assert(b.rect.height>=40&&b.rect.height<=44);assert.equal(b.font,'14px');assert.equal(b.radius,'8px');assert(b.rect.y>=m.footer.y&&b.rect.bottom<=m.box.bottom&&b.rect.right<=m.box.right&&b.rect.x>=m.box.x);}
   assert(Math.abs(m.buttons[0].rect.y-m.buttons[1].rect.y)<1);assert(m.buttons[1].rect.x-m.buttons[0].rect.right>=8);
  }
  await page.locator('#manual-note').focus();await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.id),'manual-cancel');
  await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.id),'manual-submit');
  if([1366,1280,390].includes(width))await page.screenshot({path:path.join(output,'quick-add-'+width+'x'+height+'.png')});
 }
 await page.setViewportSize({width:1366,height:768});
 await selectValue(rule,'');await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 assert((await page.locator('#manual-preview').textContent()).includes('95.00'));
 await selectValue(rule,option);await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 await page.locator('#manual-quantity').fill('0');assert(await page.locator('#manual-submit').isDisabled());
 await page.locator('#manual-quantity').fill('2');await page.locator('#manual-price').fill('0');assert(await page.locator('#manual-submit').isDisabled());
 await page.locator('#manual-price').fill('50');await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);
 await page.locator('#manual-submit').click();await page.waitForFunction(()=>document.querySelectorAll('#sale-cart tr').length===1&&document.querySelector('#billing-calculation-status').hidden);
 assert((await page.locator('#sale-cart').textContent()).includes('Loose cord'));
 assert((await page.locator('#summary-total').textContent()).includes('99.75'));
 await page.locator('.billing-manual-edit').click();await page.waitForFunction(()=>!document.querySelector('#manual-rule').disabled);assert.equal(await page.locator('#manual-note').inputValue(),'Keep this manual item note');assert.equal(await rule.inputValue(),'7','Edited item restores its original classification ID');
 await page.locator('#manual-name').fill('Cancelled edit');await page.locator('#manual-cancel').click();assert(!await dialog.isVisible());assert(!(await page.locator('#sale-cart').textContent()).includes('Cancelled edit'));
 await page.locator('#quick-add-item').click();await page.waitForFunction(()=>!document.querySelector('#manual-rule').disabled);
 assert.equal(await rule.inputValue(),'','Fresh item forgets the previous item selection');assert.equal(await rule.locator('option:checked').textContent(),'No GST');
 await page.locator('#manual-cancel').click();
 // An older delayed edit response must not replace the next fresh item's default.
 let releaseOld,started;const oldResponse=new Promise(resolve=>releaseOld=resolve),oldStarted=new Promise(resolve=>started=resolve);let calls=0;
 const delayOld=async route=>{if(++calls===1){started();await oldResponse;}await route.fallback();};
 await page.route('**/api/sales/manual-tax-rules',delayOld);
 await page.locator('.billing-manual-edit').click();await oldStarted;assert(await rule.isDisabled());
 await page.locator('#manual-name').fill('Still loading');await page.locator('#manual-price').fill('50');assert(await page.locator('#manual-submit').isDisabled());
 await page.locator('#manual-cancel').click();await page.locator('#quick-add-item').click();await page.waitForFunction(()=>!document.querySelector('#manual-rule').disabled);
 const delivered=page.waitForResponse(r=>r.url().endsWith('/api/sales/manual-tax-rules'));releaseOld();await delivered;
 await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 assert.equal(await rule.inputValue(),'','Delayed previous edit cannot overwrite fresh No GST');
 await page.unroute('**/api/sales/manual-tax-rules',delayOld);await page.locator('#manual-cancel').click();
 // Real policy path must still lock Discount, with distinct read-only styling.
 await page.route('**/api/sales/billing/policy',route=>route.fulfill({contentType:'application/json',body:JSON.stringify({canOverridePrice:true,canOverrideTax:true,canDiscount:false,canApproveDiscount:false,discountLimitPercent:0})}));
 await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 await page.locator('#quick-add-item').click();
 assert(await page.locator('#manual-discount').evaluate(n=>n.readOnly));
 assert.equal(await page.locator('#manual-discount').evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 245, 249)');
 assert(await page.locator('#manual-submit').isDisabled());
 // Disabled presentation, without changing any production permissions.
 await page.locator('#manual-price').evaluate(n=>n.disabled=true);
 assert.equal(await page.locator('#manual-price').evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(241, 245, 249)');
 await page.locator('#manual-close').click();
 await page.route('**/api/sales/gst-context',route=>route.fulfill({contentType:'application/json',body:JSON.stringify({mapped:true,registrationType:'REGULAR'})}));
 await page.evaluate(()=>{localStorage.clear();sessionStorage.clear();});await page.reload();await page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 await page.locator('#quick-add-item').click();await page.waitForFunction(()=>!document.querySelector('#manual-rule').disabled);
 assert(await rule.evaluate(n=>n.required));assert.equal(await rule.locator('option:checked').textContent(),'Select GST classification','Mandatory classification is not defaulted to No GST');
 await page.locator('#manual-name').fill('Required GST item');await page.locator('#manual-price').fill('100');assert(await page.locator('#manual-submit').isDisabled());
 await selectValue(rule,'7');await page.waitForFunction(()=>!document.querySelector('#manual-submit').disabled);await page.locator('#manual-close').click();
 console.log('PASS Quick Add/GST: shared dropdown, clean labels, ascending rates, all classification IDs preserved, NIL and No GST, 12 responsive sizes, visible footer, keyboard, validation, 99.75 bill total, notes, edit persistence and Cancel. Mock HTTP.');
};
