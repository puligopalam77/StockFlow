-- MANUAL: back up, stop StockFlow posting, apply as superuser after 088,
-- deploy the matching application build and restart. No historical rows change.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.cash_opening_expense_reviews') IS NULL THEN RAISE EXCEPTION 'Apply migration 088 first'; END IF;
END $$;
-- Keep reason policy at the same authoritative insert boundary as cash amounts.
-- Existing idempotent requests return before INSERT and retain their original payload.
CREATE FUNCTION public.sf_cash_entry_reason_guard() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE code text:=NEW.request_payload->>'reasonCode'; note text:=NEW.request_payload->>'note'; label text;
BEGIN
 IF NEW.kind NOT IN('OWNER_IN','OWNER_OUT') THEN RETURN NEW; END IF;
 label:=CASE
  WHEN NEW.kind='OWNER_IN' AND code='OWNER_CONTRIBUTION' THEN 'Owner Contribution'
  WHEN NEW.kind='OWNER_IN' AND code='OTHER_INFLOW' THEN 'Other Cash Inflow'
  WHEN NEW.kind='OWNER_OUT' AND code='OWNER_WITHDRAWAL' THEN 'Owner Withdrawal'
  WHEN NEW.kind='OWNER_OUT' AND code='OTHER_OUTFLOW' THEN 'Other Cash Outflow'
 END;
 IF label IS NULL OR NEW.reason<>label THEN RAISE EXCEPTION 'Choose a valid reason for the selected cash entry type' USING ERRCODE='23514'; END IF;
 IF NEW.request_payload ? 'note' AND jsonb_typeof(NEW.request_payload->'note') IS DISTINCT FROM 'string'
  OR length(coalesce(note,''))>250 OR coalesce(note,'')~'[\x01-\x08\x0B\x0C\x0E-\x1F\x7F]'
  OR code IN('OTHER_INFLOW','OTHER_OUTFLOW') AND (length(btrim(coalesce(note,'')))<3 OR note!~'[[:alnum:]]')
 THEN RAISE EXCEPTION 'Enter a valid explanation for Other cash entries and limit notes to 250 characters' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_cash_entry_reason_guard() FROM PUBLIC;
CREATE TRIGGER cash_entry_reason_guard BEFORE INSERT ON public.cash_adjustments
 FOR EACH ROW EXECUTE FUNCTION public.sf_cash_entry_reason_guard();
COMMIT;
