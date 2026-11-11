// Offline safety checks only: never opens a database or executes migration SQL.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..');
const folder=path.join(root,'database/review/bank-accounts');
const files=['090_bank_accounts_schema.sql','091_bank_permissions.sql'];
const read=name=>fs.readFileSync(path.join(folder,name),'utf8');
const uncomment=sql=>sql.replace(/--[^\n]*/g,'').replace(/\/\*[\s\S]*?\*\//g,'');
const schema=uncomment(read(files[0])),permissions=uncomment(read(files[1]));
for(const [name,sql] of files.map(name=>[name,uncomment(read(name))])){
 assert.match(sql,/current_setting\('stockflow\.bank_schema_review_approved',true\) IS DISTINCT FROM 'yes'/,name+' requires approval guard');
 assert.match(sql,/RAISE EXCEPTION/,name+' fails closed');
 assert.match(sql,/rolsuper/,name+' requires separate maintenance authority');
 assert.match(sql,/^\s*BEGIN;/);assert.match(sql,/COMMIT;\s*$/);
 assert.doesNotMatch(sql,/\b(?:DROP|TRUNCATE|DELETE\s+FROM)\b/i,'No destructive down/data operations');
 assert.doesNotMatch(sql,/\bGRANT\s+/i,'No runtime access or activation grants');
 assert.doesNotMatch(sql,/\b(?:INSERT\s+INTO|UPDATE|ALTER\s+TABLE)\s+(?:public\.)?(?:payments|sales|supplier_payments|expense_payments|sale_returns|purchase_returns|service_payments|cash_adjustments)\b/i,'No source financial changes or backfill');
 assert.doesNotMatch(sql,/INSERT\s+INTO\s+(?:public\.)?(?:user_action_permissions|role_action_permissions)\b/i,'No implicit verifier grants');
 assert(!fs.existsSync(path.join(root,'database/migrations',name)),'Review draft is outside normal migrations');
}
const tables=[...schema.matchAll(/CREATE TABLE public\.(bank_\w+)/g)].map(match=>match[1]);
assert.equal(tables.length,9);
const rlsList=schema.match(/FOREACH tab IN ARRAY ARRAY\[([^\]]+)\] LOOP/)[1];
for(const table of tables)assert(rlsList.includes("'"+table+"'"),'RLS/revoke list covers '+table);
assert.match(schema,/ENABLE ROW LEVEL SECURITY/);assert.match(schema,/FORCE ROW LEVEL SECURITY/);
assert.match(schema,/REVOKE ALL ON TABLE/);assert.doesNotMatch(schema,/CREATE POLICY/i,'No permissive runtime policy');
assert.match(schema,/bank_one_default_per_shop[\s\S]*?\(tenant_id,store_id\)[\s\S]*?WHERE is_active AND is_default_receiving/);
assert.match(schema,/UNIQUE\(tenant_id,source_kind,source_key\)/);
assert.match(schema,/bank_unique_evidenced_posting[\s\S]*?WHERE entry_kind='SETTLEMENT'/);
assert.match(schema,/UNIQUE\(tenant_id,reverses_id\)/);
assert.match(schema,/BEFORE UPDATE OR DELETE/);
assert.match(schema,/verification_label='Manually Verified'/);
assert.match(schema,/effective_date date NOT NULL, effective_at timestamptz/,'Date-only evidence has no invented timestamp');
assert.match(schema,/verified_by integer NOT NULL, verified_at timestamptz NOT NULL DEFAULT clock_timestamp\(\)/);
assert.match(schema,/protected_account_number text/);assert.match(schema,/identity_fingerprint bytea/);
assert.doesNotMatch(schema,/\b(?:actual_balance|live_balance|recorded_balance)\s+(?:numeric|decimal)/i,'No competing stored balance');
assert.match(permissions,/'BANK_ACCOUNTS','Bank Accounts','\/Settings\/BankAccounts',65,false,'bank',false/);
for(const action of ['VERIFY','VIEW_BALANCE','VIEW_DETAILS','SET_DEFAULT','INITIALIZE','REVERSE'])assert(permissions.includes("('"+action+"'"),'Explicit '+action+' permission');
console.log('PASS bank review contract: two guarded drafts not applied remotely; nine locked RLS tables; default/deduplication/audit constraints; inactive explicit permissions; no grants, backfill, source writes or destructive operations.');
console.log('LIMIT: this checker is static only. For actual local SQL/RLS results and remaining posting/UI blockers, see docs/bank-accounts-local-test-results.txt and bank-migration-local-checks.ps1.');
