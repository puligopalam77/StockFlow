// Render existing Razor fixtures with current CSS. Scripts/API workflows are tested separately.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const folders=['field-validation-fixtures','password-page-fixtures','expense-modern-fixtures','company-views-fixtures','gst-ui-fixtures','store-management-fixtures','supplier-directory-fixtures','replenishment-fixtures','quotation-fixtures','low-stock-fixtures','business-profile-phase2-fixtures'];
const fixtures=folders.flatMap(folder=>{const dir=path.join(root,'tests/bin',folder);return fs.existsSync(dir)?fs.readdirSync(dir).filter(f=>f.endsWith('.html')).map(f=>path.join(dir,f)):[];});
const screenshots=path.join(root,'.artifacts/field-focus');fs.mkdirSync(screenshots,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage();page.setDefaultTimeout(6000);let html='',checked=0,screens=0,wrapped=0;const types=new Set();
 await page.route('**/*',route=>{
  const p=new URL(route.request().url()).pathname;
  if(p==='/focus-audit')return route.fulfill({contentType:'text/html',body:html});
  const file=path.resolve(assets,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
  return file.startsWith(assets+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
 });
 for(const [width,height]of [[1366,768],[390,844]]){
  await page.setViewportSize({width,height});
  for(const fixture of fixtures){
   html=fs.readFileSync(fixture,'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
   await page.goto('http://stockflow.test/focus-audit');
   // Avoid measuring a transitional color, and expose server-disabled fields for
   // style checks only. This is not a permission or interactive workflow claim.
   await page.addStyleTag({content:'*,*::before,*::after{transition:none!important;animation:none!important}'});
   const result=await page.evaluate(()=>{
    document.querySelectorAll('fieldset[disabled]').forEach(e=>e.disabled=false);
    const fields=[...document.querySelectorAll('input,select,textarea')].filter(e=>!e.disabled&&!['hidden','checkbox','radio','range','file','color','button','submit','reset'].includes(e.type)&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden');
    const failures=[],types=[],paint=document.createElement('span');document.body.append(paint);let wrapped=0;
    for(const e of fields){
     const state=e.matches('.is-invalid,.input-validation-error,.is-valid,[aria-invalid=true]'),before=getComputedStyle(e).borderColor,wrapper=e.closest('.sf-input-wrap'),beforeWrap=wrapper&&getComputedStyle(wrapper).borderColor;
     e.focus();if(document.activeElement!==e)continue;const s=getComputedStyle(e);
     paint.style.color=s.getPropertyValue('--sf-pink').trim()||'#ff007f';const accent=getComputedStyle(paint).color;
     const context=(e.id||e.name||e.tagName)+': '+e.type;
     if(s.outlineStyle!=='none'||s.boxShadow!=='none')failures.push(context+' has outline/shadow '+s.outline+' / '+s.boxShadow);
     if(state&&s.borderColor!==before)failures.push(context+' validation color changed');
     if(!state&&s.borderColor!==accent)failures.push(context+' focus border is '+s.borderColor+', expected '+accent);
     if(wrapper){wrapped++;const w=getComputedStyle(wrapper);if(w.boxShadow!=='none'||w.borderColor!==(state?beforeWrap:accent))failures.push(context+' wrapper duplicates focus or loses its focus/validation color');}
     types.push(e.type||e.tagName.toLowerCase());
    }
    paint.remove();return {failures,types,wrapped};
   });
   assert.deepEqual(result.failures,[],path.relative(root,fixture)+' at '+width);result.types.forEach(t=>types.add(t));checked+=result.types.length;wrapped+=result.wrapped;screens++;
   if(fixture.endsWith(path.join('expense-modern-fixtures','Create.html'))){await page.locator('#expense-incurred').focus();await page.screenshot({path:path.join(screenshots,'expense-date-'+width+'.png')});}
  }
 }
 assert(checked>200&&screens>30&&wrapped>0,'Representative fields and sign-in wrappers: '+JSON.stringify({checked,screens,wrapped}));
 // Exercise both shared dropdown trigger variants against the real expense CSS.
 html=fs.readFileSync(path.join(root,'tests/bin/expense-modern-fixtures/Create.html'),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
 await page.goto('http://stockflow.test/focus-audit');await page.addStyleTag({content:'*,*::before,*::after{transition:none!important;animation:none!important}'});
 await page.addScriptTag({path:path.join(assets,'js/common/select.js')});
 await page.evaluate(()=>{document.querySelector('#expense-fields').disabled=false;for(const [id,button]of [['expense-category',false],['expense-payee',true]]){const e=document.getElementById(id);e.replaceChildren(new Option('Select entry',''),new Option('Entry','1'));StockFlowSelect.create(e,{button});}});
 for(const id of ['expense-category','expense-payee']){
  const trigger=page.locator('#'+id+'-search');await trigger.focus();
  assert(await trigger.evaluate(e=>getComputedStyle(e).outlineStyle==='none'&&getComputedStyle(e).boxShadow==='none'));
  await trigger.click();await page.locator('#'+id+'-query').focus();
  assert(await page.locator('#'+id+'-query').evaluate(e=>getComputedStyle(e).outlineStyle==='none'&&getComputedStyle(e).boxShadow==='none'));await page.keyboard.press('Escape');
 }
 await page.evaluate(()=>{const b=document.createElement('button');b.id='audit-button';b.className='btn';b.textContent='Action';const c=document.createElement('input');c.id='audit-checkbox';c.type='checkbox';const submit=document.createElement('input');submit.id='audit-input-button';submit.type='button';submit.value='Action';document.body.append(b,c,submit);});
 for(const id of ['audit-button','audit-checkbox','audit-input-button']){const e=page.locator('#'+id);await e.focus();assert(await e.evaluate(e=>getComputedStyle(e).outlineStyle!=='none'),'Keyboard focus retained on '+id);}
 await page.emulateMedia({forcedColors:'active'});await page.locator('#expense-incurred').focus();
 assert(await page.locator('#expense-incurred').evaluate(e=>{const p=document.createElement('span');p.style.border='1px solid Highlight';document.body.append(p);const expected=getComputedStyle(p).borderColor,s=getComputedStyle(e);p.remove();return s.borderColor===expected&&s.outlineStyle==='none'&&s.boxShadow==='none';}),'Forced colors retains a single system-highlight border');
 console.log('PASS global field focus: '+checked+' actual fields over '+screens+' fixture/viewports; types '+[...types].sort().join(', ')+'. Sign-in wrappers, validation colors, input/button dropdown triggers and searches, action/toggle keyboard focus and forced colors verified. CSS/render coverage; API workflows tested separately.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
