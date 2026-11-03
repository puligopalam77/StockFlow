-- MANUAL ONLY, after 070. Back up; stop writers; run the whole file as postgres.
-- No legacy backfill. Working tax snapshots preserve existing financial amounts.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run GST migration as PostgreSQL superuser'; END IF;
 IF to_regclass('public.gst_registrations') IS NULL THEN RAISE EXCEPTION 'Apply migration 070 first'; END IF;
END $$;
CREATE TABLE public.gst_documents(
 document_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 registration_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,store_id bigint NOT NULL,location_id bigint NOT NULL,
 store_name_snapshot varchar(150) NOT NULL DEFAULT '',location_name_snapshot varchar(150) NOT NULL DEFAULT '',
 direction varchar(10) NOT NULL CHECK(direction IN('SALES','PURCHASE')),
 kind varchar(15) NOT NULL CHECK(kind IN('INVOICE','CREDIT_NOTE','DEBIT_NOTE')),
 document_number varchar(100) NOT NULL,document_date date NOT NULL,party_name varchar(200) NOT NULL,party_gstin varchar(15),
 party_address text,party_state_code varchar(2),place_of_supply varchar(2) NOT NULL,delivery_address varchar(1000),
 registration_snapshot jsonb NOT NULL,business_snapshot jsonb NOT NULL,
 sale_id bigint,parent_document_id bigint,purchase_ids bigint[] NOT NULL DEFAULT '{}',
 evidence_reference varchar(1000),reason varchar(1000),
 taxable_value numeric(16,2) NOT NULL CHECK(taxable_value>=0),cgst numeric(16,2) NOT NULL CHECK(cgst>=0),
 sgst numeric(16,2) NOT NULL CHECK(sgst>=0),utgst numeric(16,2) NOT NULL CHECK(utgst>=0),igst numeric(16,2) NOT NULL CHECK(igst>=0),cess numeric(16,2) NOT NULL CHECK(cess>=0),
 invoice_discount numeric(16,2) NOT NULL DEFAULT 0 CHECK(invoice_discount>=0),total numeric(16,2) NOT NULL CHECK(total>=0),
 lines jsonb NOT NULL CHECK(jsonb_typeof(lines)='array' AND jsonb_array_length(lines) BETWEEN 1 AND 200),
 warnings text[] NOT NULL DEFAULT '{}',request_id uuid NOT NULL,request_payload jsonb NOT NULL,
 created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,document_id),UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,registration_id) REFERENCES public.gst_registrations(tenant_id,registration_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,parent_document_id) REFERENCES public.gst_documents(tenant_id,document_id),
 FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES public.sales(tenant_id,location_id,sale_id),
 CHECK((kind='INVOICE' AND parent_document_id IS NULL) OR (kind<>'INVOICE' AND parent_document_id IS NOT NULL)),
 CHECK(total=taxable_value+cgst+sgst+utgst+igst+cess-invoice_discount));
CREATE UNIQUE INDEX ux_gst_sale ON public.gst_documents(tenant_id,sale_id) WHERE sale_id IS NOT NULL;
-- Includes inactive/cancelled historical documents: identities are never recycled.
CREATE UNIQUE INDEX ux_gst_document_number ON public.gst_documents(tenant_id,registration_id,direction,kind,
 (CASE WHEN direction='PURCHASE' THEN coalesce(party_gstin,request_payload->>'supplierId','') ELSE '' END),
 upper(btrim(document_number)),(extract(year FROM document_date)-CASE WHEN extract(month FROM document_date)<4 THEN 1 ELSE 0 END));
