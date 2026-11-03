const fs=require('fs'),path=require('path');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const origin='http://127.0.0.1:55863';
const output=path.resolve(__dirname,'../bin/responsive-audit');
const sizes=process.env.AUDIT_FULL ? [[1920,1080],[1536,864],[1366,768],[1280,720],[1280,650],[1280,600],[1024,768],[768,1024],[390,844],[375,667],[320,640],[1024,480],[853,400],[640,400]] : [[1280,600],[390,844],[320,640]];
(async()=>{
 const inventory=await (await fetch(origin+'/_audit/inventory')).json();
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const results=[],errors=[],network=[],dialogs=[];
 const selected=inventory.urls.filter(url=>!process.env.AUDIT_URL_FILTER||new RegExp(process.env.AUDIT_URL_FILTER).test(url));
 const queue=selected.flatMap(url=>sizes.map(([width,height])=>({url,width,height})));
 const started=Date.now();
 try{await Promise.all(Array.from({length:4},async(_,worker)=>{
  const context=await browser.newContext();const page=await context.newPage();let current;
  page.on('pageerror',error=>errors.push({...current,message:error.message}));
  page.on('response',response=>{if(response.status()>=400&&response.url().includes('/api/'))network.push({...current,endpoint:new URL(response.url()).pathname,status:response.status()});});
  while(queue.length){current=queue.shift();const record={...current};
   try{
    await page.setViewportSize({width:current.width,height:current.height});
    const response=await page.goto(origin+current.url,{waitUntil:'domcontentloaded',timeout:20000});
    record.status=response.status();record.destination=new URL(page.url()).pathname;
    if(response.status()!==200){results.push(record);continue;}
    await page.waitForTimeout(120);
    if(process.env.AUDIT_LOADED_FORMS&&/\/(create|edit|view)(\?|$)/i.test(current.url)) {
      await page.waitForFunction(()=>document.querySelector('#master-data')?.dataset.screenReady==='true',null,{timeout:10000});
    }
    record.layout=await page.evaluate(()=>{
      const main=document.querySelector('main')||document.body;
      const outside=[...main.querySelectorAll('*')].filter(n=>{const r=n.getBoundingClientRect();return r.width>0&&r.height>0&&(r.right>innerWidth+2||r.left < -2)&&!n.closest('dialog:not([open])');}).slice(0,12).map(n=>({tag:n.tagName,id:n.id,cls:typeof n.className==='string'?n.className:'svg',right:Math.round(n.getBoundingClientRect().right)}));
      return {viewport:innerWidth,document:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight,outside,tableCount:main.querySelectorAll('table').length};
    });
    if(record.layout.document>current.width+1)record.failure='Horizontal page overflow';
    if((process.env.AUDIT_SCREENSHOTS||record.failure)&&current.width===390||['/','/Sales/Create','/Receiving','/Products/create','/Admin/Users'].includes(current.url)&&[1280,390].includes(current.width)){
      const name=(current.url==='/'?'dashboard':current.url.replace(/[^a-z0-9]/gi,'_'))+'-'+current.width+'x'+current.height+'.png';
      await page.screenshot({path:path.join(output,name),fullPage:true});record.screenshot=name;
    }
    if(process.env.AUDIT_DIALOGS&&[1280,390,320].includes(current.width)){
     const list=await page.locator('dialog').evaluateAll(nodes=>nodes.map(n=>({id:n.id,cls:n.className})).filter(n=>n.id));
     for(const item of list){
      if(dialogs.some(d=>d.id===item.id&&d.width===current.width))continue;
      const d={...current,...item};
      try{
       await page.evaluate(id=>{document.querySelectorAll('dialog[open]').forEach(n=>n.close());const n=document.getElementById(id);n.hidden=false;n.showModal();},item.id);
       d.layout=await page.locator('#'+item.id).evaluate(n=>{const r=n.getBoundingClientRect();return{left:r.left,right:r.right,top:r.top,bottom:r.bottom,viewport:innerHeight,overflow:n.scrollWidth>n.clientWidth+1};});
       if(d.layout.left<0||d.layout.right>current.width+1||d.layout.top<0||d.layout.bottom>current.height+1||d.layout.overflow)d.failure='Dialog outside viewport or horizontally overflowing';
       await page.locator('#'+item.id).evaluate(n=>n.close());
      }catch(error){d.unavailable=error.message.split('\n')[0];}
      dialogs.push(d);
     }
    }
   }catch(error){record.failure=error.message.split('\n')[0];}
   results.push(record);
   if(results.length%50===0)console.log('Audited '+results.length+' / '+(results.length+queue.length)+' screen/viewports');
  }
  await context.close();
 }));}finally{await browser.close();}
 const report={inventory:inventory.pages,selected,elapsedSeconds:(Date.now()-started)/1000,sizes,results,dialogs,scriptErrors:errors,apiLimitations:network};
 fs.writeFileSync(path.join(output,process.env.AUDIT_REPORT||'browser-results.json'),JSON.stringify(report,null,2));
 const failures=results.filter(r=>r.failure),modalFailures=dialogs.filter(d=>d.failure);
 console.log(JSON.stringify({screens:inventory.urls.length,checks:results.length,failures:failures.map(r=>({url:r.url,width:r.width,failure:r.failure,outside:r.layout?.outside})),modalFailures,unavailable:results.filter(r=>r.status!==200).map(r=>({url:r.url,status:r.status})),scriptErrors:errors.length},null,2));
 if(failures.length||modalFailures.length)process.exitCode=1;
})().catch(error=>{console.error(error);process.exitCode=1;});
