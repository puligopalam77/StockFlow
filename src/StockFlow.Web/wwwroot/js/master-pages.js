// Master registration uses its own URL and the existing validated form.
(() => {
 'use strict';
 const root=document.getElementById('master-data');if(!root)return;
 const modules=['products','categories','brands','suppliers','customers'],parts=location.pathname.split('/').filter(Boolean),module=root.dataset.module;
 if(!modules.includes(module)||parts[0]?.toLowerCase()!==module)return;
 // Razor also registers /Module/Index/{mode}; use the same responsive editor
 // for that alias rather than leaving its modal-shaped markup uninitialized.
 if(parts[1]?.toLowerCase()==='index')parts.splice(1,1);
 const mode=(parts[1]||'').toLowerCase();if(mode&&!['create','edit','view'].includes(mode))return;
 const moduleName=module[0].toUpperCase()+module.slice(1),singular={products:'Product',categories:'Category',brands:'Brand',suppliers:'Supplier',customers:'Customer'}[module];
 const params=new URLSearchParams(location.search),page=!!mode,listPath='/'+moduleName;
 let returnUrl=listPath;try{const proposed=new URL(params.get('returnTo')||listPath,location.origin);if(proposed.origin===location.origin&&proposed.pathname.toLowerCase()===listPath.toLowerCase())returnUrl=proposed.pathname+proposed.search;}catch{}
 const key='stockflow.master-list.'+module+'.'+document.body.dataset.businessProfile;
 if(!page){try{const state=JSON.parse(sessionStorage.getItem(key)||'null');if(state){const search=document.getElementById('master-search'),status=document.getElementById('status-filter');if(search&&!params.has('search'))search.value=state.search||'';if(status)status.value=state.status||'false';}}catch{}}
 function navigate(action,id,options={}){
  if(!page){try{sessionStorage.setItem(key,JSON.stringify({search:document.getElementById('master-search')?.value,status:document.getElementById('status-filter')?.value}));}catch{}}
  const query=new URLSearchParams();if(id!=null)query.set('id',id);query.set('returnTo',page?returnUrl:location.pathname+location.search);
  if(module==='categories'&&action.toLowerCase()==='create'){query.set('kind',options.kind==='subcategory'?'subcategory':'parent');if(options.parentId)query.set('parentId',options.parentId);}
  location.assign(listPath+'/'+action+(query.size?'?'+query:''));
 }
 const categoryPopup=module==='categories'&&mode==='create'&&params.get('categoryPopup')==='1'&&window.parent!==window;let popupCompleted=false;
 const api={page,mode,categoryPopup,id:mode==='create'?null:Number(params.get('id'))||null,listPath,returnUrl,navigate,saved:false,back(){if(categoryPopup){if(!popupCompleted)window.parent.postMessage({type:'stockflow-category-cancel'},location.origin);return;}location.assign(returnUrl);},complete(result){if(categoryPopup){popupCompleted=true;window.parent.postMessage({type:'stockflow-category-created',category:result},location.origin);}}};window.StockFlowMasterScreens=api;
 if(!page)return;
 document.body.classList.add('master-form-page');if(categoryPopup)document.body.classList.add('category-popup-page');document.body.dataset.masterScreen=module;document.body.dataset.masterScreenMode=mode;
 const selected=mode==='create'&&module==='products'?document.getElementById('purchase-registration'):document.getElementById('editor');
 if(selected instanceof HTMLDialogElement){
  selected.classList.add('master-screen-form');selected.setAttribute('role','region');selected.removeAttribute('aria-modal');
  // Native non-modal rendering retains existing form event/validation behavior.
  selected.showModal=function(){if(!this.open)this.show();};
  selected.addEventListener('close',()=>api.back());
 }else if(selected)selected.classList.add('master-screen-form');
 const header=document.createElement('header');header.className='master-screen-heading';const title=document.createElement('h1');title.textContent=(mode==='create'?'Add ':mode==='view'?'View ':'Edit ')+singular;const back=document.createElement('a');back.className='btn btn-outline-secondary';back.href=returnUrl;back.textContent='Back to '+moduleName;header.append(title,back);root.before(header);document.title=title.textContent+' - StockFlow';
 const crumb=document.getElementById('breadcrumb-current');if(crumb){crumb.textContent=title.textContent;const item=document.createElement('li'),link=document.createElement('a');link.href=returnUrl;link.textContent=moduleName;item.append(link);crumb.closest('li').before(item);}const crumbBack=document.getElementById('breadcrumb-back');if(crumbBack)crumbBack.href=returnUrl;
 api.ready=()=>{root.dataset.screenReady='true';if(mode==='view'){
  if(module==='categories')window.CategoryRegistration?.setViewOnly();
  for(const input of selected.querySelectorAll('form input,form select,form textarea,form button'))input.disabled=true;
  const cancel=document.getElementById('cancel-edit');if(cancel)cancel.disabled=false;
  document.getElementById('category-save')?.setAttribute('disabled','disabled');
 }};
})();
