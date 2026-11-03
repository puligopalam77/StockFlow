const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const source=fs.readFileSync(path.join(root,'src/StockFlow.Web/Help/RetailScreenGuides.cs'),'utf8');
const guides=new Map();for(const line of source.split('\n')){const match=line.match(/^\s*\[("[^"]+")\] = new\(("(?:\\.|[^"])*"), \[(.*)\]\),/);if(match)guides.set(JSON.parse(match[1]),{purpose:JSON.parse(match[2]),steps:JSON.parse('['+match[3]+']')});}
const excluded=new Set(['/Error','/Privacy','/Account/Login','/Account/ForgotPassword','/Account/Logout','/Sales/Invoice']);
const screens=[];function scan(dir){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){const file=path.join(dir,entry.name);if(entry.isDirectory())scan(file);else if(file.endsWith('.cshtml')&&!entry.name.startsWith('_')&&!file.includes('Shared'))screens.push('/'+path.relative(path.join(root,'src/StockFlow.Web/Pages'),file).replaceAll('\\','/').replace(/\.cshtml$/,''));}}scan(path.join(root,'src/StockFlow.Web/Pages'));
assert.equal(screens.length,58);assert.deepEqual(screens.filter(screen=>!excluded.has(screen)&&!guides.has(screen)),[]);
assert.equal(guides.size,52);assert([...guides.values()].every(guide=>guide.steps.length===3));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});try{
  const page=await browser.newPage({viewport:{width:1280,height:900},reducedMotion:'reduce'}),errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/test'){
    const guide=guides.get(url.searchParams.get('screen'));
    return route.fulfill({contentType:'text/html',body:`<!doctype html><html><head><meta name="viewport" content="width=device-width"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/css/retail-usability.css"></head><body class="sf-app sf-retail-ui"><main id="main-content" class="sf-main" style="padding:16px"><header class="sf-page-heading"><h1>Shop task</h1></header><nav class="sf-breadcrumb">Home / Shop task</nav><form><label for="amount">Amount</label><input id="amount" type="number" step="0.01" class="form-control"><details id="optional"><summary>More details</summary><label for="required">Required detail</label><input id="required" class="form-control" required></details><button type="button" class="btn">Save</button></form><aside id="retail-screen-guide" class="retail-screen-guide"><details><summary><span>${guide.purpose}</span><span class="retail-guide-prompt">Show steps</span></summary><ol>${guide.steps.map(step=>'<li>'+step+'</li>').join('')}</ol></details></aside></main><script src="/js/retail-usability.js"></script></body></html>`});
   }
   const file=path.resolve(assets,'.'+url.pathname);if(file.startsWith(assets+path.sep)&&fs.existsSync(file))return route.fulfill({path:file});return route.fulfill({status:404,body:''});
  });
  for(const screen of ['/Sales/Create','/Purchases/Create','/Products/Index','/Service/Create','/Warranty/Lookup','/Inventory/Index','/Reports/Activity','/Admin/Users','/Settings/Company']){
   await page.goto('http://stockflow.test/test?screen='+encodeURIComponent(screen));
   assert.equal(await page.locator('.sf-breadcrumb').evaluate(el=>el.nextElementSibling.id),'retail-screen-guide');
   await page.locator('#retail-screen-guide summary').click();assert.equal(await page.locator('#retail-screen-guide li:visible').count(),3);
   assert.equal(await page.locator('#amount').getAttribute('inputmode'),'decimal');
   await page.locator('#required').evaluate(input=>input.reportValidity());assert(await page.locator('#optional').evaluate(el=>el.open));
   for(const width of [1280,390,320]){
    await page.setViewportSize({width,height:900});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    assert(await page.locator('#amount').evaluate(input=>input.getBoundingClientRect().height>=38));
   }
   await page.emulateMedia({media:'print'});assert.equal(await page.locator('#retail-screen-guide').isVisible(),false);await page.emulateMedia({media:'screen'});
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: all 58 screen sources reviewed; 52 task guides covered; shared guidance placement, readable controls, mobile widths, hidden-field validation and print behavior.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
