const assert=require('node:assert/strict'),crypto=require('node:crypto');
module.exports=async(page,drafts)=>{
 const ready=()=>page.waitForFunction(()=>!document.querySelector('#sale-editor').disabled);
 const waitSaved=()=>page.waitForFunction(()=>document.querySelector('#draft-status').textContent==='Draft saved');
 const saveMessage='Add at least one item before saving this bill.',holdMessage='Add at least one item before putting this bill on hold.';
 async function blocked(id,message){await page.locator('#'+id).click();await page.waitForFunction(text=>document.querySelector('#sf-notifications')?.textContent.includes(text),message);assert.equal(await page.evaluate(()=>document.activeElement.id),'sale-scan');assert(await page.locator('#sale-message').evaluate(el=>el.hidden),'No alert banner');await page.evaluate(()=>StockFlowNotify.dismiss('bill-minimum-items'));}
 await page.waitForTimeout(1100);assert.equal(drafts.size,0);
 await blocked('save-draft',saveMessage);await blocked('hold-bill',holdMessage);
 assert(await page.locator('#complete-sale').isDisabled());await page.locator('#sale-form').evaluate(el=>el.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));assert.equal(drafts.size,0);
 await page.locator('#customer-search').fill('Counter');await page.locator('#customer-results [role=option]').first().click();await page.waitForTimeout(1100);assert.equal(drafts.size,0,'Customer selection creates no server draft');
 await blocked('save-draft',saveMessage);await page.reload();await ready();await page.waitForTimeout(1100);assert.equal(drafts.size,0,'Customer-only refresh stays local');assert.equal(await page.locator('#sale-customer').inputValue(),'10');
 await page.route('**/local-minimum-away',route=>route.fulfill({contentType:'text/html',body:'Local navigation fixture'}));await page.goto('http://stockflow.test/local-minimum-away');await page.goto('http://stockflow.test/Sales/Create');await ready();await page.waitForTimeout(1100);assert.equal(drafts.size,0,'Customer-only navigation creates no permanent draft');
 await page.locator('#sale-scan').fill('fixture');await page.locator('#sale-scan').press('Enter');await waitSaved();assert.equal(drafts.size,1);
 for(const [width,height]of [[1366,768],[1280,600],[390,844],[320,568]]){await page.setViewportSize({width,height});await page.locator('#calculate-change').check();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'No overflow while syncing with expanded cash fields at '+width);await page.locator('#calculate-change').uncheck();}await page.setViewportSize({width:1366,height:768});await waitSaved();
 const id=[...drafts.keys()][0];await page.locator('#save-draft').dblclick();await waitSaved();assert.equal(drafts.size,1,'Repeated saves reuse one identity');
 await page.locator('#sale-cart button[aria-label^="Remove"]').first().click();await page.waitForTimeout(1100);const revision=drafts.get(id).revision;await blocked('save-draft',saveMessage);await blocked('hold-bill',holdMessage);assert.equal(drafts.get(id).revision,revision,'Empty edits never overwrite persisted cart');
 assert.equal(drafts.get(id).payload.cart.length,1);await page.locator('#sale-scan').fill('fixture');await page.locator('#sale-scan').press('Enter');await waitSaved();
 await page.locator('#hold-bill').click();await page.waitForFunction(()=>document.querySelector('#sale-cart tr')===null&&!document.querySelector('#sale-editor').disabled);assert.equal(drafts.get(id).draftStatus,'HELD');
 // Explicit historical fixture, not a new SAVE through the API.
 const legacy=crypto.randomUUID();drafts.set(legacy,{draftStatus:'LEGACY',revision:1,leaseUntil:null,editorId:crypto.randomUUID(),label:'Historical empty',payload:{savedAt:Date.now(),draftId:legacy,customerId:10,customerLabel:'Counter customer',cart:[],estimatedTotal:0}});
 await page.locator('#show-held').click();await page.locator('#saved-bills-recovery').click();await page.locator('.billing-saved-resume').click();await page.waitForFunction(()=>!document.querySelector('#saved-bills-drawer').open);assert.equal(await page.locator('#sale-cart tr').count(),0);
 await blocked('save-draft',saveMessage);assert.equal(drafts.get(legacy).payload.cart.length,0,'Resumed legacy empty bill remains unchanged');
 await page.locator('#sale-scan').fill('fixture');await page.locator('#sale-scan').press('Enter');await waitSaved();assert.equal(drafts.get(legacy).payload.cart.length,1,'Adding an item allows a resumed empty draft to save');
 console.log('PASS minimum items: empty Save/Hold/payment blocked; focus/toast; customer-only refresh remains local; valid Save/Hold; last-item removal; repeated saves; historical empty recovery then add/save. Mocked APIs.');
};