CREATE INDEX ix_gst_document_report ON public.gst_documents(tenant_id,registration_id,document_date);
CREATE FUNCTION public.sf_gst_document_identity() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 SELECT s.store_name,l.location_name INTO NEW.store_name_snapshot,NEW.location_name_snapshot FROM public.stores s JOIN public.store_locations l USING(tenant_id,store_id)
 WHERE s.tenant_id=NEW.tenant_id AND s.store_id=NEW.store_id AND l.location_id=NEW.location_id;
 IF NOT FOUND THEN RAISE EXCEPTION 'Tax document shop and location do not agree' USING ERRCODE='23514'; END IF;
 IF NEW.parent_document_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.gst_documents p WHERE p.tenant_id=NEW.tenant_id AND p.document_id=NEW.parent_document_id
  AND p.registration_id=NEW.registration_id AND p.business_profile_code=NEW.business_profile_code AND p.store_id=NEW.store_id AND p.location_id=NEW.location_id
  AND p.direction=NEW.direction AND p.kind='INVOICE' AND p.document_date<=NEW.document_date) THEN
  RAISE EXCEPTION 'The original tax invoice must belong to the same registration and shop' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_document_identity() FROM PUBLIC;
CREATE TRIGGER gst_document_identity BEFORE INSERT ON public.gst_documents FOR EACH ROW EXECUTE FUNCTION public.sf_gst_document_identity();
CREATE TABLE public.gst_document_reviews(
 review_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),document_id bigint NOT NULL,
 status varchar(20) NOT NULL CHECK(status IN('PENDING_REVIEW','ACCOUNTANT_REVIEWED','NEEDS_CORRECTION')),
 notes varchar(1000) NOT NULL CHECK(length(btrim(notes))>=3),created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,document_id) REFERENCES public.gst_documents(tenant_id,document_id));
CREATE TABLE public.gst_export_history(
 export_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),registration_id bigint NOT NULL,
 report varchar(30) NOT NULL,from_date date NOT NULL,to_date date NOT NULL,scope_label text NOT NULL,
 filters jsonb NOT NULL,row_count integer NOT NULL,sha256 varchar(64) NOT NULL,created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),version integer NOT NULL DEFAULT 1,
 FOREIGN KEY(tenant_id,registration_id) REFERENCES public.gst_registrations(tenant_id,registration_id));
CREATE TABLE public.gst_document_events(
 event_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,document_id bigint NOT NULL,status varchar(30) NOT NULL,
 actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,document_id) REFERENCES public.gst_documents(tenant_id,document_id));
CREATE INDEX ix_gst_review_document ON public.gst_document_reviews(tenant_id,document_id,review_id DESC);
CREATE INDEX ix_gst_event_document ON public.gst_document_events(tenant_id,document_id,event_id DESC);
CREATE INDEX ix_gst_export_history ON public.gst_export_history(tenant_id,export_id DESC);
CREATE FUNCTION public.sf_gst_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 RAISE EXCEPTION 'Posted GST snapshots and review/export history are append-only; use a linked adjustment' USING ERRCODE='23514'; END $$;
CREATE TRIGGER gst_document_immutable BEFORE UPDATE OR DELETE ON public.gst_documents FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_review_immutable BEFORE UPDATE OR DELETE ON public.gst_document_reviews FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_export_immutable BEFORE UPDATE OR DELETE ON public.gst_export_history FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_audit_immutable BEFORE UPDATE OR DELETE ON public.gst_audit_history FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_event_immutable BEFORE UPDATE OR DELETE ON public.gst_document_events FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_document_audit AFTER INSERT ON public.gst_documents FOR EACH ROW EXECUTE FUNCTION public.sf_gst_audit('document_id');
CREATE TRIGGER gst_review_audit AFTER INSERT ON public.gst_document_reviews FOR EACH ROW EXECUTE FUNCTION public.sf_gst_audit('review_id');
CREATE TRIGGER gst_export_audit AFTER INSERT ON public.gst_export_history FOR EACH ROW EXECUTE FUNCTION public.sf_gst_audit('export_id');
CREATE FUNCTION public.sf_gst_source_event() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NEW.status IS DISTINCT FROM OLD.status THEN INSERT INTO public.gst_document_events(tenant_id,document_id,status,actor_id)
 SELECT tenant_id,document_id,NEW.status,public.sf_actor() FROM public.gst_documents WHERE tenant_id=NEW.tenant_id AND sale_id=NEW.sale_id; END IF;
 RETURN NEW; END $$;
