const fs = require('fs'), path = require('path'), assert = require('assert');
const {chromium} = require('./bin/Debug/net10.0/.playwright/package');
const root = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({channel:'msedge',headless:true,args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
    try {
        const page = await browser.newPage({viewport:{width:390,height:844}});
        if(process.argv.includes('--mp4')) await page.addInitScript(()=>{
            MediaRecorder.isTypeSupported=type=>type==='audio/mp4';
            window.webkitAudioContext=window.AudioContext;window.AudioContext=undefined;
            window.webkitOfflineAudioContext=window.OfflineAudioContext;window.OfflineAudioContext=undefined;
        });
        const errors = [], recordings = []; let fail = false, questions = 0, slow = false;
        page.on('pageerror',error=>errors.push(error.message));
        await page.route('**/*',async route=>{
            const url = new URL(route.request().url());
            if(url.pathname==='/Purchases/Create') return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(root,'tests/bin/help-panel-fixtures/purchase.html'),'utf8')});
            if(url.pathname==='/api/help/voice') {
                assert(route.request().headers()['x-csrf-token']);
                const body=route.request().postDataBuffer(), start=body.indexOf(Buffer.from('RIFF'));
                assert(start>0,'Recorder uploads a WAV, not browser-dependent WebM');
                const size=body.readUInt32LE(start+4)+8;const wave=body.subarray(start,start+size);
                assert.equal(wave.readUInt32LE(24),16000);assert.equal(wave.readUInt16LE(22),1);
                recordings.push(wave);
                if(slow) await new Promise(resolve=>setTimeout(resolve,400));
                return route.fulfill({status:fail?503:200,contentType:'application/json',body:JSON.stringify(fail?{message:'Speech temporarily unavailable'}:{text:'what are sales today'})}).catch(()=>{});
            }
            if(url.pathname==='/api/help/questions') questions++;
            if(url.pathname.startsWith('/api/')) return route.fulfill({contentType:'application/json',body:'[]'});
            const file=path.join(root,'src/StockFlow.Web/wwwroot',url.pathname.replace(/\.[a-z0-9]{10}(?=\.(js|css|png)$)/,''));
            return fs.existsSync(file)&&fs.statSync(file).isFile()?route.fulfill({path:file}):route.fulfill({status:404,body:''});
        });
        await page.goto('http://localhost:5220/Purchases/Create');
        await page.locator('#help-open').click();
        const record=async()=>{await page.locator('#help-voice-start').click();await page.waitForFunction(()=>document.querySelector('#help-voice-status').textContent.includes('Recording 00:'));await page.waitForTimeout(650);};
        await record();
        assert(await page.locator('#help-ai-submit').isDisabled());
        assert.equal(await page.locator('#help-voice-start').getAttribute('aria-pressed'),'true');
        await page.locator('#help-voice-stop').click();
        await page.waitForFunction(()=>document.querySelector('#help-ai-question').value==='what are sales today');
        assert.equal(questions,0,'Transcription never sends a question automatically');
        assert(!(await page.locator('#help-ai-submit').isDisabled()));
        await record();await page.locator('#help-voice-cancel').click();
        assert.equal(await page.locator('#help-ai-question').inputValue(),'what are sales today');
        assert.equal(recordings.length,1,'Cancel does not upload audio');
        fail=true; await record();await page.locator('#help-voice-start').click();
        await page.waitForSelector('.sf-voice-retry:not([hidden])');
        fail=false;await page.locator('.sf-voice-retry').click();
        await page.waitForFunction(()=>document.querySelector('#help-ai-question').value==='what are sales today what are sales today');
        assert(recordings[1].equals(recordings[2]),'Retry uses the exact retained recording');
        await page.evaluate(()=>{window.savedMic=navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);navigator.mediaDevices.getUserMedia=()=>Promise.reject(new DOMException('Denied','NotAllowedError'));});
        await page.locator('#help-voice-start').click();
        await page.waitForFunction(()=>document.querySelector('#help-voice-status').textContent.includes('denied'));
        assert(!(await page.locator('#help-ai-submit').isDisabled()));
        await page.evaluate(()=>navigator.mediaDevices.getUserMedia=window.savedMic);
        slow=true; await record();await page.locator('#help-voice-stop').click();
        await page.waitForFunction(()=>document.querySelector('#help-voice-status').textContent.includes('Transcribing'));
        const draft=await page.locator('#help-ai-question').inputValue();await page.locator('#help-close').click();
        await page.waitForTimeout(500);assert.equal(await page.locator('#help-ai-question').inputValue(),draft,'Closed assistant discards late transcription');
        await page.locator('#help-open').click();assert(!(await page.locator('#help-voice-start').isDisabled()));
        for(const width of [320,390,768,1024,1440]) {
            await page.setViewportSize({width,height:844});
            assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            assert(await page.locator('#help-panel').evaluate(el=>el.scrollWidth<=el.clientWidth+1));
            assert(await page.locator('#help-voice-start').isVisible());
        }
        await page.evaluate(()=>Object.defineProperty(window,'isSecureContext',{configurable:true,value:false}));
        await page.locator('#help-voice-start').click();
        assert((await page.locator('#help-voice-status').textContent()).includes('HTTPS'));
        assert.deepEqual(errors,[]);
        console.log('PASS: '+(process.argv.includes('--mp4')?'MP4 + WebKit API aliases':'WebM')+' recording/WAV conversion, timer, stop, cancel, review, permission denial, exact retry, close cancellation, HTTPS requirement and desktop/tablet/mobile layouts.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
