const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({ channel:'msedge', headless:true });
    try {
        const page = await browser.newPage({ viewport:{width:390,height:900} });
        const errors = [];
        page.on('pageerror', (error) => errors.push(error.message));
        let posts = 0;
        await page.route('**/*', async (route) => {
            const request = route.request();
            const url = new URL(request.url());
            if (request.method() === 'POST') { posts++; return route.fulfill({status:400,body:'Unexpected submission'}); }
            if (url.pathname === '/Account/Login' || url.pathname === '/Account/ForgotPassword')
                return route.fulfill({contentType:'text/html', body:fs.readFileSync(path.join(root,
                    'tests/bin/password-page-fixtures', url.pathname.endsWith('Login') ? 'login.html' :
                        url.searchParams.get('username') === 'admin' ? 'forgot-prefilled.html' : 'forgot.html'), 'utf8')});
            const file = path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            if (fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({path:file});
            return route.fulfill({status:404,body:''});
        });
        await page.goto('http://stockflow.test/Account/Login');
        await page.locator('#Input_Username').fill('admin');
        await page.locator('#Input_Password').fill('login-secret-not-to-transfer');
        await page.getByText('Forgot Password?', {exact:true}).click();
        await page.waitForURL('**/Account/ForgotPassword?username=admin');
        assert.equal(await page.locator('#Input_Username').inputValue(), 'admin');
        assert(!page.url().includes('login-secret'), 'Only the username is transferred');
        assert.equal(await page.locator('input[type=password]').count(),2);
        assert.equal(await page.locator('#Input_CurrentPassword').count(),0);
        await page.getByRole('button',{name:'Submit',exact:true}).click();
        assert.equal(posts,0);
        assert.equal(await page.locator('input.is-invalid').count(),2);
        await page.locator('#Input_Username').fill('admin');
        await page.locator('#Input_Password').fill('New secure phrase 123!');
        await page.locator('#Input_ConfirmPassword').fill('Different phrase 456!');
        await page.getByRole('button',{name:'Submit',exact:true}).click();
        assert.equal(posts,0);
        assert(await page.locator('#Input_ConfirmPassword').evaluate((input) => input.classList.contains('is-invalid')));
        await page.getByRole('button',{name:'Show new password',exact:true}).click();
        assert.equal(await page.locator('#Input_Password').getAttribute('type'),'text');
        await page.getByRole('button',{name:'Hide new password',exact:true}).click();
        assert.equal(await page.locator('#Input_Password').getAttribute('type'),'password');
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        await page.screenshot({path:path.join(root,'tests/bin/forgot-password-page.png'),fullPage:true});
        await page.getByText('Back to sign in',{exact:false}).click();
        await page.waitForURL('**/Account/Login');
        assert.deepEqual(errors,[]);
        console.log('PASS: forgot-password link opens a separate page, all required fields, confirmation mismatch, show/hide controls, back link and mobile layout.');
    } finally { await browser.close(); }
})().catch((error) => {console.error(error);process.exitCode=1;});
