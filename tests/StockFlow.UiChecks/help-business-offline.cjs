const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:390,height:844}});
  const errors=[],questions=[],votes=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/*',route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/api/help/questions'){
    questions.push(route.request().postDataJSON());
    assert(route.request().headers()['x-csrf-token']);
    return route.fulfill({status:questions.length===1?503:200,contentType:'application/json',body:JSON.stringify(questions.length===1?
     {status:'unavailable',message:'The records could not be reached. Please retry; no total was calculated.',sections:[],links:[]}:
     {status:'answered',message:'0',sections:[],links:[],answerId:'a'.repeat(32)})});
   }
   if(url.pathname==='/api/help/questions/feedback'){
    votes.push(route.request().postDataJSON());assert(route.request().headers()['x-csrf-token']);
    return route.fulfill({contentType:'application/json',body:'{"message":"Thank you for your feedback."}'});
   }
   if(url.pathname==='/Sales/Create')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/help-panel-fixtures/billing.html'),'utf8')});
   if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
   const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales/Create');await page.locator('#help-open').click();
  assert.equal(questions.length,0,'Opening does not fetch business data');
  assert((await page.locator('#help-suggested').textContent()).includes('quantity sold today'));
  await page.locator('#help-ai-question').fill('Quantity sold yesterday');await page.locator('#help-ai-submit').click();
  await page.getByRole('button',{name:'Retry',exact:true}).waitFor();
  assert((await page.locator('#help-ai-answer').textContent()).includes('no total was calculated'));
  assert.equal(await page.locator('#help-ai-question').inputValue(),'');
  await page.getByRole('button',{name:'Retry',exact:true}).click();
  await page.getByRole('button',{name:'Not helpful',exact:true}).waitFor();
  assert.equal(await page.locator('#help-ai-answer').textContent(),'0');
  assert.equal(questions[0].question,questions[1].question,'Retry repeats the same question');
  assert.equal(questions[0].conversationId,questions[1].conversationId,'Retry preserves conversation');
  await page.getByRole('button',{name:'Not helpful',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('#help-answer-tools').textContent.includes('Thank you'));
  assert.deepEqual(votes,[{answerId:'a'.repeat(32),helpful:false}]);
  const sample='Repair deliveries:\n1. REP-001 — READY\n2. REP-002 — REPAIRING';
  await page.route('**/api/help/questions',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({status:'answered',message:sample,sections:[],links:[],answerId:'b'.repeat(32)})}));
  await page.locator('#help-ai-question').fill('Which repairs are overdue or ready for collection?');
  await page.locator('#help-ai-submit').click();
  await page.waitForFunction(()=>document.querySelector('#help-ai-answer').textContent.includes('REP-002'));
  assert.equal(await page.locator('#help-ai-answer p').evaluate(el=>getComputedStyle(el).whiteSpace),'pre-line','List line breaks remain visible');
  for(const width of [320,390,768,1366]){
   await page.setViewportSize({width,height:844});
   assert(await page.locator('#help-panel').evaluate(el=>el.scrollWidth<=el.clientWidth+1));
   assert(await page.locator('#help-ai-submit').isVisible());
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: No fetch on opening, contextual suggestions, failed lookup/Retry, confirmed zero, input clearing, feedback and mobile layout.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
