// Browser-only fake of the server contract. Real lease/transaction safety is tested in C#.
module.exports=function draftLifecycleMock(drafts,referenceFor,controls=()=>({})){
 const hasItem=p=>Array.isArray(p?.cart)&&p.cart.some(i=>i&&Number.isInteger(i.quantity)&&i.quantity>0&&i.quantity<=10000&&(i.manualItem?.name?.trim()||Number.isInteger(i.productId)&&i.productId>0&&Number.isInteger(i.variantId)&&i.variantId>0));
 const reply=(id,d)=>({draftId:id,draftNumber:referenceFor(id),revision:d.revision,draftStatus:d.draftStatus,leaseUntil:d.leaseUntil});
 const init=d=>{if(d){d.draftStatus??='HELD';d.revision??=1;}return d;};
 return async(req,url,route,json)=>{
  const fail=controls(),bad=(message,status=409)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify({message})});
  if(url.pathname==='/api/sales/billing/drafts'){
   if(fail.failList)return bad('List unavailable',500);
   const recover=url.searchParams.get('recovery')==='true',search=(url.searchParams.get('search')||'').toLowerCase(),page=Number(url.searchParams.get('page')||1);
   const includeActive=url.searchParams.get('includeActive')==='true',editor=url.searchParams.get('editorId');
   const all=[...drafts].filter(([,raw])=>{const d=init(raw);return recover?d.draftStatus==='LEGACY'||d.draftStatus==='ACTIVE'&&(!d.leaseUntil||d.leaseUntil<Date.now()):d.draftStatus==='HELD'||includeActive&&d.draftStatus==='ACTIVE'&&(d.editorId===editor||!d.leaseUntil||d.leaseUntil<Date.now());}).map(([draftId,d])=>({...reply(draftId,d),label:d.label,customerName:d.payload.customerId?d.payload.customerLabel:null,creationTimeSource:d.creationTimeSource||'SERVER',itemCount:d.payload.cart.length,units:d.payload.cart.reduce((s,i)=>s+i.quantity,0),estimatedTotal:d.payload.cart.length?d.payload.estimatedTotal:0,updatedAt:new Date().toISOString()})).filter(d=>(d.label+' '+d.draftId+' '+d.draftNumber).toLowerCase().includes(search));
   const status=url.searchParams.get('status')||'ALL',filtered=all.filter(d=>status==='ALL'||status==='HELD'&&d.draftStatus==='HELD'||status==='DRAFTS'&&['ACTIVE','LEGACY'].includes(d.draftStatus));
   return json({items:filtered.slice((page-1)*25,page*25),total:filtered.length,page,pageSize:25,counts:{all:all.length,drafts:all.filter(d=>['ACTIVE','LEGACY'].includes(d.draftStatus)).length,onHold:all.filter(d=>d.draftStatus==='HELD').length}});
  }
  const path=url.pathname.split('/'),id=path[5],action=path[6],d=init(drafts.get(id));
  if(req.method()==='GET'){
   if(fail.failLoad)return bad('Draft no longer available',404);
   return !d||['COMPLETED','DISCARDED'].includes(d.draftStatus)?bad('Draft not available',404):json({...d.payload,...reply(id,d)});
  }
  const b=req.postDataJSON();
  if(d?.lastOperation===b.operationId)return json(reply(id,d));
  if(req.method()==='PUT'){
   if(!hasItem(b.payload))return bad('Add at least one item before saving this bill.',400);
   if(fail.failSave)return bad('Save unavailable',500);
   if(d&&(d.draftStatus!=='ACTIVE'||d.revision!==b.revision||d.editorId!==b.editorId||!d.leaseUntil||d.leaseUntil<Date.now()))return bad('Stale save or another editor');
   if(!d&&b.revision!==0)return bad('Missing draft');
   const next={...d,label:b.label,payload:b.payload,draftStatus:'ACTIVE',revision:(d?.revision||0)+1,editorId:b.editorId,leaseUntil:Date.now()+120000,lastOperation:b.operationId};drafts.set(id,next);return json(reply(id,next));
  }
  if(!d)return bad('Draft not available',404);
  if(action==='release'){if(d.editorId===b.editorId&&d.draftStatus==='ACTIVE')d.leaseUntil=null;return json(reply(id,d));}
  if(action==='heartbeat'){if(d.editorId!==b.editorId||d.draftStatus!=='ACTIVE'||!d.leaseUntil||d.leaseUntil<Date.now())return bad('Editing lease expired');d.leaseUntil=Date.now()+120000;return json(reply(id,d));}
  if(['COMPLETED','DISCARDED'].includes(d.draftStatus))return bad('Draft is terminal');
  if(action==='resume'){
   if(d.revision!==b.revision||(!b.recover&&d.draftStatus!=='HELD')||(b.recover&&!(d.draftStatus==='LEGACY'||d.draftStatus==='ACTIVE'&&(!d.leaseUntil||d.leaseUntil<Date.now()))))return bad('Draft no longer resumable or being edited');
   const current=init(drafts.get(b.currentDraftId));
   if(current){
    if(current.revision!==b.currentRevision||current.editorId!==b.editorId||current.draftStatus!=='ACTIVE')return bad('Current draft changed');
    if(b.currentAction==='DISCARD'&&fail.failDelete)return bad('Delete unavailable',500);
    if(b.currentAction==='HOLD'&&!hasItem(current.payload))return bad('Add an item before holding this bill.');
    current.draftStatus=b.currentAction==='DISCARD'?'DISCARDED':b.currentAction==='HOLD'?'HELD':'ACTIVE';current.leaseUntil=null;current.revision++;current.lastOperation=b.operationId;
   }
   d.draftStatus='ACTIVE';d.editorId=b.editorId;d.leaseUntil=Date.now()+120000;d.revision++;d.lastOperation=b.operationId;if(fail.loseRecoverResponse){fail.loseRecoverResponse=false;return route.abort('failed');}return json(reply(id,d));
  }
  if(d.revision!==b.revision)return bad('Stale revision');
  if(d.draftStatus==='ACTIVE'&&(d.editorId!==b.editorId||!d.leaseUntil||d.leaseUntil<Date.now()))return bad('Another editor owns this draft');
  if(req.method()==='DELETE'){
   if(fail.failDelete)return bad('Delete unavailable',500);d.draftStatus='DISCARDED';
  }else if(action==='hold'){
   if(d.draftStatus!=='ACTIVE'||!hasItem(d.payload))return bad('Add an item before holding this bill.');d.draftStatus='HELD';
  }else return bad('Unknown action');
  d.leaseUntil=null;d.revision++;d.lastOperation=b.operationId;d.editorId=b.editorId;if(req.method()==='DELETE'&&fail.loseDeleteResponse){fail.loseDeleteResponse=false;return route.abort('failed');}return json(reply(id,d));
 };
};
