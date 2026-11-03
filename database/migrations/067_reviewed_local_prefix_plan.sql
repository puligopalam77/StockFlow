-- Reviewed mapping from the read-only inspection of this installation (tenant 1).
-- This prepares a TEMP table only. Run in the same session before migration 067.
-- Name checks prevent applying this installation's IDs to a different catalog.
CREATE TEMP TABLE sf_sku_prefix_plan(tenant_id bigint,category_id integer,prefix text);
INSERT INTO sf_sku_prefix_plan
SELECT c.tenant_id,c.category_id,p.prefix FROM public.categories c JOIN (VALUES
 (1,'Battery','BAT'),(2,'Inverter','INV'),(3,'Water Purifier','WPU'),(4,'Accessory','ACC'),(5,'Spare Part','SPR'),(6,'Create Test categor','CTC'),
 (7,'Helmets','HEL'),(8,'Bike Spare Parts','BSP'),(9,'Side Guards','SDG'),(10,'Head Lights','HDL'),(11,'Mirrors','MIR'),(12,'Brake Parts','BRK'),
 (13,'Chains & Sprockets','CHS'),(14,'Gloves','GLV'),(15,'Bike Covers','BKC'),(16,'Bike Luggage','BKL'),(37,'Bike Parts & Accessories','BPA'),
 (38,'Full Face Helmets','HLM'),(39,'Half Face Helmets','HFL'),(40,'Modular Helmets','MHL'),(41,'Filters','FLT'),(42,'Clutch Parts','CLT'),
 (43,'Lights & Electrical','LEL'),(44,'Indicators','IND'),(45,'Fog Lights','FGL'),(46,'Horns','HRN'),(47,'Guards & Protection','GPR'),
 (48,'Crash Guards','CRG'),(49,'Engine Guards','ENG'),(50,'Riding Accessories','RAC'),(51,'Riding Jackets','RJK'),(52,'BIKE BAGS','BAG'),
 (53,'Plastic bags','PLB'),(54,'Leather luggage bags','LLB'),(55,'Tank Bags','TNK')) p(id,name,prefix)
 ON c.tenant_id=1 AND c.category_id=p.id AND c.category_name=p.name;
SELECT c.tenant_id,c.business_profile_code,c.category_id,c.category_code,c.category_name,p.prefix
FROM sf_sku_prefix_plan p JOIN public.categories c USING(tenant_id,category_id) ORDER BY c.category_id;

