// Run after MasterDataChecks --configuration-ui-fixtures. All APIs are mocked.
const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
const fixtures = path.join(root, 'tests/bin/configuration-ui-fixtures');
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage({ reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const definitions = { Categories: ['category', 'categoryName'], Brands: ['brand', 'brandName'], Suppliers: ['supplier', 'supplierName'] };
    let current, saved, fail, writes, duplicate = false;
    await page.route('**/*', async route => {
      const req = route.request(), url = new URL(req.url()), p = url.pathname;
      const json = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
      if (p === '/api/branches/context') return json({ locations: [], storeId: 0, locationId: 0 });
      if (p === '/api/suppliers/options') return json({ countries: [{ code: 'IN', name: 'India' }, { code: 'US', name: 'United States' }] });
      if (p === '/api/brands/categories') return json([{categoryId:1,categoryName:'Battery'}]);
      if (p === '/api/suppliers/duplicates') return json(duplicate ? [{ supplierId: 2, supplierCode: 'SUP000002', supplierName: 'Existing Supplier', mobileNumber: '+919876543210', isActive: true }] : []);
      if (p.startsWith('/api/')) {
        const endpoint = '/api/' + current.toLowerCase();
        if (p === endpoint || p === endpoint + '/1') {
          if (req.method() !== 'GET') {
            writes.push(req.method()); assert.ok(req.headers()['x-csrf-token']);
            if (fail) return json({ message: 'A record with this name already exists.' }, 400);
            if (req.method() === 'DELETE') saved.isActive = false;
            else saved = { ...req.postDataJSON(), [definitions[current][0] + 'Id']: 1, isActive: true };
            return json({ id: 1 });
          }
          return json(p.endsWith('/1') ? saved : saved ? [saved] : []);
        }
        if (p === '/api/categories') return json([{ categoryId: 1, categoryName: 'Battery' }]);
        if (p === '/api/brands') return json([{ brandId: 1, brandName: 'Example brand' }]);
        return json([]);
      }
      const fixture = path.join(fixtures, p.slice(1) + '.html');
      if (fs.existsSync(fixture)) return route.fulfill({ path: fixture, contentType: 'text/html' });
      const file = path.resolve(assets, '.' + p.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/, ''));
      if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
      return route.fulfill({ status: 404, body: '' });
    });
    for (current of Object.keys(definitions)) {
      saved = null; fail = false; writes = [];
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto('http://stockflow.test/' + current);
      await page.getByText('No matching records.').waitFor();
      assert.equal(await page.locator('.sf-nav a[href="/Configuration"]').count(), 1);
      assert.equal(await page.locator('.configuration-menu a[aria-current="page"]').innerText(), current);
      await page.locator('#add-record').click();
      await page.locator('#editor[open]').waitFor();
      assert.equal(await page.locator('#master-list').isVisible(), true);
      await page.locator('#master-form [type=submit]').click(); assert.equal(writes.length, 0);
      await page.locator('#' + definitions[current][1]).fill('Example ' + current);
      if (current === 'Brands') await page.locator('#brand-category-choices input[value="1"]').check();
      if (current === 'Categories' || current === 'Brands') {
        const code = current === 'Categories' ? 'categoryCode' : 'brandCode';
        assert.equal(await page.locator('#' + code).evaluate(e => e.readOnly), true);
        await page.locator('#description').fill('Description for ' + current);
        assert.equal(await page.locator('#description').getAttribute('maxlength'), '500');
        if (current === 'Categories') assert.equal(await page.locator('#parentCategoryId').inputValue(), '');
      }
      if (current === 'Suppliers') {
        assert.equal(await page.locator('#supplierCode').getAttribute('readonly'), '');
        await page.locator('#paymentTermsChoice').selectOption('CUSTOM');
        await page.locator('#paymentTermsDays').fill('21');
        await page.locator('#gstNumber').fill('29ABCDE1234F1Z5');
        assert.equal(await page.locator('#state').evaluate(e => e.required), true);
        await page.locator('#state').fill('Karnataka');
        duplicate = true;
        await page.locator('#master-form [type=submit]').click();
        await page.getByText('Possible Duplicate Supplier', { exact: true }).waitFor();
        await page.getByText('View Existing Supplier: SUP000002 Existing Supplier').click();
        await page.getByText('Mobile: +919876543210', { exact: true }).waitFor();
        await page.locator('dialog').last().getByRole('button', { name: 'Cancel', exact: true }).click();
        assert.equal(writes.length, 0);
        await page.locator('#master-form [type=submit]').click();
        await page.getByRole('button', { name: 'Continue Anyway' }).click();
        await page.getByText('Record saved.').waitFor();
        assert.equal(saved.paymentTermsDays, 21);
        assert.equal(saved.supplierCode, undefined);
        assert.equal(saved.paymentTermsChoice, undefined);
        duplicate = false;
        await page.getByRole('button', { name: 'Edit', exact: true }).click();
        assert.equal(await page.locator('#paymentTermsChoice').inputValue(), 'CUSTOM');
        assert.equal(await page.locator('#paymentTermsDays').inputValue(), '21');
      }
      if (current === 'Products') await page.locator('#categoryId').selectOption('1');
      fail = true; await page.locator('#master-form [type=submit]').click();
      await page.locator('#editor-message').getByText('A record with this name already exists.').waitFor();
      assert.equal(await page.locator('#editor').evaluate(d => d.open), true);
      fail = false; await page.locator('#master-form [type=submit]').click();
      await page.getByText('Record saved.').waitFor();
      assert.equal(await page.locator('#editor').evaluate(d => d.open), false);
      await page.getByRole('button', { name: 'Edit', exact: true }).click();
      await page.locator('#editor[open]').waitFor();
      assert.equal(await page.locator('#' + definitions[current][1]).inputValue(), 'Example ' + current);
      if (current === 'Categories' || current === 'Brands') {
        assert.equal(await page.locator('#description').inputValue(), 'Description for ' + current);
        assert.equal(await page.locator('#master-rows td[title="Description for ' + current + '"]').count(), 1);
        assert.equal(saved[current === 'Categories' ? 'categoryCode' : 'brandCode'], undefined);
        if (current === 'Categories') assert.equal(await page.locator('#parentCategoryId option[value="1"]').evaluate(e => e.disabled), true);
      }
      await page.locator('#master-form [type=submit]').click(); await page.locator('#editor').waitFor({ state: 'hidden' });
      assert.ok(writes.includes('PUT'));
      await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
      await page.locator('#cancel-deactivate').click(); assert.ok(!writes.includes('DELETE'));
      await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
      await page.locator('#confirm-deactivate').click(); await page.getByText('Record deactivated.').waitFor();
      assert.ok(writes.includes('DELETE'));
      for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, JSON.stringify(await page.evaluate(() => [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth && getComputedStyle(e).position !== 'fixed').slice(0, 12).map(e => [e.tagName, e.className, e.getBoundingClientRect().right]))));
        await page.locator('#add-record').click(); await page.locator('#editor[open]').waitFor();
        const box = await page.locator('#editor').boundingBox(); assert.ok(box.x >= 0 && box.x + box.width <= width);
        if (current === 'Suppliers') {
          const footer = await page.locator('#master-form .configuration-dialog-actions').boundingBox();
          assert.ok(footer.y >= box.y && footer.y + footer.height <= 900, 'Supplier footer stays visible');
          assert.ok(await page.locator('#form-fields').evaluate(e => e.scrollHeight > e.clientHeight), 'Supplier fields scroll');
          await page.screenshot({ path: path.join(fixtures, 'supplier-popup-' + width + '.png') });
        }
        await page.keyboard.press('Escape'); assert.equal(await page.locator('#editor').evaluate(d => d.open), false);
        assert.equal(await page.locator('#add-record').evaluate(b => b === document.activeElement), true);
      }
      await page.locator('#add-record').click(); await page.locator('#editor[open]').waitFor();
      await page.locator('#close-editor').click();
      await page.locator('#add-record').click(); await page.locator('#editor[open]').waitFor();
      await page.locator('#cancel-edit').click();
      console.log('PASS:', current, 'create/edit, validation, deactivate/cancel, Escape/focus, close/cancel, responsive layout');
    }
    current = 'Categories';
    await page.goto('http://stockflow.test/Categories-readonly');
    await page.locator('#master-rows td').first().waitFor();
    assert.equal(await page.locator('#add-record').count(), 0);
    assert.equal(await page.locator('#master-rows button').count(), 0);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('http://stockflow.test/Categories'); await page.locator('#master-rows td').first().waitFor();
    await page.screenshot({ path: path.join(fixtures, 'configuration-list.png') });
    await page.locator('#add-record').click(); await page.locator('#editor[open]').waitFor();
    await page.screenshot({ path: path.join(fixtures, 'configuration-popup.png') });
    assert.deepEqual(errors, []);
    console.log('PASS: read-only actions hidden; no browser errors.');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });




