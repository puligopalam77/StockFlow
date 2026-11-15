// Run --help-panel-checks first to create the fixtures used below.
// All API responses are mocked; this test never connects to a shop database.
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');

const projectRoot = path.resolve(__dirname, '../..');
const assetsFolder = path.join(projectRoot, 'src/StockFlow.Web/wwwroot');
const fixturesFolder = path.join(projectRoot, 'tests/bin/help-panel-fixtures');
const restrictedArticles = JSON.parse(fs.readFileSync(path.join(fixturesFolder, 'scoped.json'), 'utf8'));
const billingHtml = fs.readFileSync(path.join(fixturesFolder, 'billing.html'), 'utf8');
const fullArticles = JSON.parse(billingHtml.match(/<script id="help-data" type="application\/json">(.*?)<\/script>/s)[1]);

async function runPermissionChecks() {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1366, height: 620 } });
        const browserErrors = [];
        let accessRevoked = false;
        let incompleteAccessResponse = false;
        let finishStockRequest;

        page.on('pageerror', error => browserErrors.push(error.message));
        await page.route('**/*', async route => {
            const requestPath = new URL(route.request().url()).pathname;
            if (requestPath === '/Sales/Create') {
                return route.fulfill({ body: billingHtml, contentType: 'text/html' });
            }
            if (requestPath === '/api/help/assistant') {
                const response = incompleteAccessResponse ? {} : {
                    scope: 'Test shop',
                    contextStamp: accessRevoked ? 'limited' : 'full',
                    suggestions: [],
                    articles: accessRevoked ? restrictedArticles : fullArticles
                };
                return route.fulfill({ json: response });
            }
            if (requestPath === '/api/help/assistant/health') {
                return route.fulfill({ json: { modelStatus: 'disabled', voiceStatus: 'unavailable' } });
            }
            if (requestPath === '/api/help/stock') {
                // Keep this request pending until permissions have changed.
                await new Promise(resolve => { finishStockRequest = resolve; });
                return route.fulfill({ json: {
                    scopeName: 'Old shop', checkedAt: new Date().toISOString(), truncated: false,
                    items: [{ productName: 'Old private stock result', sku: 'OLD', currentStock: 9, availableQuantity: 9 }]
                } });
            }
            if (requestPath.startsWith('/api/')) return route.fulfill({ json: [] });

            // Serve real application assets and keep paths within wwwroot.
            const assetPath = requestPath.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, '');
            const assetFile = path.resolve(assetsFolder, '.' + assetPath);
            const isLocalFile = assetFile.startsWith(assetsFolder + path.sep) &&
                fs.existsSync(assetFile) && fs.statSync(assetFile).isFile();
            return isLocalFile ? route.fulfill({ path: assetFile }) : route.fulfill({ status: 404 });
        });

        async function openHelp() {
            const refreshed = page.waitForResponse(response => new URL(response.url()).pathname === '/api/help/assistant');
            await page.locator('#help-open').click();
            await refreshed;
        }

        async function browseArticles() {
            await page.locator('#help-menu-toggle').click();
            await page.locator('#help-browse').click();
        }

        // 1. Authorized help remains available.
        await page.goto('http://stockflow.test/Sales/Create');
        await openHelp();
        await browseArticles();
        await page.locator('#help-results button').filter({ hasText: 'Adding products' }).click();
        assert.match(await page.locator('#help-content').textContent(), /Add a product/);

        await page.locator('#help-back').click();
        await page.locator('[data-help-stock]').click();
        await page.locator('#help-stock-term').fill('Battery');
        const stockRequested = page.waitForRequest('**/api/help/stock?*');
        await page.locator('#help-stock-check').click();
        await stockRequested;
        await page.locator('#help-close').click();

        // 2. A later response must not restore data from the old access context.
        accessRevoked = true;
        await openHelp();
        await page.waitForFunction(() => !JSON.parse(document.querySelector('#help-data').textContent).some(article => article.id === 'products'));
        finishStockRequest();
        await page.waitForFunction(() => !document.querySelector('#help-stock-check').disabled);
        assert.equal(await page.locator('#help-content').textContent(), '');
        assert.equal(await page.locator('#help-stock-result').textContent(), '', 'Discard the stale stock reply');
        assert(await page.locator('[data-help-article="billing"]').isHidden());
        assert(await page.locator('[data-help-article="receiving-stock"]').isHidden());

        const filteredContent = await page.locator('#help-data').textContent();
        const forbiddenHeadings = ['## Staff accounts', '## Roles and permissions', '## Create a bill', '## Receive a delivery', '## Search and export'];
        for (const heading of forbiddenHeadings) assert(!filteredContent.includes(heading), heading);

        await browseArticles();
        await page.locator('#help-search').fill('staff');
        assert.equal(await page.locator('#help-results button').count(), 0);
        await page.locator('#help-search').fill('Search sales invoices');
        assert(await page.locator('#help-results button').count() > 0, 'Permitted help remains searchable');

        // 3. Even HTTP 200 must clear old help if its access data is incomplete.
        await page.locator('#help-close').click();
        incompleteAccessResponse = true;
        await openHelp();
        await page.waitForFunction(() => document.querySelector('#help-data').textContent === '[]');
        assert.equal(await page.locator('#help-results button').count(), 0);
        assert.equal(await page.locator('#help-suggested button').count(), 0);
        assert.deepEqual(browserErrors, []);
        console.log('PASS: permitted help, revoked guides, delayed stock replies and incomplete access responses.');
    } finally {
        await browser.close();
    }
}

runPermissionChecks().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
