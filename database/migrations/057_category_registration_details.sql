-- MANUAL ONLY. Back up, stop application/workers, and run after 056 as PostgreSQL superuser.
-- Existing category IDs, codes, hierarchy and product/history relationships are retained.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 057 as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_save_category_configuration(integer,jsonb)') IS NULL THEN RAISE EXCEPTION 'Apply 056 first'; END IF;
END $$;
ALTER TABLE public.categories ALTER COLUMN category_code DROP EXPRESSION IF EXISTS;
ALTER TABLE public.categories ADD COLUMN IF NOT EXISTS display_order integer NOT NULL DEFAULT 0 CHECK(display_order BETWEEN 0 AND 99999);
CREATE OR REPLACE FUNCTION public.sf_category_registration_code() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 NEW.category_code:=COALESCE(NULLIF(upper(btrim(NEW.category_code)),''),'CAT'||lpad(NEW.category_id::text,greatest(4,length(NEW.category_id::text)),'0'));
 IF NEW.category_code!~'^[A-Z0-9][A-Z0-9_-]{1,29}$' THEN RAISE EXCEPTION 'Use a category code of 2 to 30 letters, numbers, hyphens or underscores' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_category_registration_code ON public.categories;
CREATE TRIGGER aa_category_registration_code BEFORE INSERT OR UPDATE ON public.categories FOR EACH ROW EXECUTE FUNCTION public.sf_category_registration_code();
REVOKE ALL ON FUNCTION public.sf_category_registration_code() FROM PUBLIC;
-- Existing tenant code uniqueness and category RLS continue to apply.
COMMIT;
