// Disposable profile_ui_fixture database only, explicit local test port.
const assert=require('assert'),{execFile}=require('child_process');
const db=process.argv[2];if(!/^profile_ui_fixture_[a-f0-9]{32}$/.test(db||''))throw Error('Disposable profile UI database required');
const args=['-X','-h','127.0.0.1','-p','55443','-U','postgres','-v','ON_ERROR_STOP=1','-At','-d',db];
const sql=q=>new Promise((resolve,reject)=>execFile('C:/Program Files/PostgreSQL/18/bin/psql.exe',[...args,'-c',q],{windowsHide:true,timeout:15000},(error,stdout,stderr)=>error?reject(Object.assign(error,{details:stderr})):resolve(stdout.trim())));
const context="SET app.tenant_id='1';SET app.user_id='1';SET app.store_id='1';SET app.location_id='1';SET app.business_profile_code='ELECTRICAL';SET ROLE stockflow_runtime;";
(async()=>{
 const initial=JSON.parse(await sql("SELECT json_build_object('code',business_profile_code,'version',business_profile_version) FROM public.tenants WHERE tenant_id=1"));assert.equal(initial.code,'ELECTRICAL');assert(Number.isSafeInteger(initial.version));
 const activity='profile_ui_race_'+Date.now();
 // Observe the first session holding the tenant row after its update, then submit
 // the independently authorized second session with the same previously read version.
 const first=sql(`BEGIN;SET application_name='${activity}';${context}SELECT public.sf_change_business_profile('BIKE_ACCESSORIES',${initial.version},'First isolated concurrency session',true);SELECT pg_sleep(2);COMMIT;`);
 let observed=false;for(let i=0;i<60;i++){if(await sql(`SELECT count(*) FROM pg_stat_activity WHERE application_name='${activity}' AND wait_event='PgSleep'`)==='1'){observed=true;break;}await new Promise(r=>setTimeout(r,25));}assert(observed,'First independent session holds row after update');
 const second=sql(`${context}SELECT public.sf_change_business_profile('BIKE_ACCESSORIES',${initial.version},'Second isolated concurrency session',true);`).then(()=>{throw Error('Stale concurrent update unexpectedly accepted');},error=>{assert(/changed|reload|version/i.test(error.details),error.details);});
 await Promise.all([first,second]);
 const saved=JSON.parse(await sql("SELECT json_build_object('code',business_profile_code,'version',business_profile_version) FROM public.tenants WHERE tenant_id=1"));assert.equal(saved.code,'BIKE_ACCESSORIES');assert.equal(saved.version,initial.version+1,'Only one concurrent request updates version');
 await sql(`${context}SELECT public.sf_change_business_profile('ELECTRICAL',${saved.version},'Restore isolated concurrency fixture',true);`);
 console.log('PASS two independent authorized PostgreSQL sessions: one same-version switch commits; waiting stale request rejected; version increments once; original profile restored.');
})().catch(e=>{console.error(e);process.exitCode=1;});
