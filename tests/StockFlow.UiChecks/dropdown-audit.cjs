// Source inventory, not a claim of complete runtime workflow coverage.
const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'../..'),base=path.join(root,'src/StockFlow.Web'),records=[];
function walk(dir){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){
 if(['bin','obj','lib'].includes(entry.name))continue;const file=path.join(dir,entry.name);
 if(entry.isDirectory()){walk(file);continue;}if(!/\.(cshtml|js)$/.test(file))continue;
 const content=fs.readFileSync(file,'utf8'),lines=content.split(/\r?\n/);
 for(const [index,line]of lines.entries()){
  const kinds=[];
  if(/<select\b/i.test(line))kinds.push('select markup');
  if(/(?:createElement|node|element|make)\(['"]select['"]/.test(line))kinds.push('select factory');
  if(/new Option\(/.test(line))kinds.push('option population');
  if(/StockFlowSelect\.create\(/.test(line))kinds.push('shared picker');
  if(/(?:listbox|role.?[=,].?['"]?option|<datalist\b|product-category-select-option)/.test(line))kinds.push('custom search/list');
  if(kinds.length)records.push({file:path.relative(root,file).replaceAll('\\','/'),line:index+1,kinds,snippet:line.trim().slice(0,1200)});
 }
}}
walk(base);
const report={scope:'StockFlow.Web Razor and application JavaScript; vendor/build output excluded',note:'Source declarations and option populators; not runtime control counts. Native multi-selects, hidden backing sources, remote entity searches and commands retain their controllers.',files:new Set(records.map(r=>r.file)).size,entries:records.length,records};
const output=path.join(root,'docs/dropdown-source-audit.json');fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({files:report.files,entries:report.entries,selectMarkup:records.filter(r=>r.kinds.includes('select markup')).length,selectFactories:records.filter(r=>r.kinds.includes('select factory')).length,customLists:records.filter(r=>r.kinds.includes('custom search/list')).length,output}));
