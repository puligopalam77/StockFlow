// Run an existing journey with: node -r ./address-fixture-hook.cjs journey.cjs
// Adds the current shared address assets to older rendered Razor fixtures and
// isolates the postal service. Application API mocks remain owned by each journey.
const fs=require('fs'),path=require('path');
const {chromium}=require('./bin/Debug/net10.0/.playwright/package');
const launch=chromium.launch.bind(chromium);
chromium.launch=async(...args)=>{
 const browser=await launch(...args),newPage=browser.newPage.bind(browser);
 browser.newPage=async(...settings)=>{
  const page=await newPage(...settings),route=page.route.bind(page);
  page.route=(pattern,handler,options)=>route(pattern,async(requestRoute,request)=>{
   if(new URL(requestRoute.request().url()).hostname==='api.postalpincode.in'){
    const pin=new URL(requestRoute.request().url()).pathname.split('/').at(-1),tamil=pin==='600001';
    return requestRoute.fulfill({contentType:'application/json',body:JSON.stringify([{Status:'Success',PostOffice:[{Name:tamil?'Chennai':'Bengaluru',State:tamil?'Tamil Nadu':'Karnataka',District:tamil?'Chennai':'Bengaluru Urban'}]}])});
   }
   const fulfill=requestRoute.fulfill.bind(requestRoute);
   requestRoute.fulfill=async response=>{
    let html=response.path&&path.extname(response.path)==='.html'?fs.readFileSync(response.path,'utf8'):response.contentType?.includes('text/html')?response.body:null;
    if(html&&html.includes('<form')&&(!/common\/select(?:\.[a-z0-9]+)?\.js/.test(html)||!/list-pagination(?:\.[a-z0-9]+)?\.js/.test(html))){
     if(!/list-pagination(?:\.[a-z0-9]+)?\.js/.test(html))html=html.replace('</head>','<script src="/js/list-pagination.js"></script></head>');
     const shared='<link rel="stylesheet" href="/css/select.css"><script src="/js/common/select.js"></script><script src="/js/common/select-migrations.js"></script>';
     if(!/common\/select(?:\.[a-z0-9]+)?\.js/.test(html)){
      if(/address-fields(?:\.[a-z0-9]+)?\.js/.test(html))html=html.replace(/<script src="[^"]*address-fields/,shared+'<script src="/js/address-fields');
      else html=html.replace('</head>',shared+'<link rel="stylesheet" href="/css/address-fields.css"><script src="/js/address-fields.js"></script></head>');
     }
     const {path:file,...rest}=response;return fulfill({...rest,body:html,contentType:'text/html'});
    }
    return fulfill(response);
   };
   return handler(requestRoute,request);
  },options);
  return page;
 };
 return browser;
};
