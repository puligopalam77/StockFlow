const fs = require('fs');
const path = require('path');
const assert = require('assert');
const {chromium} = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');

(async () => {
    const browser = await chromium.launch({channel:'msedge', headless:true});
    const errors = [];
    let failHelpPack = false;
    let aiStatus = 'answered';
    let stockCalls = 0;
    let stockFailure = false;
    try {
        const page = await browser.newPage({viewport:{width:1440,height:950}});
        page.on('pageerror', error => errors.push(error.message));
        await page.route('**/*', route => {
            const url = new URL(route.request().url());
            if (url.pathname === '/api/help/stock') {
                stockCalls++;
                assert.equal(route.request().method(),'GET');
                if(stockFailure)return route.fulfill({status:403,contentType:'application/json',body:'{"message":"Stock access denied."}'});
                return route.fulfill({contentType:'application/json',body:JSON.stringify({scopeName:'Main store / Main stock',checkedAt:new Date().toISOString(),truncated:false,
                    items:url.searchParams.get('term')==='missing'?[]:[{productName:'Battery / 150 Ah',sku:'BAT-150',currentStock:7,availableQuantity:5,hasDiscrepancy:true}]})});
            }
            if (url.pathname === '/api/help/questions') {
                assert(route.request().headers()['x-csrf-token']);
                assert.equal(route.request().method(),'POST');
                if (route.request().postDataJSON().question === 'HOW MUCH QUANTITY SOLD TODAY')
                    return route.fulfill({contentType:'application/json',body:JSON.stringify({status:'answered',message:'7',sections:[],links:[]})});
                return route.fulfill({contentType:'application/json',body:JSON.stringify({
                    status:aiStatus, message:aiStatus === 'answered' ? 'These approved instructions match your question.' : 'The approved guides do not provide an answer. Contact your administrator.',
                    sections:aiStatus === 'answered' ? [{id:'receiving-stock:1',articleId:'receiving-stock',title:'Receive goods now',
                        content:'1. Open **Receive Stock → Receive new stock** at `/Purchases/Create`.\n2. Choose **Supplier**.'}] : [],
                    links:aiStatus === 'answered' ? [{label:'Receive new stock',url:'/Purchases/Create'}] : []
                })});
            }
            if (failHelpPack && url.pathname === '/languages/help/te.json') return route.fulfill({status:503,body:''});
            const fixtures = {'/Purchases/Create':'purchase', '/Sales/Create':'billing', '/restricted':'restricted',
                '/Reports/Activity':'purchase', '/Inventory':'purchase', '/Settings/Company':'purchase',
                '/CashClosing':'purchase', '/StockInflow':'purchase', '/Sales':'purchase'};
            if (fixtures[url.pathname]) return route.fulfill({contentType:'text/html',
                body:fs.readFileSync(path.join(root,'tests/bin/help-panel-fixtures',fixtures[url.pathname]+'.html'),'utf8')});
            if (url.pathname.startsWith('/api/')) return route.fulfill({contentType:'application/json',body:'[]'});
            const file = path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            if (fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({path:file});
            return route.fulfill({status:404,body:''});
        });
        await page.goto('http://stockflow.test/Purchases/Create');
        await page.locator('#help-open').click();
        assert(await page.locator('#help-panel').evaluate(element => element.open));
        assert.equal(await page.locator('#help-ai-question').evaluate(element => element === document.activeElement),true);
        assert.equal(await page.locator('#help-title').textContent(),'StockFlow Assistant');
        assert.equal(await page.locator('#help-ai-submit').evaluate(element => getComputedStyle(element).backgroundColor),'rgb(255, 0, 126)');
        assert.equal(await page.locator('#help-example').count(),0,'No preloaded example conversation');
        assert.equal(await page.locator('#help-user-question').isVisible(),false);
        assert.equal(await page.locator('#help-ai-answer').isVisible(),false);
        assert.equal(await page.locator('#help-library').isVisible(),false);
        assert.equal(await page.locator('.sf-assistant-shortcuts').isVisible(),false);
        assert.equal(await page.locator('.sf-help-stock').isVisible(),false);
        assert.equal(await page.locator('#help-suggested button').count(),3);
        assert.equal(await page.locator('#help-ai-form').evaluate(element=>element.noValidate),true,'Chat owns its validation instead of save-form alerts');
        await page.locator('#help-ai-submit').click();
        assert(await page.locator('#help-question-error').isVisible(),'An empty question has a compact inline error');
        assert.equal(await page.locator('#help-ai-form .sf-validation-summary').count(),0,'Save validation alert cannot break the composer');
        await page.locator('#help-ai-question').fill('ab');
        await page.locator('#help-ai-submit').click();
        assert(await page.locator('#help-question-error').isVisible(),'Too-short questions are rejected locally');
        await page.locator('#help-ai-question').fill('');
        const panelWidth = await page.locator('#help-panel').evaluate(element => element.getBoundingClientRect().width);
        assert(panelWidth <= 350,'Assistant uses smaller desktop width');
        assert(await page.locator('#help-panel').evaluate(element => element.getBoundingClientRect().height <= 480));
        await page.screenshot({path:path.join(root,'tests/bin/stockflow-assistant-preview.png')});
        await page.locator('#help-suggested button').first().click();
        assert((await page.locator('#help-ai-answer').textContent()).includes('Save stock receipt'),'Suggested question opens approved instructions without AI');
        assert.equal(await page.locator('#help-user-question').isVisible(),true);
        await page.locator('#help-browse').click();
        await page.locator('[data-help-stock]').click();
        assert.equal(await page.locator('#help-stock-term').evaluate(element => element === document.activeElement),true);
        assert((await page.locator('#help-questions').textContent()).includes('receive goods'));
        await page.locator('#help-stock-term').fill('Battery');
        await page.locator('#help-stock-check').click();
        await page.waitForFunction(() => document.querySelector('#help-stock-result').textContent.includes('Available quantity: 5'));
        assert((await page.locator('#help-stock-result').textContent()).includes('Stock on hand: 7'));
        assert((await page.locator('#help-stock-result').textContent()).includes('serials differ'));
        assert.equal(await page.locator('#help-stock-result a[href="/Inventory"]').count(),1);
        await page.locator('#help-stock-term').fill('missing');
        await page.locator('#help-stock-check').click();
        await page.waitForFunction(() => document.querySelector('#help-stock-result').textContent.includes('No matching'));
        stockFailure=true;
        await page.locator('#help-stock-term').fill('Battery');
        await page.locator('#help-stock-check').click();
        await page.waitForFunction(() => document.querySelector('#help-stock-result').textContent === 'Stock access denied.');
        assert(!(await page.locator('#help-stock-result').textContent()).includes('Battery'));
        assert.equal(stockCalls,3);
        await page.locator('.sf-help-stock summary').click();
        await page.locator('#help-browse').click();
        await page.locator('#help-questions button').first().click();
        assert.equal(await page.locator('#help-article-title').textContent(),'Receiving stock');
        assert(await page.locator('#help-content ol li').count() >= 8);
        assert((await page.locator('#help-content').textContent()).includes('Save stock receipt'));
        assert.equal(await page.locator('#help-links a[href="/Purchases/Create"]').count(),1);
        await page.locator('#help-ai-question').fill('How do I receive goods?');
        await page.locator('#help-ai-submit').click();
        await page.locator('#help-ai-answer button').filter({hasText:'Source: Receiving stock'}).waitFor();
        assert.equal(await page.locator('#help-user-question').textContent(),'How do I receive goods?');
        assert.equal(await page.locator('#help-ai-question').inputValue(),'','Submitted input is cleared');
        assert.equal(await page.locator('#help-library').isVisible(),false,'Unrelated articles stay hidden after asking');
        assert((await page.locator('#help-ai-answer').textContent()).includes('Choose Supplier'));
        await page.locator('#help-ai-answer button').filter({hasText:'Source: Receiving stock'}).click();
        assert.equal(await page.locator('#help-article-title').textContent(),'Receiving stock');
        aiStatus = 'unsupported';
        await page.locator('#help-ai-question').fill('What is the weather?');
        await page.locator('#help-ai-submit').click();
        await page.locator('#help-ai-support').waitFor();
        await page.locator('#help-ai-support').click();
        assert(await page.locator('#help-ai-support-note').isVisible());
        aiStatus = 'answered';
        await page.locator('#help-ai-question').fill('How do I receive goods?');
        await page.locator('#help-ai-submit').click();
        await page.locator('#help-ai-answer button').filter({hasText:'Source: Receiving stock'}).waitFor();
        await page.locator('#help-ai-answer button').filter({hasText:'Source: Receiving stock'}).click();
        await page.locator('#help-content .sf-help-article-link').filter({hasText:'Adding products'}).click();
        assert.equal(await page.locator('#help-article-title').textContent(),'Adding products');
        assert(await page.locator('#help-content table th').count() > 0);
        await page.locator('#help-back').click();
        await page.locator('#help-search').fill('refund');
        assert((await page.locator('#help-results').textContent()).includes('Customer returns'));
        await page.locator('#help-search').fill('zzzznonexistent');
        assert((await page.locator('#help-count').textContent()).includes('No matching articles'));
        await page.locator('#help-search').fill('');
        assert.equal(await page.locator('#help-results button').count(),8);
        // Each local language pack must cover the whole guide, not just its title.
        const english = JSON.parse(fs.readFileSync(path.join(root,'src/StockFlow.Web/wwwroot/languages/help/en.json'),'utf8'));
        failHelpPack = true;
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('te'));
        await page.locator('#help-language-status').waitFor({state:'visible'});
        assert.equal(await page.locator('#help-title').textContent(),'StockFlow Assistant');
        failHelpPack = false;
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('en'));
        const requested = process.argv.find(value => value.startsWith('--languages='))?.slice(12).split(',');
        const codes = (await page.locator('#language-choice option').evaluateAll(options => options.map(option => option.value).filter(code => code !== 'en')))
            .filter(code => !requested || requested.includes(code));
        const step = 'Open Receive Stock → Receive new stock at /Purchases/Create.';
        for (const code of codes) {
            const pack = JSON.parse(fs.readFileSync(path.join(root,'src/StockFlow.Web/wwwroot/languages/help',code+'.json'),'utf8'));
            for (const [key,value] of Object.entries(english)) {
                assert(pack[key]?.trim(),code+': missing '+key);
                for(const token of value.match(/\{\w+\}/g)||[]) assert(pack[key].includes(token),code+': missing '+token);
            }
            await page.evaluate(code => window.StockFlowLanguage.setLanguage(code),code);
            await page.waitForFunction(value => document.querySelector('#help-back').textContent === 'Back to articles ('+value+')',pack['back to articles']);
            await page.locator('#help-search').fill(pack['receiving stock']);
            assert((await page.locator('#help-results').textContent()).includes('Receiving stock'),code+': search translated titles');
            await page.locator('#help-results button').filter({hasText:'Receiving stock'}).click();
            const first = await page.locator('#help-content ol li').first().textContent();
            assert(first.includes('Receive Stock') && first.includes('Receive new stock'),code+': English screen captions remain visible');
            assert(pack[step.toLowerCase()] ? first.endsWith(' ('+pack[step.toLowerCase()]+')') : first === step,code+': translated instruction or current English fallback');
            const aiStep = await page.locator('#help-ai-answer ol li').first().textContent();
            assert(pack[step.toLowerCase()] ? aiStep.endsWith(' ('+pack[step.toLowerCase()]+')') : aiStep === step,code+': selected instructions follow language or English fallback');
            assert.equal(await page.locator('#help-links a[href="/Purchases/Create"]').count(),1);
            await page.locator('#help-back').click();
            await page.locator('#help-search').fill('');
        }
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('en'));
        await page.waitForFunction(() => document.querySelector('#help-back').textContent === 'Back to articles');
        assert.equal(await page.locator('#help-open').getAttribute('aria-label'),'Open StockBot Assist');
        assert.equal(await page.locator('#help-open .sf-mascot-hand').count(),1);
        assert.equal(await page.locator('.sf-mascot-caption').textContent(),'Ask StockBot Assist');
        assert(Math.abs(await page.locator('.sf-mascot-body').evaluate(element => element.getBoundingClientRect().width)-35.2)<0.1);
        assert.equal(await page.locator('.sf-mascot-caption').evaluate(element => getComputedStyle(element).fontSize),'14px','Caption text size is preserved');
        assert.equal(await page.locator('.sf-mascot-eyelids').evaluate(element => getComputedStyle(element,'::before').animationName),'sf-mascot-blink');
        await page.emulateMedia({reducedMotion:'reduce'});
        assert.equal(await page.locator('.sf-mascot-hand').evaluate(element => getComputedStyle(element).animationName),'none');
        assert.equal(await page.locator('.sf-mascot-eyelids').evaluate(element => getComputedStyle(element,'::before').animationName),'none');
        await page.emulateMedia({reducedMotion:'no-preference'});
        assert.equal(await page.locator('.sf-mascot-hand').evaluate(element => getComputedStyle(element).animationName),'sf-mascot-wave');
        assert.equal(await page.locator('#help-language-status').isVisible(),false);
        await page.locator('#help-ai-question').fill('HOW MUCH QUANTITY SOLD TODAY');
        await page.locator('#help-ai-submit').click();
        await page.waitForFunction(() => document.querySelector('#help-ai-answer').textContent === '7');
        assert.equal(await page.locator('#help-ai-question').inputValue(),'');
        assert.equal(await page.locator('#help-ai-answer a, #help-ai-answer ol').count(),0,'Count answers have no guides or links');
        await page.locator('#help-browse').click();
        await page.locator('#help-results button').filter({hasText:'Receiving stock'}).click();
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('hi'));
        await page.waitForFunction(() => document.querySelector('#help-article-title').textContent.startsWith('Receiving stock ('));
        assert.equal(await page.locator('#help-article').isVisible(),true,'Changing language keeps the current article open');
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('en'));
        await page.waitForFunction(() => document.querySelector('#help-article-title').textContent === 'Receiving stock');
        assert.equal(await page.locator('#help-content .sf-help-translation').count(),0);
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('#help-open').evaluate(element => element === document.activeElement),true);
        await page.screenshot({path:path.join(root,'tests/bin/stockbot-launcher-preview.png')});
        await page.goto('http://stockflow.test/Sales/Create');
        await page.locator('#language-choice').selectOption('hi');
        await page.waitForFunction(() => document.querySelector('#help-back').textContent.startsWith('Back to articles ('));
        await page.locator('#help-open').click();
        assert((await page.locator('#help-questions').textContent()).includes('make a bill'));
        await page.locator('#help-close').click();
        await page.goto('http://stockflow.test/restricted');
        await page.locator('#help-open').click();
        await page.locator('#help-browse').click();
        await page.locator('#help-results button').filter({hasText:'Creating bills'}).click();
        assert.equal(await page.locator('#help-links a[href="/Sales/Create"]').count(),0);
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('ur'));
        await page.waitForFunction(() => document.querySelector('#help-article-title').textContent.startsWith('Creating bills and taking payment ('));
        for (const width of [320,390]) {
            await page.setViewportSize({width,height:800});
            const fits = await page.locator('#help-panel').evaluate(element => {
                const box=element.getBoundingClientRect();
                return box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight;
            });
            assert(fits,'Help dialog fits mobile width '+width);
        }
        await page.screenshot({path:path.join(root,'tests/bin/help-language-urdu-mobile.png')});
        await page.locator('#help-close').click();
        for (const [width,height] of [[1920,1080],[1366,768],[1024,768],[768,1024],[390,844],[320,568],[844,390]]) {
            await page.setViewportSize({width,height});
            const launcher = await page.locator('#help-open').evaluate(element => {
                const box = element.getBoundingClientRect();
                const caption = element.querySelector('.sf-mascot-caption');
                return {fits:box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight,
                    captionFits:caption.scrollWidth <= caption.clientWidth,
                    width:box.width,height:box.height,robot:!!element.querySelector('.sf-mascot-body')};
            });
            assert(launcher.fits && launcher.captionFits,'Robot and caption fit '+width+'x'+height);
            assert(Math.abs(launcher.size-size)<0.1,'Robot scales for '+width+'x'+height);
            await page.locator('#help-open').click();
            assert(await page.locator('#help-ai-submit').isVisible(),'Send control visible at '+width+'x'+height);
            assert(await page.locator('#help-ai-form').evaluate(element=>element.scrollWidth<=element.clientWidth+1),'Composer has no horizontal overflow at '+width+'x'+height);
            assert(await page.locator('#help-panel').evaluate(element => {
                const box = element.getBoundingClientRect();
                return box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight;
            }),'Assistant fits '+width+'x'+height);
            await page.locator('#help-close').click();
        }
        // Reuse the rendered shell to isolate pathname-driven suggestions from page data.
        for (const [route,expected] of [
            ['/Reports/Activity','How do I use Shop summary?'],
            ['/Inventory','How do I count stock in the shop?'],
            ['/Settings/Company','How do I change the business logo?'],
            ['/CashClosing','How do I print a saved cash closing?'],
            ['/StockInflow','How do I view stock receipt details?'],
            ['/Sales','How do I export sales invoices?']
        ]) {
            await page.goto('http://stockflow.test'+route);
            await page.locator('#help-open').click();
            assert((await page.locator('#help-suggested').textContent()).includes(expected),'Current screen suggestion: '+route);
        }
        assert.deepEqual(errors,[]);
        console.log('PASS: help questions, guides, AI answer/source/support UI with mocked provider, '+codes.length+' language packs including English fallback, permitted links, search, keyboard and mobile layout.');
    } finally {await browser.close();}
})().catch(error => {console.error(error);process.exitCode=1;});
