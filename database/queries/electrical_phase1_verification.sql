-- READ ONLY. Run manually after migration 046 in the intended StockFlow database.
BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name;

-- Expected: one NOT NULL varchar(40) column, default 'ELECTRICAL'.
SELECT column_name, data_type, character_maximum_length, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'tenants'
  AND column_name = 'business_profile_code';

-- Expected: every current tenant is ELECTRICAL and profile_persisted is true.
-- The JSON access also allows this check before migration: code is then NULL.
SELECT t.tenant_id, t.tenant_code, t.business_name,
       to_jsonb(t)->>'business_profile_code' AS business_profile_code,
       COALESCE(to_jsonb(t)->>'business_profile_code' = 'ELECTRICAL',false) AS profile_persisted
FROM public.tenants t
ORDER BY t.tenant_id;

-- Expected: one validated constraint permitting only ELECTRICAL in Phase 1.
SELECT conname, convalidated, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conrelid = 'public.tenants'::regclass
  AND conname = 'ck_tenants_phase1_business_profile';

COMMIT;
