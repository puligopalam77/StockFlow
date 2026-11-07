const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const {selectValue}=require('./address-test-helpers.cjs');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1366,height:768},deviceScaleFactor:1.5});page.setDefaultTimeout(6000);
 let saved='en',reject=false,readOnly=false;const writes=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{
  const req=route.request(),p=new URL(req.url()).pathname,json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/Purchases/Create'){
   let html=fs.readFileSync(path.join(root,'tests/bin/screen-language-fixtures/Purchases-Create.html'),'utf8');
   if(readOnly)html=html.replace(/data-can-save="true"/g,'data-can-save="false"');
   return route.fulfill({contentType:'text/html',body:html});
  }
  if(p==='/api/screenlanguages'){
   if(req.method()==='PUT'){assert(req.headers()['x-csrf-token']);writes.push(req.postDataJSON());if(reject)return json({message:'Language save rejected.'},403);saved=req.postDataJSON().language;return route.fulfill({status:204});}
   return json([{screen:'application',language:saved,captions:{}}]);
  }
  if(p.startsWith('/api/'))return json([]);
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 await page.goto('http://stockflow.test/Purchases/Create');
 const source=page.locator('#language-choice'),trigger=page.locator('#language-choice-search');
 await trigger.waitFor();assert.equal(await source.locator('option').count(),23);
 await selectValue(source,'te');await page.waitForFunction(()=>!document.querySelector('#language-choice').disabled);assert.equal(saved,'te');assert.equal(writes.length,1);
 reject=true;await selectValue(source,'hi');await page.locator('#language-message').waitFor({state:'visible'});assert.equal(saved,'te');assert.equal(await source.inputValue(),'te');assert.equal(writes.length,2);
 for(const [width,height]of [[1366,768],[1024,768],[768,1024],[390,844],[375,667],[320,640]]){
  await page.setViewportSize({width,height});await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+1);
  await trigger.click();await page.locator('#language-choice-panel').waitFor({state:'visible'});
  assert(await page.locator('#language-choice-panel').evaluate(e=>{const r=e.getBoundingClientRect();return r.x>=0&&r.y>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1;}),'Header popup fits '+width);
  await page.keyboard.press('Escape');
 }
 readOnly=true;await page.goto('http://stockflow.test/Purchases/Create');await trigger.waitFor();assert(await trigger.isDisabled());assert.equal(writes.length,2);
 assert.deepEqual(errors,[]);console.log('PASS actual header dropdown: 23 languages, exact change/CSRF, successful save, rejected save restoration, read-only permission, six responsive popup bounds at DPR 1.5.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
