// Rendered application forms and mocked APIs only. No database writes.
const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/bin/field-validation-fixtures');
const scripts = path.join(root, 'src/StockFlow.Web/wwwroot/js');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', (error) => errors.push(error.message));
        let fieldChecks = 0;
        const limits = [];
        // Isolate each rendered field from loading/saving handlers to test its declared limits.
        for (const file of fs.readdirSync(fixtures).filter((file) => file.endsWith('.html'))) {
            const html = fs.readFileSync(path.join(fixtures, file), 'utf8')
                .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '');
            await page.setContent(html);
            await page.addScriptTag({ path: path.join(scripts, 'common/form-validation.js') });
            const result = await page.evaluate(() => {
                let count = 0;
                const limits = [];
                function check(condition, input, message) {
                    if (!condition) throw new Error((input.id || input.name) + ': ' + message);
                    count++;
                }
                // Disabled fields normally skip validation. Enable them here to exercise their bounds.
                document.querySelectorAll('fieldset[disabled], input[disabled], select[disabled], textarea[disabled]')
                    .forEach((element) => element.disabled = false);
                for (const input of document.querySelectorAll('input, select, textarea')) {
                    StockFlowValidation.fieldMessage(input);
                    if (!input.willValidate) continue;
                    limits.push({ id: input.id || input.name, type: input.type, required: input.required,
                        min: input.min, max: input.max, minLength: input.minLength,
                        maxLength: input.maxLength, pattern: input.pattern });
                    const original = input.value;
                    if (input.required) {
                        input.value = '';
                        check(!!StockFlowValidation.fieldMessage(input), input, 'blank required field accepted');
                        if (['text', 'textarea', 'password'].includes(input.type)) {
                            input.value = '   ';
                            check(!!StockFlowValidation.fieldMessage(input), input, 'whitespace accepted');
                        }
                    }
                    if (input.type === 'tel' && input.pattern === '[0-9]{10}') {
                        for (const phone of ['123456789', '12345678901', '+919876543210', '12345abc90', '98765 43210']) {
                            input.value = phone;
                            check(!!StockFlowValidation.fieldMessage(input), input, 'invalid phone accepted');
                        }
                        for (const phone of ['0123456789', '9876543210']) {
                            input.value = phone;
                            check(!StockFlowValidation.fieldMessage(input), input, 'valid phone rejected');
                        }
                        if (!input.required) {
                            input.value = '';
                            check(!StockFlowValidation.fieldMessage(input), input, 'optional blank phone rejected');
                        }
                    }
                    if (input.type === 'email') {
                        input.value = 'invalid-email';
                        check(!!StockFlowValidation.fieldMessage(input), input, 'invalid email accepted');
                        input.value = 'shop@example.com';
                        check(!StockFlowValidation.fieldMessage(input), input, 'valid email rejected');
                    }
                    if (input.type === 'number') {
                        if (input.min !== '') {
                            input.value = String(Number(input.min) - 1);
                            check(!!StockFlowValidation.fieldMessage(input), input, 'below-minimum value accepted');
                            input.value = input.min;
                            check(!StockFlowValidation.fieldMessage(input), input, 'minimum value rejected');
                        }
                        if (input.max !== '') {
                            input.value = String(Number(input.max) + 1);
                            check(!!StockFlowValidation.fieldMessage(input), input, 'above-maximum value accepted');
                            input.value = input.max;
                            check(!StockFlowValidation.fieldMessage(input), input, 'maximum value rejected');
                        }
                        if (input.step === '1') {
                            input.value = String(Number(input.min || 0) + 0.5);
                            check(!!StockFlowValidation.fieldMessage(input), input, 'fractional count accepted');
                        }
                    }
                    if (input.maxLength >= 0 && !['number', 'file'].includes(input.type)) {
                        input.value = 'x'.repeat(input.maxLength + 1);
                        check(!!StockFlowValidation.fieldMessage(input), input, 'programmatic overlength value accepted');
                    }
                    if (input.minLength > 1) {
                        input.value = 'x'.repeat(input.minLength - 1);
                        check(!!StockFlowValidation.fieldMessage(input), input, 'short text accepted');
                    }
                    input.value = original;
                }
                return { count, limits };
            });
            fieldChecks += result.count;
            limits.push({ page: file, fields: result.limits });
        }

        // Exercise real dynamic registration forms and prove invalid submissions make no API write.
        for (const [module, script, name] of [
            ['Customers', 'customer.js', 'customerName'],
            ['Suppliers', 'supplier.js', 'supplierName'],
            ['Categories', 'category.js', 'categoryName']
        ]) {
            const html = fs.readFileSync(path.join(fixtures, module + '.html'), 'utf8')
                .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '');
            await page.setContent(html);
            await page.evaluate(() => {
                window.validationWrites = [];
                window.fetch = async (url, options = {}) => {
                    if (options.method && options.method !== 'GET') {
                        window.validationWrites.push(JSON.parse(options.body));
                    }
                    let body = [];
                    if (String(url).includes('/suppliers/options')) body = { countries: [{ code: 'IN', name: 'India' }] };
                    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
                };
            });
            await page.addScriptTag({ path: path.join(scripts, 'common/form-validation.js') });
            await page.addScriptTag({ path: path.join(scripts, 'master-data.js') });
            await page.addScriptTag({ path: path.join(scripts, script) });
            await page.locator('#add-record').click();
            await page.locator('#master-form [type="submit"]').click();
            assert.match(await page.locator('.sf-validation-summary').textContent(), /highlighted fields/);
            assert.match(await page.locator('#' + name + '-validation-error').textContent(), /required/);
            assert.equal(await page.evaluate(() => validationWrites.length), 0);
            await page.locator('#' + name).fill('Example ' + module);
            if (module !== 'Categories') {
                await page.locator('#mobileNumber').fill('123456789');
                await page.locator('#master-form [type="submit"]').click();
                assert.match(await page.locator('#mobileNumber-validation-error').textContent(), /exactly 10 digits/);
                assert.equal(await page.evaluate(() => validationWrites.length), 0);
                // Pasted/typed extra digits cannot exceed maxlength; leading zero is kept.
                await page.locator('#mobileNumber').fill('');
                await page.locator('#mobileNumber').pressSequentially('012345678901');
                assert.equal(await page.locator('#mobileNumber').inputValue(), '0123456789');
            }
            await page.locator('#master-form [type="submit"]').click();
            await page.waitForFunction(() => validationWrites.length === 1);
            assert.equal((await page.evaluate(() => validationWrites[0]))[name], 'Example ' + module);
        }
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(root, 'tests/bin/field-limit-audit.json'), JSON.stringify(limits, null, 2));
        console.log(`PASS: ${fieldChecks} browser field boundary checks across 23 pages; mandatory alerts, dynamic registrations, rejected saves, 10-digit phone input and leading zeros.`);
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exitCode = 1; });
