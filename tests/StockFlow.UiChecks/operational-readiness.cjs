// Actual Razor output/assets; HTTP mocks test UI behavior, not operational evidence authenticity.
const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        let profile = 'ELECTRICAL', recoveryStatus = 'ArchiveOnly';
        await page.route('**/*', async route => {
            const p = new URL(route.request().url()).pathname;
            const json = value => route.fulfill({ contentType: 'application/json', body: JSON.stringify(value) });
            if (p === '/Admin/Pilot') return route.fulfill({ contentType: 'text/html', body: fs.readFileSync(path.join(root, 'tests/bin/operational-readiness', profile + '.html')) });
            if (p === '/api/database/operations') return json({ database: 'Ready', migrationCapabilities: { BillingPolicy082: true, Quotations083: false }, recovery: { status: recoveryStatus, backupUtc: '2026-11-01T00:00:00Z', restoreTestUtc: null, backupOverdue: true } });
            if (p === '/api/branches/context') return json({ canReviewStores: true, locations: [{ locationId: 1, storeId: 1, storeName: 'Main store', locationName: 'Main stock' }], locationId: 1, storeId: 1 });
            if (p === '/api/notifications') return json({ unread: 0, items: [] });
            const file = path.join(root, 'src/StockFlow.Web/wwwroot', decodeURIComponent(p).replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
            if (file.startsWith(path.join(root, 'src/StockFlow.Web/wwwroot')) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
            return json({});
        });
        await page.goto('http://stockflow.test/Admin/Pilot');
        await page.waitForSelector('#pilot-status-legal');
        assert((await page.locator('#pilot-progress').textContent()).includes('0 / 15'));
        for (const name of ['store', 'operator', 'build', 'workstation', 'scanner', 'printer', 'backup']) await page.locator('#pilot-' + name).fill('Test fixture');
        for (const select of await page.locator('#pilot-checks select').all()) await select.selectOption('PASS');
        await page.locator('#pilot-sign').click();
        assert((await page.locator('#pilot-message').textContent()).includes('include evidence'));
        for (const field of await page.locator('#pilot-checks textarea').all()) await field.fill('2026-11-02: operator reviewed test evidence');
        await page.locator('#pilot-sign').click();
        assert((await page.locator('#pilot-message').textContent()).includes('sign-off recorded'));
        await page.reload();
        assert((await page.locator('#pilot-progress').textContent()).includes('15 / 15'));
        await page.locator('#pilot-evidence-opening').fill('Changed review');
        assert.strictEqual(await page.evaluate(() => JSON.parse(localStorage.getItem(document.getElementById('pilot-workspace').dataset.storageKey)).signedAt), null);
        await page.locator('summary').filter({ hasText: 'Hosting' }).click();
        await page.locator('#pilot-operations-load').click();
        await page.waitForFunction(() => document.getElementById('pilot-operations').textContent.includes('no matching'));
        assert((await page.locator('#pilot-operations').textContent()).includes('BACKUP OVERDUE'));
        assert((await page.locator('#pilot-operations').textContent()).includes('082 capability: detected'));
        recoveryStatus = 'StaleEvidence';
        await page.locator('#pilot-operations-load').click();
        await page.waitForFunction(() => document.getElementById('pilot-operations').textContent.includes('stale'));
        for (profile of ['BIKE_ACCESSORIES', 'CLOTHING', 'FOOTWEAR']) {
            await page.goto('http://stockflow.test/Admin/Pilot');
            await page.waitForSelector('#pilot-status-legal');
            assert((await page.locator('#pilot-progress').textContent()).includes('0 / 15'), 'A different profile inherited confirmations');
            assert.strictEqual(await page.locator('#pilot-workspace').getAttribute('data-profile'), profile);
        }
        await page.setViewportSize({ width: 390, height: 844 });
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Mobile page overflow');
        assert.deepStrictEqual(errors, []);
        console.log('PASS: actual onboarding Razor, evidence-required sign-off, edit invalidation, reload, all four profile contexts, archive/stale/overdue warnings and mobile layout; mocked HTTP.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
