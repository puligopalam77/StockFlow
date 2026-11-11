const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{const u=new URL(route.request().url());if(u.pathname==='/Sales/Create')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/billing-ui-fixtures/Billing.html'),'utf8')});if(u.pathname.endsWith('.js')&&!/\/(stockflow-shell|billing-layout|billing-customer|responsive-shell|bootstrap\.bundle\.min)(\.[a-z0-9]{10})?\.js$/.test(u.pathname))return route.fulfill({contentType:'text/javascript',body:''});const file=path.join(root,'src/StockFlow.Web/wwwroot',u.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({contentType:'application/json',body:'{}'});});
 let checks=0;
 for(const [width,height]of [[1920,1080],[1536,864],[1366,768],[1280,720],[1024,768],[768,1024],[375,667],[1280,600],[1067,512],[911,512]])if(!process.env.STOCKFLOW_LAYOUT_WIDTH||width===Number(process.env.STOCKFLOW_LAYOUT_WIDTH))for(const collapsed of [false,true]){
  await page.setViewportSize({width,height});await page.goto('http://stockflow.test/Sales/Create');
  await page.evaluate(c=>{document.querySelector('#sale-editor').disabled=false;document.body.classList.toggle('sf-nav-collapsed',c);window.originalComplete=document.querySelector('#complete-sale');},collapsed);
  await page.locator('#billing-notes > summary').evaluate(n=>{if(!n.parentElement.open)n.click();});await page.locator('#sale-notes').fill('Notes retained while the page scrolls and resizes.');
  for(const populated of [false,true]){
   if(populated)await page.evaluate(()=>{
    document.querySelector('#customer-current').hidden=false;document.querySelector('#customer-search').hidden=true;
    document.querySelector('#customer-name').textContent='Sri Lakshmi Automotive Replacement Parts and Retail Accessories Trading Company';document.querySelector('#customer-mobile').textContent='Mobile: 9876543210';
    document.querySelector('#sale-empty').hidden=true;const table=document.querySelector('.billing-cart');table.closest('.table-responsive').hidden=false;
    table.querySelector('tbody').innerHTML=Array.from({length:24},(_,i)=>'<tr><td class="billing-item-product"><strong>Replacement product '+(i+1)+'</strong></td><td>SKU-'+i+'</td><td>30</td><td><input aria-label="Test quantity '+i+'" value="1" style="width:44px" /></td><td>100.00</td><td>0.00</td><td>18%</td><td>118.00</td><td><button type="button">Remove</button></td></tr>').join('');
    const warning=document.querySelector('#billing-payment-warning');warning.hidden=false;warning.textContent='Enter a payment reference before completing this sale.';
   });
   await page.waitForTimeout(300);
   const flow=await page.evaluate(()=>{const button=document.querySelector('#complete-sale'),payment=document.querySelector('#billing-payment-card'),checkout=document.querySelector('.billing-checkout-pane');const problems=[];
    for(const node of [button,payment,checkout,document.querySelector('.billing-cart').closest('.table-responsive')])for(let n=node;n&&n!==document.body;n=n.parentElement){const s=getComputedStyle(n);if(/auto|scroll|hidden|clip/.test(s.overflowY)&&n.scrollHeight>n.clientHeight+1)problems.push(n.id||n.className);}
    return {same:button===window.originalComplete,count:document.querySelectorAll('#complete-sale').length,integrated:button.parentElement.classList.contains('billing-checkout-actions')&&button.parentElement.previousElementSibling===payment&&button.form?.id==='sale-form',position:getComputedStyle(checkout).position,maxHeight:getComputedStyle(checkout).maxHeight,detached:document.querySelectorAll('.billing-checkout-scroll,.billing-checkout-actions').length,problems,overflow:document.documentElement.scrollWidth>innerWidth+1,sideBySide:checkout.getBoundingClientRect().left>=document.querySelector('.billing-catalog-pane').getBoundingClientRect().right-1,wide:document.querySelector('#billing-workspace').clientWidth>=980};});
   assert(flow.same&&flow.integrated&&flow.count===1);assert.equal(flow.detached,1);assert.equal(flow.position,'static');assert.equal(flow.maxHeight,'none');assert.deepEqual(flow.problems,[]);assert(!flow.overflow);assert.equal(flow.sideBySide,flow.wide);
   for(const selector of ['#sale-notes','#complete-sale']){
    await page.locator(selector).scrollIntoViewIfNeeded();await page.waitForTimeout(200);
    const r=await page.locator(selector).boundingBox(),bot=await page.locator('.sf-help-open').boundingBox();assert(r.y>=50&&r.y+r.height<=height+1,JSON.stringify({width,height,populated,selector,r}));
    if(bot)assert(bot.x>=r.x+r.width||bot.x+bot.width<=r.x||bot.y>=r.y+r.height||bot.y+bot.height<=r.y,JSON.stringify({width,height,populated,selector,r,bot}));
   }
   for(const delta of [-120,120]){
    await page.evaluate(d=>scrollBy({top:d,behavior:"instant"}),delta);await page.waitForTimeout(100);
    const collisions=await page.evaluate(()=>{const launcher=document.querySelector('.sf-help-open'),r=launcher.getBoundingClientRect();
     // The sticky header intentionally paints above fields scrolling behind it.
     // Its launcher must remain clickable; floating launchers still need field clearance.
     if(launcher.closest('.sf-topbar'))return launcher.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2))?[]:['Header launcher is covered'];
     return [...document.querySelectorAll('#billing-payment-card :is(input,textarea,button),#billing-summary input')].filter(n=>{const b=n.getBoundingClientRect();return b.width&&b.height&&b.top<innerHeight&&b.bottom>52&&b.left<r.right&&b.right>r.left&&b.top<r.bottom&&b.bottom>r.top;}).map(n=>n.id||n.className);});if(collisions.length)console.log(JSON.stringify(await page.evaluate(()=>({bot:document.querySelector('.sf-help-open').getBoundingClientRect().toJSON(),clearance:document.querySelector('.sf-help-open').style.cssText,fields:[...document.querySelectorAll('#billing-payment-card :is(input,textarea,button),#billing-summary input')].map(n=>({id:n.id,r:n.getBoundingClientRect().toJSON()})).filter(x=>x.r.height&&x.r.y<innerHeight&&x.r.bottom>0)}))));
    assert.deepEqual(collisions,[],JSON.stringify({width,height,populated,delta,collisions}));
   }
   if(populated)assert(await page.locator('#customer-name').evaluate(n=>getComputedStyle(n).textOverflow==='ellipsis'),'Long customer name truncates');
   assert.equal(await page.locator('#sale-notes').inputValue(),'Notes retained while the page scrolls and resizes.');
   const tiles=await page.locator('.billing-payment-tile').evaluateAll(nodes=>nodes.map(n=>({text:n.textContent,r:n.getBoundingClientRect().toJSON()})));assert.equal(tiles.length,5);for(const t of tiles)assert(t.r.width>0&&t.r.x>=0&&t.r.right<=width);if(width===1920)assert(tiles.every(t=>Math.abs(t.r.y-tiles[0].r.y)<1));
   checks++;
  }
 }
 assert.deepEqual(errors,[]);console.log('PASS '+checks+' natural-flow layouts: ten viewports, both sidebar states, empty and 24-product bills, long customer name and validation text; no nested vertical scrolling, original button in the sticky column footer after Notes, five readable payment methods, StockBot clear of Notes and completion action.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
