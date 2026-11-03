const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'tests/bin/login-responsive');
fs.mkdirSync(output, { recursive: true });
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        const failures = [];
        let posts = 0;
        page.on('pageerror', error => failures.push(error.message));
        await page.route('**/*', async route => {
            const url = new URL(route.request().url());
            if (route.request().method() === 'POST') {
                posts++;
                return route.fulfill({ contentType: 'text/html', body: '<p>Submitted</p>' });
            }
            if (url.pathname === '/Account/Login') {
                const file = url.searchParams.has('invalid') ? 'login-invalid.html' : 'login.html';
                return route.fulfill({ contentType: 'text/html', path: path.join(root, 'tests/bin/password-page-fixtures', file) });
            }
            if (url.pathname === '/Account/ForgotPassword') return route.fulfill({ contentType: 'text/html', body: '<h1>Forgot password</h1>' });
            const file = path.join(root, 'src/StockFlow.Web/wwwroot', url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png|svg)$)/, ''));
            return fs.existsSync(file) && fs.statSync(file).isFile() ? route.fulfill({ path: file }) : route.fulfill({ status: 404, body: '' });
        });
        async function reachable(selector) {
            const locator = page.locator(selector);
            await locator.scrollIntoViewIfNeeded();
            assert(await locator.isVisible(), selector + ' is visible');
            assert(await locator.evaluate(node => {
                const r = node.getBoundingClientRect();
                return r.left >= -1 && r.right <= innerWidth + 1 && r.top >= -1 && r.bottom <= innerHeight + 1 &&
                    node.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2));
            }), selector + ' is fully reachable without clipping');
        }
        // CSS viewport sizes include browser/taskbar deductions and scaled/zoomed equivalents.
        for (const [width, height, label] of [
            [1920,1080,'desktop'], [1366,768,'laptop'], [1280,720,'720p'], [1024,768,'small-laptop'],
            [1536,792,'1080p-scale125'], [1280,600,'1080p-scale150'],
            [1093,546,'laptop-zoom125'], [911,455,'laptop-zoom150'], [1067,465,'hp-short-window'],
            [800,500,'windowed'], [768,1024,'tablet'], [390,844,'mobile'], [320,568,'small-mobile'], [390,360,'keyboard-height']
        ]) {
            await page.setViewportSize({ width, height });
            await page.goto('http://stockflow.test/Account/Login');
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), label + ': no horizontal scroll');
            for (const selector of ['#Input_Username','#Input_Password','.sf-remember','#forgot-password-link','.sf-signin','.sf-login-footer','.sf-handwritten']) await reachable(selector);
            for (let i = 1; i <= 3; i++) await reachable('.sf-feature:nth-child(' + i + ')');
            assert(await page.evaluate(() => {
                const form = document.querySelector('.sf-login-form').getBoundingClientRect(),
                    panel = document.querySelector('.sf-login-panel').getBoundingClientRect(),
                    cards = document.querySelector('.sf-feature-grid').getBoundingClientRect(),
                    visual = document.querySelector('.sf-login-visual').getBoundingClientRect(),
                    title = document.querySelector('.sf-handwritten').getBoundingClientRect();
                return form.bottom <= panel.bottom && cards.bottom <= visual.bottom && title.bottom < cards.top;
            }), label + ': content remains inside panels without overlap');
            if (label === 'desktop') assert(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), 'Tall desktop fits without document scrolling');
            await page.evaluate(() => scrollTo({ top: 0, behavior: 'instant' }));
            if (['desktop','laptop','1080p-scale150','hp-short-window','mobile'].includes(label)) await page.screenshot({ path: path.join(output, label + '.png'), fullPage: true });
            console.log('PASS: login controls and all hero cards reachable at ' + width + 'x' + height + ' (' + label + ')');
        }
        await page.setViewportSize({width:1067,height:465});
        await page.goto('http://stockflow.test/Account/Login');
        await page.locator('.sf-signin').click();
        assert.equal(posts, 0, 'Invalid fields do not submit');
        for (const field of ['Username','Password']) await reachable('#Input_' + field + '-validation-error');
        await reachable('.sf-signin');
        await page.screenshot({path:path.join(output,'short-required-errors.png'),fullPage:true});
        await page.goto('http://stockflow.test/Account/Login?invalid=1');
        await reachable('.sf-login-errors');await reachable('.sf-signin');
        assert(await page.locator('.sf-login-errors').evaluate(node => node.getBoundingClientRect().bottom <= document.querySelector('.sf-signin').getBoundingClientRect().top), 'Server error does not overlap Sign In');
        const usernameAutocomplete = await page.locator('#Input_Username').getAttribute('autocomplete');
        await page.locator('#Input_Username').fill('admin');
        await page.locator('#Input_Password').fill('Example password 123!');
        await page.locator('#password-toggle').click();assert.equal(await page.locator('#Input_Password').getAttribute('type'),'text');
        await page.locator('#password-toggle').click();assert.equal(await page.locator('#Input_Password').getAttribute('type'),'password');
        assert.equal(await page.locator('#Input_Username').getAttribute('autocomplete'),usernameAutocomplete,'Existing autofill policy is unchanged');
        assert.equal(await page.locator('#Input_Password').getAttribute('autocomplete'),'current-password');
        await page.locator('#Input_Password').press('Enter');await page.waitForSelector('text=Submitted');assert.equal(posts,1,'Enter submits once');
        await page.goto('http://stockflow.test/Account/Login');
        await page.locator('#Input_Username').focus();
        for(let i=0;i<5;i++) await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(()=>document.activeElement.className),'sf-signin','Keyboard reaches Sign In');
        await page.locator('#forgot-password-link').click();await page.waitForURL('**/Account/ForgotPassword**');
        assert.deepEqual(failures,[],'No browser script errors');
        console.log('PASS: required/server errors, password visibility, autofill attributes, Enter submission, keyboard focus and Forgot Password navigation.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
