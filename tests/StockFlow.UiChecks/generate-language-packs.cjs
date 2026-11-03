// Development-only tool: translate public UI wording and save offline JSON packs.
// Never send business data, customer records, passwords or configuration to a service.
// The running application uses only the saved files, with no translation service calls.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { spawn } = require('child_process');
const readline = require('readline');
const root = path.resolve(__dirname, '../..');
const helpOnly = process.argv.includes('--help-only');
const output = path.join(root, 'src/StockFlow.Web/wwwroot/languages', helpOnly ? 'help' : '');
fs.mkdirSync(output, { recursive: true });
const context = { window: {}, document: { addEventListener() {} } };
vm.runInNewContext(fs.readFileSync(path.join(root, 'src/StockFlow.Web/wwwroot/js/common/language.js'), 'utf8'), context);
vm.runInNewContext(fs.readFileSync(path.join(root, 'src/StockFlow.Web/wwwroot/js/common/screen-language-catalog.js'), 'utf8'), context);
const english = new Map();
function addCaption(value) {
    value = value.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
        .replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/\s+/g, ' ').trim();
    if (!value || value.includes('@') || !/[a-zA-Z]/.test(value) || /[^\x20-\x7e]/.test(value)) return;
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
            for (const match of source.matchAll(/(?:placeholder|aria-label)="([^"]+)"/g)) addCaption(match[1]);
            for (const match of source.matchAll(/(?:ViewData\["Title"\]|parentLabel|title)\s*=\s*"([^"]+)"/g)) addCaption(match[1]);
            for (const match of source.matchAll(/=>\s*"([^"]+)"/g)) addCaption(match[1]);
        }
    }
}
readPages(path.join(root, 'src/StockFlow.Web/Pages'));
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
const transport = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
    '-File', path.join(__dirname, 'Translate-PublicCaptions.ps1')]);
const replies = readline.createInterface({ input: transport.stdout });
let pendingReply;
replies.on('line', (line) => {
    if (!pendingReply) return;
    const { resolve, reject } = pendingReply;
    pendingReply = null;
    try {
        const reply = JSON.parse(line);
        if (reply.error) reject(new Error(reply.error)); else resolve(reply.text);
    } catch (error) { reject(error); }
});
transport.on('exit', () => {
    if (pendingReply) pendingReply.reject(new Error('Translation helper stopped.'));
});
function requestTranslation(text, language) {
    return new Promise((resolve, reject) => {
        pendingReply = { resolve, reject };
        transport.stdin.write(JSON.stringify({text, language}) + '\n');
    });
}
async function translateBatch(batch, language) {
    const text = batch.map(([, value]) => protect(value)).join('\n');
    if (text.length > 2000 && batch.length > 1) {
        const middle = Math.ceil(batch.length / 2);
        return [...await translateBatch(batch.slice(0, middle), language),
            ...await translateBatch(batch.slice(middle), language)];
    }
    let translated;
    try {
        translated = await requestTranslation(text, aliases[language] || language);
    } catch (error) {
        if (batch.length > 1 && error.message.includes('statusCode\":400')) {
            const middle = Math.ceil(batch.length / 2);
            return [...await translateBatch(batch.slice(0, middle), language),
                ...await translateBatch(batch.slice(middle), language)];
        }
        throw new Error(language + ':' + batch[0][0] + ': ' + error.message);
    }
    const lines = translated.trim().split(/\r?\n/);
    if (lines.length !== batch.length) {
        if (batch.length === 1) throw new Error('Unexpected translation line count for ' + language);
        const middle = Math.ceil(batch.length / 2);
        return [...await translateBatch(batch.slice(0, middle), language),
            ...await translateBatch(batch.slice(middle), language)];
    }
    return lines.map((line, index) => restore(line, batch[index][1]));
}
async function generate(language) {
    const file = path.join(output, language + '.json');
    const pack = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {};
    // Regenerate templates with semantic placeholders rather than numeric markers.
    if (!helpOnly) for (const key of Object.keys(templates)) delete pack['validation.' + key];
    // Retain the original Telugu/Hindi wording rather than replacing it automatically.
    for (const [key, value] of Object.entries(helpOnly ? {} : context.window.StockFlowLanguage.defaults(language)))
        pack[key.toLowerCase()] = value;
    const missing = entries.filter(([key]) => !pack[key]);
    for (let start = 0; start < missing.length; start += 20) {
        const batch = missing.slice(start, start + 20);
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
    }
    console.log(language + ': ' + Object.keys(pack).length + ' captions/messages');
}
(async () => {
    console.log(entries.length + ' public captions/messages per language');
    const requested = process.argv.find((value) => value.startsWith('--languages='))?.slice(12).split(',');
    const languages = context.window.StockFlowLanguages.languages.map(([code]) => code)
        .filter((code) => code !== 'en' && (!requested || requested.includes(code)));
    // Sequential requests keep generation modest and allow each pack to resume.
    for (const language of languages) await generate(language);
    fs.writeFileSync(path.join(output, 'en.json'), JSON.stringify(Object.fromEntries(entries), null, 2) + '\n');
})().catch((error) => { console.error(error.message); process.exitCode = 1; })
    .finally(() => transport.stdin.end());
