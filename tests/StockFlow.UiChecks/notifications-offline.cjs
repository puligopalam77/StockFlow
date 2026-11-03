const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/bin/notification-artifacts/bin/StockFlow.MasterDataChecks/debug/notification-ui-fixtures');
const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage(), errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.route('**/*', async route => {
            const url = new URL(route.request().url()), p = url.pathname;
            const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
            if (p === '/Configuration/Notifications') return route.fulfill({ contentType: 'text/html', path: path.join(fixtures, 'settings.html') });
            if (p === '/Notifications') return route.fulfill({ contentType: 'text/html', path: path.join(fixtures, 'history.html') });
            if (p === '/api/notifications') return json({ unread: 3, total: 3, items: [] });
            if (p === '/api/branches/context') return json({ canReviewStores: false, locations: [{ locationId: 1, storeId: 1, storeName: 'Main store', locationName: 'Main stock' }], locationId: 1, storeId: 1 });
            if (p.startsWith('/api/') || p.startsWith('/account/session/')) return json([]);
            const file = path.resolve(assets, '.' + p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
            if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
            return route.fulfill({ status: 404, body: '' });
        });
        for (const width of [1920, 1440, 1024, 768, 390, 320]) {
            await page.setViewportSize({ width, height: 900 });
            for (const url of ['/Configuration/Notifications', '/Notifications']) {
                await page.goto('http://stockflow.test' + url);
                await page.waitForFunction(() => document.querySelector('#notification-count').textContent === '3');
                assert.equal(await page.locator('#notification-bell').getAttribute('href'), '/Notifications');
                assert(await page.locator('#notification-count').isVisible());
                const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2);
                assert(!overflow, `${url} overflows at ${width}px`);
            }
            console.log(`PASS: notifications fit ${width}px with working unread badge`);
        }
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.goto('http://stockflow.test/Configuration/Notifications');
        await page.locator('#notification-preview-button').click();
        assert(await page.locator('#notification-preview-dialog').isVisible());
        assert((await page.locator('#notification-preview-body').textContent()).includes('DOC-2026-000001'));
        await page.locator('#notification-preview-close').click();
        await page.locator('#notification-staff-search').fill('no matching staff');
        assert.equal(await page.locator('[data-staff-name]:visible').count(), 0);
        console.log('PASS: template preview and recipient search');
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
