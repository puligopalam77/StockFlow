// Supplier directory only. Add/Edit/View and inline purchasing forms keep their existing editor.
window.StockFlowSupplierDirectory = (() => {
    'use strict';
    const node = (tag, text, className) => { const e = document.createElement(tag); if (text != null) e.textContent = text; if (className) e.className = className; return e; };
    function start() {
        const root = document.getElementById('master-data'), screen = window.StockFlowMasterScreens;
        if (!root || root.dataset.module !== 'suppliers' || !screen || screen.page || root.dataset.createOnly === 'true') return false;
        if (root.dataset.directoryReady) return true;
        root.dataset.directoryReady = 'true'; root.classList.add('supplier-directory');
        const search = document.getElementById('master-search'), status = document.getElementById('status-filter');
        const rows = document.getElementById('master-rows'), head = document.getElementById('master-head'), table = rows.closest('.table-responsive');
        const list = document.getElementById('master-list'), add = document.getElementById('add-record');
        const canEdit = root.dataset.canEdit === 'true', canStatus = root.dataset.canDisable === 'true';
        const key = ['stockflow.supplier-directory.v1', document.body.dataset.businessProfile, document.querySelector('meta[name=stockflow-location]')?.content].join('|');
        const params = new URLSearchParams(location.search); let saved = {}, timer, pending = new Set();
        try { saved = JSON.parse(sessionStorage.getItem(key) || '{}') || {}; } catch {}
        search.value = params.get('search') ?? saved.search ?? search.value;
        const storedStatus = params.get('status') ?? saved.status ?? status.value;
        status.replaceChildren(new Option('All Statuses', 'false'), new Option('Active', 'true'), new Option('Inactive', 'inactive'));
        status.value = ['false', 'true', 'inactive'].includes(storedStatus) ? storedStatus : 'false';
        search.maxLength = 200; search.placeholder = 'Search supplier name, code, mobile or GSTIN';
        document.querySelector('label[for=master-search]').textContent = 'Search';
        const heading = root.querySelector('.configuration-heading'), originalTitle = heading.querySelector('h2,h1'), title = node('div', null, 'supplier-directory-heading');
        const h1 = node('h1', 'Suppliers');
        title.append(h1, node('p', 'Manage suppliers, contact details and purchasing preferences.'));
        originalTitle.replaceWith(title);
        if (add) {
            add.classList.add('supplier-add'); add.removeAttribute('aria-haspopup'); add.removeAttribute('aria-controls');
            add.onclick = () => { persist(); screen.navigate('Create'); };
        }
        const filter = search.closest('.row'); filter.classList.add('supplier-filter-card');
        const reset = node('button', 'Reset Filters', 'btn btn-dark'); reset.type = 'button'; filter.append(reset);
        const empty = node('section', null, 'supplier-empty'); empty.hidden = true; empty.setAttribute('role', 'status'); table.after(empty);
        const host = node('div'); host.id = 'supplier-pager'; list.append(host);
        list.dataset.sfPaginationRemote = 'true';
        const header = node('tr'); for (const label of ['Supplier', 'Supplier Code', 'Type', 'Mobile', 'Status', 'Actions']) { const th = node('th', label); th.scope = 'col'; header.append(th); } head.replaceChildren(header);
        rows.closest('table').setAttribute('aria-label', 'Suppliers');
        rows.closest('table').setAttribute('role', 'table');
        let confirmation;
        const notify = (type, text) => StockFlowNotify.show(type, text, { key: 'supplier-directory' });
        async function request(url, options = {}) {
            const response = await fetch(url, { credentials: 'same-origin', ...options, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector('meta[name=csrf-token]')?.content || '' } });
            const body = response.status === 204 ? null : await response.json().catch(() => null);
            if (!response.ok) throw new Error(response.status === 403 ? 'You do not have permission for this supplier action.' : response.status === 401 ? 'Your session has expired. Sign in again.' : body?.message || 'Unable to load suppliers. Try again.');
            return body;
        }
        function persist() {
            const state = { search: search.value, status: status.value, page: pager.state.page, size: pager.state.size };
            try { sessionStorage.setItem(key, JSON.stringify(state)); } catch {}
            const url = new URL(location.href);
            for (const [name, value] of Object.entries({ search: search.value, status: status.value, page: state.page, size: state.size })) {
                if (value === '' || name === 'status' && value === 'false' || name === 'page' && value === 1 || name === 'size' && value === 10) url.searchParams.delete(name);
                else url.searchParams.set(name, value);
            }
            history.replaceState(history.state, '', url);
            reset.hidden = !search.value && status.value === 'false';
        }
        function href(action, id) { return '/Suppliers/' + action + '?' + new URLSearchParams({ id, returnTo: location.pathname.toLowerCase() === '/suppliers/index' ? '/Suppliers' + location.search : location.pathname + location.search }); }
        function icon(id) { const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'), use = document.createElementNS(svg.namespaceURI, 'use'); svg.classList.add('sf-icon'); svg.setAttribute('aria-hidden', 'true'); use.setAttribute('href', '/images/icons.svg#' + id); svg.append(use); return svg; }
        function action(label, glyph, link) {
            const e = node(link ? 'a' : 'button', null, 'btn btn-dark supplier-row-action');
            if (link) e.href = link; else e.type = 'button';
            e.setAttribute('aria-label', label); e.append(icon(glyph)); return e;
        }
        function typeLabel(value) {
            if (!value) return 'Not specified';
            return String(value).replace(/_/g, ' ').toLowerCase().replace(/\b\w/g, c => c.toUpperCase());
        }
        function emptyState(title, description, label, callback) {
            empty.replaceChildren(node('h2', title)); if (description) empty.append(node('p', description));
            if (callback) { const button = node('button', label, 'btn btn-dark' + (label === '+ Add Supplier' ? ' supplier-add' : '')); button.type = 'button'; button.onclick = callback; empty.append(button); }
            empty.hidden = false; table.hidden = true;
        }
        function render(records) {
            persist(); rows.replaceChildren(); table.hidden = !records.length; empty.hidden = !!records.length;
            if (!records.length) {
                if (search.value || status.value === 'true') emptyState('No suppliers found', 'Try another name, code or mobile number.', 'Reset Filters', resetFilters);
                else if (status.value === 'inactive') emptyState('No inactive suppliers found.', '', 'Reset Filters', resetFilters);
                else emptyState('No suppliers yet', 'Add your first supplier to start managing purchases.', '+ Add Supplier', add ? () => add.click() : null);
                return;
            }
            for (const record of records) {
                const row = node('tr'); row.dataset.recordId = record.supplierId; row.setAttribute('role', 'row');
                const cell = (label, content, className) => { const td = node('td', null, className); td.setAttribute('role', 'cell'); td.dataset.label = label; typeof content === 'string' ? td.textContent = content : td.append(content); row.append(td); return td; };
                const name = node('a', record.supplierName, 'supplier-name'); name.href = href('View', record.supplierId); name.title = record.supplierName; name.onclick = persist;
                cell('Supplier', name); cell('Supplier Code', record.supplierCode || '—', 'supplier-code'); cell('Type', typeLabel(record.supplierType)); cell('Mobile', record.mobileNumber || '—');
                const badge = node('span', record.isActive ? 'Active' : 'Inactive', 'supplier-status'); badge.dataset.active = record.isActive; cell('Status', badge);
                const actions = node('div', null, 'supplier-actions');
                const viewEdit = action(canEdit ? 'Edit Supplier' : 'View Supplier', canEdit ? 'edit' : 'eye', href(canEdit ? 'Edit' : 'View', record.supplierId));
                viewEdit.onclick = persist; actions.append(viewEdit);
                if (canStatus) {
                    const toggle = action(record.isActive ? 'Deactivate Supplier' : 'Activate Supplier', record.isActive ? 'power' : 'check');
                    toggle.dataset.statusAction = 'true';
                    toggle.disabled = pending.has(record.supplierId); toggle.onclick = () => changeStatus(record, toggle); actions.append(toggle);
                }
                cell('Actions', actions); rows.append(row);
            }
        }
        function confirm(record, submit) {
            if (confirmation) return Promise.resolve(false);
            const dialog = node('dialog', null, 'supplier-status-dialog'); dialog.tabIndex = -1; dialog.setAttribute('aria-labelledby', 'supplier-confirm-title'); dialog.setAttribute('aria-describedby', 'supplier-confirm-name supplier-confirm-copy');
            const title = node('h2', 'Deactivate Supplier?'); title.id = 'supplier-confirm-title';
            const header = node('div', null, 'supplier-dialog-header'), warning = icon('report-warning'), close = node('button', null, 'supplier-dialog-close');
            close.type = 'button'; close.setAttribute('aria-label', 'Close dialog'); header.append(warning, title, close);
            const name = node('p', record.supplierName, 'supplier-dialog-name'); name.id = 'supplier-confirm-name';
            const copy = node('p', "This supplier won't be available for new purchases. Existing purchase history and supplier mappings will remain unchanged."); copy.id = 'supplier-confirm-copy';
            const note = node('p', 'Editing or completing draft purchases requires an active supplier. Saved receipts follow the existing receiving rules.', 'supplier-dialog-note'); note.id = 'supplier-confirm-note';
            dialog.setAttribute('aria-describedby', 'supplier-confirm-name supplier-confirm-copy supplier-confirm-note');
            const error = node('p', null, 'supplier-dialog-error'); error.setAttribute('role', 'alert'); error.hidden = true;
            const buttons = node('div', null, 'configuration-dialog-actions'), cancel = node('button', 'Cancel', 'btn btn-dark'), proceed = node('button', 'Deactivate Supplier', 'btn btn-primary');
            cancel.type = proceed.type = 'button'; proceed.dataset.confirm = 'true'; buttons.append(cancel, proceed); dialog.append(header, name, copy, note, error, buttons); root.append(dialog); confirmation = dialog;
            return new Promise(resolve => {
                let saving = false;
                close.onclick = cancel.onclick = () => { if (!saving) dialog.close('cancel'); };
                dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
                dialog.addEventListener('keydown', event => {
                    if (event.key !== 'Tab') return;
                    const enabled = [close, cancel, proceed].filter(button => !button.disabled);
                    const target = event.shiftKey ? enabled.at(-1) : enabled[0];
                    if (!enabled.length || event.shiftKey && document.activeElement === enabled[0] || !event.shiftKey && document.activeElement === enabled.at(-1) || document.activeElement === dialog) {
                        event.preventDefault(); (target || dialog).focus();
                    }
                });
                proceed.onclick = async () => {
                    if (saving) return;
                    saving = true; error.hidden = true; dialog.setAttribute('aria-busy', 'true');
                    close.disabled = cancel.disabled = proceed.disabled = true; proceed.textContent = 'Deactivating…';
                    dialog.focus();
                    try { await submit(); dialog.close('confirm'); }
                    catch (failure) { notify('error', failure.message); error.textContent = 'Supplier remains active. Try again or cancel.'; error.hidden = false; }
                    finally {
                        saving = false; dialog.removeAttribute('aria-busy'); close.disabled = cancel.disabled = proceed.disabled = false;
                        proceed.textContent = 'Deactivate Supplier';
                        if (dialog.open) proceed.focus();
                    }
                };
                dialog.addEventListener('close', () => { const allowed = dialog.returnValue === 'confirm'; confirmation = null; dialog.remove(); resolve(allowed); }, { once: true });
                dialog.showModal(); cancel.focus();
            });
        }
        async function changeStatus(record, button) {
            if (pending.has(record.supplierId)) return; pending.add(record.supplierId); button.disabled = true;
            try {
                const submit = () => request('/api/suppliers/' + record.supplierId + '/status', { method: 'POST', body: JSON.stringify({ isActive: !record.isActive }) });
                if (record.isActive) { if (!await confirm(record, submit)) return; }
                else await submit();
                notify('success', record.isActive ? 'Supplier deactivated.' : 'Supplier activated.');
                await pager.reload();
                const replacement = rows.querySelector('[data-record-id="' + record.supplierId + '"] [data-status-action]');
                if (replacement) replacement.disabled = false;
                (replacement || search).focus();
            } catch (error) { notify('error', error.message); if (button.isConnected) button.focus(); }
            finally {
                pending.delete(record.supplierId);
                const current = rows.querySelector('[data-record-id="' + record.supplierId + '"] [data-status-action]');
                if (current) current.disabled = false;
                if (button.isConnected) { button.disabled = false; button.focus(); }
            }
        }
        const pager = StockFlowPagination.remote({
            host, root: list, label: 'suppliers', size: Number(params.get('size') || saved.size) || 10,
            load: ({ offset, limit, signal }) => {
                persist(); list.setAttribute('aria-busy', 'true');
                return request('/api/suppliers/page?' + new URLSearchParams({ term: search.value.trim(), status: status.value === 'true' ? 'active' : status.value === 'inactive' ? 'inactive' : 'all', offset, limit }), { signal });
            },
            render: records => { render(records); list.setAttribute('aria-busy', 'false'); },
            onError: error => { persist(); list.setAttribute('aria-busy', 'false'); rows.replaceChildren(); emptyState('Unable to load suppliers', error.message, 'Try again', () => pager.reload()); notify('error', error.message); }
        });
        const page = Number(params.get('page') || saved.page);
        if (Number.isSafeInteger(page) && page > 0 && page <= 21474836) pager.state.page = page;
        function resetFilters() { clearTimeout(timer); search.value = ''; status.value = 'false'; pager.reload({ reset: true }); }
        reset.onclick = resetFilters;
        search.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => pager.reload({ reset: true }), 300); });
        search.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.isComposing) { event.preventDefault(); clearTimeout(timer); pager.reload({ reset: true }); } });
        status.addEventListener('change', () => { clearTimeout(timer); pager.reload({ reset: true }); });
        async function savedNotice() {
            try {
                const item = JSON.parse(sessionStorage.getItem('stockflow.supplier-saved') || 'null'); sessionStorage.removeItem('stockflow.supplier-saved');
                if (!item?.id) return;
                const record = await request('/api/suppliers/' + item.id);
                notify('success', 'Supplier saved: ' + record.supplierCode);
                if (item.context) {
                    const link = StockFlowSupplierNavigation.editUrl(item.context.productId, item.context.variantId, item.context.from);
                    if (link) { const note = node('section', null, 'supplier-success'), back = node('a', 'Return to Product Edit', 'btn btn-dark'); back.href = link; note.append(node('p', record.supplierName), back, node('p', 'Select this supplier explicitly. No product mapping or planning policy has been changed.')); root.prepend(note); }
                }
            } catch (error) { notify('error', error.message); }
        }
        (async () => { await pager.reload(); await savedNotice(); root.dataset.screenReady = 'true'; })();
        return true;
    }
    return { start };
})();
