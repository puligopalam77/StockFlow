-- MANUAL: stop StockFlow, back up, execute after 026 as PostgreSQL superuser, then restart.
-- Repair transfer authorization after action-permission migration 025. No permission grants or stock are changed.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.stock_intakes') IS NULL OR to_regclass('public.user_action_permissions') IS NULL THEN RAISE EXCEPTION 'Apply existing migrations through 026 first'; END IF;
END $$;
-- Retain movement identity after a physical unit moves to another store or transit location.
ALTER TABLE inventory_transactions ADD COLUMN IF NOT EXISTS serial_number_snapshot varchar(150);
UPDATE inventory_transactions it SET serial_number_snapshot=u.serial_number FROM product_units u
WHERE it.tenant_id=u.tenant_id AND it.product_unit_id=u.product_unit_id AND it.serial_number_snapshot IS NULL;
CREATE OR REPLACE FUNCTION sf_movement_serial_snapshot() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NEW.product_unit_id IS NOT NULL THEN
  SELECT serial_number INTO NEW.serial_number_snapshot FROM public.product_units
  WHERE tenant_id=NEW.tenant_id AND product_unit_id=NEW.product_unit_id;
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS movement_serial_snapshot ON inventory_transactions;
CREATE TRIGGER movement_serial_snapshot BEFORE INSERT ON inventory_transactions FOR EACH ROW EXECUTE FUNCTION sf_movement_serial_snapshot();
CREATE OR REPLACE FUNCTION sf_transfer_permission(code text,action text) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 WITH wanted AS (
  SELECT CASE WHEN code IN('TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL') THEN 'STOCK_TRANSFER' ELSE code END module,
         CASE code WHEN 'TRANSFER_DISPATCH' THEN 'DISPATCH' WHEN 'TRANSFER_RECEIVE' THEN 'RECEIVE' WHEN 'TRANSFER_CANCEL' THEN 'CANCEL' ELSE action END act
 )
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id
 JOIN public.tenants t ON t.tenant_id=u.tenant_id CROSS JOIN wanted w
 JOIN public.modules m ON m.module_code=w.module AND m.is_active
 JOIN public.module_actions a ON a.module_id=m.module_id AND a.action_code=w.act AND a.is_active
 JOIN public.user_action_permissions p ON p.module_action_id=a.module_action_id AND p.user_id=u.user_id AND p.tenant_id=u.tenant_id AND p.is_allowed
 WHERE u.user_id=public.sf_actor() AND u.tenant_id=public.sf_tenant() AND u.is_active AND r.is_active AND t.is_active
 AND EXISTS(SELECT 1 FROM public.module_actions v JOIN public.user_action_permissions vp ON vp.module_action_id=v.module_action_id AND vp.user_id=u.user_id AND vp.tenant_id=u.tenant_id AND vp.is_allowed
 WHERE v.module_id=m.module_id AND v.action_code='VIEW' AND v.is_active)) $$;
CREATE OR REPLACE FUNCTION sf_transfer_action(id bigint,act text,body jsonb,retry uuid) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
<<transfer_action>>
DECLARE tid bigint:=public.sf_tenant(); actor int:=public.sf_actor(); h public.stock_transfers; old public.stock_transfer_operations;
 item record; line jsonb; unitid bigint; ids bigint[]; n int; good int; damage int; lost int; resolution text; dest bigint; notes text;
