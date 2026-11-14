// Development-only tool: translate public UI wording and save offline JSON packs.
// Never send business data, customer records, passwords or configuration to a service.
// The running application uses only the saved files, with no translation service calls.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const root = path.resolve(__dirname, '../..');
const helpOnly = process.argv.includes('--help-only');
const useBrowserTranslator = process.argv.includes('--provider=bing');
const useGoogleBrowser = process.argv.includes('--provider=google-browser');
const output = path.join(root, 'src/StockFlow.Web/wwwroot/languages', helpOnly ? 'help' : '');
fs.mkdirSync(output, { recursive: true });
const context = { window: {}, document: { addEventListener() {} } };
vm.runInNewContext(fs.readFileSync(path.join(root, 'src/StockFlow.Web/wwwroot/js/common/language.js'), 'utf8'), context);
vm.runInNewContext(fs.readFileSync(path.join(root, 'src/StockFlow.Web/wwwroot/js/common/screen-language-catalog.js'), 'utf8'), context);
const english = new Map();
function addCaption(value) {
    value = value.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
        .replace(/&#(?:x([\da-f]+)|(\d+));/gi, (_, hex, decimal) => String.fromCodePoint(parseInt(hex || decimal, hex ? 16 : 10)))
        .replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/\s+/g, ' ').trim();
    // These display aliases reuse existing localized readiness/expand/collapse
    // terminology; they do not require a separate machine translation.
    if(['ai ready','expand stockbot','compact view','how many low stock products?'].includes(value.toLowerCase()))return;
    if (!value || /[@{}]|=>/.test(value) || !/[a-zA-Z]/.test(value) || /[^\x20-\x7e]/.test(value)) return;
    if (value.length > 800) return;
    value = value.replace(/(?:\s*\*|:)$/, '');
    english.set(value.toLowerCase(), value);
}
for (const key of Object.keys(context.window.StockFlowLanguage.defaults('hi')))
    if (!key.startsWith('validation.')) addCaption(key);
