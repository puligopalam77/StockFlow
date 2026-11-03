-- MANUAL after 020, using the same database administrator. Application stopped.
BEGIN;
CREATE TABLE stock_transfers(transfer_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL REFERENCES tenants,
 transfer_number VARCHAR(50) NOT NULL,from_location_id BIGINT NOT NULL,to_location_id BIGINT NOT NULL,transit_location_id BIGINT NOT NULL,
 status VARCHAR(30) NOT NULL DEFAULT 'DRAFT' CHECK(status IN('DRAFT','REQUESTED','APPROVED','DISPATCHED','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')),
 requested_by INT NOT NULL,approved_by INT,dispatched_by INT,received_by INT,requested_at TIMESTAMPTZ NOT NULL DEFAULT now(),dispatched_at TIMESTAMPTZ,received_at TIMESTAMPTZ,remarks VARCHAR(2000),
 UNIQUE(tenant_id,transfer_id),UNIQUE(tenant_id,transfer_number),CHECK(from_location_id<>to_location_id),
 FOREIGN KEY(tenant_id,from_location_id) REFERENCES store_locations(tenant_id,location_id),FOREIGN KEY(tenant_id,to_location_id) REFERENCES store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,transit_location_id) REFERENCES store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,approved_by) REFERENCES users(tenant_id,user_id),
 FOREIGN KEY(tenant_id,dispatched_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,received_by) REFERENCES users(tenant_id,user_id));
CREATE TABLE stock_transfer_items(transfer_item_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL,transfer_id BIGINT NOT NULL,variant_id BIGINT NOT NULL,
 requested_quantity INT NOT NULL CHECK(requested_quantity BETWEEN 1 AND 10000),dispatched_quantity INT NOT NULL DEFAULT 0,
 received_quantity INT NOT NULL DEFAULT 0,damaged_quantity INT NOT NULL DEFAULT 0,lost_quantity INT NOT NULL DEFAULT 0,
 UNIQUE(tenant_id,transfer_item_id),UNIQUE(transfer_id,variant_id),
 FOREIGN KEY(tenant_id,transfer_id) REFERENCES stock_transfers(tenant_id,transfer_id),
 CHECK(dispatched_quantity BETWEEN 0 AND requested_quantity),CHECK(received_quantity>=0 AND damaged_quantity>=0 AND lost_quantity>=0 AND received_quantity+damaged_quantity+lost_quantity<=dispatched_quantity));
ALTER TABLE product_variants ADD UNIQUE(tenant_id,variant_id);
ALTER TABLE stock_transfer_items ADD FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id);
ALTER TABLE product_units ADD UNIQUE(tenant_id,product_unit_id);
CREATE TABLE stock_transfer_units(tenant_id BIGINT NOT NULL,transfer_item_id BIGINT NOT NULL,product_unit_id BIGINT NOT NULL,
 resolution VARCHAR(20) NOT NULL DEFAULT 'IN_TRANSIT' CHECK(resolution IN('IN_TRANSIT','RECEIVED','DAMAGED','LOST')),PRIMARY KEY(transfer_item_id,product_unit_id),
 FOREIGN KEY(tenant_id,transfer_item_id) REFERENCES stock_transfer_items(tenant_id,transfer_item_id),FOREIGN KEY(tenant_id,product_unit_id) REFERENCES product_units(tenant_id,product_unit_id));
CREATE TABLE stock_transfer_operations(tenant_id BIGINT NOT NULL,request_id UUID NOT NULL,transfer_id BIGINT NOT NULL,action_name TEXT NOT NULL,payload JSONB NOT NULL,
 created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),PRIMARY KEY(tenant_id,request_id),
 FOREIGN KEY(tenant_id,transfer_id) REFERENCES stock_transfers(tenant_id,transfer_id),FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id));
