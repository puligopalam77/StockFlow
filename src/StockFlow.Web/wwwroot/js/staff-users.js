(() => {
    'use strict';
    const root = document.getElementById('admin-users');
    if (!root) return;
    const el = id => document.getElementById(id);
    el('admin-message').dataset.keepBanner = 'true';
    if (el('new-user')) el('new-user').disabled = true;
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    let options, current = null, resetUser, readOnly = false, busy = false, offset = 0, pager, baseline = '';
    const canWrite = () => root.dataset.security === 'true' && (current ? root.dataset.edit === 'true' : root.dataset.create === 'true');
    const message = (text, error = false) => { el('admin-message').textContent = text; el('admin-message').className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    async function api(path, method = 'GET', body) {
        const response = await fetch('/api/admin/' + path, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || '' }, body: body === undefined ? undefined : JSON.stringify(body) });
        if (response.status === 204) return null;
        let data; try { data = await response.json(); } catch {}
        if (!response.ok) throw Error(data?.message || Object.values(data?.errors || {}).flat().join(' ') || 'Unable to complete this action. Reload and try again.');
        return data;
    }
    async function run(work) {
        if (busy) return; busy = true; root.inert = true; root.setAttribute('aria-busy', 'true');
        try { await work(); } catch (e) { message(e.message, true); } finally { busy = false; root.inert = false; root.removeAttribute('aria-busy'); }
    }
    function show(mode) {
        el('user-list-view').hidden = mode !== 'list'; el('admin-list-actions').hidden = mode !== 'list';
        el('user-editor').hidden = mode !== 'editor'; el('reset-form').hidden = mode !== 'reset';
        el('admin-page-title').textContent = mode === 'list' ? 'Staff accounts & access' : mode === 'reset' ? 'Reset Password' : current ? (readOnly ? 'User Details' : 'Edit User') : 'New User';
        if (el('breadcrumb-current')) el('breadcrumb-current').textContent = el('admin-page-title').textContent;
        if (el('breadcrumb-user-list')) el('breadcrumb-user-list').hidden = mode === 'list';
        window.scrollTo({ top: 0 });
    }
    function roleOptions(selected) {
        const type = el('account-type').value;
        el('role').replaceChildren(new Option('Choose a role', ''), ...options.roles.filter(r => r.staffAccountType === type).map(r => new Option(r.roleName, r.roleId)));
        if (current && current.roleId === Number(selected) && ![...el('role').options].some(o => o.value === String(selected)))
            el('role').add(new Option(current.roleName + ' (existing assignment)', selected));
        el('role').value = selected ? String(selected) : '';
    }
    function checked(id) { return [...el(id).querySelectorAll('input:checked')]; }
    function access() { return { storeIds: checked('assigned-stores').map(c => Number(c.value)), warehouseLocationIds: checked('assigned-warehouses').map(c => Number(c.value)), defaultStoreId: Number(el('default-store').value) || null }; }
    function defaults(preferred) {
        const shops = checked('assigned-stores'), prior = Number(preferred) || Number(el('default-store').value);
        el('default-store').replaceChildren(new Option('Choose default shop', ''), ...shops.map(c => new Option(c.dataset.name, c.value)));
        el('default-store').value = shops.length === 1 ? shops[0].value : String(prior || '');
        el('default-store').required = !readOnly && shops.length > 0;
        el('multi-store-default').hidden = shops.length <= 1;
        el('primary-store').value = el('default-store').value;
        el('primary-store').required = !current?.isTenantOwner;
        el('store-assignment-help').textContent = current?.isTenantOwner ? 'Business ownership retains authorized business-wide access.' : shops.length > 1 ? `${shops.length} shops assigned; staff sign in to the default shop.` : 'Staff sign in to the assigned shop automatically.';
        summary();
    }
    function shops() {
        const assigned = current?.storeAccess || { storeIds: [], warehouseLocationIds: [] };
        el('primary-store').replaceChildren(new Option('Choose assigned shop', ''), ...options.stores.map(s => new Option(s.storeName, s.storeId)));
        for (const [id, items, key, valueKey, nameKey] of [['assigned-stores', options.stores, 'storeIds', 'storeId', 'storeName'], ['assigned-warehouses', options.warehouses, 'warehouseLocationIds', 'locationId', 'locationName']]) {
            el(id).replaceChildren();
            for (const item of items) {
                const label = node('label', null, 'col-md-4 staff-access-choice'), input = node('input'); input.type = 'checkbox'; input.value = item[valueKey]; input.dataset.name = item[nameKey];
                input.checked = assigned[key].includes(item[valueKey]); input.disabled = readOnly || !options.canUseAdvancedAccess;
                input.onchange = () => defaults(); label.append(input, document.createTextNode(' ' + item[nameKey])); el(id).append(label);
            }
        }
        el('advanced-store-access').hidden = !options.canUseAdvancedAccess; el('advanced-store-access').open = false;
        defaults(assigned.defaultStoreId);
        el('primary-store').disabled = readOnly || root.dataset.stores !== 'true' || !!current?.isTenantOwner;
    }
    function summary() {
        const shop = el('primary-store').selectedOptions[0], role = el('role').selectedOptions[0];
        const permissions = current && Number(el('role').value) === current.roleId ? current.permissions : selectedPermissions;
        el('staff-access-summary').textContent = !el('role').value || !el('primary-store').value && !current?.isTenantOwner ? 'Choose a shop and role to preview access.' : `${current?.isTenantOwner ? 'Business owner' : shop.textContent} · ${role.textContent} · ${(permissions || []).reduce((n, p) => n + (p.actions?.length || 0), 0)} actions. Shop and owner restrictions apply.`;
        el('staff-permission-summary').replaceChildren();
        el('staff-permission-highlights').replaceChildren();
        el('staff-permission-summary').closest('details').hidden = !(permissions || []).some(p => p.actions?.length);
        for (const permission of permissions || []) if (permission.actions?.length) {
            const row = node('p'); row.append(node('strong', permission.moduleName + ': '), document.createTextNode(permission.actions.map(actionName).join(', '))); el('staff-permission-summary').append(row);
            if (el('staff-permission-highlights').childElementCount < 6) el('staff-permission-highlights').append(node('span', permission.moduleName, 'staff-permission-chip'));
        }
    }
    const actionName = code => code === 'VIEW' ? 'View' : window.StockFlowPermissionEditor?.actionNames[code] || code.toLowerCase().replaceAll('_', ' ').replace(/^./, letter => letter.toUpperCase());
    let selectedPermissions = [];
    async function preview() {
        selectedPermissions = el('role').value ? await api(`roles/${el('role').value}/permissions`) : [];
        el('selected-role-help').textContent = current && !current.usesRolePermissions && current.roleId === Number(el('role').value) ? 'This existing account retains its individual permissions. Changing its role links it to that role.' : 'Permissions follow the assigned role automatically. Future role changes apply to this account.';
        summary();
    }
    function payload() {
        return { fullName: el('full-name').value, username: el('username').value, mobileNumber: el('mobile').value, email: el('email').value, accountType: el('account-type').value, roleId: Number(el('role').value), isActive: el('active').checked, mustChangePassword: el('must-change').checked, expectedVersion: current?.securityVersion || 0, useRoleTemplate: true, permissions: [], storeAccess: access(), ...(!current ? { password: el('password').value, confirmPassword: el('confirm-password').value } : {}) };
    }
    async function edit(id, view = false) {
        current = id ? await api('users/' + id) : null; readOnly = view || !canWrite();
        el('user-editor').reset();
        for (const input of el('user-editor').querySelectorAll('input,select,button')) input.disabled = false;
        el('business-name').value = options.businessName;
        for (const [id, key] of [['full-name', 'fullName'], ['username', 'username'], ['mobile', 'mobileNumber'], ['email', 'email']]) el(id).value = current?.[key] || '';
        el('account-type').value = current?.accountType || 'EMPLOYEE';
        el('account-type').querySelector('[value="SHOP_ADMIN"]').disabled = !options.canCreateShopAdmin;
        el('account-type').disabled = readOnly || !!current?.isTenantOwner;
        roleOptions(current?.roleId); el('active').checked = current?.isActive ?? true; el('must-change').checked = current?.mustChangePassword ?? true;
        el('initial-password').hidden = !!current;
        for (const id of ['password', 'confirm-password']) el(id).required = !current && !readOnly;
        el('editor-title').textContent = current ? (readOnly ? 'View staff account: ' : 'Edit staff account: ') + current.username : 'Add staff account';
        el('save-user').textContent = current ? 'Save Changes' : 'Create User'; el('save-user').hidden = readOnly;
        usernameEdited = !!current; suggestionSequence++; el('username-help').textContent = current ? 'Existing username; change it only if needed.' : 'Suggested from the staff name; you can change it.';
        statusLabel();
        selectedPermissions = []; shops(); await preview();
        if (readOnly) for (const field of el('user-editor').querySelectorAll('input,select')) field.disabled = true;
        el('active').disabled = readOnly || !!current && root.dataset.status !== 'true';
        show('editor'); baseline = JSON.stringify(payload());
    }
    function action(parent, label, symbol, work) {
        const button = node('button', null, 'admin-row-action'); button.type = 'button'; button.title = label; button.setAttribute('aria-label', label);
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'), use = document.createElementNS(svg.namespaceURI, 'use');
        use.setAttribute('href', '/images/icons.svg#' + symbol); svg.setAttribute('aria-hidden', 'true'); svg.append(use); button.append(svg); button.onclick = () => run(work); parent.append(button);
    }
    async function list(request) {
        const query = new URLSearchParams({ term: el('search-term').value.trim(), offset });
        for (const [id, key] of [['filter-shop', 'storeId'], ['filter-account-type', 'accountType'], ['filter-role', 'roleId'], ['filter-status', 'isActive']]) if (el(id).value) query.set(key, el(id).value);
        const data = pager.capture(await api('users?' + query), request); if (!data) return;
        el('user-rows').replaceChildren();
        for (const user of data) {
            const row = node('tr'), identity = node('td'); identity.append(node('strong', user.fullName), node('small', [user.username, user.mobileNumber, user.email].filter(Boolean).join(' · ')));
            const accessDetails = node('details', null, 'staff-list-access'), accessBody = node('div');
            accessDetails.append(node('summary', 'View access'), accessBody);
            let accessLoaded = false;
            accessDetails.ontoggle = async () => {
                if (!accessDetails.open || accessLoaded) return; accessBody.textContent = 'Loading access...';
                try { const detail = await api('users/' + user.userId); accessBody.replaceChildren();
                    for (const permission of detail.permissions || []) if (permission.actions?.length) accessBody.append(node('p', permission.moduleName + ': ' + permission.actions.map(actionName).join(', ')));
                    if (!accessBody.childElementCount) accessBody.textContent = 'No module permissions assigned.';
                    accessLoaded = true;
                } catch (error) { accessBody.textContent = error.message; }
            };
            identity.append(accessDetails);
            const status = node('td'); status.append(node('span', user.isActive ? 'Active' : 'Inactive', 'admin-status ' + (user.isActive ? 'active' : 'inactive')));
            const actions = node('td', null, 'admin-row-actions');
            action(actions, 'View ' + user.username, 'eye', () => edit(user.userId, true));
            if (root.dataset.edit === 'true' && root.dataset.security === 'true') action(actions, 'Edit ' + user.username, 'document', () => edit(user.userId));
            if (root.dataset.reset === 'true') action(actions, 'Reset password', 'key', async () => { resetUser = await api('users/' + user.userId); el('reset-form').reset(); el('reset-title').textContent = 'Reset password: ' + user.username; show('reset'); });
            if (root.dataset.resetAuth === 'true') action(actions, 'Reset authenticator', 'shield', async () => {
                const reason = prompt('Record how the user identity was verified (at least 10 characters):'); if (!reason) return;
                if (!await StockFlowDialog.confirm('I verified this user identity. Reset their authenticator and require enrollment again?')) return;
                const code = prompt('Enter your own current authenticator code:'); if (!code) return;
                await api(`users/${user.userId}/reset-authenticator`, 'POST', { reason, identityVerified: true, code, expectedVersion: user.securityVersion }); message('Authenticator reset and sessions revoked.'); await list();
            });
            if (root.dataset.status === 'true') action(actions, user.isActive ? 'Deactivate' : 'Activate', 'shield', async () => {
                if (!await StockFlowDialog.confirm(`${user.isActive ? 'Deactivate' : 'Activate'} ${user.username}?`)) return;
                await api(`users/${user.userId}/status`, 'PUT', { isActive: !user.isActive, expectedVersion: user.securityVersion }); message('User status saved. Existing sessions have ended.'); await list();
            });
            row.append(identity, node('td', user.isTenantOwner ? 'Main Owner' : user.accountType === 'SHOP_ADMIN' ? 'Shop Admin' : 'Employee'), node('td', user.isTenantOwner ? 'All authorized shops' : user.assignedStores || 'Warehouse / unassigned'), node('td', user.roleName), status, actions); el('user-rows').append(row);
        }
        if (!data.length) { const row = node('tr'), td = node('td', 'No matching users'); td.colSpan = 6; row.append(td); el('user-rows').append(row); }
        el('user-count').textContent = data.length ? `${offset + 1}–${offset + data.length} records shown` : 'No matching users';
        el('previous-users').disabled = offset === 0; el('next-users').disabled = data.length < 100;
    }
    el('primary-store').onchange = () => {
        for (const input of el('assigned-stores').querySelectorAll('input')) input.checked = input.value === el('primary-store').value;
        for (const input of el('assigned-warehouses').querySelectorAll('input')) input.checked = false;
        defaults(el('primary-store').value);
    };
    el('default-store').onchange = () => defaults(el('default-store').value);
    let usernameEdited = false, suggestionSequence = 0;
    el('username').oninput = () => { usernameEdited = !!el('username').value; suggestionSequence++; el('username-help').textContent = 'Username must be unique within this business.'; };
    el('full-name').onblur = async () => {
        if (current || readOnly || usernameEdited || el('full-name').value.trim().length < 2) return;
        const sequence = ++suggestionSequence, name = el('full-name').value.trim();
        try {
            const data = await api('users/username-suggestion?fullName=' + encodeURIComponent(name));
            if (sequence !== suggestionSequence || current || usernameEdited || name !== el('full-name').value.trim()) return;
            el('username').value = data.username; el('username-help').textContent = 'Suggested username; you can change it. Availability is rechecked when saved.';
        } catch { if (sequence === suggestionSequence) el('username-help').textContent = 'Enter a unique username for this staff account.'; }
    };
    function statusLabel() { el('staff-status-label').textContent = el('active').checked ? 'Active' : 'Inactive'; }
    el('active').onchange = statusLabel;
    el('account-type').onchange = () => run(async () => { roleOptions(); await preview(); });
    el('role').onchange = () => run(preview);
    el('new-user')?.addEventListener('click', () => run(() => edit()));
    el('cancel-user').onclick = () => run(async () => { if (!readOnly && JSON.stringify(payload()) !== baseline && !await StockFlowDialog.discard('Discard unsaved staff account changes?')) return; el('user-editor').reset(); show('list'); });
    el('cancel-reset').onclick = () => { el('reset-form').reset(); show('list'); };
    el('user-editor').onsubmit = event => {
        event.preventDefault(); if (readOnly || !canWrite() || !event.target.reportValidity()) return;
        run(async () => { await api(current ? 'users/' + current.userId : 'users', current ? 'PUT' : 'POST', payload()); el('user-editor').reset(); show('list'); message('Staff account saved. Security changes end affected sessions.'); await pager.load(); });
    };
    el('reset-form').onsubmit = event => { event.preventDefault(); run(async () => {
        await api(`users/${resetUser.userId}/reset-password`, 'POST', { password: el('reset-password').value, confirmPassword: el('reset-confirm').value, mustChangePassword: true, expectedVersion: resetUser.securityVersion });
        el('reset-form').reset(); show('list'); message('Password reset. Existing sessions have ended.'); await pager.load();
    }); };
    const filterIds = ['filter-shop', 'filter-account-type', 'filter-role', 'filter-status'];
    let searchTimer;
    function filterState() {
        const count = filterIds.filter(id => el(id).value).length;
        el('user-filter-count').textContent = ' (' + count + ')'; el('user-filter-count').hidden = count === 0;
        el('clear-users').hidden = !count && !el('search-term').value;
    }
    function search() {
        clearTimeout(searchTimer); offset = 0; filterState();
        if (options && !el('user-list-view').hidden) return pager.load();
    }
    el('search-term').oninput = () => { filterState(); clearTimeout(searchTimer); searchTimer = setTimeout(search, 300); };
    el('user-search').onsubmit = event => { event.preventDefault(); search(); };
    el('toggle-user-filters').onclick = () => {
        el('user-filter-panel').hidden = !el('user-filter-panel').hidden;
        el('toggle-user-filters').setAttribute('aria-expanded', String(!el('user-filter-panel').hidden));
    };
    el('clear-users').onclick = () => { el('user-search').reset(); search(); };
    for (const id of filterIds) el(id).onchange = search;
    pager = StockFlowPagination.adapt({ host: '.admin-pagination', root: '#user-rows', legacy: { previous: '#previous-users', next: '#next-users', range: '#user-count' }, getOffset: () => offset, setOffset: value => offset = value, load: list });
    run(async () => {
        options = await api('users/registration-options');
        for (const role of options.roles) role.staffAccountType ||= 'EMPLOYEE';
        for (const shop of options.stores) el('filter-shop').add(new Option(shop.storeName, shop.storeId));
        for (const role of options.roles) el('filter-role').add(new Option(role.roleName, role.roleId));
        if (el('new-user')) { el('new-user').hidden = !options.canManageStaff; el('new-user').disabled = false; }
        await pager.load();
    });
})();
