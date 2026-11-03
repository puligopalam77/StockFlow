-- Run manually as database administrator after 037. Existing invoices and receipt numbers are unchanged.
BEGIN;
ALTER TABLE public.company_settings ADD COLUMN IF NOT EXISTS profile jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE public.sales ADD COLUMN IF NOT EXISTS salesperson_user_id integer;
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_sales_salesperson_user') THEN
  ALTER TABLE public.sales ADD CONSTRAINT fk_sales_salesperson_user FOREIGN KEY (tenant_id,salesperson_user_id) REFERENCES public.users(tenant_id,user_id);
 END IF;
END $$;
CREATE OR REPLACE FUNCTION public.sf_purchase_receipt_number() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE prefix text;
BEGIN
 IF NEW.receipt_number IS NULL OR btrim(NEW.receipt_number)='' THEN
  SELECT COALESCE(NULLIF(profile->>'ReceiptPrefix',''),'GRN-{YYYY}-') INTO prefix
   FROM public.company_settings WHERE tenant_id=NEW.tenant_id AND company_settings_id=1;
  prefix:=replace(COALESCE(prefix,'GRN-{YYYY}-'),'{YYYY}',to_char(NEW.received_date,'YYYY'));
  NEW.receipt_number:=prefix||lpad(NEW.receipt_id::text,GREATEST(6,length(NEW.receipt_id::text)),'0');
 END IF;
 RETURN NEW;
END $$;
COMMENT ON COLUMN public.company_settings.profile IS 'Business information, validated logo and billing defaults. Completed invoices retain a company snapshot.';
COMMIT;
