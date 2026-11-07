// Source inventory, not a claim of runtime authorization or endpoint coverage.
const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'../..'),web=path.join(root,'src/StockFlow.Web');
const walk=dir=>fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(dir,e.name)):[path.join(dir,e.name)]);
const files=[...walk(path.join(web,'Pages')).filter(p=>p.endsWith('.cshtml')),...walk(path.join(web,'wwwroot/js')).filter(p=>p.endsWith('.js')&&!p.includes('/lib/'))];
const rows=[],relative=p=>path.relative(root,p).replace(/\\/g,'/'),clean=text=>text.replace(/<[^>]*>/g,' ').replace(/\s+/g,' ').trim();
const moduleOf=file=>file.includes('/Pages/')?file.split('/Pages/')[1].split('/')[0]:'Dynamic/shared JS';
for(const file of files){
 const text=fs.readFileSync(file,'utf8'),ref=relative(file);
 const apis=[...new Set([...text.matchAll(/['"\x60](\/api\/[^'"\x60\s?+{]*)/g)].map(m=>m[1]))].join('; ');
 const scripts=[...text.matchAll(/src="~?\/js\/([^"]+)"/g)].map(m=>m[1]);
 const linkedApis=scripts.flatMap(script=>{const source=path.join(web,'wwwroot/js',script);if(!fs.existsSync(source))return[];return[...fs.readFileSync(source,'utf8').matchAll(/['"\x60](\/api\/[^'"\x60\s?+{]*)/g)].map(m=>m[1]);});
 const apiHints=[...new Set([apis,...linkedApis].filter(Boolean))].join('; ');
 const patterns=file.endsWith('.cshtml')?[
  {regex:/<select\b([^>]*)>([\s\S]*?)<\/select>/g,type:'native select'},
  {regex:/<[^>]*\brole=["'](?:combobox|listbox|menu|menuitem)["'][^>]*>/g,type:'custom selector/action semantics'},
  {regex:/<[^>]*\bclass=["'][^"']*dropdown-menu[^"']*["'][^>]*>/g,type:'Bootstrap action menu'}]:
  [{regex:/<select\b([^>]*)>([\s\S]*?)<\/select>/g,type:'dynamic HTML select'},
   {regex:/name:\s*['"]([^'"]+)['"][\s\S]{0,300}?type:\s*['"]select['"]/g,type:'master-data select definition'},
   {regex:/createElement\(['"]select['"]\)/g,type:'dynamic select construction'},
   {regex:/setAttribute\(['"]role['"],\s*['"](?:combobox|listbox|menu|menuitem)['"]\)/g,type:'dynamic picker/action semantics'}];
 for(const {regex,type} of patterns)for(const match of text.matchAll(regex)){
  const attrs=match[1]||'',body=match[2]||'',id=(attrs.match(/(?:id|asp-for|name)=["']([^"']+)/)||[])[1]||(/definition/.test(type)?attrs:'generated ID; see source');
  const multiple=/\bmultiple\b/.test(attrs),line=text.slice(0,match.index).split('\n').length;
  const values=[...body.matchAll(/<option\b([^>]*)>([\s\S]*?)<\/option>/g)].map(m=>((m[1].match(/value=["']([^"']*)/)||[])[1]??clean(m[2]))+' = '+clean(m[2])).join('; ').slice(0,1800);
  const fieldId=id.replace(/([a-z0-9])([A-Z])/g,'$1-$2').toLowerCase();
  const specialized=/(?:^|-)(?:product|variant|barcode|serial)(?:$|-(?:id|sku|code|number|search|lookup|unit|picker|list)$)/i.test(fieldId)||/(?:^|-)unit(?:$|-)/i.test(fieldId);
  rows.push([moduleOf(ref),ref,line,id,type,multiple?'multiple':'single',values||'Populated by existing controller/page JS; inspect source',apiHints||'Server-rendered or module/shared JS; see source',
   multiple?'Native multi-select; preserve supported assignment/apply semantics':/select/.test(type)?'Every visible single-select uses shared UI regardless of list length or route; hidden backing fields stay hidden':'Keep existing autocomplete or command semantics; no duplicate component',
   'Existing authorized options only; no new endpoint or client authorization boundary',
   'Shared single-select keyboard: arrows, Home/End, Enter, Escape, Tab; preserve barcode/SKU/autocomplete and multi-select semantics',
   'Focused module suites listed in delivery report; source inventory is not per-control runtime proof']);
 }
}
for(const [file,control,behavior,api]of[
 ['wwwroot/js/address-fields.js','State/UT and District','Shared searchable adapter; same India data and original GST codes','/data/india-state-districts.json; existing PIN provider unchanged'],
 ['wwwroot/js/product-registration-form.js','Category and Brand','Shared searchable adapter, hierarchy and Add Brand retained','/api/categories; /api/brands'],
 ['wwwroot/js/billing-customer.js','Billing customer autocomplete','Keep server search, IME, keyboard and cashier focus','Existing customer search'],
 ['wwwroot/js/billing.js','Barcode/SKU product lookup','Keep existing specialized lookup','Existing product search'],
 ['wwwroot/js/supplier-identifiers.js','Supplier identifiers entity picker','Keep server-side supplier search and mapping identity','Existing scoped supplier search'],
 ['Pages/Shared/_Layout.cshtml','Account/action menus','Keep command semantics; shared hover/focus tokens, no selected checkmarks','Existing account actions']
 ])rows.push(['Shared/specialized','src/StockFlow.Web/'+file,'',control,'custom picker/menu','existing supported mode','Existing saved IDs/codes',api,behavior,'Existing backend authorization retained','Existing specialized tests plus shared primitive suite','See delivery report']);
const header=['Module','Source','Line','Control','Existing type','Selection mode','Existing values','API/source hints (file-level)','Migration decision','Permission boundary','Keyboard/scanner decision','Coverage note'];
const quote=v=>'"'+String(v).replace(/"/g,'""')+'"';
const target=path.join(root,'docs/planning/dropdown-modernization-2026-11-06.csv');
fs.writeFileSync(target,[header,...rows].map(row=>row.map(quote).join(',')).join('\n')+'\n');
console.log('Inventoried '+rows.length+' source controls/constructions in '+new Set(rows.map(r=>r[1])).size+' files: '+path.relative(root,target));
