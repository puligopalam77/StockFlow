const assert=require('node:assert/strict'),fs=require('fs'),path=require('path');
module.exports=async(page,out)=>{
 const results=[];
 await page.evaluate(()=>window.originalComplete=document.querySelector('#complete-sale'));
 for(const [width,height]of [[1366,768],[1536,864],[1920,1080],[1280,720],[1024,768],[390,844]])for(const collapsed of [false,true]){
  await page.setViewportSize({width,height});
  await page.evaluate(c=>{document.body.classList.toggle('sf-nav-collapsed',c);document.querySelector('#billing-notes').open=false;document.querySelector('#sale-notes').style.height='';window.scrollTo({top:0,behavior:'instant'});},collapsed);
  await page.waitForTimeout(300);
  const measure=()=>page.evaluate(()=>{
   const r=selector=>document.querySelector(selector).getBoundingClientRect().toJSON(),footer=document.querySelector('.billing-checkout-actions'),button=document.querySelector('#complete-sale');
   return{button:r('#complete-sale'),footer:r('.billing-checkout-actions'),summary:r('#billing-summary'),lookup:r('.billing-lookup-card'),payment:r('#billing-payment-card'),checkout:r('.billing-checkout-pane'),tiles:[...document.querySelectorAll('.billing-payment-tile')].map(n=>n.getBoundingClientRect().toJSON()),overflow:document.documentElement.scrollWidth>innerWidth+1,zoom:visualViewport.scale,position:getComputedStyle(footer).position,same:button===window.originalComplete&&document.querySelectorAll('#complete-sale').length===1,form:button.form?.id,scrollContainers:[...document.querySelectorAll('.billing-checkout-pane,.billing-checkout-pane>*')].filter(n=>/auto|scroll|hidden|clip/.test(getComputedStyle(n).overflowY)&&n.scrollHeight>n.clientHeight+1).map(n=>n.id||n.className)};
  });
  let m=await measure();assert(m.same&&m.form==='sale-form',JSON.stringify(m));assert.equal(m.position,'sticky');assert(!m.overflow);assert.equal(m.zoom,1);assert.deepEqual(m.scrollContainers,[]);
  if(width>=1280){const actions=await page.locator('#billing-header-actions').boundingBox();assert(m.summary.y>=actions.y+actions.height&&m.summary.y-actions.y-actions.height<=12);assert(actions.y< m.lookup.y,'Actions start above the Customer card');assert(m.payment.y-m.summary.bottom>=10&&m.payment.y-m.summary.bottom<=12);assert(m.tiles.every(t=>Math.abs(t.y-m.tiles[0].y)<1),'Five methods share one row');assert(m.button.y>=50&&m.button.bottom<=height,'Initial checkout action is visible');}
  if(width===1366&&!collapsed)await page.screenshot({path:path.join(out,'sticky-checkout-initial-1366.png')});
  // Optional payment fields and long notes remain in the document, without a nested scroller.
  await page.locator('.billing-payment-tile[data-payment-mode=CASH]').click();await page.locator('#calculate-change').check();
  await page.locator('#billing-notes').evaluate(n=>n.open=true);
  await page.locator('#sale-notes').evaluate(n=>n.style.height='200px');
  await page.evaluate(()=>{const warning=document.querySelector('#billing-payment-warning');warning.hidden=false;warning.textContent='Payment validation remains readable above the checkout action.';window.scrollTo({top:0,behavior:'instant'});});
  m=await measure();if(width>=1280)assert(m.button.bottom<=height&&m.button.y>=50,'Expanded payment keeps checkout visible '+JSON.stringify(m));
  for(const selector of ['#cash-received','#billing-payment-warning','#sale-notes']){
   await page.locator(selector).evaluate(n=>n.scrollIntoView({block:'center',behavior:'instant'}));
   const field=await page.locator(selector).boundingBox();m=await measure();assert(field.y>=50&&field.y+field.height<=m.footer.y+1,'Field fully accessible above sticky footer '+JSON.stringify({width,selector,field,m}));
  }
  // A long cart stretches the column. The action stays visible at different page scroll positions.
  await page.evaluate(()=>{const table=document.querySelector('.billing-cart');table.closest('.table-responsive').hidden=false;document.querySelector('#sale-empty').hidden=true;table.querySelector('tbody').innerHTML=Array.from({length:30},(_,i)=>'<tr><td>Layout fixture product '+i+'</td><td>SKU-'+i+'</td><td>1</td><td>100</td><td>0</td><td>18</td><td>118</td></tr>').join('');});
  if(width>=1280)for(const fraction of [0,.25,.65,1]){
   await page.evaluate(f=>window.scrollTo({top:(document.documentElement.scrollHeight-innerHeight)*f,behavior:'instant'}),fraction);
   m=await measure();assert(m.button.y>=50&&m.button.bottom<=height,'Long cart checkout remains visible '+JSON.stringify({width,fraction,m}));assert(!m.overflow);assert.deepEqual(m.scrollContainers,[]);
  }
  await page.evaluate(()=>{document.querySelector('.billing-cart tbody').innerHTML='';document.querySelector('.billing-cart').closest('.table-responsive').hidden=true;document.querySelector('#sale-empty').hidden=false;document.querySelector('#billing-payment-warning').hidden=true;window.scrollTo({top:0,behavior:'instant'});});
  await page.locator('#calculate-change').uncheck();
  results.push({width,height,collapsed,...m});
  if(width===1366&&!collapsed)await page.screenshot({path:path.join(out,'sticky-checkout-1366.png')});
 }
 fs.writeFileSync(path.join(out,'sticky-checkout-measurements.json'),JSON.stringify(results,null,2));
 console.log('PASS sticky checkout: six viewports, both sidebar states, independent top-right actions and summary, five payment methods, optional cash fields, long notes, validation, 30-item cart and page scrolling; original submit button, no nested scrollbars or horizontal overflow.');
};