BEGIN
 IF public.sf_location()<=0 OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Select an assigned stock location' USING ERRCODE='42501'; END IF;
 IF retry IS NULL OR retry='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'A request ID is required' USING ERRCODE='23514'; END IF;
 IF NOT public.sf_transfer_permission('STOCK_TRANSFER','VIEW') THEN RAISE EXCEPTION 'Transfer access denied' USING ERRCODE='42501'; END IF;
 IF NOT public.sf_transfer_permission(CASE act WHEN 'dispatch' THEN 'TRANSFER_DISPATCH' WHEN 'receive' THEN 'TRANSFER_RECEIVE' WHEN 'cancel' THEN 'TRANSFER_CANCEL' ELSE 'STOCK_TRANSFER' END,
 CASE act WHEN 'request' THEN 'CREATE' ELSE 'APPROVE' END) THEN RAISE EXCEPTION 'Transfer action is not permitted' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended(tid||':transfer:'||retry,0));
 SELECT * INTO old FROM public.stock_transfer_operations WHERE tenant_id=tid AND request_id=retry;
 IF FOUND THEN IF old.transfer_id<>id OR old.action_name<>act OR old.payload<>body OR old.created_by<>actor THEN RAISE EXCEPTION 'Request ID was already used' USING ERRCODE='23514'; END IF; RETURN id; END IF;
 SELECT * INTO h FROM public.stock_transfers WHERE tenant_id=tid AND transfer_id=id FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Transfer not found' USING ERRCODE='23514'; END IF;
 IF NOT (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id)) THEN RAISE EXCEPTION 'Store access denied' USING ERRCODE='42501'; END IF;
 IF act IN('approve','dispatch','cancel') AND (public.sf_location()<>h.from_location_id OR NOT public.sf_can_location(h.from_location_id)) THEN RAISE EXCEPTION 'Source location access is required' USING ERRCODE='42501'; END IF;
 IF act IN('receive','discrepancy') AND (public.sf_location()<>h.to_location_id OR NOT public.sf_can_location(h.to_location_id)) THEN RAISE EXCEPTION 'Destination access is required' USING ERRCODE='42501'; END IF;
 IF public.sf_location() NOT IN(h.from_location_id,h.to_location_id) THEN RAISE EXCEPTION 'Use the source or destination location' USING ERRCODE='42501'; END IF;
 IF act='request' AND h.status='DRAFT' THEN UPDATE public.stock_transfers SET status='REQUESTED' WHERE transfer_id=id;
 ELSIF act='approve' AND h.status='REQUESTED' THEN UPDATE public.stock_transfers SET status='APPROVED',approved_by=actor WHERE transfer_id=id;
 ELSIF act='cancel' AND h.status IN('DRAFT','REQUESTED','APPROVED') THEN UPDATE public.stock_transfers SET status='CANCELLED' WHERE transfer_id=id;
 ELSIF (act='dispatch' AND h.status='APPROVED') OR (act IN('receive','discrepancy') AND h.status IN('DISPATCHED','PARTIALLY_RECEIVED')) THEN
  -- Same product lock order as existing sales/receipts/adjustments; source stock cannot be oversold.
  PERFORM p.product_id FROM public.products p WHERE p.tenant_id=tid AND p.product_id IN(SELECT v.product_id FROM public.product_variants v JOIN public.stock_transfer_items i ON i.variant_id=v.variant_id WHERE i.transfer_id=id AND i.tenant_id=tid) ORDER BY p.product_id FOR UPDATE;
  IF jsonb_typeof(body->'items')<>'array' OR jsonb_array_length(body->'items') NOT BETWEEN 1 AND 200 THEN RAISE EXCEPTION 'Supply transfer lines' USING ERRCODE='23514'; END IF;
  IF (SELECT count(DISTINCT x->>'transferItemId') FROM jsonb_array_elements(body->'items') x)<>jsonb_array_length(body->'items') THEN RAISE EXCEPTION 'Duplicate transfer lines' USING ERRCODE='23514'; END IF;
  IF act='dispatch' AND jsonb_array_length(body->'items')<>(SELECT count(*) FROM public.stock_transfer_items WHERE transfer_id=id) THEN RAISE EXCEPTION 'Dispatch every requested line once' USING ERRCODE='23514'; END IF;
  notes:=COALESCE(body->>'notes','');
  IF length(notes)>2000 OR (act='discrepancy' AND length(btrim(notes))=0) THEN RAISE EXCEPTION 'A discrepancy reason is required (maximum 2000 characters)' USING ERRCODE='23514'; END IF;
  FOR line IN SELECT value FROM jsonb_array_elements(body->'items') LOOP
   SELECT i.*,v.product_id,v.is_serial_tracked INTO item FROM public.stock_transfer_items i JOIN public.product_variants v ON v.variant_id=i.variant_id AND v.tenant_id=i.tenant_id WHERE i.tenant_id=tid AND i.transfer_id=id AND i.transfer_item_id=(line->>'transferItemId')::bigint;
   IF NOT FOUND THEN RAISE EXCEPTION 'Line does not belong to transfer' USING ERRCODE='23514'; END IF;
   n:=(line->>'quantity')::int;resolution:=COALESCE(line->>'resolution','RECEIVED');
   IF n IS NULL OR n<=0 OR n>10000 THEN RAISE EXCEPTION 'Invalid quantity' USING ERRCODE='23514'; END IF;
   IF act='dispatch' AND (n<>item.requested_quantity OR public.sf_stock(tid,h.from_location_id,item.variant_id)<n) THEN RAISE EXCEPTION 'Insufficient source stock or incorrect dispatch quantity' USING ERRCODE='23514'; END IF;
   IF act<>'dispatch' AND n>item.dispatched_quantity-item.received_quantity-item.damaged_quantity-item.lost_quantity THEN RAISE EXCEPTION 'Receipt exceeds outstanding transit quantity' USING ERRCODE='23514'; END IF;
   IF act='receive' AND resolution<>'RECEIVED' OR act='discrepancy' AND resolution NOT IN('DAMAGED','LOST') THEN RAISE EXCEPTION 'Use the discrepancy action to record damage/loss' USING ERRCODE='23514'; END IF;
   SELECT COALESCE(array_agg(x::bigint),'{}'::bigint[]) INTO ids FROM jsonb_array_elements_text(COALESCE(line->'unitIds','[]')) x;
   IF item.is_serial_tracked AND (cardinality(ids)<>n OR (SELECT count(DISTINCT x) FROM unnest(ids) x)<>n) OR NOT item.is_serial_tracked AND cardinality(ids)<>0 THEN RAISE EXCEPTION 'Select exactly one distinct serial per serialized unit' USING ERRCODE='23514'; END IF;
   IF item.is_serial_tracked THEN
    PERFORM product_unit_id FROM public.product_units WHERE tenant_id=tid AND product_unit_id=ANY(ids) ORDER BY product_unit_id FOR UPDATE;
    IF act='dispatch' THEN
     IF (SELECT count(*) FROM public.product_units WHERE tenant_id=tid AND variant_id=item.variant_id AND location_id=h.from_location_id AND unit_status='IN_STOCK' AND sale_id IS NULL AND product_unit_id=ANY(ids))<>n THEN RAISE EXCEPTION 'A source unit is unavailable' USING ERRCODE='23514'; END IF;
    ELSE
     IF (SELECT count(*) FROM public.stock_transfer_units tu JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id WHERE tu.tenant_id=tid AND tu.transfer_item_id=item.transfer_item_id AND tu.resolution='IN_TRANSIT' AND u.location_id=h.transit_location_id AND u.unit_status='IN_TRANSIT' AND tu.product_unit_id=ANY(ids))<>n THEN RAISE EXCEPTION 'A unit is not outstanding on this transfer' USING ERRCODE='23514'; END IF;
    END IF;
   ELSE ids:=ARRAY[NULL::bigint]; END IF;
   FOREACH unitid IN ARRAY ids LOOP
    IF act='dispatch' THEN
     INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by)
     VALUES(tid,h.from_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_OUT',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor),
           (tid,h.transit_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_IN',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor);
     IF unitid IS NOT NULL THEN
      INSERT INTO public.stock_transfer_units(tenant_id,transfer_item_id,product_unit_id) VALUES(tid,item.transfer_item_id,unitid);
      UPDATE public.product_units SET location_id=h.transit_location_id,unit_status='IN_TRANSIT',updated_at=now() WHERE tenant_id=tid AND product_unit_id=unitid;
     END IF;
    ELSE
     INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
     VALUES(tid,h.transit_location_id,item.product_id,item.variant_id,unitid,CASE WHEN resolution='LOST' THEN 'TRANSFER_LOSS' ELSE 'TRANSFER_OUT' END,CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes);
     IF resolution<>'LOST' THEN
      INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
      VALUES(tid,h.to_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_IN',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes);
      IF resolution='DAMAGED' THEN INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
       VALUES(tid,h.to_location_id,item.product_id,item.variant_id,unitid,'DAMAGED',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes); END IF;
     END IF;
     IF unitid IS NOT NULL THEN
      UPDATE public.stock_transfer_units SET resolution=transfer_action.resolution WHERE tenant_id=tid AND transfer_item_id=item.transfer_item_id AND product_unit_id=unitid;
      UPDATE public.product_units SET location_id=CASE WHEN resolution='LOST' THEN h.transit_location_id ELSE h.to_location_id END,
       unit_status=CASE WHEN resolution='RECEIVED' THEN 'IN_STOCK' ELSE resolution END,updated_at=now() WHERE tenant_id=tid AND product_unit_id=unitid;
     END IF;
    END IF;
   END LOOP;
   IF act='dispatch' THEN UPDATE public.stock_transfer_items SET dispatched_quantity=n WHERE transfer_item_id=item.transfer_item_id;
   ELSE UPDATE public.stock_transfer_items SET received_quantity=received_quantity+CASE WHEN resolution='RECEIVED' THEN n ELSE 0 END,
    damaged_quantity=damaged_quantity+CASE WHEN resolution='DAMAGED' THEN n ELSE 0 END,lost_quantity=lost_quantity+CASE WHEN resolution='LOST' THEN n ELSE 0 END WHERE transfer_item_id=item.transfer_item_id; END IF;
  END LOOP;
  IF act='dispatch' THEN UPDATE public.stock_transfers SET status='DISPATCHED',dispatched_by=actor,dispatched_at=now() WHERE transfer_id=id;
  ELSE UPDATE public.stock_transfers SET status=CASE WHEN EXISTS(SELECT 1 FROM public.stock_transfer_items WHERE transfer_id=id AND received_quantity+damaged_quantity+lost_quantity<dispatched_quantity) THEN 'PARTIALLY_RECEIVED' ELSE 'RECEIVED' END,received_by=actor,received_at=now() WHERE transfer_id=id; END IF;
 ELSE RAISE EXCEPTION 'Invalid transfer state transition; reload the transfer' USING ERRCODE='23514'; END IF;
 INSERT INTO public.stock_transfer_operations VALUES(tid,retry,id,act,body,actor,now());
 INSERT INTO public.audit_logs(tenant_id,location_id,user_id,module_name,action_name,table_name,record_id,new_values) VALUES(tid,NULL,actor,'Stock transfers',upper(act),'stock_transfers',id,body);
 RETURN id;
END $$;
CREATE OR REPLACE FUNCTION sf_transfer_unit_labels(id bigint)
RETURNS TABLE(transfer_item_id bigint,product_unit_id bigint,serial_number varchar,resolution varchar,barcodes text[])
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT tu.transfer_item_id,tu.product_unit_id,u.serial_number,tu.resolution,
 ARRAY[u.serial_number::text,u.barcode::text,u.internal_barcode::text,u.manufacturer_barcode::text]
 FROM public.stock_transfer_units tu
 JOIN public.stock_transfer_items i ON i.transfer_item_id=tu.transfer_item_id AND i.tenant_id=tu.tenant_id
 JOIN public.stock_transfers h ON h.transfer_id=i.transfer_id AND h.tenant_id=i.tenant_id
 JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id AND u.tenant_id=tu.tenant_id
 WHERE h.tenant_id=public.sf_tenant() AND h.transfer_id=id AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW')
 AND (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id))
 AND (public.sf_location()=0 OR public.sf_location() IN(h.from_location_id,h.to_location_id)) $$;
REVOKE ALL ON FUNCTION sf_transfer_permission(text,text),sf_transfer_action(bigint,text,jsonb,uuid),sf_transfer_unit_labels(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_transfer_permission(text,text),sf_transfer_action(bigint,text,jsonb,uuid),sf_transfer_unit_labels(bigint) TO stockflow_runtime;
COMMIT;