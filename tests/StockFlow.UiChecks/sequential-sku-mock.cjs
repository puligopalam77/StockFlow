// Deterministic API fixture for UI checks. Atomic allocation is tested separately
// against PostgreSQL; this fixture verifies the browser uses server responses.
module.exports=()=>{
 const parents=new Map(),suffixes=new Map(),operations=new Map(),prefixes=new Map();let count=0;
 return {
  reserve(body){
   if(body.reserveParent||!body.parentCode){const key=body.requestId+'|'+body.categoryId+'|'+(body.parentCode||'');if(!parents.has(key))parents.set(key,body.parentCode||'TST-'+String(++count).padStart(4,'0'));return {code:parents.get(key)};}
   if(!operations.has(body.operationId)){const start=suffixes.get(body.parentCode)||0;operations.set(body.operationId,{codes:Array.from({length:body.count},(_,i)=>body.parentCode+'-'+String(start+i+1).padStart(2,'0'))});suffixes.set(body.parentCode,start+body.count);}
   return operations.get(body.operationId);
  },
  prefix(name){if(!name.trim())return '';if(!prefixes.has(name)){let base=name.replace(/[^a-z0-9]/gi,'').slice(0,3).toUpperCase().padEnd(3,'X'),value=base,n=0;while([...prefixes.values()].includes(value))value=base+(++n);prefixes.set(name,value);}return prefixes.get(name);}
 };
};
