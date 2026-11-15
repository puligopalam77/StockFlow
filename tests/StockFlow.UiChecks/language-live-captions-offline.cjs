// Local assets and mocked settings only; no database or external translator.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../../src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const errors=[];
 try{
  const page=await browser.newPage();page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{
   const name=new URL(route.request().url()).pathname;
   if(name==='/')return route.fulfill({contentType:'text/html',body:`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width"><meta name="csrf-token" content="test">
    <link rel="stylesheet" href="/css/select.css"><main id="main-content">
    <h1>Staff accounts &amp; access</h1><legend>1. Staff Information</legend>
    <span data-i18n id="caption">Access Preview</span><span class="admin-status">Active</span>
    <table><tr><td id="empty-table" colspan="4">No matching users</td></tr><tr><td id="record-cell">Employee</td></tr></table>
    <table><thead><tr><th>Full name</th></tr></thead><tbody><tr><td id="mobile-record" data-label="Full name">Employee</td></tr></tbody></table>
    <label for="type">Account type *</label><select id="type" data-sf-select="search"><option value="EMPLOYEE" data-i18n>Employee</option><option value="SHOP_ADMIN" data-i18n>Shop Admin</option></select>
    <label for="shop">Shop</label><select id="shop" data-sf-select="search"><option value="1">Employee</option></select>
    <select id="implicit-value"><option data-i18n>Employee</option></select>
    <input id="name" value="Employee" placeholder="Full name"><textarea placeholder="Description" aria-label="Description"></textarea>
    <span data-no-translate id="saved-name">Active</span><p id="caption-parent"><span data-no-translate>Employee</span></p>
    <aside data-no-translate><button id="guide" data-i18n-source="Explain This Screen">Explain This Screen</button><input id="help-placeholder" placeholder="Full name" data-i18n-attributes="placeholder"></aside>
    </main><script src="/js/common/language.js"></script><script src="/js/common/select.js"></script>`});
   if(name==='/api/screenlanguages')return route.fulfill({json:[]});
   const file=path.join(root,name);return fs.existsSync(file)?route.fulfill({path:file}):route.fulfill({status:404});
  });
  await page.goto('http://stockflow.test/');
  await page.waitForFunction(()=>document.querySelector('#type').sfSelect);
  for(const width of [1440,768,390,320]){
   await page.setViewportSize({width,height:900});
   for(const code of ['te','hi','mr','en','te','en']){
    await page.evaluate(code=>StockFlowLanguage.setLanguage(code),code);
    for(const [selector,caption]of [['h1','Staff accounts & access'],['legend','1. Staff Information'],['#caption','Access Preview'],['.admin-status','Active'],['#guide','Explain This Screen']]){
     const expected=await page.evaluate(s=>StockFlowLanguage.text(s),caption);
     if(code!=='en')assert(expected.includes(' ('),`${code}: bundled translation for ${caption}`);
     assert.equal(await page.locator(selector).textContent(),expected,`${width} ${code} ${selector}`);
    }
    const expected=await page.evaluate(()=>StockFlowLanguage.text('Employee'));
    assert.equal(await page.locator('#type-search').inputValue(),expected);
    assert.equal(await page.locator('#type').inputValue(),'EMPLOYEE');
    assert.equal(await page.locator('#implicit-value').inputValue(),'Employee','implicit native values stay stable');
    assert.equal(await page.evaluate(()=>StockFlowLanguage.originalText(document.querySelector('label[for="type-search"]'))),'Account type *','validation and field help retain the English caption');
    assert.equal(await page.locator('#shop-search').inputValue(),'Employee','saved shop name stays literal');
    assert.equal(await page.locator('#name').inputValue(),'Employee');
    assert.equal(await page.locator('#saved-name').textContent(),'Active');
    assert.equal(await page.locator('#empty-table').textContent(),await page.evaluate(()=>StockFlowLanguage.text('No matching users')));
    assert.equal(await page.locator('#record-cell').textContent(),'Employee');
    assert.equal(await page.locator('#mobile-record').getAttribute('data-label'),await page.evaluate(()=>StockFlowLanguage.text('Full name')),'stacked mobile table labels follow the header');
    assert.equal(await page.locator('#mobile-record').textContent(),'Employee');
    assert.equal(await page.locator('#caption-parent').textContent(),'Employee');
    assert.equal(await page.locator('textarea').getAttribute('placeholder'),await page.evaluate(()=>StockFlowLanguage.text('Description')));
    assert.equal(await page.locator('#help-placeholder').getAttribute('placeholder'),await page.evaluate(()=>StockFlowLanguage.text('Full name')));
   }
  }
  await page.evaluate(()=>StockFlowLanguage.setLanguage('te'));
  await page.evaluate(()=>{
   const p=document.createElement('p');p.id='late';p.textContent='Access Preview';document.querySelector('main').append(p);
   document.querySelector('#name').placeholder='Account type';
  });
  await page.waitForFunction(()=>document.querySelector('#late').textContent===StockFlowLanguage.text('Access Preview'));
  await page.waitForFunction(()=>document.querySelector('#name').placeholder===StockFlowLanguage.text('Account type'));
  assert((await page.locator('#late').textContent()).includes(' ('),'new caption has a real translation');
  await page.evaluate(()=>document.querySelector('#type').sfSelect.open());
  await page.locator('#type-query').fill('Employee');
  await page.evaluate(()=>StockFlowLanguage.setLanguage('hi'));
  assert.equal(await page.locator('#type-query').inputValue(),'Employee','open dropdown keeps the search query');
  assert.equal(await page.locator('#type-panel [role="option"]').first().textContent(),await page.evaluate(()=>StockFlowLanguage.text('Employee')));
  assert.equal(await page.locator('#type').inputValue(),'EMPLOYEE');
  await page.evaluate(()=>document.querySelector('#type').sfSelect.close());
  const beforeFailure=await page.locator('h1').textContent();
  assert(await page.evaluate(()=>StockFlowLanguage.setLanguage('missing-pack').then(()=>false,()=>true)),'missing language file reports failure');
  assert.equal(await page.locator('h1').textContent(),beforeFailure,'failed language load retains usable captions');
  await page.evaluate(()=>StockFlowLanguage.setLanguage('en'));
  assert.equal(await page.locator('#late').textContent(),'Access Preview','English remains available after a failed load');
  assert.deepEqual(errors,[]);
  console.log('PASS: live captions, fixed dropdowns, protected saved names/values, late captions and attributes, English reset, desktop/tablet/mobile widths.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