CREATE TRIGGER gst_sale_status AFTER UPDATE OF status ON public.sales FOR EACH ROW EXECUTE FUNCTION public.sf_gst_source_event();
REVOKE ALL ON FUNCTION public.sf_gst_source_event() FROM PUBLIC;
ALTER TABLE public.gst_document_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_document_events FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_event_read ON public.gst_document_events FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.document_id=gst_document_events.document_id));
GRANT SELECT ON public.gst_document_events TO stockflow_runtime;
ALTER TABLE public.gst_documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_documents FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_document_read ON public.gst_documents FOR SELECT USING(tenant_id=public.sf_tenant()
 AND public.sf_gst_allowed('VIEW','GST_REPORTS') AND (public.sf_gst_allowed('VIEW_REGISTRATION','GST_REPORTS')
 OR (business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND public.sf_can_location(location_id))));
ALTER TABLE public.gst_document_reviews ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_document_reviews FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_review_read ON public.gst_document_reviews FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.document_id=gst_document_reviews.document_id));
CREATE POLICY gst_review_insert ON public.gst_document_reviews FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND created_by=public.sf_actor() AND public.sf_gst_allowed('REVIEW','GST_REPORTS') AND EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.document_id=gst_document_reviews.document_id));
ALTER TABLE public.gst_export_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_export_history FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_export_read ON public.gst_export_history FOR SELECT USING(tenant_id=public.sf_tenant() AND public.sf_gst_allowed('VIEW','GST_REPORTS') AND (created_by=public.sf_actor() OR public.sf_gst_allowed('VIEW_REGISTRATION','GST_REPORTS')));
CREATE POLICY gst_export_insert ON public.gst_export_history FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND created_by=public.sf_actor() AND public.sf_gst_allowed('EXPORT','GST_REPORTS') AND EXISTS(SELECT 1 FROM public.gst_registrations r WHERE r.registration_id=gst_export_history.registration_id));
GRANT SELECT ON public.gst_documents TO stockflow_runtime;
GRANT INSERT ON public.gst_documents TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.gst_documents_document_id_seq TO stockflow_runtime;
CREATE POLICY gst_document_insert ON public.gst_documents FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND created_by=public.sf_actor()
 AND public.sf_gst_allowed('CREATE','GST_REPORTS') AND sale_id IS NULL AND business_profile_code=public.sf_visible_business_profile()
 AND store_id=public.sf_store() AND location_id=public.sf_location() AND public.sf_can_location(location_id)
 AND EXISTS(SELECT 1 FROM public.gst_shop_mappings m WHERE m.tenant_id=gst_documents.tenant_id AND m.registration_id=gst_documents.registration_id
 AND m.business_profile_code=gst_documents.business_profile_code AND m.store_id=gst_documents.store_id AND m.status='ACTIVE'
 AND gst_documents.document_date BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity')));
GRANT SELECT,INSERT ON public.gst_document_reviews,public.gst_export_history TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.gst_document_reviews_review_id_seq,public.gst_export_history_export_id_seq TO stockflow_runtime;

-- Narrow POS context: no report permission and no cross-shop reads granted to a cashier.
CREATE FUNCTION public.sf_gst_sale_context() RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE r public.gst_registrations; BEGIN
 IF NOT public.sf_gst_allowed('VIEW','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Sales permission required' USING ERRCODE='42501'; END IF;
 SELECT g.* INTO r FROM public.gst_shop_mappings m JOIN public.gst_registrations g USING(tenant_id,registration_id)
 WHERE m.tenant_id=public.sf_tenant() AND m.business_profile_code=public.sf_visible_business_profile() AND m.store_id=public.sf_store()
 AND m.status='ACTIVE' AND g.status='ACTIVE' AND CURRENT_DATE BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity')
 AND CURRENT_DATE BETWEEN g.effective_from AND coalesce(g.effective_to,'infinity');
 IF NOT FOUND THEN
  IF EXISTS(SELECT 1 FROM public.gst_shop_mappings m WHERE m.tenant_id=public.sf_tenant() AND m.business_profile_code=public.sf_visible_business_profile() AND m.store_id=public.sf_store()) THEN
   RAISE EXCEPTION 'This shop has GST mapping history but no active registration for today; review its effective dates and status before billing' USING ERRCODE='23514'; END IF;
  RETURN jsonb_build_object('mapped',false);
 END IF;
 RETURN jsonb_build_object('mapped',true,'gstin',r.gstin,'registrationType',r.registration_type,'stateCode',r.state_code);
END $$;

CREATE FUNCTION public.sf_gst_capture_sale(id bigint,pos text) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE s public.sales; r public.gst_registrations; b public.gst_legal_businesses; item record; tax public.gst_tax_configurations;
 rows jsonb:='[]'; base numeric:=0; ct numeric:=0; st numeric:=0; ut numeric:=0; it numeric:=0; lt numeric; cg numeric; sg numeric; ig numeric; ug numeric; taxable numeric; result bigint;
 warn text[]:=ARRAY['WORKING_REGISTER_PRACTITIONER_REVIEW']; BEGIN
 IF NOT public.sf_gst_allowed('CREATE','SALES') THEN RAISE EXCEPTION 'Sales creation permission required' USING ERRCODE='42501'; END IF;
 SELECT * INTO STRICT s FROM public.sales WHERE tenant_id=public.sf_tenant() AND sale_id=id AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_can_location(location_id);
 SELECT document_id INTO result FROM public.gst_documents WHERE tenant_id=s.tenant_id AND sale_id=id; IF FOUND THEN RETURN result; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.sales WHERE tenant_id=s.tenant_id AND sale_id=id AND xmin=pg_current_xact_id()::xid) THEN
  RAISE EXCEPTION 'Legacy invoices cannot be reconstructed from current GST masters; capture only during original finalization' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock_shared(hashtextextended('gst-config:'||s.tenant_id::text,0));
 SELECT g.* INTO r FROM public.gst_shop_mappings m JOIN public.gst_registrations g USING(tenant_id,registration_id)
 WHERE m.tenant_id=s.tenant_id AND m.business_profile_code=s.business_profile_code AND m.store_id=s.store_id AND m.status='ACTIVE' AND g.status='ACTIVE'
 AND s.sale_date::date BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND s.sale_date::date BETWEEN g.effective_from AND coalesce(g.effective_to,'infinity');
 IF NOT FOUND THEN RETURN NULL; END IF;
 IF s.status<>'COMPLETED' THEN RAISE EXCEPTION 'Only completed invoices can enter GST registers' USING ERRCODE='23514'; END IF;
 IF s.customer_gst_snapshot IS NOT NULL AND NOT public.sf_gst_valid_gstin(upper(btrim(s.customer_gst_snapshot))) THEN RAISE EXCEPTION 'Recipient GSTIN format or checksum needs correction' USING ERRCODE='23514'; END IF;
 -- Rule 46(d)-(f), reviewed 2026-11-01. Never fabricate registered/high-value recipient particulars.
 IF r.registration_type='REGULAR' AND (nullif(btrim(s.customer_gst_snapshot),'') IS NOT NULL OR s.total_amount>=50000 OR coalesce((s.request_payload->>'GstRecipientDetailsRequested')::boolean,false))
 AND (nullif(btrim(s.customer_name_snapshot),'') IS NULL OR nullif(btrim(s.customer_address_snapshot),'') IS NULL OR nullif(btrim(s.request_payload->>'GstDeliveryAddress'),'') IS NULL) THEN
  RAISE EXCEPTION 'Select a named customer with billing address and enter the delivery address required for this tax invoice' USING ERRCODE='23514'; END IF;
 IF length(s.request_payload->>'GstDeliveryAddress')>1000 THEN RAISE EXCEPTION 'Delivery address is limited to 1,000 characters' USING ERRCODE='23514'; END IF;
 IF pos IS NULL OR pos !~ '^(0[1-9]|[12][0-9]|3[0-8])$' OR pos IN('25','28') THEN RAISE EXCEPTION 'Select the actual domestic place of supply before billing' USING ERRCODE='23514'; END IF;
 IF r.registration_type='OTHER' THEN RAISE EXCEPTION 'This registration scheme requires a separately verified issuance workflow' USING ERRCODE='23514'; END IF;
 IF s.invoice_number !~ '^[A-Za-z0-9/-]{1,16}$' THEN RAISE EXCEPTION 'GST invoice number must follow the configured statutory serial format' USING ERRCODE='23514'; END IF;
 SELECT * INTO STRICT b FROM public.gst_legal_businesses WHERE tenant_id=r.tenant_id AND business_id=r.business_id;
 FOR item IN SELECT i.*,p.hsn_code FROM public.sale_items i JOIN public.products p USING(tenant_id,product_id) WHERE i.tenant_id=s.tenant_id AND i.sale_id=id ORDER BY sale_item_id LOOP
  SELECT * INTO tax FROM public.gst_tax_configurations t WHERE t.tenant_id=s.tenant_id AND t.product_id=item.product_id AND t.business_profile_code=s.business_profile_code
   AND s.sale_date::date BETWEEN t.effective_from AND coalesce(t.effective_to,'infinity');
  IF NOT FOUND THEN RAISE EXCEPTION 'Review effective GST classification for product %',item.product_id USING ERRCODE='23514'; END IF;
  IF tax.classification<>'GOODS' OR tax.treatment='ZERO_RATED' OR tax.reverse_charge OR tax.cess_rate<>0 THEN
   RAISE EXCEPTION 'Service place-of-supply, zero-rated, reverse-charge and cess issuance require additional verified calculator support' USING ERRCODE='23514'; END IF;
  IF (CASE WHEN r.registration_type IN('UNREGISTERED','COMPOSITION') THEN 0 ELSE tax.tax_rate END)<>item.tax_percentage OR (tax.prices_include_tax IS NOT NULL AND tax.prices_include_tax IS DISTINCT FROM coalesce((s.request_payload->>'PricesIncludeTax')::boolean,false)) THEN
   RAISE EXCEPTION 'Product/bill tax settings differ from the reviewed GST version' USING ERRCODE='23514'; END IF;
  IF r.registration_type IN('UNREGISTERED','COMPOSITION') AND item.tax_percentage<>0 THEN RAISE EXCEPTION 'This registration cannot collect GST through this invoice' USING ERRCODE='23514'; END IF;
  taxable:=item.quantity*item.selling_price-item.discount_amount; lt:=item.line_total-taxable;
  IF lt<0 OR abs(lt-round(taxable*item.tax_percentage/100,2))>0.01 THEN RAISE EXCEPTION 'Stored line tax does not reconcile' USING ERRCODE='23514'; END IF;
  cg:=0;sg:=0;ug:=0;ig:=0;
  IF r.state_code<>pos THEN ig:=lt; ELSE cg:=round(lt/2,2); IF r.state_code IN('04','26','31','35','38') THEN ug:=lt-cg; ELSE sg:=lt-cg; END IF; END IF;
  base:=base+taxable;ct:=ct+cg;st:=st+sg;ut:=ut+ug;it:=it+ig;
  rows:=rows||jsonb_build_array(jsonb_build_object('sourceLineId',item.sale_item_id,'taxConfigId',tax.tax_config_id,'productId',item.product_id,'description',item.product_name_snapshot,'hsnSac',tax.hsn_sac,'uqc',tax.uqc,'classification',tax.classification,'treatment',tax.treatment,'quantity',item.quantity,'taxRate',item.tax_percentage,'taxableValue',taxable,'cgst',cg,'sgst',sg,'utgst',ug,'igst',ig,'cess',0,'total',item.line_total));
 END LOOP;
 IF s.customer_gst_snapshot IS NOT NULL AND EXISTS(SELECT 1 FROM jsonb_array_elements(rows) l WHERE l->>'treatment'='TAXABLE')
 AND EXISTS(SELECT 1 FROM jsonb_array_elements(rows) l WHERE l->>'treatment' IN('NIL','EXEMPT','NON_GST')) THEN
  RAISE EXCEPTION 'Mixed taxable and exempt B2B document issuance needs a separately reviewed document workflow' USING ERRCODE='23514'; END IF;
 IF s.discount_amount>0 THEN warn:=warn||ARRAY['AFTER_TAX_INVOICE_DISCOUNT_REVIEW']; END IF;
 IF s.customer_gst_snapshot IS NOT NULL THEN warn:=warn||ARRAY['RECIPIENT_GSTIN_PORTAL_STATUS_NOT_VERIFIED']; END IF;
 IF base<>s.subtotal OR ct+st+ut+it<>s.tax_amount THEN RAISE EXCEPTION 'Invoice GST totals do not reconcile with posted sale' USING ERRCODE='23514'; END IF;
 INSERT INTO public.gst_documents(tenant_id,registration_id,business_profile_code,store_id,location_id,direction,kind,document_number,document_date,party_name,party_gstin,party_address,party_state_code,place_of_supply,delivery_address,registration_snapshot,business_snapshot,sale_id,taxable_value,cgst,sgst,utgst,igst,cess,invoice_discount,total,lines,warnings,request_id,request_payload,created_by)
 VALUES(s.tenant_id,r.registration_id,s.business_profile_code,s.store_id,s.location_id,'SALES','INVOICE',s.invoice_number,s.sale_date::date,coalesce(s.customer_name_snapshot,'Walk-in Customer'),nullif(btrim(s.customer_gst_snapshot),''),s.customer_address_snapshot,left(s.customer_gst_snapshot,2),pos,nullif(btrim(s.request_payload->>'GstDeliveryAddress'),''),to_jsonb(r),to_jsonb(b),s.sale_id,base,ct,st,ut,it,0,s.discount_amount,s.total_amount,rows,warn,s.request_id,jsonb_build_object('source','POS','saleRequestId',s.request_id,'pricesIncludeTax',s.request_payload->'PricesIncludeTax'),s.created_by) RETURNING document_id INTO result;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_sale_context(),public.sf_gst_capture_sale(bigint,text),public.sf_gst_immutable() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_sale_context(),public.sf_gst_capture_sale(bigint,text) TO stockflow_runtime;
CREATE FUNCTION public.sf_gst_product_rate(id integer) RETURNS numeric LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE rate numeric; BEGIN
 IF NOT public.sf_gst_allowed('VIEW','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Sales permission required' USING ERRCODE='42501'; END IF;
 IF NOT coalesce((public.sf_gst_sale_context()->>'mapped')::boolean,false) THEN RETURN NULL; END IF;
 SELECT t.tax_rate INTO rate FROM public.gst_tax_configurations t JOIN public.products p USING(tenant_id,product_id)
 WHERE t.tenant_id=public.sf_tenant() AND t.product_id=id AND t.business_profile_code=public.sf_visible_business_profile()
 AND CURRENT_DATE BETWEEN t.effective_from AND coalesce(t.effective_to,'infinity');
 IF NOT FOUND THEN RAISE EXCEPTION 'Review effective GST classification for the selected product' USING ERRCODE='23514'; END IF;
 IF public.sf_gst_sale_context()->>'registrationType' IN('UNREGISTERED','COMPOSITION') THEN RETURN 0; END IF;
 RETURN rate;
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_product_rate(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_product_rate(integer) TO stockflow_runtime;

CREATE FUNCTION public.sf_gst_invoice(id bigint) RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT to_jsonb(d) FROM public.gst_documents d JOIN public.sales s USING(tenant_id,sale_id)
 WHERE d.tenant_id=public.sf_tenant() AND d.sale_id=id AND d.business_profile_code=public.sf_visible_business_profile()
 AND public.sf_visible_location(d.location_id) AND public.sf_gst_allowed('VIEW','INVOICES')
$$;
REVOKE ALL ON FUNCTION public.sf_gst_invoice(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_invoice(bigint) TO stockflow_runtime;

-- Ensure a mapped sale cannot commit without its transaction-bound tax snapshot.
CREATE FUNCTION public.sf_gst_sale_commit_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NEW.status='COMPLETED' AND EXISTS(SELECT 1 FROM public.gst_shop_mappings m
  WHERE m.tenant_id=NEW.tenant_id AND m.business_profile_code=NEW.business_profile_code AND m.store_id=NEW.store_id)
  AND NOT EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.tenant_id=NEW.tenant_id AND d.sale_id=NEW.sale_id) THEN
  RAISE EXCEPTION 'Mapped invoice needs a GST snapshot in the same transaction' USING ERRCODE='23514';
 END IF; RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER gst_sale_commit_guard AFTER INSERT ON public.sales DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_gst_sale_commit_guard();
REVOKE ALL ON FUNCTION public.sf_gst_sale_commit_guard() FROM PUBLIC;

-- Once documents exist, changing metadata must not reassign historical issuance.
CREATE FUNCTION public.sf_gst_used_config_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_TABLE_NAME='gst_registrations' AND EXISTS(SELECT 1 FROM public.gst_documents WHERE tenant_id=OLD.tenant_id AND registration_id=OLD.registration_id) THEN
  IF (to_jsonb(NEW)-ARRAY['status','effective_to','updated_at','authorized_contact']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','effective_to','updated_at','authorized_contact']) THEN
   RAISE EXCEPTION 'Used registration identity is permanent; create a new effective registration where legally appropriate' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.gst_documents WHERE tenant_id=OLD.tenant_id AND registration_id=OLD.registration_id AND document_date>coalesce(NEW.effective_to,'infinity')) THEN RAISE EXCEPTION 'Effective end excludes issued documents' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='gst_shop_mappings' AND EXISTS(SELECT 1 FROM public.gst_documents WHERE tenant_id=OLD.tenant_id AND registration_id=OLD.registration_id AND store_id=OLD.store_id AND business_profile_code=OLD.business_profile_code AND document_date BETWEEN OLD.effective_from AND coalesce(OLD.effective_to,'infinity')) THEN
  IF NEW.registration_id<>OLD.registration_id OR NEW.store_id<>OLD.store_id OR NEW.business_profile_code<>OLD.business_profile_code OR NEW.effective_from<>OLD.effective_from THEN RAISE EXCEPTION 'Used mapping identity cannot change' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.gst_documents WHERE tenant_id=OLD.tenant_id AND registration_id=OLD.registration_id AND store_id=OLD.store_id AND business_profile_code=OLD.business_profile_code AND document_date BETWEEN OLD.effective_from AND coalesce(OLD.effective_to,'infinity') AND document_date>coalesce(NEW.effective_to,'infinity')) THEN RAISE EXCEPTION 'Mapping end excludes issued documents' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER gst_used_registration BEFORE UPDATE ON public.gst_registrations FOR EACH ROW EXECUTE FUNCTION public.sf_gst_used_config_guard();
CREATE TRIGGER gst_used_mapping BEFORE UPDATE ON public.gst_shop_mappings FOR EACH ROW EXECUTE FUNCTION public.sf_gst_used_config_guard();
REVOKE ALL ON FUNCTION public.sf_gst_used_config_guard() FROM PUBLIC;
COMMIT;
