const assert = require('assert');
const { chromium } = require('./bin/Debug/net10.0/.playwright/package');
(async () => {
 const browser = await chromium.launch({channel:'msedge',headless:true});
 try {
  const page = await browser.newPage();
  const errors=[]; page.on('pageerror',error=>errors.push(error.message));
  await page.goto((process.argv[2] || 'http://localhost:5220') + '/swagger/index.html');
  await page.waitForTimeout(1500);
  assert.deepEqual(errors,[], 'Swagger browser errors: '+errors.join('; '));
  await page.locator('.opblock').first().waitFor({timeout:15000});
  console.log('PASS: Swagger renders '+await page.locator('.opblock').count()+' API operations without browser errors.');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
