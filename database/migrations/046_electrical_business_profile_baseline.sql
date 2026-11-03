-- MANUAL migration: Phase 1 of multi-business support; run after migrations 020-045.
-- Review the existing businesses first: this release assumes they use the current
-- electrical/electronics workflow. It does not enable other business profiles.
-- Run in the intended StockFlow database as its administrator, with a backup.
-- This file is deliberately not executed by application startup.
BEGIN;

ALTER TABLE public.tenants
    ADD COLUMN IF NOT EXISTS business_profile_code varchar(40) DEFAULT 'ELECTRICAL';

-- A rerun preserves every nonblank profile value; unexpected values fail the
-- constraint rather than silently reclassifying a business.
UPDATE public.tenants
SET business_profile_code = 'ELECTRICAL'
WHERE business_profile_code IS NULL OR btrim(business_profile_code) = '';

ALTER TABLE public.tenants
    ALTER COLUMN business_profile_code SET DEFAULT 'ELECTRICAL',
    ALTER COLUMN business_profile_code SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.tenants'::regclass
          AND conname = 'ck_tenants_phase1_business_profile'
    ) THEN
        ALTER TABLE public.tenants
            ADD CONSTRAINT ck_tenants_phase1_business_profile
            CHECK (business_profile_code = 'ELECTRICAL');
    END IF;
END $$;

COMMENT ON COLUMN public.tenants.business_profile_code IS
    'Phase 1 baseline: existing businesses retain the Electrical / Electronics experience. No profile selector or additional business types are enabled yet.';

COMMIT;

SELECT tenant_id, tenant_code, business_name, business_profile_code
FROM public.tenants
ORDER BY tenant_id;
