-- Optional read-only checks after manually applying migration 024.
-- Use the appropriate tenant context; privileged users can see all tenants.
SELECT tenant_id,category_id,category_code,category_name,parent_category_id,
       is_active,created_by,updated_by,updated_at
FROM categories ORDER BY tenant_id,category_id;
SELECT tenant_id,brand_id,brand_code,brand_name,is_active,created_by,updated_by,updated_at
FROM brands ORDER BY tenant_id,brand_id;

-- Legacy names needing review on next edit.
SELECT 'Category' AS kind,tenant_id,category_id AS id,category_name AS name
FROM categories WHERE length(btrim(category_name)) NOT BETWEEN 2 AND 100 OR category_name !~ '[[:alnum:]]'
UNION ALL
SELECT 'Brand',tenant_id,brand_id,brand_name
FROM brands WHERE length(btrim(brand_name)) NOT BETWEEN 2 AND 100 OR brand_name !~ '[[:alnum:]]';

-- No duplicate sibling/root names should be returned.
SELECT tenant_id,parent_category_id,lower(btrim(category_name)),count(*)
FROM categories GROUP BY tenant_id,parent_category_id,lower(btrim(category_name)) HAVING count(*)>1;

-- Existing active products using inactive catalog entries: review without deleting history.
SELECT p.tenant_id,p.product_id,p.product_name,c.category_name,b.brand_name
FROM products p JOIN categories c ON (c.tenant_id,c.category_id)=(p.tenant_id,p.category_id)
LEFT JOIN brands b ON (b.tenant_id,b.brand_id)=(p.tenant_id,p.brand_id)
WHERE p.is_active AND (NOT c.is_active OR b.is_active=FALSE);