function readPages(folder) {
    for (const file of fs.readdirSync(folder, { withFileTypes: true })) {
        const fullPath = path.join(folder, file.name);
        if (file.isDirectory()) readPages(fullPath);
        else if (file.name.endsWith('.cshtml')) {
            const source = fs.readFileSync(fullPath, 'utf8').replace(/<script[\s\S]*?<\/script>/gi, '');
            for (const match of source.matchAll(/>([^<>\r\n]+)</g)) addCaption(match[1]);
            for (const match of source.matchAll(/(?:placeholder|aria-label|title)="([^"]+)"/g)) addCaption(match[1]);
            for (const match of source.matchAll(/(?:ViewData\["Title"\]|parentLabel|title)\s*=\s*"([^"]+)"/g)) addCaption(match[1]);
            for (const match of source.matchAll(/=>\s*"([^"]+)"/g)) addCaption(match[1]);
        }
    }
}
readPages(path.join(root, 'src/StockFlow.Web/Pages'));
// Include public captions created after page load. Restrict this to literal
// English phrases, never interpolation, identifiers, queries or business data.
function readDynamicCaptions(folder, extension) {
    for (const file of fs.readdirSync(folder, {withFileTypes:true})) {
        const full = path.join(folder, file.name);
        if(file.isDirectory()) readDynamicCaptions(full, extension);
        else if(file.name.endsWith(extension)) {
            const source=fs.readFileSync(full,'utf8');
            for(const match of source.matchAll(/'((?:[^'\\\r\n]|\\')*)'|"((?:[^"\\\r\n]|\\")*)"/g)) {
                const value=(match[1]??match[2]).replace(/\\(['"])/g,'$1');
                if (/^[A-Z][A-Za-z0-9 .,!?;:'"()&%+*\/-]+$/.test(value) && /[a-z] [a-zA-Z]|[a-z][A-Z]|^[A-Z][a-z]+$/.test(value)
                    && !/\b(?:SELECT|FROM|INSERT|UPDATE|DELETE|System|Microsoft)\b|https?:|\.js\b|\.css\b/.test(value)) addCaption(value);
            }
        }
    }
}
readDynamicCaptions(path.join(root,'src/StockFlow.Web/wwwroot/js'),'.js');
for(const file of ['StockBotGuidance.cs','StockBotErrorCodes.cs','RetailScreenGuides.cs','RetailAssistantService.cs']) {
    // The Help folder contains reviewed, constant interface instructions.
    const source=fs.readFileSync(path.join(root,'src/StockFlow.Web/Help',file),'utf8');
    for(const match of source.matchAll(/"([^"\r\n]+)"/g))
        if(/^[A-Z][A-Za-z0-9 .,!?;:'()&%+*\/-]+$/.test(match[1]))addCaption(match[1]);
}
for (const value of ['Language saved. Changes apply automatically across the app.',
    'Roles & permissions', 'Stores', 'Company settings', 'Settings', 'My account',
    'Choose one language for the whole app. Captions show English followed by the translation in parentheses.',
    'All listed languages include interface captions and validation messages. Text without a translation stays in English.']) addCaption(value);
const templates = {
    required: '{label} is required.',
    phone: '{label}: enter exactly 10 digits, without spaces or a country code.',
    maximum: '{label}: enter no more than {limit} characters.',
    minimum: '{label}: enter at least {limit} characters.',
    email: '{label}: enter a valid email address.',
    lower: '{label}: minimum value is {limit}.',
    upper: '{label}: maximum value is {limit}.',
    step: '{label}: enter a value in steps of {limit}.',
    format: '{label}: enter a valid value.'
};
for (const [key, value] of Object.entries(templates)) english.set('validation.' + key, value);
if (helpOnly) {
    english.clear();
    function helpText(value) {
        value = value.replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
            .replaceAll('**\\***', '*').replaceAll('**', '').replaceAll('`', '')
            .replace(/\s+/g, ' ').trim();
        if (value && /[a-zA-Z]/.test(value)) english.set(value.toLowerCase(), value);
    }
    for (const name of ['products', 'receiving-stock', 'billing', 'returns', 'warranty', 'common-problems']) {
        const guide = fs.readFileSync(path.join(root, 'docs/help', name + '.md'), 'utf8');
        for (let line of guide.split(/\r?\n/)) {
            line = line.trim();
            if (line.startsWith('|')) {
                for (const cell of line.split('|').slice(1, -1))
                    if (!/^\s*:?-+:?\s*$/.test(cell)) helpText(cell);
            } else helpText(line.replace(/^#+\s+|^\d+\.\s+/, ''));
        }
    }
    const catalog = fs.readFileSync(path.join(root, 'src/StockFlow.Web/Help/HelpCatalog.cs'), 'utf8');
    for (const match of catalog.matchAll(/"([^"\r\n]+)"/g))
        if (!match[1].startsWith('/') && !match[1].includes('{') && match[1].includes(' ')) helpText(match[1]);
    for (const value of ['Need help?', 'StockFlow help', 'Close', 'Close help', 'Search help articles',
        'Try barcode, payment or warranty', 'Back to articles', 'Questions for this screen', 'Common shop questions',
        '{count} help articles', 'No matching articles. Try another word or ask your administrator.',
        'Available screens depend on your permissions. Ask your administrator if you need access.']) helpText(value);
}
const entries = [...english.entries()];
if(process.argv.includes('--audit')) {
    let missing=0;
    for(const [code] of context.window.StockFlowLanguages.languages.filter(([code])=>code!=='en')){
        const pack=JSON.parse(fs.readFileSync(path.join(output,code+'.json'),'utf8'));
        const keys=entries.filter(([key])=>!pack[key]);missing+=keys.length;
        console.log(code+': '+keys.length+' missing of '+entries.length);
        if (process.argv.includes('--details')) {
            for (const [key] of keys) console.log('  ' + key);
        }
    }
    process.exitCode=missing?1:0;
    return;
}
fs.writeFileSync(path.join(output, 'en.json'), JSON.stringify(Object.fromEntries(entries), null, 2) + '\n');
const aliases = { kok: 'gom', mni: 'mni-Mtei' };
function protect(value) {
    return value.replaceAll('{label}', '{FIELD_NAME}').replaceAll('{limit}', '{MAXIMUM_VALUE}')
        .replaceAll('{id}', '{RECORD_ID}').replaceAll('{count}', '{ARTICLE_COUNT}');
}
function restore(value, original) {
    // Preserve identifiers even if a provider omits the closing brace.
    value = value.replace(/\{RECORD_ID\b\}?/g, '{id}')
        .replace(/\{ARTICLE_COUNT\b\}?/g, '{count}');
    // A provider sometimes folds the word "steps" into this brace-delimited token.
    value = value.replace(/\{MAXIMUM_VALUE\s+steps\b/g, '{limit}');
    // Some providers localize digits; restore the protected tokens in every script.
    const zeroes = [0x660, 0x6f0, 0x966, 0x9e6, 0xa66, 0xae6, 0xb66, 0xbe6,
        0xc66, 0xce6, 0xd66, 0x1c50, 0xabf0, 0xff10];
    value = value.replace(/\p{Decimal_Number}/gu, (digit) => {
        const point = digit.codePointAt(0);
        const zero = zeroes.find((start) => point >= start && point <= start + 9);
        return zero === undefined ? digit : String(point - zero);
    });
    const expectedTokens = original.match(/\{\w+\}/g) || [];
    const receivedTokens = value.match(/\{[^{}]+\}/g) || [];
    if (expectedTokens.length === receivedTokens.length) {
        let index = 0;
        value = value.replace(/\{[^{}]+\}/g, () => expectedTokens[index++]);
    }
    return value.replaceAll('{FIELD_NAME}', '{label}').replaceAll('{MAXIMUM_VALUE}', '{limit}');
}
let bingPagePromise, bingBrowser, googleBrowserPromise, activeRequests=0;
const waitingRequests=[];
async function requestTranslation(text, language) {
    if(activeRequests>=4)await new Promise(resolve=>waitingRequests.push(resolve));
    activeRequests++;
    try {
        for(let attempt=0;;attempt++){
            try{return await requestTranslationCore(text,language);}
            catch(error){if(attempt===2 || /statusCode\\?":400|Translation HTTP 400/.test(error.message))throw error;await new Promise(resolve=>setTimeout(resolve,1000*(attempt+1)));}
        }
    }finally{activeRequests--;waitingRequests.shift()?.();}
}
async function requestTranslationCore(text, language) {
    if(useGoogleBrowser){
        if(!googleBrowserPromise){const {chromium}=require('./bin/Debug/net10.0/.playwright/package');googleBrowserPromise=chromium.launch({channel:'msedge',headless:true});}
        const page=await (await googleBrowserPromise).newPage();
        try{
            await page.goto('https://translate.google.com/?'+new URLSearchParams({sl:'en',tl:language,text,op:'translate'}),{waitUntil:'domcontentloaded'});
            await page.locator('.ryNqvb').first().waitFor({timeout:30000});
            return await page.locator('.ryNqvb').first().evaluate(e=>e.closest('.HwtZe').innerText);
        }finally{await page.close();}
    }
    if(useBrowserTranslator || ['brx','ks'].includes(language)) {
        if(!bingPagePromise)bingPagePromise=(async()=>{
            const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
            bingBrowser=await chromium.launch({channel:'msedge',headless:true});
            const page=await bingBrowser.newPage();await page.goto('https://www.bing.com/translator',{waitUntil:'domcontentloaded'});return page;
        })();
        return (await bingPagePromise).evaluate(async({text,language})=>{
            const html=document.documentElement.innerHTML;
            const helper=JSON.parse(html.match(/params_AbusePreventionHelper\s*=\s*(\[[^;]+\])/)[1]);
            const ig=html.match(/IG:"([^"]+)"/)[1],iid=html.match(/data-iid="([^"]+)"/)[1];
            const response=await fetch('/ttranslatev3?isVertical=1&IG='+ig+'&IID='+iid+'.'+Date.now(),{method:'POST',signal:AbortSignal.timeout(30000),body:new URLSearchParams({fromLang:'en',to:language,text,key:helper[0],token:helper[1]})});
            if(!response.ok)throw new Error('Translation HTTP '+response.status);
            const data=await response.json();if(!data[0]?.translations?.[0]?.text)throw new Error('Translator returned no text: '+JSON.stringify(data));
            return data[0].translations[0].text.replaceAll('\\n','\n');
        },{text,language});
    }
    const response=await fetch('https://translate.googleapis.com/translate_a/single?'+new URLSearchParams({client:'gtx',sl:'en',dt:'t',tl:language,q:text}),{signal:AbortSignal.timeout(30000)});
    if(!response.ok)throw new Error('Translation HTTP '+response.status);
    const data=await response.json();return data[0].map(part=>part[0]).join('');
}
async function translateBatch(batch, language) {
    const text = batch.map(([, value]) => protect(value)).join('\n');
    if (text.length > (useGoogleBrowser?4500:useBrowserTranslator || ['brx','ks'].includes(language)?900:2000) && batch.length > 1) {
        const middle = Math.ceil(batch.length / 2);
        return (await Promise.all([translateBatch(batch.slice(0,middle),language),translateBatch(batch.slice(middle),language)])).flat();
    }
    let translated;
    try {
        translated = await requestTranslation(text, aliases[language] || language);
    } catch (error) {
        if (batch.length > 1 && /statusCode\\?":400|Translation HTTP 400/.test(error.message)) {
            const middle = Math.ceil(batch.length / 2);
            return [...await translateBatch(batch.slice(0, middle), language),
                ...await translateBatch(batch.slice(middle), language)];
        }
        throw new Error(language + ':' + batch[0][0] + ': ' + error.message);
    }
    const lines = translated.trim().split(/\r?\n/);
    if (lines.length !== batch.length || lines.some(line=>!line.trim())) {
        if (batch.length === 1) return [restore(translated.replace(/\s+/g,' ').trim(),batch[0][1])];
        const middle = Math.ceil(batch.length / 2);
        return (await Promise.all([translateBatch(batch.slice(0,middle),language),translateBatch(batch.slice(middle),language)])).flat();
    }
    return lines.map((line, index) => restore(line, batch[index][1]));
}
async function generate(language) {
    const file = path.join(output, language + '.json');
    const pack = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {};
    // Regenerate templates with semantic placeholders rather than numeric markers.
    // Existing validated templates and translations remain stable across updates.
    // Retain the original Telugu/Hindi wording rather than replacing it automatically.
    for (const [key, value] of Object.entries(helpOnly ? {} : context.window.StockFlowLanguage.defaults(language)))
        pack[key.toLowerCase()] = value;
    const missing = entries.filter(([key]) => !pack[key]);
    const batchSize=useBrowserTranslator || useGoogleBrowser?100:40;
    for (let start = 0; start < missing.length; start += batchSize) {
        const batch = missing.slice(start, start + batchSize);
        const translated = await translateBatch(batch, language);
        for (let index = 0; index < batch.length; index++) {
            const [key, original] = batch[index];
            const value = translated[index].trim();
            for (const token of original.match(/\{\w+\}/g) || [])
                if (!value.includes(token)) throw new Error('Missing placeholder ' + token + ' in ' + language + ':' + key);
            if (!value) throw new Error('Empty translation: ' + language + ':' + key);
            pack[key] = value;
        }
        fs.writeFileSync(file, JSON.stringify(pack, null, 2) + '\n');
        if(start % 400 === 0)console.log(language+': '+Math.min(start+batchSize,missing.length)+' / '+missing.length+' added');
    }
    console.log(language + ': ' + Object.keys(pack).length + ' captions/messages');
}
(async () => {
    console.log(entries.length + ' public captions/messages per language');
    const requested = process.argv.find((value) => value.startsWith('--languages='))?.slice(12).split(',');
    const languages = context.window.StockFlowLanguages.languages.map(([code]) => code)
        .filter((code) => code !== 'en' && (!requested || requested.includes(code)));
    // Small, bounded concurrency; each pack can resume from its last saved batch.
    const queue=[...languages];
    const failures=[];
    await Promise.all(Array.from({length:3},async()=>{
        while(queue.length){const code=queue.shift();try{await generate(code);}catch(error){failures.push(code);console.error(error.message);}}
    }));
    fs.writeFileSync(path.join(output, 'en.json'), JSON.stringify(Object.fromEntries(entries), null, 2) + '\n');
    if(failures.length)throw new Error('Incomplete packs: '+failures.join(', ')+'. Re-run to resume missing entries.');
})().catch((error) => { console.error(error.message); process.exitCode = 1; })
    .finally(async()=>{await bingBrowser?.close();if(googleBrowserPromise)await (await googleBrowserPromise).close();});
