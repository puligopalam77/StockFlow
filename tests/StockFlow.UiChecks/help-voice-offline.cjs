const fs = require('fs');
const path = require('path');
const assert = require('assert');
const {chromium} = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({channel:'msedge', headless:true});
    try {
        const page = await browser.newPage({viewport:{width:390,height:844}});
        const errors = [];
        let requests = [];
        page.on('pageerror', error=>errors.push(error.message));
        await page.addInitScript(() => {
            // This suite covers the browser-speech fallback. Recorder flow has its own suite.
            window.MediaRecorder = undefined;
            Object.defineProperty(window,'isSecureContext',{value:true});
            window.localRecognition = true;
            window.voiceStarts = 0;
            class Recognition {
                constructor() { this.processLocally=false; window.recognition=this; }
                static available() {return Promise.resolve(window.localRecognition?'available':'unavailable');}
                start() {window.voiceStarts++;}
                stop() {this.onend?.();}
                abort() {this.onend?.();}
                emit(text,final=true) {const result=[{transcript:text}];result.isFinal=final;this.onresult?.({results:[result]});}
            }
            window.SpeechRecognition=Recognition;
            window.spoken=[];
            window.SpeechSynthesisUtterance=class {constructor(text){this.text=text;}};
            Object.defineProperty(window,'speechSynthesis',{value:{cancel(){},getVoices(){return [{lang:'en-IN',localService:true},{lang:'hi-IN',localService:true}];},speak(value){window.spoken.push({text:value.text,lang:value.lang});}}});
        });
        await page.route('**/*',route=> {
            const url=new URL(route.request().url());
            if(url.pathname==='/api/help/questions') {
                requests.push(route.request().postDataJSON().question);
                assert(route.request().headers()['x-csrf-token']);
                return route.fulfill({contentType:'application/json',body:JSON.stringify({status:'answered',message:'7',sections:[],links:[]})});
            }
            if(url.pathname==='/Purchases/Create') return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/help-panel-fixtures/purchase.html'),'utf8')});
            if(url.pathname.startsWith('/api/'))return route.fulfill({contentType:'application/json',body:'[]'});
            const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            return fs.existsSync(file) && fs.statSync(file).isFile() ? route.fulfill({path:file}) : route.fulfill({status:404,body:''});
        });
        await page.goto('http://stockflow.test/Purchases/Create');
        await page.locator('#help-open').click();
        await page.locator('#help-voice-auto').check();
        await page.locator('#help-voice-start').click();
        await page.waitForFunction(()=>window.voiceStarts===1);
        assert(await page.evaluate(()=>recognition.processLocally));
        await page.evaluate(()=>recognition.emit('How many units sold today?',false));
        assert.equal(requests.length,0,'Interim speech is never submitted');
        await page.evaluate(()=>{recognition.emit('How many units sold today?');recognition.stop();});
        assert.equal(requests.length,0,'Final speech waits for staff review');
        assert.equal(await page.locator('#help-ai-question').inputValue(),'How many units sold today?');
        assert((await page.locator('#help-voice-status').textContent()).includes('Review'));
        await page.locator('#help-ai-question').fill('How many units sold yesterday?');
        await page.locator('#help-ai-submit').click();
        await page.waitForFunction(()=>document.getElementById('help-ai-answer').textContent==='7' && window.spoken.length===1);
        assert.deepEqual(requests,['How many units sold yesterday?']);
        assert.equal(await page.locator('#help-ai-question').inputValue(),'');
        await page.locator('#help-voice-start').click();
        await page.waitForFunction(()=>window.voiceStarts===2);
        await page.evaluate(()=>{recognition.onerror({error:'not-allowed'});recognition.stop();});
        assert((await page.locator('#help-voice-status').textContent()).includes('denied'));
        assert.equal(requests.length,1,'Denied microphone cannot send a question');
        await page.evaluate(()=>window.localRecognition=false);
        await page.locator('#help-voice-start').click();
        await page.locator('#help-voice-consent').waitFor({state:'visible'});
        assert.equal(await page.evaluate(()=>voiceStarts),2,'No online recognition before explicit opt-in');
        await page.locator('#help-voice-allow').click();
        await page.waitForFunction(()=>window.voiceStarts===3);
        assert.equal(await page.evaluate(()=>recognition.processLocally),false);
        await page.locator('#help-close').click();
        await page.evaluate(()=>{recognition.emit('This late speech must not submit');recognition.stop();});
        assert.equal(requests.length,1,'Closing the panel cancels pending speech');
        await page.locator('#help-open').click();
        await page.evaluate(()=>document.dispatchEvent(new CustomEvent('stockflow:language-ready',{detail:{language:'hi'}})));
        await page.locator('#help-voice-start').click();
        await page.waitForFunction(()=>window.voiceStarts===4);
        assert.equal(await page.evaluate(()=>recognition.lang),'hi-IN');
        await page.locator('#help-voice-stop').click();
        await page.locator('#help-voice-read').click();
        assert.equal(await page.evaluate(()=>spoken.at(-1).lang),'hi-IN');
        const spokenBefore=await page.evaluate(()=>spoken.length);
        await page.evaluate(()=>{
            window.voiceEvents=new EventTarget();window.installedVoices=speechSynthesis.getVoices();
            speechSynthesis.getVoices=()=>[];
            speechSynthesis.addEventListener=voiceEvents.addEventListener.bind(voiceEvents);
            speechSynthesis.removeEventListener=voiceEvents.removeEventListener.bind(voiceEvents);
        });
        await page.locator('#help-voice-read').click();
        assert((await page.locator('#help-voice-status').textContent()).includes('Loading'));
        await page.evaluate(()=>{speechSynthesis.getVoices=()=>installedVoices;voiceEvents.dispatchEvent(new Event('voiceschanged'));});
        await page.waitForFunction(before=>spoken.length===before+1,spokenBefore);
        for(const width of [320,390,768,1366]) {
            await page.setViewportSize({width,height:844});
            assert(await page.locator('#help-voice-start').isVisible());
            assert(await page.locator('#help-ai-submit').isVisible());
            assert(await page.locator('#help-panel').evaluate(el=>el.scrollWidth<=el.clientWidth+1),'Voice controls fit on screen');
        }
        assert.deepEqual(errors,[]);
        console.log('PASS: Voice transcription, authenticated submission, local/online consent, permission errors, cancellation, language, local read-aloud and responsive controls (mock browser speech).');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
