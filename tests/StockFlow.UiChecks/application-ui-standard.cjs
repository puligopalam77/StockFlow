// Run against --responsive-audit-server: synthetic repositories, GET only, no shop DB.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const origin='http://127.0.0.1:55863',output=path.resolve(__dirname,'../../.artifacts/application-ui-standard/components');
const routes=['/Products/create','/Products','/Categories','/Suppliers','/Customers','/Purchases','/Purchases/Create?mode=order','/Sales/Create','/Inventory','/Transfers','/Expenses','/Expenses/Create','/Expenses/Report','/Admin/Users','/Settings/Company'];
(async()=>{fs.mkdirSync(output,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true});const results=[];try{
 const page=await browser.newPage({reducedMotion:'reduce'});page.setDefaultTimeout(10000);
 // Recording/navigation guards remain enabled. This audit never submits forms.
 for(const [width,height]of [[1920,1080],[1366,900],[768,1024],[390,844]]){
  await page.setViewportSize({width,height});
  for(const route of routes){
   await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});
   const response=await page.goto(origin+route);assert.equal(response.status(),200,route+' renders');
   if(route==='/Products/create')await page.waitForFunction(()=>document.querySelector('#master-data')?.dataset.screenReady==='true');
   await page.waitForFunction(()=>{
    const main=document.querySelector('main'),launcher=document.getElementById('help-open');
    if(!main||!launcher||!launcher.getClientRects().length)return false;
    const dock=launcher.closest('[class*="assistant-dock"],.supplier-navigation-dock');
    return getComputedStyle(launcher).position!=='fixed'&&(dock||launcher.getBoundingClientRect().top>=main.getBoundingClientRect().bottom-1);
   }).catch(error=>{throw new Error(route+' at '+width+': launcher readiness: '+error.message);});
   const result=await page.evaluate(()=>{
    const main=document.querySelector('main'),launcher=document.getElementById('help-open'),box=launcher.getBoundingClientRect(),mainBox=main.getBoundingClientRect();
    const rendered=e=>e.getClientRects().length&&!e.parentElement.closest('details:not([open]),dialog:not([open])')&&getComputedStyle(e).visibility!=='hidden';
    const controls=[...main.querySelectorAll('input.form-control,select.form-select,.sf-select-trigger,.btn')].filter(e=>rendered(e)&&!e.closest('table')&&!e.matches('.sf-action-icon'));
    const minimum=e=>innerWidth<=767?44:e.matches('.btn')?parseFloat(getComputedStyle(e).getPropertyValue('--sf-button-min-height'))||36:38;
    const undersized=controls.filter(e=>e.getBoundingClientRect().height<minimum(e)-.5).map(e=>({id:e.id,height:e.getBoundingClientRect().height}));
    const content=[...main.querySelectorAll('.card,table,form,.sf-page-heading')].filter(e=>rendered(e)&&!e.closest('dialog'));
    const contentBottom=Math.max(mainBox.top,...content.map(e=>e.getBoundingClientRect().bottom));
    return {overflow:document.documentElement.scrollWidth>innerWidth+1,undersized,launcher:{top:box.top,bottom:box.bottom,height:box.height,contentBottom,position:getComputedStyle(launcher).position},font:getComputedStyle(main).fontFamily};
   });
   assert(!result.overflow,route+' page fits '+width);assert(result.launcher.top>=result.launcher.contentBottom-1,route+' assistant clear of content at '+width+': '+JSON.stringify(result.launcher));assert(result.launcher.height>=44,'Assistant touch target');assert.notEqual(result.launcher.position,'fixed');
   assert.deepEqual(result.undersized,[],route+' consistently sized controls at '+width);
   if(width===1366||width===390){await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(output,route.replace(/[^a-z0-9]/gi,'_')+'-'+width+'.png'),fullPage:true});}
   results.push({route,width,...result});
  }
 }
 await page.locator('#help-open').click();assert(await page.locator('#help-panel').isVisible());await page.locator('#help-close').click();assert(await page.locator('#help-panel').isHidden());
 fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(results,null,2));console.log('PASS: shared controls and non-overlapping assistant on 15 screens at four sizes; help open/close preserved.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
