const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const out=path.join(root,'.artifacts/stockbot-retail');fs.mkdirSync(out,{recursive:true});
const permittedArticles=JSON.parse(fs.readFileSync(path.join(__dirname,'../../tests/bin/help-retail-fixtures/billing.html'),'utf8').match(/<script id="help-data" type="application\/json">(.*?)<\/script>/s)[1]);
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const context=await browser.newContext({hasTouch:true,viewport:{width:1366,height:768}}),page=await context.newPage();
  const errors=[],questions=[];let changed=false;
  page.on('pageerror',e=>errors.push(e.message));
  const provenance={title:"Today's sales",scope:'Main shop / Sales floor',from:'2026-11-14',to:'2026-11-14',timeZone:'Asia/Kolkata',checkedAt:'2026-11-14T06:00:00Z',basis:'Completed invoice totals before returns. Collections are separate.',sources:[{label:'Open Sales',url:'/Sales'},{label:'Unsafe external source',url:'https://invalid.example/'}],followUps:['Yesterday','Payment breakdown today']};
  await page.route('**/*',route=>{
   const req=route.request(),url=new URL(req.url()),p=url.pathname,json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
   if(p==='/api/help/assistant')return json({articles:permittedArticles,scope:changed?'Assigned shop / Stock room':'Main shop / Sales floor',contextStamp:changed?'new-access':'original-access',suggestions:changed?['How many low stock products?']:['Summarize today\'s business','How much sales today?','What needs attention today?']});
   if(p==='/api/help/assistant/health')return json({modelStatus:changed?'model-missing':'available',model:'qwen3:4b',latencyMilliseconds:15,voiceStatus:'unavailable'});
   if(p==='/api/help/questions'){
    questions.push(req.postDataJSON());assert(req.headers()['x-csrf-token']);
    return json({status:'answered',message:'Summary of your permitted records.',sections:[],links:[],context:provenance,facts:[{label:"Today's sales",value:'₹100.00',note:'2 completed bills'},{label:'Money collected today',value:'₹75.00',note:'Payments received today; not sales revenue.'},{label:'Pending transfers',value:'Current transfers\n1. <img src=x onerror=alert(1)>\n2. Transfer TWO'}]});
   }
   if(p==='/Sales/Create')return route.fulfill({contentType:'text/html',path:path.join(root,'tests/bin/help-retail-fixtures/billing.html')});
   if(p.startsWith('/api/'))return json([]);
   const f=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return f.startsWith(assets+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile()?route.fulfill({path:f}):route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales/Create');await page.locator('#help-open').click();
  await page.getByText('AI Ready',{exact:true}).waitFor();
  assert.equal(questions.length,0,'Opening only checks capabilities and context');
  assert.match(await page.locator('#help-shop-scope').textContent(),/Main shop/);
  await page.locator('#help-suggested').getByRole('button',{name:/Summarize today's business/}).click();
  await page.locator('.sf-stockbot-facts').waitFor();
  assert.match(await page.locator('.sf-stockbot-answer-scope').textContent(),/Main shop.*2026-11-14/);
  assert.equal(await page.locator('#help-ai-answer a[href="/Sales"]').count(),1);
  assert.equal(await page.locator('#help-ai-answer a[href^="https:"]').count(),0,'Unapproved links are rejected');
  assert.equal(await page.locator('#help-ai-answer img').count(),0,'Business values are rendered as text');
  await page.locator('.sf-stockbot-basis summary').click();assert.match(await page.locator('.sf-stockbot-basis').textContent(),/before returns.*Collections are separate/);
  await page.locator('.sf-stockbot-facts').getByText('Show records',{exact:true}).click();assert.match(await page.locator('.sf-stockbot-facts').textContent(),/Transfer TWO/);
  assert.equal(await page.locator('.sf-help-followup').count(),2);
  for(const [width,height]of [[1920,1080],[1366,768],[1024,768],[768,1024],[844,390],[390,844],[320,640]]){
   await page.setViewportSize({width,height});
   const bounds=await page.locator('#help-panel').evaluate(el=>({left:el.getBoundingClientRect().left,right:el.getBoundingClientRect().right,top:el.getBoundingClientRect().top,bottom:el.getBoundingClientRect().bottom,overflow:el.scrollWidth>el.clientWidth+1}));
   assert(bounds.left>=-1&&bounds.right<=width+1&&bounds.top>=-1&&bounds.bottom<=height+1&&!bounds.overflow,JSON.stringify(bounds));
   assert(await page.locator('#help-ai-submit').isVisible());
   await page.screenshot({path:path.join(out,'stockbot-'+width+'.png')});
  }
  await page.locator('#help-close').click();changed=true;await page.locator('#help-open').click();
  await page.getByText('Built-in assistance available',{exact:true}).waitFor();
  await page.waitForFunction(()=>document.querySelector('#help-shop-scope').textContent.includes('Assigned shop'));
  assert(await page.locator('#help-ai-answer').isHidden(),'A scope/permission change removes previously displayed data');
  assert.equal(await page.locator('#help-suggested button').count(),1);
  assert.match(await page.locator('#help-runtime-detail').textContent(),/not installed/);
  assert.deepEqual(errors,[]);
  console.log('PASS: StockBot context/health, explicit summary requests, source/date cards, safe text and links, follow-ups, changed-access clearing and seven responsive viewports.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
