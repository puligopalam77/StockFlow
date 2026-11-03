const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname,'../..');
(async () => {
    const browser = await chromium.launch({channel:'msedge',headless:true});
    try {
        const page = await browser.newPage({viewport:{width:1440,height:1000}});
        let posts = 0;
        await page.route('**/*', async (route) => {
            const url = new URL(route.request().url());
            if (route.request().method() === 'POST') { posts++; return route.fulfill({status:400,body:''}); }
            if (url.pathname === '/Account/Login') return route.fulfill({contentType:'text/html',
                body:fs.readFileSync(path.join(root,'tests/bin/password-page-fixtures',
                    url.searchParams.has('inactive') ? 'login-inactive.html' :
                    url.searchParams.has('invalid') ? 'login-invalid.html' : 'login.html'),'utf8')});
            const file = path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            if (fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({path:file});
            return route.fulfill({status:404,body:''});
        });
        await page.goto('http://stockflow.test/Account/Login');
        await page.getByRole('button',{name:'Sign In'}).click();
        assert.equal(posts,0);
        for (const [field,text] of [['Username','Username is required.'],['Password','Password is required.']]) {
            const error = page.locator('#Input_' + field + '-validation-error');
            assert.equal(await error.textContent(),text);
            assert(await error.isVisible());
            assert(await error.evaluate((node) => node.getBoundingClientRect().top >=
                document.getElementById(node.id.replace('-validation-error','')).closest('.sf-input-wrap').getBoundingClientRect().bottom));
        }
        assert.equal(await page.locator('.sf-validation-summary').count(),0);
        await page.locator('#Input_Username').fill('admin');
        assert(!(await page.locator('#Input_Username-validation-error').isVisible()));
        await page.screenshot({path:path.join(root,'tests/bin/login-required-validation.png'),fullPage:true});
        await page.goto('http://stockflow.test/Account/Login?invalid=1');
        const error = page.locator('.sf-login-errors');
        assert((await error.textContent()).includes('Invalid username or password.'));
        assert(await error.evaluate((node) => node.getBoundingClientRect().bottom <= document.querySelector('.sf-signin').getBoundingClientRect().top));
        assert.equal(await page.locator('#Input_Password').inputValue(),'');
        await page.screenshot({path:path.join(root,'tests/bin/login-invalid-validation.png'),fullPage:true});
        await page.goto('http://stockflow.test/Account/Login?inactive=1');
        assert((await page.locator('.sf-login-errors').textContent()).includes(
            'Your account is inactive. Please contact your administrator.'));
        assert(await page.locator('.sf-login-errors').evaluate((node) =>
            node.getBoundingClientRect().bottom <= document.querySelector('.sf-signin').getBoundingClientRect().top));
        assert.equal(await page.locator('#Input_Username').inputValue(),'inactive-user');
        assert.equal(await page.locator('#Input_Password').inputValue(),'');
        await page.screenshot({path:path.join(root,'tests/bin/login-inactive-validation.png'),fullPage:true});
        console.log('PASS: required errors below inputs, no duplicate summary, correction clears error, invalid credentials above Sign In, password cleared; existing screen retained.');
    } finally { await browser.close(); }
})().catch((error) => {console.error(error);process.exitCode=1;});