ALTER TABLE inventory_transactions ADD COLUMN transfer_id BIGINT;
ALTER TABLE inventory_transactions ADD FOREIGN KEY(tenant_id,transfer_id) REFERENCES stock_transfers(tenant_id,transfer_id);
CREATE INDEX ON stock_transfers(tenant_id,from_location_id,to_location_id,status);
CREATE INDEX ON inventory_transactions(transfer_id);
CREATE FUNCTION sf_transfer_permission(code text,action text) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT EXISTS(SELECT 1 FROM public.user_module_permissions p JOIN public.modules m USING(module_id) JOIN public.users u ON u.user_id=p.user_id AND u.tenant_id=p.tenant_id
 JOIN public.roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id JOIN public.tenants t ON t.tenant_id=u.tenant_id
 WHERE p.tenant_id=public.sf_tenant() AND p.user_id=public.sf_actor() AND u.is_active AND r.is_active AND t.is_active AND m.is_active AND m.module_code=code AND p.can_view
 AND CASE action WHEN 'CREATE' THEN p.can_create WHEN 'APPROVE' THEN p.can_approve WHEN 'VIEW' THEN TRUE ELSE FALSE END) $$;
CREATE FUNCTION sf_transfer_locations() RETURNS TABLE(location_id bigint,store_name text,location_name varchar,location_type varchar) LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT l.location_id,COALESCE(s.store_name::text,'Central warehouse'),l.location_name,l.location_type FROM public.store_locations l LEFT JOIN public.stores s ON s.store_id=l.store_id AND s.tenant_id=l.tenant_id
 WHERE l.tenant_id=public.sf_tenant() AND l.is_active AND l.location_type<>'TRANSIT' AND (l.store_id IS NULL OR s.is_active) AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW') ORDER BY l.location_id $$;
CREATE FUNCTION sf_stock(tid bigint,loc bigint,vid bigint) RETURNS bigint LANGUAGE sql STABLE AS $$
 SELECT COALESCE(SUM(CASE WHEN transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::bigint
 WHEN transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::bigint ELSE 0 END),0)::bigint
 FROM inventory_transactions WHERE tenant_id=tid AND location_id=loc AND variant_id=vid $$;
CREATE FUNCTION sf_create_transfer(source_id bigint,destination_id bigint,lines jsonb,notes text,retry uuid) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); actor int:=public.sf_actor(); id bigint; transit bigint; line jsonb; old public.stock_transfer_operations; body jsonb;
BEGIN
 IF NOT public.sf_transfer_permission('STOCK_TRANSFER','CREATE') THEN RAISE EXCEPTION 'Transfer creation is not permitted' USING ERRCODE='42501'; END IF;
 IF retry IS NULL OR retry='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'A request ID is required' USING ERRCODE='23514'; END IF;
 body:=jsonb_build_object('source',source_id,'destination',destination_id,'lines',lines,'notes',notes);
 PERFORM pg_advisory_xact_lock(hashtextextended(tid||':transfer:'||retry,0));
 SELECT * INTO old FROM public.stock_transfer_operations WHERE tenant_id=tid AND request_id=retry;
 IF FOUND THEN IF old.action_name<>'CREATE' OR old.payload<>body OR old.created_by<>actor THEN RAISE EXCEPTION 'Request ID was already used' USING ERRCODE='23514'; END IF; RETURN old.transfer_id; END IF;
 IF source_id=destination_id OR NOT (public.sf_can_location(source_id) OR public.sf_can_location(destination_id)) OR
 (SELECT count(*) FROM public.store_locations WHERE tenant_id=tid AND location_id IN(source_id,destination_id) AND is_active AND location_type<>'TRANSIT')<>2 THEN
 RAISE EXCEPTION 'Choose different active locations in your business and an assigned source or destination' USING ERRCODE='42501'; END IF;
 IF jsonb_typeof(lines)<>'array' OR jsonb_array_length(lines) NOT BETWEEN 1 AND 200 OR length(notes)>2000 THEN RAISE EXCEPTION 'Invalid transfer lines or notes' USING ERRCODE='23514'; END IF;
 INSERT INTO public.store_locations(tenant_id,location_name,location_type) VALUES(tid,'Transfer in transit','TRANSIT') RETURNING location_id INTO transit;
 id:=nextval(pg_get_serial_sequence('public.stock_transfers','transfer_id'));
 INSERT INTO public.stock_transfers(transfer_id,tenant_id,transfer_number,from_location_id,to_location_id,transit_location_id,requested_by,remarks)
 VALUES(id,tid,'TRF-'||id,source_id,destination_id,transit,actor,notes);
 UPDATE public.store_locations SET location_name='In transit / TRF-'||id WHERE location_id=transit;
 FOR line IN SELECT value FROM jsonb_array_elements(lines) LOOP
  IF NOT EXISTS(SELECT 1 FROM public.product_variants v JOIN public.products p ON p.product_id=v.product_id AND p.tenant_id=v.tenant_id WHERE v.tenant_id=tid AND v.variant_id=(line->>'variantId')::bigint AND v.is_active AND p.is_active) THEN RAISE EXCEPTION 'Invalid variant' USING ERRCODE='23514'; END IF;
  INSERT INTO public.stock_transfer_items(tenant_id,transfer_id,variant_id,requested_quantity) VALUES(tid,id,(line->>'variantId')::bigint,(line->>'quantity')::int);
 END LOOP;
 INSERT INTO public.stock_transfer_operations VALUES(tid,retry,id,'CREATE',body,actor,now());
 RETURN id;
