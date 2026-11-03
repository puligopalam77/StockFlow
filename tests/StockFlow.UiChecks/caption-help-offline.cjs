const fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const assets = path.join(root, 'src/StockFlow.Web/wwwroot');
const fixtures = path.join(root, 'tests/bin/caption-help-fixtures');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        for (const name of fs.readdirSync(fixtures).filter(name => name.endsWith('.html'))) {
            // Use the actual server-rendered forms without making business API calls.
            const rendered = fs.readFileSync(path.join(fixtures, name), 'utf8');
            const styles = [...rendered.matchAll(/<link\b[^>]*href="([^"]+)"[^>]*>/gi)]
                .map(match => match[1].split('?')[0].replace(/^\//, '').replace(/\.[a-z0-9]{8,}\.css$/, '.css'))
                .filter(file => file.endsWith('.css') && fs.existsSync(path.join(assets, file)));
            const html = rendered
                .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '')
                .replace(/<link\b[^>]*>/gi, '');
            await page.setContent(html);
            for (const css of styles)
                await page.addStyleTag({ path: path.join(assets, css) });
            for (const js of ['common/caption-catalog.js', 'common/caption-help.js'])
                await page.addScriptTag({ path: path.join(assets, 'js', js) });
            await page.evaluate(() => {
                document.querySelectorAll('fieldset[disabled]').forEach(node => node.disabled = false);
                const registration = document.querySelector('#purchase-registration');
                if (registration) registration.showModal();
                StockFlowCaptionHelp.refresh();
            });
            const misaligned = await page.evaluate(() => [...document.querySelectorAll('.sf-caption-group:not(.sf-caption-field-group)')].filter(group => {
                const caption = group.firstElementChild.getBoundingClientRect();
                const icon = group.querySelector('.sf-caption-info').getBoundingClientRect();
                if (!caption.width || !icon.width) return false;
                return Math.abs(icon.width - 14) > 1 || Math.abs(icon.height - 14) > 1
                    || Math.abs((icon.top + icon.height / 2) - (caption.top + caption.height / 2)) > 3
                    || icon.left - caption.right > 7;
            }).map(group => group.firstElementChild.textContent.trim()));
            assert.deepEqual(misaligned, [], `Caption/icon alignment on ${name}`);
            const button = await page.locator('dialog[open]').count()
                ? page.locator('dialog[open] .sf-caption-info:visible').first()
                : page.locator('main .sf-caption-info:visible').first();
            if (!await button.count()) continue;
            await button.focus();
            const id = await button.getAttribute('aria-controls');
            const tip = page.locator('#' + id);
            await tip.waitFor({ state: 'visible' });
            assert((await tip.textContent()).length > 15, `Explanation missing on ${name}`);
            const bounds = await tip.boundingBox();
            assert(bounds.x >= 0 && bounds.x + bounds.width <= 391 && bounds.y >= 0 && bounds.y + bounds.height <= 845, `Tooltip clipped on ${name}: ${JSON.stringify(bounds)}`);
            await page.keyboard.press('Escape');
            await tip.waitFor({ state: 'hidden' });
            if (await page.locator('#purchase-registration').count())
                assert(await page.locator('#purchase-registration').evaluate(dialog => dialog.open), 'Escape should dismiss help before closing the form');
            if (name === 'Products.html') {
                await page.screenshot({ path: path.join(fixtures, 'registration-mobile.png') });
                await page.setViewportSize({ width: 1200, height: 900 });
                await page.screenshot({ path: path.join(fixtures, 'registration-desktop.png') });
                await page.setViewportSize({ width: 390, height: 844 });
            }
        }
        // Dynamic fields keep their accessible name, data, validation and label behaviour.
        await page.setContent('<main><form><div><label for="tracking">Tracking *</label><select id="tracking"><option value="SERIAL">Serial Tracking</option></select><small class="text-muted">Each item needs its own serial number.</small></div><label><input id="variants" type="checkbox">Has Variants</label><div id="dynamic"></div></form></main>');
        for (const js of ['common/caption-catalog.js', 'common/language.js', 'common/caption-help.js'])
            await page.addScriptTag({ path: path.join(assets, 'js', js) });
        await page.addStyleTag({ path: path.join(assets, 'css/caption-help.css') });
        await page.evaluate(() => {
            document.querySelector('#dynamic').innerHTML = '<label for="due">Due Date</label><input id="due" value="2026-10-27" aria-describedby="existing"><small id="existing" class="text-muted">Only dated credit sales can be overdue.</small>';
        });
        await page.locator('#dynamic .sf-caption-info').waitFor();
        assert.equal(await page.getByLabel('Due Date', { exact: true }).inputValue(), '2026-10-27');
        assert((await page.locator('#due').getAttribute('aria-describedby')).includes('existing'));
        assert.equal(await page.getByLabel('Tracking *', { exact: true }).inputValue(), 'SERIAL');
        const count = await page.locator('.sf-caption-info').count();
        await page.evaluate(() => { for (let i = 0; i < 5; i++) StockFlowCaptionHelp.refresh(); });
        assert.equal(await page.locator('.sf-caption-info').count(), count, 'Repeated rendering duplicated info icons');
        const checkInfo = page.locator('label:has(#variants) + .sf-caption-help button');
        await checkInfo.click();
        assert.equal(await page.locator('#variants').isChecked(), false, 'Opening help toggled the checkbox');
        const checkTip = page.locator('#' + await checkInfo.getAttribute('aria-controls'));
        await checkTip.waitFor({ state: 'visible' });
        await page.locator('#tracking').click();
        await checkTip.waitFor({ state: 'hidden' });
        await page.evaluate(() => { document.querySelector('#tracking').blur(); });
        const trackingInfo = page.locator('label[for="tracking"] + .sf-caption-help button');
        await trackingInfo.hover();
        const trackingTip = page.locator('#' + await trackingInfo.getAttribute('aria-controls'));
        await trackingTip.waitFor({ state: 'visible' });
        assert((await trackingTip.textContent()).includes('Each item needs its own serial number.'));
        assert.equal(await page.locator('small.text-muted:visible').count(), 0, 'Static help should be available through icons');
        const touch = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
        await touch.setContent('<meta name="viewport" content="width=device-width,initial-scale=1"><main><label><input id="touch-variants" type="checkbox">Has Variants</label></main>');
        await touch.addStyleTag({ path: path.join(assets, 'css/caption-help.css') });
        for (const js of ['common/caption-catalog.js', 'common/caption-help.js'])
            await touch.addScriptTag({ path: path.join(assets, 'js', js) });
        const touchInfo = touch.locator('.sf-caption-info');
        await touchInfo.tap();
        await touch.locator('[role="tooltip"]').waitFor({ state: 'visible' });
        assert.equal(await touch.locator('#touch-variants').isChecked(), false);
        await touchInfo.tap();
        await touch.locator('[role="tooltip"]').waitFor({ state: 'hidden' });
        await touch.close();
        assert.deepEqual(errors, []);
        console.log('PASS: module forms, modal top-layer tooltips, mobile bounds, focus, Escape, hover, touch, click dismissal, dynamic fields, descriptions and unchanged input values.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
