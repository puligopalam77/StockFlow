// Presentation checks against rendered Razor fixtures and mocked APIs only.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const output=path.join(root,'.artifacts/stockbot-chat');fs.mkdirSync(output,{recursive:true});
const permittedArticles=JSON.parse(fs.readFileSync(path.join(__dirname,'../../tests/bin/help-retail-fixtures/billing.html'),'utf8').match(/<script id="help-data" type="application\/json">(.*?)<\/script>/s)[1]);
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1366,height:620}}),errors=[],questions=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{
   const request=route.request(),p=new URL(request.url()).pathname,json=body=>route.fulfill({json:body});
   if(p==='/Sales/Create')return route.fulfill({path:path.join(root,'tests/bin/help-retail-fixtures/billing.html'),contentType:'text/html'});
   if(p==='/api/help/assistant')return json({articles:permittedArticles,scope:'Test shop',contextStamp:'one',suggestions:["Summarize today's business",'What needs attention today?','Compare sales by shop this week','How much sales today?','Payment breakdown today','How much stock is available?']});
   if(p==='/api/help/assistant/health')return json({modelStatus:'available',model:'qwen3:4b',latencyMilliseconds:12,voiceStatus:'unavailable'});
   if(p==='/api/help/questions'){
    questions.push(request.postDataJSON());
    return json({status:'answered',message:'Synthetic test answer.',sections:[],links:[],context:{title:"Today's sales",scope:'Test shop',from:'2026-11-14',to:'2026-11-14',timeZone:'Asia/Kolkata',checkedAt:'2026-11-14T06:00:00Z',basis:'Completed bills only.',sources:[],followUps:['Yesterday']},facts:[{label:'Completed sales',value:'123.45',note:'Synthetic fixture, not business data.'},{label:'Completed bills',value:'2'}]});
   }
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404});
  });
  async function bounds(width,height){
   const m=await page.locator('#help-panel').evaluate(panel=>{
    const r=panel.getBoundingClientRect(),composer=panel.querySelector('#help-ai-question').getBoundingClientRect(),send=panel.querySelector('#help-ai-submit').getBoundingClientRect();
    return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,overflow:panel.scrollWidth>panel.clientWidth+1,outerScroll:panel.scrollHeight>panel.clientHeight+1,inputBottom:composer.bottom,inputTop:composer.top,sendBottom:send.bottom};
   });
   assert(m.left>=-1&&m.top>=-1&&m.right<=width+1&&m.bottom<=height+1&&!m.overflow&&!m.outerScroll,JSON.stringify(m));
   assert(m.inputTop>=m.top&&m.inputBottom<=height&&m.sendBottom<=height,'Composer remains in the viewport');
  }
  await page.goto('http://stockflow.test/Sales/Create');await page.locator('#help-open').click();
  await page.getByText('AI Ready',{exact:true}).waitFor();
  assert.equal(questions.length,0,'Opening the drawer never runs a business query');
  assert(await page.locator('#help-options').isHidden());
  assert.equal(await page.locator('.sf-stockbot-guide-tools>button').count(),4);
  assert(!(await page.locator('#help-panel').innerText()).includes('qwen3'),'Technical model details do not clutter chat');
  await bounds(1366,620);await page.screenshot({path:path.join(output,'compact-laptop.png')});
  await page.locator('#help-ai-question').fill('Draft question');
  const narrow=await page.locator('#help-panel').evaluate(e=>e.getBoundingClientRect().width);
  await page.locator('#help-expand').click();
  assert(await page.locator('#help-panel').evaluate((e)=>e.getBoundingClientRect().width>600));
  assert.equal(await page.locator('#help-ai-question').inputValue(),'Draft question');
  await page.locator('#help-menu-toggle').click();
  assert(await page.locator('#help-browse').isVisible());assert(await page.locator('#help-voice-auto').isVisible());
  await page.keyboard.press('Escape');assert(await page.locator('#help-options').isHidden());
  assert(await page.locator('#help-panel').evaluate(e=>e.open),'Escape closes the menu before the chat');
  await page.locator('#help-expand').click();
  assert.equal(await page.locator('#help-panel').evaluate(e=>e.getBoundingClientRect().width),narrow);
  await page.locator('#help-ai-question').fill('How much sales today?');
  await page.locator('#help-ai-question').press('Shift+Enter');assert.equal(questions.length,0,'Shift+Enter keeps a new line');
  await page.locator('#help-ai-question').press('Enter');await page.locator('.sf-stockbot-facts').waitFor();
  assert.equal(questions.length,1);assert.equal(questions[0].question,'How much sales today?');
  assert.equal(await page.locator('.sf-stockbot-facts>section').count(),2);
  assert(await page.locator('.sf-chat-fact-note small').isHidden(),'Calculation notes are available on demand');
  await page.locator('.sf-chat-fact-note summary').click();assert(await page.locator('.sf-chat-fact-note small').isVisible());
  await page.locator('#help-expand').click();
  assert.match(await page.locator('#help-ai-answer').textContent(),/123.45/,'Expanding preserves the actual response');
  await page.screenshot({path:path.join(output,'expanded-laptop.png')});
  await page.locator('#help-expand').click();
  for(const [width,height]of [[1366,768],[1280,600],[1024,600],[768,1024],[390,844],[320,640],[844,390],[390,320]]){
   await page.setViewportSize({width,height});await page.waitForFunction(()=>Math.abs(parseFloat(document.querySelector('#help-panel').style.getPropertyValue('--sf-help-viewport-height'))-innerHeight)<2);
   await bounds(width,height);
   await page.locator('#help-expand').click();await bounds(width,height);await page.locator('#help-expand').click();
   await page.screenshot({path:path.join(output,`chat-${width}x${height}.png`)});
  }
  const languages=await page.evaluate(()=>StockFlowLanguages.languages.map(([code])=>code));
  for(const code of languages){
   await page.evaluate(code=>StockFlowLanguage.setLanguage(code),code);
   await page.waitForFunction(()=>document.querySelector('#help-runtime-status').textContent===StockFlowLanguage.text('AI Ready'));
   if(code!=='en')assert((await page.locator('#help-runtime-status').textContent()).includes(' ('),code+': readiness caption is localized');
   await bounds(390,320);
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: compact/expanded chat, menu keyboard access, fixed composer, Enter/Shift+Enter, unchanged requests/results, 8 viewport sizes, all 22 translations and no automatic business queries.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
