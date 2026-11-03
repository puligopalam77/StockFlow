const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/bin/shop-readiness-fixtures');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
        const errors = [];
        const posts = [];
        const history = [];
        let failedResponse = true;
        page.on('pageerror', (error) => errors.push(error.message));
        await page.route('**/*', async (route) => {
            const request = route.request();
            const url = new URL(request.url());
            const json = (value) =>
                route.fulfill({ contentType: 'application/json', body: JSON.stringify(value) });
            const pages = {
                '/CashClosing': 'CashClosing',
                '/Admin/Pilot': 'Admin-Pilot',
                '/Account/Login': 'Account-Login'
            };
            if (pages[url.pathname])
                return route.fulfill({
                    contentType: 'text/html',
                    body: fs.readFileSync(
                        path.join(fixtures, pages[url.pathname] + '.html'),
                        'utf8'
                    )
                });
            if (url.pathname === '/api/branches/context')
                return json({
                    canReviewStores: true,
                    locations: [
                        {
                            locationId: 1,
                            storeId: 1,
                            storeName: 'Test shop',
                            locationName: 'Counter'
                        }
                    ],
                    locationId: 1,
                    storeId: 1
                });
            if (url.pathname === '/api/cashclosing') {
                if (request.method() === 'POST') {
                    const body = request.postDataJSON();
                    posts.push(body);
                    if (!history.length)
                        history.push({
                            ...body,
                            closingId: 1,
                            version: 1,
                            expectedCash: 1130,
                            difference: 0,
                            totals: {salesCash: 1000, serviceCash: 100, cashRefunds: 30, supplierCash: 50},
                            closedAt: new Date().toISOString()
                        });
                    if (failedResponse) {
                        failedResponse = false;
                        return route.abort();
                    }
                    return json(history[0]);
                }
                return json({
                    date: '2026-10-07',
                    timeZone: 'Asia/Kolkata',
                    totals: {
                        salesCount: 2, serviceCount: 1, refundCount: 1, supplierCount: 1,
                        salesCash: 1000,
                        serviceCash: 100,
                        cashRefunds: 30,
                        supplierCash: 50,
                        netCash: 1020,
                        fingerprint: 'A'.repeat(64)
                    },
                    closings: history,
                    changedSinceClosing: false
                });
            }
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
        await page.goto('http://stockflow.test/CashClosing');
        await page.waitForFunction(() => !document.querySelector('#cash-fields').disabled);
        assert.equal(await page.locator('#cash-count-sales').textContent(), '(2 invoices)');
        assert((await page.locator('#cash-tile-sales').textContent()).includes('1,000.00'));
        await page.screenshot({path: path.join(fixtures, 'cash-closing-desktop.png'), fullPage: true});
        await page.locator('#cash-opening').fill('100');
        await page.locator('#cash-added').fill('20');
        await page.locator('#cash-removed').fill('10');
        await page.locator('#cash-counted').fill('1130');
        assert((await page.locator('#cash-expected').textContent()).includes('1,130.00'));
        assert((await page.locator('#cash-counted-preview').textContent()).includes('1,130.00'));
        await page.locator('#cash-counted').fill('1120');
        assert((await page.locator('#cash-difference').textContent()).includes('-'));
        await page.locator('#cash-counted').fill('1130');
        await page.locator('#cash-save').click();
        assert.equal(posts.length, 0, 'missing reason blocks save');
        await page.locator('#cash-reason').fill('Owner deposited extra cash');
        await page.locator('#cash-save').click();
        await page.waitForFunction(
            () =>
                !document.querySelector('#cash-pending').hidden &&
                !document.querySelector('#cash-retry').disabled
        );
        assert(
            await page.locator('#cash-fields').evaluate((element) => element.disabled),
            'unconfirmed save freezes original details'
        );
        await page.reload();
        await page.waitForSelector('#cash-pending:not([hidden])');
        await page.locator('#cash-retry').click();
        await page.waitForSelector('#cash-history tr');
        assert.deepEqual(posts[0], posts[1], 'reload and retry reuse the exact saved request');
        assert.equal(history.length, 1);
        assert.equal(await page.locator('#cash-history tr td').count(), 10);
        await page.getByRole('button', {name: 'View closing version 1', exact: true}).click();
        assert((await page.locator('#cash-record-details').textContent()).includes('1,130.00'));
        await page.getByRole('button', {name: 'Close', exact: true}).click();
        await page.emulateMedia({media: 'print'});
        await page.evaluate(() => { window.print = () => { window.printedClosing = document.querySelector('#cash-print-record').textContent; window.printedSaved = document.body.classList.contains('cash-print-saved'); window.savedPrintVisible = getComputedStyle(document.querySelector('#cash-print-record')).display !== 'none' && getComputedStyle(document.querySelector('#cash-workspace')).display === 'none'; }; });
        await page.emulateMedia({media: 'screen'});
        await page.evaluate(() => document.querySelector('[aria-label="Print closing version 1"]').onclick());
        await page.waitForFunction(() => window.printedSaved);
        await page.emulateMedia({media: 'print'});
        await page.evaluate(() => document.querySelector('[aria-label="Print closing version 1"]').onclick());
        await page.waitForFunction(() => window.savedPrintVisible);
        await page.emulateMedia({media: 'screen'});
        assert((await page.evaluate(() => window.printedClosing)).includes('Owner deposited extra cash'));
        assert((await page.evaluate(() => window.printedClosing)).includes('1,000.00'));
        for (let version = 2; version <= 6; version++) history.unshift({...history[0], version, closingId: version});
        await page.locator('#cash-refresh').click();
        await page.waitForFunction(() => !document.querySelector('#cash-history-all').hidden);
        assert.equal(await page.locator('#cash-history tr').count(), 5);
        await page.locator('#cash-history-all').click();
        assert.equal(await page.locator('#cash-history tr').count(), 6);
        assert.equal(await page.locator('#shop-language').count(), 0);
        for (const width of [768, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            const overflow = await page.evaluate(() =>
                [...document.querySelectorAll('body *')]
                    .filter((element) => element.getBoundingClientRect().right > innerWidth + 1)
                    .map((element) => ({
                        tag: element.tagName,
                        id: element.id,
                        class: element.className,
                        right: element.getBoundingClientRect().right
                    }))
                    .slice(0, 12)
            );
            assert(
                await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
                'cash closing fits ' + width + ': ' + JSON.stringify(overflow)
            );
        }
        await page.goto('http://stockflow.test/Admin/Pilot');
        await page.waitForSelector('#pilot-status-receiving');
        assert.equal(await page.locator('#pilot-status-receiving').inputValue(), 'PENDING');
        await page.locator('#pilot-sign').click();
        assert(
            await page.locator('#pilot-form .is-invalid').count(),
            'empty pilot details show required fields'
        );
        for (const id of [
            'store',
            'operator',
            'build',
            'workstation',
            'scanner',
            'printer',
            'backup'
        ])
            await page.locator('#pilot-' + id).fill('Test evidence: ' + id);
        await page.locator('#pilot-sign').click();
        assert(
            (await page.locator('#pilot-message').textContent()).includes('Every check must pass')
        );
        const checks = [
            'receiving',
            'scanner',
            'labels',
            'billing',
            'competition',
            'recovery',
            'access',
            'closing',
            'backup',
            'language'
        ];
        for (const id of checks) {
            await page.locator('#pilot-status-' + id).selectOption('PASS');
            await page.locator('#pilot-evidence-' + id).fill('Reviewed locally, case ' + id);
        }
        await page.locator('#pilot-sign').click();
        const readSaved = () =>
            page.evaluate(() =>
                JSON.parse(
                    localStorage.getItem(
                        document.querySelector('#pilot-workspace').dataset.storageKey
                    )
                )
            );
        assert((await readSaved()).signedAt);
        await page.reload();
        assert.equal(await page.locator('#pilot-status-receiving').inputValue(), 'PASS');
        await page.locator('#pilot-evidence-receiving').fill('Issue found during another check');
        assert.equal((await readSaved()).signedAt, null, 'any edit invalidates sign-off');
        const downloadEvent = page.waitForEvent('download');
        await page.locator('#pilot-export').click();
        const download = await downloadEvent;
        await download.saveAs(path.join(fixtures, 'pilot-export-test.json'));
        assert.equal(
            JSON.parse(fs.readFileSync(path.join(fixtures, 'pilot-export-test.json'), 'utf8'))
                .signedAt,
            null
        );
        assert.deepEqual(errors, []);
        console.log(
            'PASS: cash calculation, mandatory reason, interrupted save/reload/retry, 320/390/768 layouts, pilot evidence/sign-off/export and removal of the global language dropdown.'
        );
    } finally {
        await browser.close();
    }
})().catch((error) => {
    console.error(error);
    process.exitCode = 1;
});
