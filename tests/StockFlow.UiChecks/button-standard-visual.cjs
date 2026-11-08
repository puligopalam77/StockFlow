// Real Razor, synthetic repositories, loopback GET-only audit server. Never writes shop data.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const phase=process.argv[2]||'after',out=path.resolve(__dirname,'../../.artifacts/button-standard',phase);
const screens=[['sale','/Sales/Create'],['product','/Products/Create'],['products','/Products'],['expenses','/Expenses'],['settings','/Settings/Company']];
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({reducedMotion:'reduce'});const results=[];
 for(const width of [1920,1366,768,390]){
  await page.setViewportSize({width,height:width<768?844:900});
  for(const [name,url] of screens){
   await page.evaluate(()=>window.StockFlowUnsaved?.allowNavigation()).catch(()=>{});
   assert.equal((await page.goto('http://127.0.0.1:55863'+url)).status(),200);
   if(name==='product')await page.waitForFunction(()=>document.querySelector('#master-data')?.dataset.screenReady==='true');
   if(name==='products')await page.locator('#master-rows .sf-action-icon').first().waitFor();
   if(phase!=='before'){
    if(name==='sale'){
     assert(await page.locator('#hold-bill').evaluate(n=>n.classList.contains('btn-dark')));
     assert(await page.locator('#show-held').evaluate(n=>n.classList.contains('btn-outline-secondary')));
     assert(await page.locator('#complete-sale').evaluate(n=>n.classList.contains('btn-primary')));
    }
    if(name==='product'){
     const current=page.locator('[data-registration-tab][aria-selected=true]');
     assert.notEqual(await current.evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(255, 8, 125)');
     assert.match(await current.evaluate(n=>getComputedStyle(n).boxShadow),/inset/);
    }
   }
   await page.evaluate(()=>scrollTo(0,0));
   await page.screenshot({path:path.join(out,`${name}-${width}.png`),fullPage:true});
   if(phase!=='before'&&name==='product'){
    await page.locator('#registration-next').click();
    assert.equal(await page.locator('#registration-tab-basic').getAttribute('aria-selected'),'true','Required fields still block Next');
   }
   const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1);
   assert(!overflow,`${name} fits ${width}`);results.push({name,width,overflow});
   if(name==='expenses'){
    await page.evaluate(()=>{window.testDiscard=null;StockFlowDialog.discard().then(v=>window.testDiscard=v);});
    await page.locator('#sf-confirm-dialog').waitFor({state:'visible'});
    assert.equal(await page.evaluate(()=>document.activeElement.id),'sf-confirm-cancel');
    if(phase!=='before'){
     assert(await page.locator('#sf-confirm-cancel').evaluate(n=>n.classList.contains('btn-primary')));
     assert(await page.locator('#sf-confirm-accept').evaluate(n=>n.classList.contains('btn-outline-danger')));
    }
    await page.screenshot({path:path.join(out,`discard-${width}.png`)});
    await page.keyboard.press('Escape');await page.waitForFunction(()=>testDiscard===false);
   }
  }
 }
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));
 console.log(`PASS: ${phase} pilot captures, four viewports, no page overflow; safe discard focus/Escape preserved.`);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
