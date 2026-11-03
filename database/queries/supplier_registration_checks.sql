-- Optional read-only checks after manually applying migration 023.
-- Run under the appropriate tenant context; privileged users may see all tenants.

SELECT tenant_id, supplier_id, supplier_code, supplier_name, country_code
FROM suppliers
WHERE country_code = 'ZZ'
ORDER BY tenant_id, supplier_id;

SELECT tenant_id, lower(supplier_code) AS code, count(*)
FROM suppliers
GROUP BY tenant_id, lower(supplier_code)
HAVING count(*) > 1;

-- Legacy values needing review when next edited; no automatic data changes.
SELECT tenant_id, supplier_id, supplier_name
FROM suppliers
WHERE length(trim(supplier_name)) NOT BETWEEN 2 AND 150
   OR length(address) > 500
   OR payment_terms_days NOT BETWEEN 0 AND 365;

SELECT tenant_id, purchase_id, status, supplier_name_snapshot,
       supplier_snapshot_at, payment_terms_days
FROM purchases
ORDER BY purchase_id DESC
LIMIT 100;
