// Loopback-only interactive UI preview. Synthetic fixtures + in-memory configuration.
// NO PostgreSQL client, application connection, real authentication or financial writes.
const http=require('node:http'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'../..'),assets=path.join(root,'src/StockFlow.Web/wwwroot');
const fixture=path.join(root,'tests/bin/bank-accounts-fixtures');
const template=fs.readFileSync(path.join(fixture,'Index.html'),'utf8');
const original=JSON.parse(fs.readFileSync(path.join(fixture,'detail.json'),'utf8'));
let accounts=[structuredClone(original.account)];const requests=new Map();
const port=5227,origin='http://127.0.0.1:'+port,csrf=crypto.randomUUID();
const today=new Date().toLocaleDateString('en-CA',{timeZone:'Asia/Kolkata'});
const json=(res,value,status=200)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
function view(account,admin,url){
 const out=structuredClone(original),shop=admin?1:Number(url.searchParams.get('shopId'))||null;
 out.today=today;out.date=url.searchParams.get('date')||today;out.policy={maxActiveAccounts:1,maxAssignedShops:3};
 out.canCreate=!admin&&accounts.filter(a=>a.active).length<1;out.canVerify=false;out.canReverse=false;out.canOpen=false;
 for(const key of ['canEdit','canAssign','canDefault','canStatus','canLink'])out[key]=!admin;
 const scoped=accounts.filter(a=>!admin||a.active&&a.stores.includes(1));
 out.accounts=structuredClone(scoped);out.ownerOverview=!admin;out.showRunningBalance=!admin&&!shop;
 if(!account){if(admin)for(const a of out.accounts){delete a.recordedBalance;delete a.stores;}return {today,accounts:out.accounts,canCreate:out.canCreate,...(!admin?{policy:out.policy,shops:out.shops}:{})};}
 const a=scoped.find(a=>a.id===account);if(!a)throw Error('Account unavailable in this preview role.');out.account=structuredClone(a);
 out.selectedShopId=admin?null:shop;out.canReactivate=accounts.filter(a=>a.active).length<1;
 out.filterShops=original.shops.filter(s=>a.stores.includes(s.id)||account===original.account.id&&s.id<=3).map(s=>({...s,assigned:a.stores.includes(s.id)}));
 if(shop&&!out.filterShops.some(s=>s.id===shop))throw Error('Shop is not assigned to this account.');
 out.transactions=account===original.account.id?structuredClone(original.transactions):[];
 for(const r of out.transactions)r.date=today;
 if(shop)out.transactions=out.transactions.flatMap(r=>{const allocation=r.shops.filter(s=>s.id===shop);return allocation.length?[{...r,amount:allocation.reduce((n,s)=>n+s.amount,0),shops:allocation,shopId:shop,shopName:allocation[0].name}]:[];});
 if(admin||shop)for(const r of out.transactions)delete r.runningBalance;
 out.transactions=out.transactions.filter(r=>r.date===out.date);out.total=out.transactions.length;
 out.moneyIn=out.transactions.reduce((n,r)=>n+Math.max(0,r.amount),0);out.moneyOut=out.transactions.reduce((n,r)=>n+Math.max(0,-r.amount),0);out.netMovement=out.moneyIn-out.moneyOut;
 const term=(url.searchParams.get('search')||'').toLowerCase();out.transactions=out.transactions.filter(r=>(r.reference+' '+r.type).toLowerCase().includes(term));out.total=out.transactions.length;out.page=Number(url.searchParams.get('page'))||1;out.transactions=out.transactions.slice((out.page-1)*20,out.page*20);
 out.sources=account===original.account.id?original.sources.filter(s=>!shop||s.shopId===shop):[];out.candidates=[];
 if(account!==original.account.id){out.opening=null;out.recordedBalance=null;out.account.initialized=false;}
 if(admin){for(const key of ['opening','recordedBalance','shops','filterShops'])delete out[key];delete out.account.recordedBalance;delete out.account.stores;}
 return out;
}
async function body(req){let input='';for await(const chunk of req){input+=chunk;if(input.length>20000)throw Error('Preview request too large.');}return JSON.parse(input);}
const server=http.createServer(async(req,res)=>{try{
 if(!['127.0.0.1:'+port,'localhost:'+port].includes(req.headers.host)){res.writeHead(403);return res.end();}
 const url=new URL(req.url,origin),admin=/(?:^|; )preview-role=admin(?:;|$)/.test(req.headers.cookie||'');
 if(req.method!=='GET'&&req.headers.origin&&req.headers.origin!==origin&&req.headers.origin!=='http://localhost:'+port)return json(res,{message:'Cross-origin preview writes are blocked.'},403);
 if(['/preview/owner','/preview/admin'].includes(url.pathname)){res.writeHead(302,{'Set-Cookie':'preview-role='+url.pathname.split('/').pop()+'; Path=/; HttpOnly; SameSite=Strict',Location:'/Settings/BankAccounts'});return res.end();}
 if(url.pathname==='/preview/reset'&&req.method==='POST'){accounts=[structuredClone(original.account)];requests.clear();res.writeHead(303,{Location:'/Settings/BankAccounts'});return res.end();}
 if(url.pathname==='/preview/health')return json(res,{mode:'synthetic-ui-preview',databaseConnected:false,postingEnabled:false});
 if(url.pathname.startsWith('/api/bankaccounts')){
  const parts=url.pathname.split('/').filter(Boolean),id=parts[2];
  if(admin&&url.searchParams.has('shopId'))return json(res,{message:'Owner shop filters are not available to Shop Admins.'},403);
  if(req.method==='GET')return json(res,view(id,admin,url));
  if(req.method!=='POST'||req.headers['x-csrf-token']!==csrf)return json(res,{message:'Invalid preview request.'},403);
  if(admin)return json(res,{message:'Shop Admin preview cannot configure shared bank accounts.'},403);
  const action=parts[3];if(!['create','edit','assign','default','status'].includes(action))return json(res,{message:'Financial posting is disabled in this UI preview. Database behavior is covered by the isolated SQL tests.'},403);
  const data=await body(req),b=data.body||data.Body,key=data.requestId||data.RequestId;
  if(!key||!b)throw Error('Request identity and body required.');
  const fingerprint=crypto.createHash('sha256').update(JSON.stringify({id,action,b})).digest('hex');
  if(requests.has(key)){const prior=requests.get(key);if(prior.fingerprint!==fingerprint)throw Error('Request identity conflicts.');return json(res,prior.result);}
  let a=accounts.find(a=>a.id===id);
  if(action!=='create'&&(!a||a.version!==b.expectedVersion))throw Error('Configuration changed. Reload the account.');
  if(action==='create'||action==='status'&&b.active){if(accounts.some(a=>a.active&&a.id!==id))throw Error('Only one active bank account is allowed in this preview. Deactivate the existing account first.');}
  if(['create','assign'].includes(action)&&(!Array.isArray(b.stores)||!b.stores.length||b.stores.length>3||new Set(b.stores).size!==b.stores.length||b.stores.some(id=>!original.shops.some(s=>s.id===id))))throw Error('Choose 1 to 3 authorized unique shops.');
  if(['create','edit'].includes(action)&&(!b.displayName?.trim()||!b.bankName?.trim()||!b.holderName?.trim()||!/^[A-Z]{4}0[A-Z0-9]{6}$/.test(b.ifsc)))throw Error('Enter valid account configuration.');
  if(action==='create'){
   if(a||!/^\d{6,34}$/.test(b.accountNumber||''))throw Error('Enter a new account with 6–34 synthetic digits.');
   a={id,active:true,version:0,initialized:false,stores:b.stores,maskedNumber:'•••• '+b.accountNumber.slice(-4),recordedBalance:null};accounts.push(a);
  }
  if(['create','edit'].includes(action))Object.assign(a,{displayName:b.displayName,bankName:b.bankName,holderName:b.holderName,ifsc:b.ifsc,type:b.accountType});
  if(action==='assign')a.stores=b.stores;
  if(action==='status'){a.active=b.active;if(!a.active)a.defaultHere=false;}
  if(action==='default'){if(!a.active||!a.stores.includes(b.storeId))throw Error('Choose an active assigned shop.');if(b.storeId===1)a.defaultHere=true;}
  a.version++;const result={accountId:id};requests.set(key,{fingerprint,result});return json(res,result);
 }
 if(url.pathname.startsWith('/api/'))return json(res,url.pathname.includes('notifications')?{items:[],unread:0}:{});
 if(url.pathname==='/'||url.pathname.startsWith('/Settings/BankAccounts')){
  const id=url.pathname.split('/')[3]||'';let html=template.replace(/data-account="[^"]*"/,'data-account="'+id+'"').replace(/data-posting="[^"]*"/,'data-posting="false"');
  html=html.replace(/<meta[^>]+name="csrf-token"[^>]*>/,'').replace('</head>','<meta name="csrf-token" content="'+csrf+'"></head>');
  const banner='<aside style="padding:12px;background:#fff2bf;color:#15243c;position:relative;z-index:1100">LOCAL UI PREVIEW — SYNTHETIC DATA ONLY · '+(admin?'Shop Admin, shop 1':'Owner')+' · No database connection. Financial posting OFF. Use invented bank details only. <a href="/preview/owner">Owner</a> | <a href="/preview/admin">Shop Admin</a> | <form action="/preview/reset" method="post" style="display:inline"><button>Reset demo</button></form></aside>';
  html=html.replace(/(<body[^>]*>)/,'$1'+banner);
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});return res.end(html);
 }
 const relative=decodeURIComponent(url.pathname).replace(/\.[a-z0-9]{10}(?=\.(?:css|js|svg|png|webp)$)/i,'');
 const file=path.resolve(assets,'.'+relative);if(!file.startsWith(assets+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);return res.end('This local preview contains Bank Accounts only.');}
 const types={'.js':'application/javascript','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.woff2':'font/woff2','.ico':'image/x-icon'};
 res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream','X-Content-Type-Options':'nosniff'});fs.createReadStream(file).pipe(res);
 }catch(e){json(res,{message:e.message},400);}});
server.listen(port,'127.0.0.1',()=>console.log('Bank Accounts synthetic UI preview: '+origin+'/Settings/BankAccounts — posting OFF, no database connection.'));
