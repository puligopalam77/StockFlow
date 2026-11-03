-- Run manually after 036 as the database administrator. Do not infer dates for old invoices.
BEGIN;
ALTER TABLE public.sales ADD COLUMN IF NOT EXISTS payment_due_date date;
CREATE INDEX IF NOT EXISTS ix_sales_payment_due_date
 ON public.sales(tenant_id,location_id,payment_due_date)
 WHERE status='COMPLETED' AND customer_id IS NOT NULL AND payment_due_date IS NOT NULL;
COMMENT ON COLUMN public.sales.payment_due_date IS 'Agreed customer payment date, entered during billing; NULL means unknown, not overdue.';
COMMIT;
