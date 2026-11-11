// Distinguish CSS layout pixels from screenshot pixels; no browser zoom or account writes.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');const out=path.resolve(__dirname,'../../.artifacts/profile-menu');fs.mkdirSync(out,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});const results=[];try{
 for(const density of [1,1.5]){const page=await browser.newPage({deviceScaleFactor:density});
  await page.route('**/*.js',r=>/\/stockflow-shell(\.[a-z0-9]{10})?\.js$/.test(new URL(r.request().url()).pathname)?r.continue():r.fulfill({contentType:'text/javascript',body:''}));
  for(const [width,height] of [[1280,720],[1366,768],[1536,864],[1920,1080]]){
   await page.setViewportSize({width,height});await page.goto('http://127.0.0.1:55863/');await page.locator('.sf-profile-trigger').click();
   const m=await page.locator('.sf-profile-panel').evaluate(n=>{const s=getComputedStyle(n),r=n.getBoundingClientRect(),trigger=document.querySelector('.sf-profile-trigger').getBoundingClientRect(),rules=[];
    function walk(items,href){for(const rule of items){if(rule.type===CSSRule.MEDIA_RULE&&!matchMedia(rule.conditionText).matches)continue;if(rule.selectorText){let matches=false;try{matches=n.matches(rule.selectorText);}catch{}if(matches){const props={};for(const key of ['width','min-width','max-width','padding','zoom','transform']){const value=rule.style.getPropertyValue(key);if(value)props[key]=value;}if(Object.keys(props).length)rules.push({source:href,selector:rule.selectorText,properties:props});}}if(rule.cssRules)walk(rule.cssRules,href);}}
    for(const sheet of document.styleSheets){try{walk(sheet.cssRules,sheet.href);}catch{}}
    return {width:r.width,height:r.height,minWidth:s.minWidth,maxWidth:s.maxWidth,padding:s.padding,zoom:s.zoom,transform:s.transform,dpr:devicePixelRatio,viewportScale:visualViewport.scale,triggerWidth:trigger.width,gap:r.top-trigger.bottom,rows:[...n.querySelectorAll(':scope>a,:scope>button')].map(e=>e.getBoundingClientRect().height),inlineStyle:n.getAttribute('style'),rules};
   });assert(m.width>=180&&m.width<280);assert.equal(m.gap,6);assert(m.rows.every(h=>h>=42&&h<=44));assert.equal(m.zoom,'1');assert.equal(m.transform,'none');assert.equal(m.viewportScale,1);
   const file=path.join(out,'density-'+density+'-'+width+'.png');await page.locator('.sf-profile-panel').screenshot({path:file});const png=fs.readFileSync(file);const screenshotWidth=png.readUInt32BE(16);assert(Math.abs(screenshotWidth-m.width*density)<=2);
   results.push({viewport:[width,height],density,screenshotWidth,...m});
  }await page.close();
 }fs.writeFileSync(path.join(out,'scale-audit.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results.map(r=>({viewport:r.viewport,dpr:r.dpr,cssWidth:r.width,screenshotWidth:r.screenshotWidth,rowHeights:r.rows,gap:r.gap}))));
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
