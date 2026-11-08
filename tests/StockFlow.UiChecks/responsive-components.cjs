const fs=require('fs'),path=require('path'),assert=require('assert');
const {fillAddress}=require('./address-test-helpers.cjs');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const output=path.resolve(__dirname,'../bin/responsive-audit');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({reducedMotion:'reduce'});
  for(const [width,height]of[[1366,768],[1280,600],[390,844],[320,640],[640,400]]){
   await page.setViewportSize({width,height});await page.goto('http://127.0.0.1:55863/');
   await page.locator('.sf-main').waitFor();
   if(width<992){await page.locator('#sidebar-toggle').click();assert(await page.locator('body').evaluate(n=>n.classList.contains('sf-nav-open')));await page.locator('#sidebar-close').click();assert.equal(await page.locator('body').evaluate(n=>n.classList.contains('sf-nav-open')),false);}
   await page.locator('#profile-menu summary').first().click();
   assert(await page.locator('.sf-profile-panel').evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;}),'Profile fits viewport');
   await page.locator('#profile-menu summary').first().click();
   await page.evaluate(()=>{window.confirmResult=null;StockFlowDialog.confirm('Discard the unsaved changes to this record? '+ 'Long confirmation details '.repeat(20)).then(value=>window.confirmResult=value);});
   const modal=page.locator('#sf-confirm-dialog');await modal.waitFor({state:'visible'});
   assert(await modal.evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight&&n.scrollWidth<=n.clientWidth;}));
   await page.locator('#sf-confirm-cancel').scrollIntoViewIfNeeded();await page.keyboard.press('Escape');await page.waitForFunction(()=>window.confirmResult===false);
   await page.evaluate(()=>{const n=document.createElement('button');n.id='overlap-test-action';n.type='button';n.textContent='Save changes';n.style.cssText='position:fixed;right:12px;bottom:12px;width:190px;height:60px;z-index:1039';document.querySelector('.sf-main').append(n);});
   await page.waitForFunction(()=>{const launcher=document.querySelector('.sf-help-open'),caption=launcher.querySelector('.sf-mascot-caption')||launcher,a=caption.getBoundingClientRect(),b=document.getElementById('overlap-test-action').getBoundingClientRect();return a.right<=b.left||a.left>=b.right||a.bottom<=b.top||a.top>=b.bottom;});
   await page.locator('#overlap-test-action').click();await page.locator('#overlap-test-action').evaluate(n=>n.remove());
   await page.screenshot({path:path.join(output,'shared-components-'+width+'.png'),fullPage:true});
   console.log('PASS: navigation, profile, long confirmation/Escape and unobstructed primary action at '+width+'x'+height);
  }
  // The read-only server has no live billing policy repository. Supply only the
  // GET context needed to exercise the customer modal; never submit the form.
  await page.route('**/api/sales/billing/policy',route=>route.fulfill({contentType:'application/json',body:JSON.stringify({canOverridePrice:true,canOverrideTax:true,canDiscount:true,canApproveDiscount:false,discountLimitPercent:10})}));
  await page.route('**/api/sales/gst-context',route=>route.fulfill({contentType:'application/json',body:'{"mapped":false}'}));
  await page.setViewportSize({width:390,height:600});await page.goto('http://127.0.0.1:55863/Sales/Create');
  await page.getByRole('button',{name:'+ Add Customer',exact:true}).click();
  await page.locator('#new-customer-name').fill('Unsaved customer');
  await page.locator('#quick-customer-additional summary').click();await fillAddress(page.locator('#new-customer-address'),'Long optional address '.repeat(20));
  for(const [width,height]of[[1280,600],[390,600],[320,400]]){
   await page.setViewportSize({width,height});const modal=page.locator('#customer-quick-add-dialog');
   assert(await modal.evaluate(n=>{const r=n.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight&&n.scrollWidth<=n.clientWidth;}));
   await page.locator('#create-customer').scrollIntoViewIfNeeded();assert.equal(await page.locator('#new-customer-name').inputValue(),'Unsaved customer');
  }
  await page.keyboard.press('Escape');assert(await page.locator('#customer-quick-add-dialog').isHidden());
  console.log('PASS: customer form modal keeps fields across viewport changes; Save remains reachable.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
