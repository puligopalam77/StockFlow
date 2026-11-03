const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/bin/screen-language-fixtures');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const settings = new Map();
    const errors = [];
    let rejectSave = false;
    async function openPage() {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        page.on('pageerror', (error) => errors.push(error.message));
        await page.route('**/*', async (route) => {
            const request = route.request();
            const url = new URL(request.url());
            const json = (value) =>
                route.fulfill({ contentType: 'application/json', body: JSON.stringify(value) });
            if (url.pathname === '/report-filter-test')
                return route.fulfill({contentType:'text/html', body:'<!doctype html><html><body></body></html>'});
            const pages = {
                '/Purchases/Create': 'Purchases-Create',
                '/Sales/Create': 'Sales-Create',
                '/Customers': 'Customers'
            };
            if (pages[url.pathname])
                return route.fulfill({
                    contentType: 'text/html',
                    body: fs.readFileSync(
                        path.join(fixtures, pages[url.pathname] + '.html'),
                        'utf8'
                    )
                });
            if (url.pathname === '/api/screenlanguages') {
                if (request.method() === 'PUT') {
                    assert(request.headers()['x-csrf-token']);
                    if (rejectSave) return route.fulfill({ status: 403,
                        contentType: 'application/json', body: JSON.stringify({message:'Could not save the language.'}) });
                    const body = request.postDataJSON();
                    const captionKeys = Object.keys(body.captions).map((key) => key.toLowerCase());
                    assert.equal(new Set(captionKeys).size, captionKeys.length,
                        'Saved captions must be unique regardless of capitalization');
                    settings.set(body.screen, body);
                    return route.fulfill({ status: 204 });
                }
                return json([...settings.values()]);
            }
            if (url.pathname === '/api/branches/context')
                return json({
                    canReviewStores: true,
                    locationId: 1,
                    storeId: 1,
                    locations: [
                        { locationId: 1, storeId: 1, storeName: 'Shop', locationName: 'Counter' }
                    ]
                });
            if (url.pathname === '/api/sales/billing/policy')
                return json({ canDiscount: true, canTradeIn: true });
            if (url.pathname.startsWith('/api/')) return json([]);
            const file = path.join(
                root,
                'src/StockFlow.Web/wwwroot',
                url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, '')
            );
            if (fs.existsSync(file) && fs.statSync(file).isFile())
                return route.fulfill({ path: file });
            return route.fulfill({ status: 404, body: '' });
        });
        return page;
    }
    try {
        const page = await openPage();
        await page.goto('http://stockflow.test/Purchases/Create');
        await page.waitForFunction(() => document.querySelector('#language-choice option[value="ur"]'));
        assert.equal(await page.locator('#language-choice option').count(), 23);
        for (const id of ['language-screen', 'language-caption-rows', 'language-preview', 'language-add-caption'])
            assert.equal(await page.locator('#' + id).count(), 0, 'removed ' + id);
        await page.locator('#language-choice').selectOption('te');
        
        await page.waitForFunction(() => document.querySelector('h1').textContent.startsWith('New stock receipt ('));
        assert.equal(settings.get('application').language, 'te');
        assert.equal(settings.size, 1);
        assert.equal(await page.locator('#business-location').count(), 0);
        assert.equal(await page.locator('a[href="/Configuration/Languages"]').count(), 0);
        assert.equal(await page.locator('#language-save').count(), 0);
        await page.waitForFunction(() => !document.querySelector('#language-choice').disabled);
        rejectSave = true;
        await page.locator('#language-choice').selectOption('hi');
        await page.locator('#language-message').waitFor({state:'visible'});
        assert.equal(await page.locator('#language-choice').inputValue(), 'te');
        assert.equal(settings.get('application').language, 'te');
        assert((await page.locator('h1').textContent()).startsWith('New stock receipt ('));
        rejectSave = false;
        await page.goto('http://stockflow.test/Purchases/Create');
        await page.waitForFunction(() => document.querySelector('label[for="supplierId"]').textContent.startsWith('Supplier ('));
        assert((await page.locator('h1').textContent()).startsWith('New stock receipt ('));
        assert((await page.locator('.sf-nav a[href="/"]').textContent()).includes('Dashboard ('));
        assert((await page.locator('#productScanner').getAttribute('placeholder')).startsWith('Click here, then scan ('));
        const validation = await page.evaluate(() => window.StockFlowLanguage.fieldError('phone',
            {label:'Phone'}, undefined, true, 'Phone must contain exactly 10 digits.'));
        assert(validation.startsWith('Phone must contain exactly 10 digits. ('));
        assert(/[\u0c00-\u0c7f]/.test(validation));
        const billing = await openPage();
        await billing.goto('http://stockflow.test/Sales/Create');
        await billing.waitForFunction(() => document.querySelector('h1').textContent.startsWith('Counter billing ('));
        await page.goto('http://stockflow.test/Purchases/Create');
        await page.waitForFunction(() => document.querySelector('#language-choice option[value="ur"]'));
        await page.waitForFunction(() => document.querySelector('#language-choice').value === 'te');
        await page.locator('#language-choice').selectOption('hi');
        
        await page.waitForFunction(() => /[\u0900-\u097f]/.test(document.querySelector('h1').textContent));
        await billing.evaluate(() => window.dispatchEvent(new Event('focus')));
        await billing.waitForFunction(() => /[\u0900-\u097f]/.test(document.querySelector('h1').textContent));
        const customer = await openPage();
        await customer.goto('http://stockflow.test/Customers');
        await customer.waitForFunction(() => /[\u0900-\u097f]/.test(document.querySelector('h1').textContent));
        await page.locator('#language-choice').selectOption('mr');
        
        await page.waitForFunction(() => document.querySelector('#language-choice').value === 'mr' && !document.querySelector('#language-choice').disabled);
        assert.equal(settings.get('application').language, 'mr');
        // Every selectable non-English language must have real captions and templates.
        const languages = await page.locator('#language-choice option').evaluateAll((options) =>
            options.map((option) => option.value).filter((code) => code !== 'en'));
        const englishPack = JSON.parse(fs.readFileSync(path.join(root,
            'src/StockFlow.Web/wwwroot/languages/en.json'), 'utf8'));
        for (const code of languages) {
            const pack = JSON.parse(fs.readFileSync(path.join(root,
                'src/StockFlow.Web/wwwroot/languages', code + '.json'), 'utf8'));
            for (const [key, english] of Object.entries(englishPack)) {
                assert(pack[key]?.trim(), code + ' is missing ' + key);
                for (const token of english.match(/\{\w+\}/g) || [])
                    assert(pack[key].includes(token), code + ':' + key + ' loses ' + token);
            }
            await page.locator('#language-choice').selectOption(code);
            
            await page.waitForFunction((translation) =>
                document.querySelector('h1').textContent ===
                    'New stock receipt (' + translation + ')', pack['new stock receipt']);
            const message = await page.evaluate(() => window.StockFlowLanguage.fieldError('maximum',
                {label:'Phone', limit:10}, undefined, true, 'Phone: enter no more than 10 characters.'));
            assert(message.startsWith('Phone: enter no more than 10 characters. ('), code);
            assert(!message.includes('{label}') && !message.includes('{limit}'), code);
            const phoneCheck = await page.evaluate(() => {
                const form = document.createElement('form');
                const label = document.createElement('label');
                label.htmlFor = 'all-language-phone';
                label.textContent = 'Phone';
                const input = document.createElement('input');
                input.id = label.htmlFor;
                input.name = 'Phone';
                input.type = 'tel';
                input.pattern = '[0-9]{10}';
                input.required = true;
                form.append(label, input);
                document.querySelector('#main-content').append(form);
                window.StockFlowLanguage.refresh();
                input.value = '123456789';
                const invalid = !window.StockFlowValidation.validate(form, false);
                const text = form.querySelector('.sf-field-validation')?.textContent || '';
                input.value = '1234567890';
                const valid = window.StockFlowValidation.validate(form, false);
                form.remove();
                return { invalid, valid, text };
            });
            assert(phoneCheck.invalid && phoneCheck.valid, code + ': phone validation boundaries');
            assert(phoneCheck.text.startsWith('Phone must contain exactly 10 digits,'), code + ': English validation');
            assert(phoneCheck.text.includes(' ('), code + ': translated validation');
            assert.equal(settings.get('application').language, code);
            for (const caption of ['Roles & permissions', 'Stores', 'Company settings']) {
                const text = await page.evaluate((english) => window.StockFlowLanguage.text(english), caption);
                assert(text.startsWith(caption + ' ('), code + ': navigation caption ' + caption);
            }
        }
        await page.screenshot({ path: path.join(root, 'tests/bin/all-languages-urdu.png'), fullPage: true });
        await page.locator('#language-choice').selectOption('en');
        
        await page.waitForFunction(() => !document.querySelector('#language-choice').disabled);
        await billing.evaluate(() => window.dispatchEvent(new Event('focus')));
        await billing.waitForFunction(() => document.querySelector('h1').textContent === 'Counter billing');
        for (const width of [320, 390]) {
            await page.setViewportSize({width, height:900});
            // Wait for the shell's sidebar transition after changing viewport size.
            await page.waitForFunction(() => document.documentElement.scrollWidth <= innerWidth + 1,
                null, { timeout: 3000 });
            const overflow = await page.evaluate(() => [...document.querySelectorAll('body *')]
                .filter((item) => item.getBoundingClientRect().right > innerWidth + 1)
                .slice(0, 10).map((item) => item.tagName + '.' + item.className + ':' +
                    item.getBoundingClientRect().width + ':' + getComputedStyle(item).gridTemplateColumns));
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
                'settings fit ' + width + ': ' + overflow.join(', '));
        }
        assert.deepEqual(errors, []);
        // The Reports filter must still work after removing its former header dependency.
        const report = await openPage();
        await report.goto('http://stockflow.test/report-filter-test');
        await report.setContent('<meta name="stockflow-location" content="1"><select id="report-location" disabled></select>');
        await report.addScriptTag({path: path.join(root, 'src/StockFlow.Web/wwwroot/js/branch-context.js')});
        await report.evaluate(() => document.dispatchEvent(new Event('DOMContentLoaded')));
        await report.waitForFunction(() => !document.querySelector('#report-location').disabled);
        assert.equal(await report.locator('#report-location option').count(), 3);
        assert.equal(await report.locator('#report-location').inputValue(), '1:1');
        await report.close();
        console.log('PASS: all 22 Indian languages, complete bundled caption/template coverage, English (translation), automatic updates, validation, English reset, multiple screens and responsive layout.');
    } finally {
        await browser.close();
    }
})().catch((error) => {
    console.error(error);
    process.exitCode = 1;
});