END $$;
CREATE FUNCTION sf_transfer_action(id bigint,act text,body jsonb,retry uuid) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
<<transfer_action>>
DECLARE tid bigint:=public.sf_tenant(); actor int:=public.sf_actor(); h public.stock_transfers; old public.stock_transfer_operations;
 item record; line jsonb; unitid bigint; ids bigint[]; n int; good int; damage int; lost int; resolution text; dest bigint; notes text;
BEGIN
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
 IF act IN('approve','dispatch','cancel') AND NOT public.sf_can_location(h.from_location_id) THEN RAISE EXCEPTION 'Source location access is required' USING ERRCODE='42501'; END IF;
 IF act IN('receive','discrepancy') AND NOT public.sf_can_location(h.to_location_id) THEN RAISE EXCEPTION 'Destination access is required' USING ERRCODE='42501'; END IF;
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
ALTER TABLE stock_transfers ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_transfers FORCE ROW LEVEL SECURITY;
CREATE POLICY transfer_read ON stock_transfers FOR SELECT USING(tenant_id=sf_tenant() AND (sf_can_location(from_location_id) OR sf_can_location(to_location_id)));
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['stock_transfer_items','stock_transfer_operations'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('CREATE POLICY transfer_read ON %I FOR SELECT USING(tenant_id=sf_tenant() AND EXISTS(SELECT 1 FROM stock_transfers h WHERE h.transfer_id=%I.transfer_id))',tab,tab);
 END LOOP;
END $$;
ALTER TABLE stock_transfer_units ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_transfer_units FORCE ROW LEVEL SECURITY;
CREATE POLICY transfer_read ON stock_transfer_units FOR SELECT USING(tenant_id=sf_tenant() AND EXISTS(SELECT 1 FROM stock_transfer_items i WHERE i.transfer_item_id=stock_transfer_units.transfer_item_id));
GRANT SELECT ON stock_transfers,stock_transfer_items,stock_transfer_units,stock_transfer_operations TO stockflow_runtime;
CREATE FUNCTION sf_transfer_unit_details(id bigint) RETURNS TABLE(transfer_item_id bigint,product_unit_id bigint,serial_number varchar,resolution varchar)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT tu.transfer_item_id,tu.product_unit_id,u.serial_number,tu.resolution FROM public.stock_transfer_units tu
 JOIN public.stock_transfer_items i ON i.transfer_item_id=tu.transfer_item_id AND i.tenant_id=tu.tenant_id
 JOIN public.stock_transfers h ON h.transfer_id=i.transfer_id AND h.tenant_id=i.tenant_id
 JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id AND u.tenant_id=tu.tenant_id
 WHERE h.tenant_id=public.sf_tenant() AND h.transfer_id=id AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW')
 AND (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id)) $$;
REVOKE ALL ON FUNCTION sf_transfer_unit_details(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_transfer_unit_details(bigint) TO stockflow_runtime;
REVOKE ALL ON FUNCTION sf_transfer_permission(text,text),sf_transfer_locations(),sf_create_transfer(bigint,bigint,jsonb,text,uuid),sf_transfer_action(bigint,text,jsonb,uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_transfer_permission(text,text),sf_transfer_locations(),sf_create_transfer(bigint,bigint,jsonb,text,uuid),sf_transfer_action(bigint,text,jsonb,uuid) TO stockflow_runtime;
COMMIT;
