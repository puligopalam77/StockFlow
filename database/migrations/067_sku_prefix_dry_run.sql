-- READ ONLY. Review this mapping before applying 067. No counters are initialized here.
WITH plan AS (
 SELECT tenant_id,business_profile_code,category_id,category_code,category_name,parent_category_id,
 upper(rpad(left(regexp_replace(category_name,'[^a-zA-Z0-9]','','g'),3),3,'X')) proposed_prefix
 FROM public.categories
), ids AS (
 SELECT p.tenant_id,c.business_profile_code,p.product_id,p.parent_product_code code FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 UNION SELECT p.tenant_id,c.business_profile_code,p.product_id,p.sku FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 UNION SELECT p.tenant_id,c.business_profile_code,p.product_id,v.sku FROM public.products p JOIN public.categories c USING(tenant_id,category_id) JOIN public.product_variants v USING(tenant_id,product_id)
)
SELECT p.*,
 count(*) OVER(PARTITION BY tenant_id,business_profile_code,proposed_prefix)>1 conflicting_suggestion,
 (SELECT max(split_part(upper(btrim(code)),'-',2)::bigint) FROM ids i
 WHERE i.tenant_id=p.tenant_id AND i.business_profile_code=p.business_profile_code
 AND upper(btrim(code)) ~ ('^'||p.proposed_prefix||'-[0-9]{1,15}(-.*)?$')) existing_high_water
FROM plan p ORDER BY tenant_id,business_profile_code,category_id;
-- Also enumerate numeric conventions which do NOT match a suggested prefix.
-- This prevents a manually assigned/legacy prefix from being missed in review.
WITH identifiers AS (
 SELECT p.tenant_id,c.business_profile_code,p.category_id,p.product_id,p.parent_product_code code FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 UNION SELECT p.tenant_id,c.business_profile_code,p.category_id,p.product_id,p.sku FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 UNION SELECT p.tenant_id,c.business_profile_code,p.category_id,p.product_id,v.sku FROM public.products p JOIN public.categories c USING(tenant_id,category_id) JOIN public.product_variants v USING(tenant_id,product_id)
)
SELECT tenant_id,business_profile_code,category_id,product_id,code
FROM identifiers WHERE upper(btrim(code)) ~ '^[A-Z][A-Z0-9]{2,7}-[0-9]+(-.*)?$'
ORDER BY tenant_id,business_profile_code,category_id,product_id,code;
-- Supply reviewed overrides in the SAME psql session before running migration 067:
-- CREATE TEMP TABLE sf_sku_prefix_plan(tenant_id bigint,category_id integer,prefix text);
-- INSERT INTO sf_sku_prefix_plan VALUES(1,12,'BRK'),(1,38,'HLM');
-- \i database/migrations/067_sequential_catalog_identifiers.sql
