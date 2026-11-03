const fs=require('fs'),path=require('path'),assert=require('assert');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true,args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',route=>{const url=new URL(route.request().url());if(url.pathname==='/')return route.fulfill({contentType:'text/html',body:'<input id="scan" maxlength="200"><script src="/js/common/camera-scanner.js"></script><script src="/js/common/scanner.js"></script><link rel="stylesheet" href="/css/camera-scanner.css">'});const file=path.resolve(assets,'.'+url.pathname);return file.startsWith(assets+path.sep)&&fs.existsSync(file)?route.fulfill({path:file}):route.fulfill({status:404,body:''});});
  await page.goto('https://stockflow.test/');
  await page.evaluate(()=>{window.scans=[];StockFlowScanner.bind('scan',code=>window.scans.push(code));});
  await page.addScriptTag({path:path.join(assets,'lib/zxing/zxing-browser.min.js')});
  await page.addScriptTag({path:path.join(assets,'js/common/barcode-labels.js')});
  const decoded=await page.evaluate(async()=>{
   const svg=StockFlowBarcode.svg('SFV00000003');svg.setAttribute('width','700');svg.setAttribute('height','200');
   const image=new Image();image.src='data:image/svg+xml;base64,'+btoa(new XMLSerializer().serializeToString(svg));await image.decode();
   const canvas=document.createElement('canvas');canvas.width=760;canvas.height=240;const ctx=canvas.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,760,240);ctx.drawImage(image,30,20,700,200);
   const reader=new ZXingBrowser.BrowserMultiFormatReader();return reader.decodeFromCanvas(canvas).getText();
  });assert.equal(decoded,'SFV00000003','real bundled decoder reads StockFlow Code128 labels');
  // Drive deterministic camera frames separately from the actual image decoder.
  await page.evaluate(()=>{window.stops=0;window.ZXingBrowser={BrowserMultiFormatReader:class{async decodeFromConstraints(constraints,video,callback){const controls={stop(){window.stops++;}};window.detect=code=>callback({getText:()=>code},null,controls);return controls;}}};});
  await page.locator('.sf-camera-trigger').click();await page.waitForFunction(()=>!!window.detect);
  await page.evaluate(()=>window.detect('BAT-42'));assert.deepEqual(await page.evaluate(()=>window.scans),[],'camera result is reviewed first');
  await page.getByRole('button',{name:'Use code'}).click();await page.waitForFunction(()=>window.scans.length===1);assert.deepEqual(await page.evaluate(()=>window.scans),['BAT-42']);
  await page.locator('.sf-camera-trigger').click();await page.getByRole('button',{name:'Cancel',exact:true}).click();assert.equal(await page.locator('.sf-camera-dialog').count(),0);
  for(const width of [320,390,768]){await page.setViewportSize({width,height:600});await page.locator('.sf-camera-trigger').click();assert(await page.locator('.sf-camera-dialog').evaluate(e=>e.getBoundingClientRect().right<=innerWidth));await page.getByRole('button',{name:'Cancel',exact:true}).click();}
  await page.evaluate(()=>{window.ZXingBrowser={BrowserMultiFormatReader:class{async decodeFromConstraints(){throw new DOMException('Denied','NotAllowedError');}}};});
  await page.locator('.sf-camera-trigger').click();await page.getByRole('status').filter({hasText:'permission was denied'}).waitFor();await page.getByRole('button',{name:'Cancel',exact:true}).click();
  const native=await browser.newPage({userAgent:'StockFlowAndroid/1.1'});
  native.on('pageerror',e=>errors.push(e.message));
  await native.route('https://stockflow.test/**',route=>route.fulfill({contentType:'text/html',body:'<p>Native test screen</p>'}));
  await native.goto('https://stockflow.test/');await native.addScriptTag({path:path.join(assets,'js/common/device-client.js')});
  await native.evaluate(()=>{
   window.received=[];window.chunks=[];const channel=new MessageChannel();channel.port1.onmessage=event=>{const m=JSON.parse(event.data);window.received.push(m.action);if(m.action==='download-chunk')window.chunks.push(m.data);const reply={'print':'printed','download-start':'ready','download-chunk':'next','download-end':'saved'}[m.action];if(reply)channel.port1.postMessage(reply);};
   window.dispatchEvent(new MessageEvent('message',{source:null,data:'stockflow-device-ready',ports:[channel.port2]}));
  });
  await native.evaluate(()=>window.print());await native.waitForFunction(()=>window.received.includes('print'));
  await native.evaluate(()=>{const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['\ufeffInvoice,Total\r\nINV-1,14000'],{type:'text/csv'}));a.download='sales-invoices.csv';a.click();});
  await native.waitForFunction(()=>window.received.includes('download-end'));
  assert.equal(await native.evaluate(()=>atob(window.chunks.join(''))),'\xef\xbb\xbfInvoice,Total\r\nINV-1,14000','detached blob export reaches native save picker intact');
  assert.deepEqual(errors,[]);
  console.log('PASS: real barcode decoding, camera review/use once/cancel/denial/responsive dialog, native print message and intact detached CSV export.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
