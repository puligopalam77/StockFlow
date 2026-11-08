// Rendered Razor pages + mocked APIs only; no connection to the application database.
const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'../..'),staticRoot=path.join(root,'src/StockFlow.Web/wwwroot');
const {chromium}=require(path.join(__dirname,'bin/Debug/net10.0/.playwright/package'));
const fixtures=path.join(root,'tests/bin/phase13-ui-fixtures'),base='http://localhost:51998';
(async()=>{
    const browser=await chromium.launch({channel:'msedge',headless:true});
    try {
        const page=await browser.newPage(),errors=[]; let role='Admin',fail=false;
        page.on('pageerror',e=>errors.push(e.message));
        await page.route('**/*',async route=>{
            const url=new URL(route.request().url()),p=url.pathname;
            const json=(value,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(value)});
            if(p==='/api/dashboard/inflow-outflow-trend') {
                if(fail) return json({message:'Test outage'},500);
                const count={'7days':7,'30days':30,'6months':6,'12months':12}[url.searchParams.get('period')];
                return json(Array.from({length:count},(_,i)=>({period:'Day '+(i+1),inflow:i+10,outflow:i+2})));
            }
            if(p==='/api/dashboard/movements') return json([{createdAt:'2026-09-29T10:00:00',productName:'Fuse',quantity:2,transactionType:'SERVICE_OUT',movementLabel:'SERVICE OUT SRV-1',referenceUrl:'/Service/Details/1',partyName:'Customer'}]);
            if(p==='/api/inventory') return json({rows:[{productId:1,variantId:1,productName:'Battery',categoryName:'Battery',currentStock:2,minimumStockLevel:5,quantityNeeded:3,stockStatus:'LOW'}],total:1,summary:{lowStockVariants:1,outOfStock:0,unitsToReplenish:3}});
            if(p==='/api/dashboard/search') return json([{kind:'SERIAL',title:'BAT001',detail:'Battery / SOLD',url:'/Scan?code=BAT001'}]);
            const names={'/':'dashboard','/StockOutflow':'outflow','/Inventory/LowStock':'low-stock','/Search':'search'};
            if(names[p]) return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,role+'-'+names[p]+'.html'),'utf8')});
            const file=path.resolve(staticRoot,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            if(file.startsWith(staticRoot+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())
                return route.fulfill({path:file,contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':undefined});
            return route.fulfill({status:404,body:''});
        });
        await page.goto(base);
        const choosePeriod=async value=>{
            const native=page.locator('#chart-period');
            if(await native.isVisible())return native.selectOption(value);
            await page.locator('#chart-period-search').click();
            await page.locator(`#chart-period-options [data-value="${value}"]`).click();
        };
        assert.strictEqual(await page.locator('.sf-business-kpis .sf-kpi').count(),8);
        assert.strictEqual(await page.locator('a.sf-kpi[href="/Sales?status=OUTSTANDING"]').count(),1);
        assert.strictEqual(await page.locator('a.sf-kpi[href="/Sales?status=OVERDUE"]').count(),1);
        assert.strictEqual(await page.locator('#recent-sales').count(),1);
        assert.strictEqual(await page.locator('a.sf-kpi[href="/StockOutflow"]').count(),1);
        for(const [period,count] of [['7days',7],['30days',30],['12months',12],['6months',6]]) {
            await choosePeriod(period);
            await page.waitForFunction(n=>document.querySelectorAll('#chart-months .sf-chart-month').length===n&&document.getElementById('trend-message').hidden,count);
        }
        fail=true; await choosePeriod('7days');
        await page.waitForFunction(()=>document.getElementById('trend-message').textContent.includes('Unable'));
        assert.strictEqual(await page.locator('#chart-period').inputValue(),'6months');
        assert.strictEqual(await page.locator('#chart-months .sf-chart-month').count(),6); fail=false;
        for(const width of [1440,1024,768,390,320]) {
            await page.setViewportSize({width,height:1000});
            await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+2,null,{timeout:3000});
            assert(await page.locator('.sf-hero').evaluate(hero=>{
                const bounds=hero.getBoundingClientRect();
                return [...hero.querySelectorAll('.sf-eyebrow,h1,.sf-hero-content>p,.sf-hero-note,.sf-hero-stat')].every(node=>{
                    const r=node.getBoundingClientRect();return r.left>=bounds.left&&r.right<=bounds.right&&r.bottom<=bounds.bottom;
                });
            }),'dashboard banner grows with readable content and reflowed statistic chips');
        }
        await choosePeriod('30days');
        await page.waitForFunction(()=>document.querySelectorAll('#chart-months .sf-chart-month').length===30);
        assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'daily chart viewport overflow');
        await page.goto(base+'/StockOutflow');
        await page.waitForFunction(()=>document.getElementById('dashboard-results').textContent.includes('SERVICE OUT SRV-1'));
        assert.strictEqual(await page.locator('#movement-type option[value="WARRANTY_REPLACEMENT"]').count(),1);
        await page.goto(base+'/Inventory/LowStock');
        await page.waitForFunction(()=>document.getElementById('low-stock-results').textContent.includes('Battery'));
        await page.goto(base+'/Search?term=BAT');
        await page.waitForFunction(()=>document.getElementById('dashboard-results').textContent.includes('BAT001'));
        assert.strictEqual(await page.locator('#dashboard-results a').getAttribute('href'),'/Scan?code=BAT001');
        role='Service'; await page.goto(base);
        assert.strictEqual(await page.locator('.sf-business-kpis .sf-kpi').count(),4);
        assert.strictEqual(await page.locator('#recent-sales').count(),0);
        assert.strictEqual(await page.locator('.sf-nav a[href="/Users"]').count(),0);
        assert.deepStrictEqual(errors,[]);
        console.log('PASS: dashboard chart periods, error retention, 320–1440px layouts, KPI links, movement/low-stock/search screens and role-specific panels. Mock APIs only.');
    } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
