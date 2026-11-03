-- Phase 9 invoice numbering from the supplied roadmap.
-- Execute manually in the StockFlow application database.
-- Sales/payment tables are already defined in scripts/004_create_sales_tables.sql.
-- Creates a missing sequence only; rerunning does not reset an existing sequence.
-- If invoices already exist from another numbering system, reconcile the next
-- sequence value with those invoice numbers before using it to generate invoices.

BEGIN;
CREATE SEQUENCE IF NOT EXISTS invoice_number_seq
    AS BIGINT
    START WITH 1
    INCREMENT BY 1
    NO CYCLE;
COMMIT;
