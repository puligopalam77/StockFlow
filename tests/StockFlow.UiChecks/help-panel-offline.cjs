// Checks the current help library using rendered Razor fixtures and local packs.
// Chat layout, permission changes and voice have separate focused test files.
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');

const projectRoot = path.resolve(__dirname, '../..');
const assetsFolder = path.join(projectRoot, 'src/StockFlow.Web/wwwroot');
const fixturesFolder = path.join(projectRoot, 'tests/bin/help-panel-fixtures');
const readFixture = name => fs.readFileSync(path.join(fixturesFolder, name + '.html'), 'utf8');
const readArticles = html => JSON.parse(html.match(/<script id="help-data" type="application\/json">(.*?)<\/script>/s)[1]);
const articles = readArticles(readFixture('purchase'));

async function checkHelpLibrary() {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
        const errors = [];
        let failHelpPack = false;
        page.on('pageerror', error => errors.push(error.message));
        await page.route('**/*', route => {
            const url = new URL(route.request().url());
            if (url.pathname === '/Purchases/Create') return route.fulfill({ body: readFixture('purchase'), contentType: 'text/html' });
            if (url.pathname === '/api/help/assistant') return route.fulfill({ json: { scope: 'Test shop', contextStamp: 'unchanged', suggestions: [], articles } });
            if (url.pathname === '/api/help/assistant/health') return route.fulfill({ json: { modelStatus: 'disabled', voiceStatus: 'unavailable' } });
            if (url.pathname === '/api/help/assistant/guidance') return route.fulfill({ json: { features: [], fields: [], workflows: [] } });
            if (failHelpPack && url.pathname === '/languages/help/te.json') return route.fulfill({ status: 503 });
            if (url.pathname.startsWith('/api/')) return route.fulfill({ json: [] });
            const assetPath = url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, '');
            const file = path.resolve(assetsFolder, '.' + assetPath);
            if (file.startsWith(assetsFolder + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
            return route.fulfill({ status: 404 });
        });

        async function openLibrary() {
            if (await page.locator('#help-options').isHidden()) await page.locator('#help-menu-toggle').click();
            await page.locator('#help-browse').click();
        }

        await page.goto('http://stockflow.test/Purchases/Create');
        await page.locator('#help-open').click();
        assert.equal(await page.locator('#help-title').textContent(), 'StockBot AI');
        assert.equal(await page.locator('#help-ai-question').evaluate(input => input === document.activeElement), true);
        await openLibrary();
        assert.equal(await page.locator('#help-results button').count(), articles.length);
        await page.locator('#help-results button').filter({ hasText: 'Receiving stock' }).click();
        assert((await page.locator('#help-content ol li').count()) >= 6);
        assert.match(await page.locator('#help-content').textContent(), /Confirm & Receive/);
        assert.equal(await page.locator('#help-links a[href="/Purchases/Create"]').count(), 1);

        await page.locator('#help-back').click();
        await page.locator('#help-search').fill('refund');
        assert.match(await page.locator('#help-results').textContent(), /Customer returns/);
        await page.locator('#help-search').fill('zzzznonexistent');
        assert.match(await page.locator('#help-count').textContent(), /No matching articles/);
        await page.locator('#help-search').fill('');

        // A failed help pack keeps the reviewed English instructions available.
        failHelpPack = true;
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('te'));
        await page.locator('#help-language-status').waitFor({ state: 'visible' });
        assert.match(await page.locator('#help-language-status').textContent(), /English instructions remain available/);
        failHelpPack = false;
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('en'));

        const requested = process.argv.find(value => value.startsWith('--languages='))?.slice(12).split(',');
        const codes = (await page.locator('#language-choice option').evaluateAll(options => options.map(option => option.value).filter(code => code !== 'en')))
            .filter(code => !requested || requested.includes(code));
        const englishPack = JSON.parse(fs.readFileSync(path.join(assetsFolder, 'languages/help/en.json'), 'utf8'));
        for (const code of codes) {
            const pack = JSON.parse(fs.readFileSync(path.join(assetsFolder, 'languages/help', code + '.json'), 'utf8'));
            for (const [key, value] of Object.entries(englishPack)) {
                assert(pack[key]?.trim(), code + ': missing ' + key);
                for (const token of value.match(/\{\w+\}/g) || []) assert(pack[key].includes(token), code + ': missing ' + token);
            }
            await page.evaluate(code => window.StockFlowLanguage.setLanguage(code), code);
            await page.waitForFunction(caption => document.querySelector('#help-back').textContent === caption, 'Back to articles (' + pack['back to articles'] + ')');
            await page.locator('#help-search').fill(pack['receiving stock']);
            await page.locator('#help-results button').first().click();
            assert.match(await page.locator('#help-article-title').textContent(), /Receiving stock/);
            assert.match(await page.locator('#help-content').textContent(), /Choose the supplier/);
            assert.equal(await page.locator('#help-links a[href="/Purchases/Create"]').count(), 1);
            await page.locator('#help-back').click();
            await page.locator('#help-search').fill('');
        }
        await page.evaluate(() => window.StockFlowLanguage.setLanguage('en'));
        await page.waitForFunction(() => document.querySelector('#help-back').textContent === 'Back to articles');
        await page.emulateMedia({ reducedMotion: 'reduce' });
        assert.equal(await page.locator('.sf-stockbot-hand').evaluate(element => getComputedStyle(element).animationName), 'none');
        await page.keyboard.press('Escape');
        assert(!await page.locator('#help-panel').evaluate(panel => panel.open));
        assert(await page.locator('#help-open').evaluate(button => button === document.activeElement));
        assert.deepEqual(errors, []);
        console.log('PASS: current help articles, source links, search, failed-pack fallback, ' + codes.length + ' language packs, English reset, reduced motion and keyboard focus.');
    } finally {
        await browser.close();
    }
}

checkHelpLibrary().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
