(()=>{
 'use strict';const root=document.getElementById('role-templates-workspace');if(!root)return;
 const el=id=>document.getElementById(id),node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
 el('template-message').dataset.keepBanner='true';
 const canManage=root.dataset.edit==='true',grants=JSON.parse(root.dataset.grants||'{}');let templates=[],current=null,rows=[],editor,baseline='',busy=false;
 const writable=()=>canManage&&!(current?.omittedActions>0);
 const state=()=>JSON.stringify({key:current?.template.templateKey,name:el('template-name').value,description:el('template-description').value,active:el('template-active').checked,rows});
 const message=(text,error=false)=>{el('template-message').hidden=!text;el('template-message').textContent=text;el('template-message').className='alert '+(error?'alert-danger':'alert-success');};
 async function api(path,method='GET',body){const response=await fetch('/api/admin/roles/'+path,{method,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]')?.content||''},body:body===undefined?undefined:JSON.stringify(body)});let data;try{data=await response.json();}catch{}if(!response.ok)throw Error(data?.message||Object.values(data?.errors||{}).flat().join(' ')||'Unable to load role templates.');return data;}
 async function run(work){if(busy)return;busy=true;root.inert=true;try{await work();}catch(e){message(e.message,true);}finally{busy=false;root.inert=false;}}
 async function discard(){return el('template-form').hidden||baseline===state()||await StockFlowDialog.discard('Discard unsaved template changes?');}
 function render(){el('template-list').replaceChildren();for(const t of templates){const card=node('article',null,'role-template-tile');card.append(node('h2',t.templateName),node('p',t.description||''),node('small',(t.isSystem?'System recommendation':'Business template')+' · '+(t.isActive?'Active':'Inactive')));const button=node('button',canManage?'Configure Template':'View Template','btn btn-outline-secondary');button.type='button';button.onclick=()=>run(async()=>{if(!await discard())return;await open(t.templateKey);});card.append(button);el('template-list').append(card);}}
 async function open(key){message('');const next=key?await api('templates/'+encodeURIComponent(key)):null;const nextRows=next?structuredClone(next.permissions):await fetch('/api/admin/modules').then(r=>{if(!r.ok)throw Error('Unable to load permission catalog.');return r.json();});current=next;rows=nextRows;
  if(!current)for(const row of rows)row.actions=[];
  el('template-name').value=current?.template.templateName||'';el('template-description').value=current?.template.description||'';el('template-active').checked=current?.template.isActive!==false;
  el('template-title').textContent=current?'Configure '+current.template.templateName:'New Role Template';el('template-limits').textContent=current?.template.limitations||'Choose only the actions this staff template needs. Existing shop and owner restrictions continue to apply.';
  el('template-omitted').hidden=!(current?.omittedActions>0);el('template-omitted').textContent='This template contains permissions outside your authority. View the available selection, or create a new template. Ask its owner to edit the original.';
  el('template-permissions').replaceChildren();root.querySelector('.role-accordion-controls')?.remove();editor=StockFlowRolePermissionEditor.bind(el('template-permissions'),writable,data=>{rows=data;},grants);editor.set(rows);
  for(const field of [el('template-name'),el('template-description'),el('template-active')])field.disabled=!writable();el('template-save').hidden=!writable();el('template-form').hidden=false;baseline=state();el('template-form').scrollIntoView({block:'start',behavior:'smooth'});
 }
 el('template-new')?.addEventListener('click',()=>run(async()=>{if(await discard())await open(null);}));
 el('template-cancel').onclick=()=>run(async()=>{if(await discard()){el('template-form').hidden=true;baseline='';}});
 el('template-form').onsubmit=e=>{e.preventDefault();if(!writable()||!e.target.reportValidity())return;run(async()=>{const result=await api('templates/save','POST',{templateKey:current?.template.templateKey,templateName:el('template-name').value.trim(),description:el('template-description').value.trim()||null,isActive:el('template-active').checked,expectedVersion:current?.template.version,permissions:rows});templates=await api('templates');render();await open(result.templateKey);message('Template saved. It is available in Add Role when active. Existing roles and users were not changed.');});};
 window.StockFlowUnsaved?.register(el('template-form'),()=>!el('template-form').hidden&&baseline!==state(),()=>{el('template-form').hidden=true;});run(async()=>{templates=await api('templates');render();});
})();
