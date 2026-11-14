const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot'),fixture=path.join(root,'tests/bin/stockbot-guidance-fixtures');
const output=path.join(root,'.artifacts/stockbot-guidance');fs.mkdirSync(output,{recursive:true});
const permittedArticles=JSON.parse(fs.readFileSync(path.join(__dirname,'../../tests/bin/stockbot-guidance-fixtures/billing.html'),'utf8').match(/<script id="help-data" type="application\/json">(.*?)<\/script>/s)[1]);
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({hasTouch:true,viewport:{width:1366,height:900}}),errors=[],writes=[];
  const metadata=JSON.parse(fs.readFileSync(path.join(fixture,'sales.json'),'utf8'));let restricted=false, incompleteGuidance=false;
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{
   const req=route.request(),p=new URL(req.url()).pathname,json=(data,status=200,headers={})=>route.fulfill({status,headers,contentType:'application/json',body:JSON.stringify(data)});
   if(!['GET','HEAD'].includes(req.method()))writes.push({url:req.url(),body:req.postData()});
   if(p==='/api/help/assistant/guidance')return json(incompleteGuidance?{}:restricted?{...metadata,screen:null,fields:[],features:[],workflows:[]}:metadata);
   if(p==='/api/help/assistant')return json({articles:permittedArticles,scope:'Test shop',contextStamp:restricted?'revoked':'initial',suggestions:['How much sales today?']});
   if(p==='/api/help/assistant/health')return json({modelStatus:'disabled',model:'qwen3:4b',voiceStatus:'unavailable'});
   if(p==='/api/test-failure')return json({message:'SELECT private_column FROM private_table; password=secret'},409,{'X-StockFlow-Error-Code':'DUPLICATE_RECORD'});
   if(p==='/Sales/Create')return route.fulfill({path:path.join(fixture,'billing.html'),contentType:'text/html'});
   if(p.startsWith('/api/'))return json([]);
   const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
   return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
  });
  await page.goto('http://stockflow.test/Sales/Create');
  await page.locator('#help-open').click();await page.getByText('Built-in assistance available',{exact:true}).waitFor();
  await page.locator('#help-explain-screen').click();await page.waitForFunction(()=>!document.querySelector('#help-guidance-result').hidden);
  assert.match(await page.locator('#help-guidance-result').textContent(),/one payment method/);
  await page.locator('#help-start-guide').click();assert.match(await page.locator('#help-guidance-result').textContent(),/Step 1 of 5/);
  await page.locator('#help-guidance-result').getByRole('button',{name:'Next Step',exact:true}).click();
  assert.match(await page.locator('#help-guidance-result').textContent(),/Step 2 of 5/);
  await page.locator('#help-guidance-result').getByRole('button',{name:'Show on Screen',exact:true}).click();
  assert(!await page.locator('#help-panel').evaluate(e=>e.open));assert(await page.locator('#sale-scan').evaluate(e=>e.classList.contains('sf-stockbot-target')));
  await page.locator('.sf-stockbot-guide-banner').getByRole('button',{name:'Back to StockBot'}).click();
  assert.equal(await page.locator('.sf-stockbot-target').count(),0);
  await page.locator('#help-menu-toggle').click();await page.locator('#help-guidance-menu summary').click();await page.locator('#help-feature-search').fill('expenses');
  assert.equal(await page.locator('#help-feature-links a[href="/Expenses"]').count(),1);
  await page.locator('#help-feature-search').fill('another tenant');assert.equal(await page.locator('#help-feature-links a').count(),0);
  await page.locator('#help-onboarding').getByRole('button',{name:'Skip introduction'}).click();assert(await page.locator('#help-onboarding').isHidden());
  await page.locator('#help-onboarding-reset').click();assert(await page.locator('#help-onboarding').isVisible());
  await page.locator('#help-guidance-menu summary').click();await page.locator('#help-explain-fields').click();
  await page.locator('#help-guidance-result').getByRole('button',{name:'Selling price',exact:true}).click();
  assert.match(await page.locator('#help-guidance-result').textContent(),/tax and discount policies/);
  await page.locator('#help-close').click();
  await page.evaluate(()=>{
   const form=document.createElement('form');form.id='local-guide-form';form.noValidate=true;
   form.innerHTML='<label for="local-price">Selling price *</label><input id="local-price" name="price" type="number" required min="0" value="-1"><label for="local-email">Email *</label><input id="local-email" type="email" required value="invalid"><label for="local-password">Password *</label><input id="local-password" type="password" required value="SyntheticSecretNotToSend">';
   document.querySelector('.sf-main').append(form);
  });
  await page.locator('#local-price').focus();
  const original=await page.locator('#local-guide-form input').evaluateAll(es=>es.map(e=>e.value));
  await page.locator('#help-open').click();await page.locator('#help-check-form').click();
  await page.locator('#help-guidance-result li').first().waitFor();assert.match(await page.locator('#help-guidance-result').textContent(),/browser|local/i);
  assert.deepEqual(await page.locator('#local-guide-form input').evaluateAll(es=>es.map(e=>e.value)),original,'Guidance cannot change entered values');
  assert.equal(writes.length,0,'Explain, Next, field help and form checks never submit or send form values');
  await page.evaluate(()=>fetch('/api/test-failure'));await page.locator('#help-explain-error').click();
  const advice=await page.locator('#help-guidance-result').textContent();assert.match(advice,/Record already exists/);assert(!advice.includes('private_column')&&!advice.includes('password=secret'),'Only structured codes select advice; raw response text is ignored');
  for(const [width,height]of [[1920,1080],[1366,768],[1024,768],[768,1024],[844,390],[390,844],[320,640]]){
   await page.setViewportSize({width,height});assert(await page.locator('#help-panel').evaluate(e=>e.scrollWidth<=e.clientWidth+1));assert(await page.locator('#help-ai-submit').isVisible());
   await page.screenshot({path:path.join(output,'guide-'+width+'.png')});
  }
  await page.locator('#help-start-guide').click();
  for(const code of ['te','hi','en']){
   await page.evaluate(code=>StockFlowLanguage.setLanguage(code),code);
   const expected=await page.evaluate(()=>StockFlowLanguage.text('Explain This Screen'));
   assert.equal(await page.locator('#help-explain-screen').textContent(),expected,'StockBot tools follow the selected language');
   if(code!=='en')assert(expected.includes(' ('),'StockBot tool has a bundled translation');
   for(const [id,caption] of [['help-feature-search','Try expenses, staff or barcode'],['help-ai-question','Ask a question...']])
    assert.equal(await page.locator('#'+id).getAttribute('placeholder'),await page.evaluate(s=>StockFlowLanguage.text(s),caption),'StockBot placeholders follow the selected language');
   const title=metadata.screen.title;
   assert.equal(await page.locator('#help-guidance-result h4').textContent(),await page.evaluate(s=>StockFlowLanguage.text(s),title),'Open guide updates in place');
   assert.match(await page.locator('#help-guidance-result small').first().textContent(),/Step 1 of 5/,'Language switch retains walkthrough position');
  }
  await page.locator('#help-close').click();restricted=true;await page.locator('#help-open').click();
  await page.waitForFunction(()=>document.querySelector('#help-guidance-result').hidden);await page.locator('#help-explain-screen').click();assert.match(await page.locator('#help-guidance-result').textContent(),/No approved walkthrough/);
  // HTTP success without the required metadata must also remove the old guide.
  await page.locator('#help-close').click();
  restricted=false;incompleteGuidance=true;
  await page.locator('#help-open').click();
  await page.waitForFunction(()=>document.querySelector('#help-workflows').children.length===0);
  await page.locator('#help-explain-screen').click();
  assert.match(await page.locator('#help-guidance-result').textContent(),/No approved walkthrough/);
  assert.equal(await page.locator('#help-workflows button').count(),0,'Revoked create permission removes its walkthroughs');
  assert.deepEqual(errors,[]);console.log('PASS: Screen explanation, safe Next/Previous/highlighting, field glossary, feature search, optional onboarding, local-only form validation, structured error privacy, revoked guides and seven responsive viewports.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
