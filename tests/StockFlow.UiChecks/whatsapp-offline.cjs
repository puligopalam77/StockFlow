const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/StockFlow.MasterDataChecks/bin/Debug/net10.0/whatsapp-fixtures');
const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage(), errors = [];
        page.on('pageerror', e => errors.push(e.message));
        let signup = 0;
        await page.route('**/*', async route => {
            const url = new URL(route.request().url()), p = url.pathname;
            const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
            if (p === '/Configuration/Notifications/WhatsApp') return route.fulfill({ contentType: 'text/html', path: path.join(fixtures, 'settings.html') });
            if (p === '/api/configuration/whatsapp/signup') { signup++; return json({ ticket: '11111111-1111-1111-1111-111111111111', configurationId: '987654321', coexistenceEnabled: false }); }
            if (url.hostname === 'connect.facebook.net') return route.fulfill({ contentType: 'application/javascript', body: 'window.FB={init(){},login(callback){callback({});}};window.fbAsyncInit();' });
            if (p === '/api/notifications') return json({ unread: 0, total: 0, items: [] });
            if (p === '/api/branches/context') return json({ canReviewStores: false, locations: [{ locationId: 1, storeId: 1, storeName: 'Main store', locationName: 'Main stock' }], locationId: 1, storeId: 1 });
            if (p.startsWith('/api/') || p.startsWith('/account/session/')) return json([]);
            const file = path.resolve(assets, '.' + p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
            if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
            return route.fulfill({ status: 404, body: '' });
        });
        for (const width of [1920, 1440, 1024, 768, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.goto('http://stockflow.test/Configuration/Notifications/WhatsApp');
            await page.locator('#whatsapp-connect').waitFor();
            assert(!await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2), `WhatsApp settings overflow at ${width}px`);
            assert.equal(await page.locator('#whatsapp-registration-pin').getAttribute('type'), 'password');
            assert((await page.locator('#app-breadcrumb').textContent()).includes('Notifications'));
            console.log(`PASS: WhatsApp settings fit ${width}px`);
        }
        await page.locator('#whatsapp-connect').click();
        await page.waitForFunction(() => document.querySelector('#whatsapp-signup-status').textContent.includes('not completed'));
        assert(signup >= 1);
        await page.waitForFunction(() => !document.querySelector('#whatsapp-connect').disabled);
        console.log('PASS: cancelled Meta signup restores controls without exposing authorization data');
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
