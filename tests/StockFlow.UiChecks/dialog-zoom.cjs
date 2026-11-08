// A local test-only extension uses the browser's real tab zoom API. No CSS scale
// or device-scale emulation: devicePixelRatio must report the actual 200% zoom.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const out=path.resolve(__dirname,'../../.artifacts/dialog-standard'),ext=path.join(out,'zoom-extension');
(async()=>{fs.mkdirSync(ext,{recursive:true});fs.writeFileSync(path.join(ext,'manifest.json'),JSON.stringify({manifest_version:3,name:'StockFlow local dialog zoom check',version:'1.0',permissions:['tabs'],background:{service_worker:'worker.js'}}));
fs.writeFileSync(path.join(ext,'worker.js'),"chrome.tabs.onUpdated.addListener((id,change,tab)=>{if(change.status==='complete'&&tab.url?.startsWith('http://127.0.0.1:55863/'))chrome.tabs.setZoom(id,2);});");
const results=[];
for(const [width,height] of [[1920,1080],[1366,768],[1280,720],[768,1024]]){
const context=await chromium.launchPersistentContext('',{channel:'msedge',headless:true,viewport:null,ignoreDefaultArgs:['--disable-extensions'],args:[`--window-size=${width},${height}`,'--disable-extensions-except='+ext,'--load-extension='+ext]});
try {const worker=context.serviceWorkers()[0]||await context.waitForEvent('serviceworker');for(const [route,id] of [['/','sf-confirm-dialog'],['/Expenses/Create','expense-master-dialog'],['/Categories','editor'],['/Brands','editor']]){
 const page=await context.newPage();await page.goto('http://127.0.0.1:55863'+route);await worker.evaluate(async()=>{for(const tab of await chrome.tabs.query({}))if(tab.url?.startsWith('http://127.0.0.1:55863/'))await chrome.tabs.setZoom(tab.id,2);});await page.waitForFunction(()=>devicePixelRatio===2);
 await page.evaluate(id=>{if(id==='sf-confirm-dialog')StockFlowDialog.discard('DRF-0020: This unfinished bill will be permanently removed. Completed invoices and stock records will not be affected.',{title:'Discard saved bill?',confirmText:'Discard Bill'});else document.getElementById(id).showModal();},id);
 const r=await page.locator('#'+id).evaluate(n=>{const r=n.getBoundingClientRect();return {width:innerWidth,height:innerHeight,zoom:devicePixelRatio,left:r.left,right:r.right,top:r.top,bottom:r.bottom,overflow:n.scrollWidth-n.clientWidth};});
 assert(r.left>=0&&r.right<=r.width+1&&r.top>=0&&r.bottom<=r.height+1&&r.overflow<=1,JSON.stringify({route,width,...r}));
 const buttons=page.locator('#'+id+' button:visible');if(await buttons.count())await buttons.last().scrollIntoViewIfNeeded();
 const cdp=await context.newCDPSession(page),shot=await cdp.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});fs.writeFileSync(path.join(out,`zoom200-${width}-${route.replace(/\W/g,'')||'confirmation'}.png`),Buffer.from(shot.data,'base64'));await cdp.detach();
 results.push({route,physicalWindow:[width,height],...r});
 await page.evaluate(()=>{document.querySelectorAll('dialog[open]').forEach(n=>n.close());StockFlowUnsaved?.allowNavigation();});await page.close();
} }finally{await context.close();}}
fs.writeFileSync(path.join(out,'zoom-results.json'),JSON.stringify(results,null,2));console.log('PASS: '+results.length+' actual 200% browser-zoom checks, viewport bounds, horizontal overflow and reachable actions.');
})().catch(e=>{console.error(e);process.exitCode=1;});
