(() => {
    'use strict';
    const root = document.getElementById('eway-workspace'); if (!root) return;
    const $ = id => document.getElementById(id), api = Sales.api.bind(Sales);
    const uuid = () => crypto.randomUUID?.() || '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c => (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16));
    const node = (tag, value, cls) => { const n = document.createElement(tag); if (value != null) n.textContent = value; if (cls) n.className = cls; return n; };
    const error = e => { $('eway-message').textContent = e.message || String(e); $('eway-message').hidden = false; };
    const run = fn => Promise.resolve().then(fn).catch(error);
    const post = (url, body, method = 'POST') => api(url, { method, body: JSON.stringify(body) });
    const money = value => value == null ? 'Unknown' : new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value);
    const query = new URL(location.href).searchParams;
    let options;
    async function register() {
        let page = 1;
        for (const r of options.registrations || []) $('eway-registrationId').add(new Option(r.label, r.registrationId));
        async function load() {
            const p = new URLSearchParams({ page, size: 20 });
            for (const field of ['term', 'status', 'from', 'to', 'sourceType', 'registrationId']) if ($('eway-' + field).value) p.set(field, $('eway-' + field).value);
            const d = await api('/api/ewaybills?' + p); $('eway-rows').replaceChildren();
            for (const r of d.rows) {
                const tr = node('tr');
                for (const value of [r.externalEbn || 'Not Generated', r.sourceNumber + ' / ' + r.sourceDate, r.party, (r.fromState || 'Unknown') + ' → ' + (r.toState || 'Unknown') + ' / ' + r.mode, money(r.total), r.status === 'DRAFT' ? 'Draft — review required' : 'External — unverified', 'Not verified']) tr.append(node('td', value));
                const td = node('td'), a = node('a', 'View preparation', 'btn btn-sm btn-outline-secondary'); a.href = '/EWayBills/Prepare?id=' + r.documentId; td.append(a); tr.append(td); $('eway-rows').append(tr);
            }
            if (!d.rows.length) { const tr = node('tr'), td = node('td', 'No preparations match this shop and filters.'); td.colSpan = 8; tr.append(td); $('eway-rows').append(tr); }
            $('eway-page').textContent = `Page ${page} · ${d.total} preparations`; $('eway-prev').disabled = page === 1; $('eway-next').disabled = page * 20 >= d.total;
        }
        $('eway-filters').onsubmit = event => { event.preventDefault(); page = 1; run(load); };
        $('eway-prev').onclick = () => { page--; run(load); }; $('eway-next').onclick = () => { page++; run(load); };
        if ($('eway-usage')) {
            const today = new Date().toISOString().slice(0, 10); $('usage-to').value = today; $('usage-from').value = today.slice(0, 8) + '01';
            $('eway-usage').onclick = () => run(async () => { const d = await api('/api/ewaybills/usage?' + new URLSearchParams({ from: $('usage-from').value, to: $('usage-to').value })); $('eway-usage-result').textContent = JSON.stringify(d, null, 2); });
        }
        await load();
    }
    async function editor() {
        const form = $('eway-form'); let source, saved, selected, documentId = query.get('id') || uuid(), pending = null, busy = false, dirty = false;
        const fields = ['documentType', 'movementDocumentNumber', 'movementDocumentDate', 'movementReason', 'transactionType', 'mode', 'distanceKm', 'transporterName', 'transporterId', 'vehicle', 'transportDocument', 'transportDocumentDate', 'applicabilityReview', 'goodsEvidence'];
        const value = name => form.elements.namedItem(name);
        const blank = () => ({ documentType: 'INV', transactionType: 1, mode: 'ROAD', transferGoods: [] });
        const get = (object, key) => Object.entries(object || {}).find(([k]) => k.replaceAll('_', '').toLowerCase() === key.toLowerCase())?.[1] || '';
        function validation(data) { $('eway-validation').replaceChildren(); for (const issue of [...data.issues, ...data.warnings]) $('eway-validation').append(node('li', issue)); }
        function mode() { const m = value('mode').value; $('eway-vehicle-label').hidden = m !== 'ROAD'; $('eway-transport-doc-label').hidden = $('eway-transport-date-label').hidden = m === 'PART_A'; }
        function transferLine(line = {}) {
            const tr = node('tr');
            for (const name of ['description', 'hsnSac', 'quantity', 'uqc', 'taxableValue', 'cgst', 'sgst', 'utgst', 'igst', 'cess']) {
                const input = node('input', null, 'form-control'); input.dataset.field = name; input.setAttribute('aria-label', name);
                input.type = ['quantity', 'taxableValue', 'cgst', 'sgst', 'utgst', 'igst', 'cess'].includes(name) ? 'number' : 'text';
                if (input.type === 'number') { input.min = '0'; input.step = '0.01'; } else input.maxLength = name === 'description' ? 300 : 8;
                input.value = line[name] ?? (name === 'uqc' ? 'NOS' : ''); const td = node('td'); td.append(input); tr.append(td);
            }
            const td = node('td'), remove = node('button', 'Remove', 'btn btn-sm btn-outline-secondary'); remove.type = 'button'; remove.onclick = () => { tr.remove(); changed(); }; td.append(remove); tr.append(td); $('eway-transfer-lines').append(tr);
        }
        function read() {
            const p = Object.fromEntries(fields.map(name => [name, value(name).value.trim()]));
            p.transactionType = Number(p.transactionType); p.distanceKm = p.distanceKm === '' ? null : Number(p.distanceKm);
            for (const set of form.querySelectorAll('[data-party]')) p[set.dataset.party] = Object.fromEntries([...set.querySelectorAll('[data-field]')].map(input => [input.dataset.field, input.value.trim()]));
            p.transferGoods = [...$('eway-transfer-lines').rows].map(tr => {
                const line = Object.fromEntries([...tr.querySelectorAll('input')].map(input => [input.dataset.field, input.type === 'number' ? Number(input.value) : input.value.trim()]));
                return { ...line, classification: 'GOODS', treatment: 'TAXABLE', total: line.taxableValue + line.cgst + line.sgst + line.utgst + line.igst + line.cess };
            });
            return p;
        }
        function changed() { if (busy) return; dirty = true; $('eway-state').textContent = 'Unsaved changes — save before review, export or external evidence.'; controls(); }
        function controls() {
            const terminal = saved?.document.status === 'EXTERNAL_UNVERIFIED', canWrite = saved ? options.permissions.edit : options.permissions.prepare;
            for (const element of form.elements) if (!['eway-validate', 'eway-export', 'eway-print'].includes(element.id)) element.disabled = busy || !!pending || terminal || !canWrite;
            $('eway-save').hidden = terminal || !canWrite; $('eway-save').disabled = busy || !!pending && pending.action !== 'SAVE';
            for (const [id, allowed] of [['eway-validate', options.permissions.validate], ['eway-export', options.permissions.export], ['eway-print', options.permissions.export]]) { $(id).hidden = !saved || !allowed; $(id).disabled = busy || dirty || !!pending && pending.action !== id.replace('eway-', ''); }
            $('eway-external').hidden = !saved || terminal || !options.permissions.recordExternal;
            $('eway-record').disabled = busy || dirty || !!pending && pending.action !== 'external'; $('eway-ebn').disabled = $('eway-evidence').disabled = busy || !!pending;
            $('eway-source-section').hidden = !!source;
        }
        function display(p) {
            for (const field of fields) value(field).value = p[field] ?? '';
            for (const set of form.querySelectorAll('[data-party]')) for (const input of set.querySelectorAll('[data-field]')) input.value = p[set.dataset.party]?.[input.dataset.field] || '';
            $('eway-transfer-lines').replaceChildren(); for (const line of p.transferGoods || []) transferLine(line);
            $('eway-transfer-goods').hidden = source.type !== 'TRANSFER'; $('eway-goods').replaceChildren();
            if (source.type === 'GST') {
                const table = node('table', null, 'table'), header = node('tr'); for (const title of ['Description', 'HSN', 'Qty / UQC', 'Taxable value', 'GST / cess', 'Line total']) header.append(node('th', title)); table.append(header);
                for (const line of source.lines || []) { const tr = node('tr'); for (const v of [line.description, line.hsnSac, line.quantity + ' / ' + line.uqc, money(line.taxableValue), money(line.cgst + line.sgst + line.utgst + line.igst + line.cess), money(line.total)]) tr.append(node('td', v)); table.append(tr); } $('eway-goods').append(table);
            } else $('eway-goods').append(node('p', 'Transfer quantities are source-linked; copy goods, HSN and values only from the actual movement challan. Source stock is unchanged.'));
            $('eway-heading').textContent = `${source.type} ${source.number} · Source date ${source.date} · Value ${money(source.total)}`;
            form.hidden = false; mode(); dirty = false; controls();
        }
        function printSnapshot(d) {
            const target = $('eway-print-snapshot'), p = d.preparation; target.replaceChildren();
            target.append(node('h2', d.source.number + ' · ' + d.source.date), node('p', 'Saved revision ' + d.document.version + ' · ' + d.document.status));
            if (d.document.externalEbn) target.append(node('p', 'External EBN ' + d.document.externalEbn + ' — UNVERIFIED. ' + d.document.externalEvidence));
            for (const [name, title] of [['billFrom', 'Bill from'], ['dispatchFrom', 'Dispatch from'], ['billTo', 'Bill to'], ['shipTo', 'Ship to']]) {
                const party = p[name]; target.append(node('h3', title), node('p', [party.name, party.gstin, party.address, party.state, party.pin].filter(Boolean).join(' · ') || 'Missing actual details'));
            }
            const table = node('table', null, 'table'), header = node('tr');
            for (const title of ['Goods', 'HSN', 'Quantity / UQC', 'Taxable value', 'CGST', 'SGST', 'UTGST', 'IGST', 'Cess', 'Total']) header.append(node('th', title)); table.append(header);
            for (const l of (d.source.type === 'GST' ? d.source.lines : p.transferGoods)) {
                const tr = node('tr'); for (const value of [l.description, l.hsnSac, l.quantity + ' / ' + l.uqc, ...['taxableValue', 'cgst', 'sgst', 'utgst', 'igst', 'cess', 'total'].map(k => money(l[k]))]) tr.append(node('td', value)); table.append(tr);
            }
            target.append(table);
            for (const [title, value] of [['Actual movement document', p.documentType + ' ' + p.movementDocumentNumber + ' / ' + p.movementDocumentDate], ['Arrangement', p.transactionType], ['Movement reason', p.movementReason],
                ['Transport', p.mode], ['Distance km', p.distanceKm], ['Transporter', p.transporterName + ' ' + p.transporterId], ['Vehicle', p.vehicle], ['Transport document', p.transportDocument + ' / ' + p.transportDocumentDate],
                ['Challan goods evidence', p.goodsEvidence], ['Authorized applicability review', p.applicabilityReview]]) target.append(node('p', title + ': ' + (value ?? 'Unknown')));
            for (const issue of [...d.validation.issues, ...d.validation.warnings]) target.append(node('p', issue));
        }
        async function load() {
            saved = await api('/api/ewaybills/' + documentId); source = saved.source;
            printSnapshot(saved);
            selected = { type: saved.document.sourceType, id: saved.document.sourceId, registrationId: saved.document.registrationId };
            display(saved.preparation); validation(saved.validation);
            $('eway-state').textContent = saved.document.status === 'DRAFT' ? `Saved revision ${saved.document.version} · Not Generated · Applicability review required` : `External EBN ${saved.document.externalEbn} — unverified. Evidence: ${saved.document.externalEvidence}`;
            $('eway-history-section').hidden = false;
            const history = await api('/api/ewaybills/' + documentId + '/history'); $('eway-history').replaceChildren();
            for (const event of history) { const details = node('details'), summary = node('summary', `${event.action} · revision ${event.version} · actor ${event.actorId} · ${event.createdAt}`); details.append(summary, node('pre', JSON.stringify(JSON.parse(event.snapshotJson), null, 2))); $('eway-history').append(details); }
        }
        async function select(row) {
            selected = row; source = await api('/api/ewaybills/source?' + new URLSearchParams({ type: row.type, id: row.id, registrationId: row.registrationId }));
            const p = blank(), reg = source.registration || {}, business = source.business || {};
            p.documentType = source.type === 'TRANSFER' ? 'CHL' : 'INV';
            if (source.type === 'GST') { p.movementDocumentNumber = source.number; p.movementDocumentDate = source.date; }
            const issuer = { name: get(business, 'legalName'), gstin: get(reg, 'gstin'), address: get(reg, 'address'), state: get(reg, 'stateCode'), pin: get(business, 'pinCode') };
            const other = { name: source.partyName || '', gstin: source.partyGstin || '', address: source.partyAddress || '', state: source.partyState || '', pin: '' };
            if (source.direction === 'PURCHASE') { p.billFrom = other; p.billTo = issuer; } else { p.billFrom = issuer; p.billTo = other; }
            // Dispatch/ship-to remain explicit; a billing address does not prove physical movement.
            p.shipTo = { address: source.deliveryAddress || '' }; display(p); changed();
        }
        let sourcePage = 1;
        async function sources() {
            const p = new URLSearchParams({ term: $('eway-source-term').value, type: $('eway-source-type').value, page: sourcePage });
            for (const key of ['saleId', 'purchaseId']) if (query.get(key)) p.set(key, query.get(key));
            const result = await api('/api/ewaybills/sources?' + p); $('eway-sources').replaceChildren();
            for (const row of result.rows) { const button = node('button', row.type + ' · ' + row.number + ' · ' + row.date + ' · ' + row.party, 'btn btn-outline-secondary'); button.type = 'button'; button.onclick = () => run(() => select(row)); $('eway-sources').append(button); }
            if (!result.rows.length) $('eway-sources').append(node('p', 'No eligible recorded sources in this shop. For purchases, record the actual supplier GST invoice first. No separate delivery-challan service is available.'));
            $('eway-source-page').textContent = 'Page ' + sourcePage; $('eway-source-prev').disabled = sourcePage === 1; $('eway-source-next').disabled = !result.hasMore;
        }
        $('eway-source-search').onsubmit = event => { event.preventDefault(); sourcePage = 1; run(sources); };
        $('eway-source-prev').onclick = () => { sourcePage--; run(sources); }; $('eway-source-next').onclick = () => { sourcePage++; run(sources); };
        $('eway-source-type').value = query.get('sourceType') === 'TRANSFER' ? 'TRANSFER' : 'GST';
        for (const select of form.querySelectorAll('[data-field="state"]')) for (const [code, name] of Object.entries(options.states)) select.add(new Option(code + ' · ' + name, code));
        form.oninput = changed; form.onchange = changed; value('mode').addEventListener('change', mode);
        $('eway-add-line').onclick = () => { transferLine(); changed(); };
        async function submit(action) {
            if (busy) return;
            if (!pending) pending = { action, body: action === 'SAVE' ? { documentId, operationId: uuid(), expectedVersion: saved?.document.version || 0, sourceType: selected.type, sourceId: selected.id, registrationId: selected.registrationId, preparation: read() }
                : { operationId: uuid(), expectedVersion: saved.document.version, ...(action === 'external' ? { ebn: $('eway-ebn').value.trim(), evidence: $('eway-evidence').value.trim() } : {}) } };
            if (pending.action !== action) throw new Error('Retry the unresolved action first. Its saved identity must be preserved.');
            busy = true; controls();
            try {
                const base = '/api/ewaybills', body = pending.body;
                if (action === 'SAVE') {
                    await post(base, body, body.expectedVersion === 0 ? 'POST' : 'PUT');
                    query.set('id', documentId); history.replaceState(null, '', '?' + query);
                } else if (action === 'export' || action === 'print') {
                    const response = await fetch(base + '/' + documentId + '/export', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || '' }, body: JSON.stringify(body) });
                    if (!response.ok) { const e = new Error((await response.json()).message || 'Worksheet export failed.'); e.status = response.status; throw e; }
                    if (action === 'export') { const url = URL.createObjectURL(await response.blob()), a = node('a'); a.href = url; a.download = 'eway-preparation-' + documentId + '.csv'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
                } else await post(base + '/' + documentId + '/' + action, body);
                pending = null; await load();
            } catch (e) {
                if (e.status && e.status < 500) pending = null;
                else $('eway-state').textContent = 'Response unresolved. Retry the same action before leaving this tab; saved request identity is retained.';
                throw e;
            } finally { busy = false; controls(); }
        }
        form.onsubmit = event => { event.preventDefault(); run(() => submit('SAVE')); };
        $('eway-validate').onclick = () => run(() => submit('validate'));
        $('eway-export').onclick = () => run(() => submit('export'));
        $('eway-record').onclick = () => run(() => submit('external'));
        $('eway-print').onclick = () => run(async () => { await submit('print'); window.print(); });
        if (query.has('id')) await load(); else await sources();
    }
    run(async () => {
        if ($('eway-provider-status')) { const launcher=document.querySelector('.sf-help-open');if(launcher){const dock=node('div',null,'eway-provider-assistant-dock');root.append(dock);dock.append(launcher);} const d = await api('/api/ewaybills/provider'); $('eway-provider-status').textContent = d.name + ' · ' + d.status; return; }
        options = await api('/api/ewaybills/options');
        if ($('eway-form')) await editor(); else await register();
    });
})();
