// Actual Razor/assets and database-produced source snapshots. Provider/HTTP is mocked.
const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..'), fixtures = path.join(root, 'tests/bin/eway-fixtures');
const read = name => JSON.parse(fs.readFileSync(path.join(fixtures, name), 'utf8'));
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 720 } }), errors = [], saves = [], operations = new Map();
        const source = read('source.json'), transfer = read('transfer.json'), template = read('detail.json'), options = read('options.json');
        let saved = null, failFirst = true, sourceCalls = 0;
        page.on('pageerror', e => errors.push(e.message));
        await page.route('**/*', async route => {
            const req = route.request(), url = new URL(req.url()), p = url.pathname;
            const json = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
            if (p === '/EWayBills' || p.startsWith('/EWayBills/')) return route.fulfill({ contentType: 'text/html', path: path.join(fixtures, p === '/EWayBills' ? 'Index.html' : p.endsWith('/Provider') ? 'Provider.html' : 'Prepare.html') });
            if (p === '/api/ewaybills/options') return json(options);
            if (p === '/api/ewaybills/provider') return json({ name: 'No authorized adapter installed', status: 'Blocked: authorized contract required' });
            if (p === '/api/ewaybills/sources') { sourceCalls++; const s = url.searchParams.get('type') === 'TRANSFER' ? transfer : source; return json({ page: 1, hasMore: false, rows: [{ type: s.type, id: s.id, registrationId: s.registrationId, number: s.number, date: s.date, party: s.partyName || 'Own stock movement' }] }); }
            if (p === '/api/ewaybills/source') return json(url.searchParams.get('type') === 'TRANSFER' ? transfer : source);
            if (p === '/api/ewaybills' && ['POST', 'PUT'].includes(req.method())) {
                const r = req.postDataJSON(); saves.push(structuredClone(r)); assert(req.headers()['x-csrf-token']);
                if (!operations.has(r.operationId)) { saved = { document: { ...template.document, documentId: r.documentId, version: r.expectedVersion + 1, status: 'DRAFT', sourceType: r.sourceType, sourceId: r.sourceId, registrationId: r.registrationId }, source: r.sourceType === 'GST' ? source : transfer, preparation: r.preparation, validation: { issues: [], warnings: ['Preparation only; no official submission.'] } }; operations.set(r.operationId, saved.document.version); }
                if (failFirst) { failFirst = false; return json({ message: 'Synthetic response lost after commit' }, 503); }
                return json({ version: operations.get(r.operationId) });
            }
            if (p === '/api/ewaybills') return json({ total: saved ? 1 : 0, page: 1, pageSize: 20, rows: saved ? [{ ...saved.document, sourceNumber: source.number, sourceDate: source.date, party: source.partyName, total: source.total, fromState: '29', toState: '29', mode: 'ROAD' }] : [] });
            if (p === '/api/ewaybills/usage') return json({ officialGenerations: 0, providerRequests: 0, providerCharges: null, monthlyStockFlowCap: null });
            if (p.endsWith('/history') && p.startsWith('/api/ewaybills/')) return json([{ action: 'SAVE', version: 1, actorId: 1, createdAt: '2026-11-02T10:00:00Z', snapshotJson: JSON.stringify({ source, body: saved.preparation }) }]);
            if (p.endsWith('/validate') && p.startsWith('/api/ewaybills/')) return json({ validation: saved.validation });
            if (p.endsWith('/export') && p.startsWith('/api/ewaybills/')) return route.fulfill({ contentType: 'text/csv', body: fs.readFileSync(path.join(fixtures, 'worksheet.csv')) });
            if (p.endsWith('/external') && p.startsWith('/api/ewaybills/')) { const r = req.postDataJSON(); saved.document.status = 'EXTERNAL_UNVERIFIED'; saved.document.version++; saved.document.externalEbn = r.ebn; saved.document.externalEvidence = r.evidence; return json({ status: saved.document.status }); }
            if (p.startsWith('/api/ewaybills/')) return json(saved);
            if (p === '/api/notifications') return json({ unread: 0, items: [] });
            if (p === '/api/branches/context') return json({ canReviewStores: true, locations: [{ storeId: 1, locationId: 1, storeName: 'Main store', locationName: 'Counter' }], storeId: 1, locationId: 1 });
            const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
            const file = path.resolve(assets, '.' + p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
            if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
            return json({});
        });
        await page.goto('http://stockflow.test/EWayBills/Prepare?saleId=' + source.saleId);
        await page.locator('#eway-sources button').first().click();
        await page.waitForSelector('#eway-form:not([hidden])');
        assert.strictEqual(await page.locator('[name=movementDocumentNumber]').inputValue(), source.number);
        assert((await page.locator('#eway-goods').textContent()).includes(source.lines[0].hsnSac));
        assert.strictEqual(await page.locator('[data-party=billFrom] [data-field=gstin]').inputValue(), source.registration.gstin);
        assert.strictEqual(await page.locator('[data-party=dispatchFrom] [data-field=address]').inputValue(), '', 'Unknown physical address was inferred');
        for (const party of ['billFrom', 'dispatchFrom', 'billTo', 'shipTo']) {
            const set = page.locator('[data-party=' + party + ']');
            await set.locator('[data-field=name]').fill('Reviewed actual party'); await set.locator('[data-field=address]').fill('Reviewed actual physical address');
            await set.locator('[data-field=state]').selectOption('29'); await set.locator('[data-field=pin]').fill('560001');
            await set.locator('[data-field=gstin]').fill(party === 'billFrom' ? source.registration.gstin : 'URP');
        }
        await page.locator('[name=transactionType]').selectOption('4');
        await page.locator('[name=movementReason]').fill('Supply; actual subtype reviewed');
        await page.locator('[name=distanceKm]').fill('20'); await page.locator('[name=vehicle]').fill('KA01AB1234');
        await page.locator('[name=applicabilityReview]').fill('Authorized test reviewer checked actual movement and state rules');
        await page.locator('#eway-save').click();
        await page.waitForFunction(() => document.getElementById('eway-state').textContent.includes('unresolved'));
        assert(await page.locator('[name=movementReason]').isDisabled(), 'Uncertain save must freeze its original payload');
        await page.locator('#eway-save').click();
        await page.waitForFunction(() => document.getElementById('eway-state').textContent.includes('Saved revision 1'));
        assert.strictEqual(saves[0].operationId, saves[1].operationId); assert.deepStrictEqual(saves[0], saves[1]); assert.strictEqual(operations.size, 1);
        assert.deepStrictEqual(saves[0].preparation.transferGoods, []);
        await page.locator('#eway-validate').click(); await page.waitForFunction(() => document.getElementById('eway-save').disabled === false);
        const download = page.waitForEvent('download'); await page.locator('#eway-export').click(); assert((await download).suggestedFilename().startsWith('eway-preparation-'));
        for (const [width, height] of [[1280, 720], [1280, 650], [1280, 600], [1024, 768], [768, 1024], [390, 844], [375, 667], [320, 640]]) {
            await page.setViewportSize({ width, height });
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Horizontal overflow at ' + width);
            await page.locator('#eway-save').scrollIntoViewIfNeeded(); assert(await page.locator('#eway-save').isVisible(), 'Save clipped at ' + width + 'x' + height);
        }
        await page.setViewportSize({ width: 1280, height: 720 }); await page.evaluate(() => { window.scrollTo(0, 0); const main = document.querySelector('.sf-main'); if (main) main.scrollTop = 0; }); await page.screenshot({ path: path.join(fixtures, 'preparation-desktop.png'), fullPage: true });
        await page.pdf({ path: path.join(fixtures, 'preparation-worksheet.pdf'), format: 'A4' });
        await page.locator('#eway-ebn').fill('123456789012'); await page.locator('#eway-evidence').fill('Portal record inspected by authorized reviewer, dated test evidence');
        await page.locator('#eway-record').click(); await page.waitForFunction(() => document.getElementById('eway-state').textContent.includes('unverified'));
        assert(await page.locator('#eway-save').isHidden()); assert(await page.locator('[name=movementReason]').isDisabled());
        await page.goto('http://stockflow.test/EWayBills'); await page.waitForSelector('#eway-rows a');
        assert((await page.locator('#eway-rows').textContent()).includes('External — unverified'));
        await page.goto('http://stockflow.test/EWayBills/Provider'); await page.waitForFunction(() => document.getElementById('eway-provider-status').textContent.includes('Blocked'));
        await page.goto('http://stockflow.test/EWayBills/Prepare?sourceType=TRANSFER'); await page.locator('#eway-sources button').first().click();
        await page.waitForSelector('#eway-transfer-goods:not([hidden])'); assert.strictEqual(await page.locator('[name=documentType]').inputValue(), 'CHL');
        assert.strictEqual(await page.locator('[name=movementDocumentNumber]').inputValue(), '', 'Transfer number must not become a fabricated challan');
        await page.locator('#eway-add-line').click(); assert.strictEqual(await page.locator('#eway-transfer-lines tr').count(), 1);
        await page.locator('[name=mode]').selectOption('PART_A'); assert(await page.locator('#eway-vehicle-label').isHidden()); assert(await page.locator('#eway-transport-doc-label').isHidden());
        assert(sourceCalls >= 2); assert.deepStrictEqual(errors, []);
        console.log('PASS: real Razor/source snapshots, guided preparation, unknown fields, same-operation lost-response retry, CSV/PDF worksheets, eight viewports, unverified external state, frozen history, source links, transfer/challan distinction, Part A controls and blocked provider; mocked HTTP.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
