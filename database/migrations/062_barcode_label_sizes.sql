-- MANUAL ONLY. Back up, stop application/workers; run after 061 as PostgreSQL superuser.
-- Extends print settings only. Saved codes, stock and existing print history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 062 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.barcode_print_requests') IS NULL THEN RAISE EXCEPTION 'Apply 061 first'; END IF;
END $$;
ALTER TABLE public.barcode_print_requests DROP CONSTRAINT barcode_print_requests_format_check;
ALTER TABLE public.barcode_print_requests ADD CONSTRAINT barcode_print_requests_format_check
 CHECK(format IN('sheet','thermal','thermal50x30','thermal40x25','thermal30x20'));
ALTER TABLE public.barcode_print_requests ADD COLUMN sheet_template jsonb
 CHECK(sheet_template IS NULL OR jsonb_typeof(sheet_template)='object');
ALTER TABLE public.barcode_print_requests ADD COLUMN small_label_scan_tested boolean NOT NULL DEFAULT false;
ALTER TABLE public.barcode_print_requests ADD CONSTRAINT ck_barcode_small_label_scan_test
 CHECK(format<>'thermal30x20' OR small_label_scan_tested);
COMMENT ON COLUMN public.barcode_print_requests.sheet_template IS 'A4 label dimensions, rows/columns, margins and gaps in millimeters; NULL preserves legacy print settings.';
COMMIT;
