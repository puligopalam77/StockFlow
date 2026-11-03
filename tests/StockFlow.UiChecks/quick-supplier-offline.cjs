// Generate Razor fixtures with MasterDataChecks --supplier-checks first.
const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/bin/quick-supplier-ui-fixtures');
const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
        page.setDefaultTimeout(15000);
        const errors = [], supplierPosts = [], receiptPosts = [];
        let limited = false, duplicates = false, failure = 0, delay = false;
        const existing = { supplierId: 1, supplierCode: 'SUP000001', supplierName: 'Existing supplier', isActive: true };
        const product = { productId: 1, variantId: 11, productName: 'Cable / Standard', variantName: 'Standard', sku: 'CABLE', purchasePrice: 100, taxPercentage: 0, isActive: true, isSerialTracked: false };
        page.on('pageerror', e => errors.push(e.message));
        await page.route('**/*', async route => {
            const request = route.request(), url = new URL(request.url()), p = url.pathname;
            const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
            if (p === '/Purchases/Create' || p === '/Receiving') {
                const name = p === '/Receiving' ? 'Receiving' : 'Purchase';
                return route.fulfill({ path: path.join(fixtures, name + (limited ? 'Limited' : '') + '.html'), contentType: 'text/html' });
            }
            if (p === '/api/branches/context') return json({ locationId: 1, storeId: 1, canReviewStores: true, locations: [{ locationId: 1, storeId: 1, storeName: 'Main', locationName: 'Stock' }] });
            if (p === '/api/suppliers/options') return json({ countries: [{ code: 'IN', name: 'India' }, { code: 'US', name: 'United States' }] });
            if (p === '/api/suppliers/duplicates') return json(duplicates ? [existing] : []);
            if (p === '/api/suppliers' && request.method() === 'POST') {
                supplierPosts.push(request.postDataJSON());
                assert(request.headers()['x-csrf-token'], 'Supplier creation includes the CSRF token');
                if (delay) await new Promise(resolve => setTimeout(resolve, 300));
                if (failure) return json({ message: failure === 403 ? 'Forbidden' : 'Enter a valid supplier name.' }, failure);
                return json({ ...request.postDataJSON(), supplierId: 10 + supplierPosts.length, supplierCode: 'SUP000011', isActive: true }, 201);
            }
            if (p === '/api/suppliers') {
                assert.equal(url.searchParams.get('activeOnly'), 'true');
                return json([existing]);
            }
            if (p === '/api/products/variants') return json([product]);
            if (p === '/api/products') return json([{ ...product, hasVariants: false }]);
            if (p === '/api/categories') return json([{ categoryId: 1, categoryName: 'General' }]);
            if (p === '/api/scan') return json({ ...product, isAvailable: true });
            if (request.method() !== 'GET' && (p.startsWith('/api/purchases') || p.startsWith('/api/receiving'))) receiptPosts.push(p);
            if (p.startsWith('/api/')) return json([]);
            const file = path.join(assets, p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
            return file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()
                ? route.fulfill({ path: file }) : route.fulfill({ status: 404, body: '' });
        });
        for (const [url, selector, fields] of [['/Purchases/Create', '#supplierId', '#purchase-fields'], ['/Receiving', '#intake-supplier', '#receiving-fields']]) {
            async function start() {
                await page.goto('http://stockflow.test' + url);
                await page.waitForFunction(id => !document.querySelector(id).disabled, fields);
                if (!limited) await page.waitForFunction(() => !document.querySelector('#add-record').disabled);
            }
            async function open(name = 'New supplier') {
                await page.locator('#add-record').click();
                await page.locator('#editor[open]').waitFor();
                await page.locator('#quick-supplier-supplierName').fill(name);
            }
            const submit = page.locator('#master-form [type=submit]');
            await start();
            await page.locator(selector).selectOption('1');
            if (url === '/Purchases/Create') {
                await page.locator('#notes').fill('Keep this receipt');
                await page.locator('#purchase-search').fill('Cable');
                await page.locator('#purchase-items select').first().selectOption('11');
                await page.locator('#purchase-items input[type=number]').first().fill('3');
            } else {
                await page.locator('#intake-next').click();
                await page.locator('#intake-scan').fill('CABLE');
                await page.locator('#intake-scan').press('Enter');
                await page.locator('[data-variant="11"]').waitFor();
                await page.locator('#intake-back').click();
            }
            await open();
            await page.locator('#cancel-edit').click();
            assert.equal(await page.locator(selector).inputValue(), '1');
            let before = supplierPosts.length;
            await open(''); await submit.click(); assert.equal(supplierPosts.length, before, 'Required name prevents API calls');
            await page.locator('#quick-supplier-supplierName').fill('New supplier');
            await page.locator('#quick-supplier-paymentTermsChoice').selectOption('CUSTOM');
            await page.locator('#quick-supplier-paymentTermsDays').fill('21');
            await page.locator('#quick-supplier-gstNumber').fill('29ABCDE1234F1Z5');
            assert(await page.locator('#quick-supplier-state').evaluate(n => n.required));
            await page.locator('#quick-supplier-state').fill('Karnataka');
            duplicates = true;
            await submit.click();
            await page.getByText('Possible Duplicate Supplier', { exact: true }).waitFor();
            await page.locator('dialog').last().getByRole('button', { name: 'Cancel', exact: true }).click();
            assert.equal(supplierPosts.length, before, 'Duplicate cancellation never creates a supplier');
            assert(await page.locator('#editor').evaluate(n => n.open));
            await submit.click();
            await page.getByRole('button', { name: 'Continue Anyway' }).click();
            await page.locator('#editor').waitFor({ state: 'hidden' });
            assert.equal(supplierPosts.length, before + 1);
            assert.equal(await page.locator(selector).inputValue(), String(10 + supplierPosts.length));
            assert((await page.locator(selector + ' option:checked').textContent()).includes('New supplier'));
            assert.equal(supplierPosts.at(-1).supplierCode, undefined);
            assert.equal(supplierPosts.at(-1).paymentTermsChoice, undefined);
            assert.equal(supplierPosts.at(-1).paymentTermsDays, 21);
            if (url === '/Purchases/Create') {
                assert.equal(await page.locator('#paymentTermsDays').count(), 1, 'Purchase fields keep unique IDs');
                assert.equal(await page.locator('#paymentTermsDays').inputValue(), '21');
                assert.equal(await page.locator('#notes').inputValue(), 'Keep this receipt');
                assert.equal(await page.locator('#purchase-items select').first().inputValue(), '11');
                assert.equal(await page.locator('#purchase-items input[type=number]').first().inputValue(), '3');
            } else {
                assert.equal(await page.locator('[data-variant="11"]').count(), 1, 'Receiving cart is preserved');
            }
            duplicates = false;
            for (const status of [400, 403]) {
                failure = status; await open(); await submit.click();
                await page.locator('#editor-message').getByText(status === 403 ? 'You do not have permission for this action.' : 'Enter a valid supplier name.', { exact: true }).waitFor();
                assert(await page.locator('#editor').evaluate(n => n.open));
                assert(!(await submit.isDisabled()));
                await page.locator('#cancel-edit').click();
            }
            failure = 0; delay = true; before = supplierPosts.length;
            await open('One supplier');
            await page.evaluate(() => { document.querySelector('#master-form').requestSubmit(); document.querySelector('#master-form').requestSubmit(); });
            await page.waitForFunction(() => document.querySelector('#master-form [type=submit]').disabled);
            await page.keyboard.press('Escape'); assert(await page.locator('#editor').evaluate(n => n.open));
            await page.locator('#editor').waitFor({ state: 'hidden' });
            assert.equal(supplierPosts.length, before + 1, 'Repeated submission creates one supplier'); delay = false;
            for (const width of [1440, 768, 390, 320]) {
                await page.setViewportSize({ width, height: 900 }); await open();
                const box = await page.locator('#editor').boundingBox(); assert(box.x >= 0 && box.x + box.width <= width);
                await page.locator('#cancel-edit').click();
            }
            limited = true; await start();
            assert.equal(await page.locator('#add-record,#master-data').count(), 0, 'No creation UI without permission');
            limited = false; await page.setViewportSize({ width: 1440, height: 900 });
            console.log('PASS: Quick Add Supplier on ' + url + ': cancel, validation, duplicate review, auto-selection, failure recovery, repeated submits, permissions and responsive dialog.');
        }
        assert.deepEqual(receiptPosts, [], 'Adding a supplier never submits a receipt or changes stock');
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
