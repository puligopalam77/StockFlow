// Manage staff accounts, role defaults, permissions, and change history.
(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const users = getElement('admin-users');
    const roles = getElement('admin-roles');
    const audit = getElement('admin-audit');
    const fields = ['canView', 'canCreate', 'canEdit', 'canDelete', 'canApprove', 'canExport'];
    let rows = [];
    let current = null;
    let resetUser = null;
    let offset = 0;let userPager,auditPager;
    let busy = false;
    const editable = () => {
        if (users) {
            return users.dataset.security === 'true';
        } else {
            return roles?.dataset.edit === 'true';
        }
    };
    const showMessage = (text, error = false) => {
        const value = getElement('admin-message');
        value.className = `alert ${error ? 'alert-danger' : 'alert-success'}`;
        value.textContent = text;
    };
    async function requestJson(path, method = 'GET', body) {
        const response = await fetch('/api/admin/' + path, {
            method,
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || ''
            },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        if (!response.ok) {
            let value;
            try {
                value = await response.json();
            } catch {}
            throw Error(
                value?.message ||
                    value?.title ||
                    (response.status === 401
                        ? 'Your session changed. Sign in again.'
                        : 'Request failed. Check your permissions and try again.')
            );
        }
        if (response.status === 204) {
            return null;
        } else {
            return response.json();
        }
    }
    async function runAction(action) {
        if (busy) {
            return;
        }
        busy = true;
        const root = users || roles || audit;
        if (root) {
            root.inert = true;
            root.setAttribute('aria-busy', 'true');
        }
        try {
            await action();
        } catch (error) {
            showMessage(error.message, true);
        } finally {
            busy = false;
            if (root) {
                root.inert = false;
                root.removeAttribute('aria-busy');
            }
        }
    }
    function cell(row, text) {
        const td = document.createElement('td');
        td.textContent = text ?? '';
        row.append(td);
        return td;
    }
    function button(parent, text, action) {
        const button2 = document.createElement('button');
        button2.type = 'button';
        button2.className = 'admin-row-action';
        button2.title = text;
        button2.setAttribute('aria-label', text);
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        const value = document.createElementNS(svg.namespaceURI, 'path');
        value.setAttribute(
            'd',
            text === 'Reset password'
                ? 'M14 3a7 7 0 0 0-6 10L2 19v3h4v-3h3l3-3A7 7 0 1 0 14 3Zm3 5h.01'
                : text === 'Reset authenticator'
                  ? 'M12 3 4 6v6c0 5 8 9 8 9s8-4 8-9V6l-8-3ZM15 10a4 4 0 1 0 1 4M15 7v4h-4'
                : text === 'Deactivate'
                  ? 'M4 7h16M9 7V4h6v3M6 7l1 14h10l1-14M10 10v7M14 10v7'
                  : text === 'Activate'
                    ? 'm5 12 4 4L19 6'
                    : 'm15 4 5 5M4 20l4-1L20 7a2 2 0 0 0-4-4L4 15v5Z'
        );
        svg.append(value);
        button2.append(svg);
        button2.onclick = () => {
            return runAction(action);
        };
        parent.append(button2);
    }
    function showUserView(mode) {
        getElement('user-list-view').hidden = mode !== 'list';
        getElement('admin-list-actions').hidden = mode !== 'list';
        getElement('admin-page-title').textContent =
            mode === 'list'
                ? 'Staff accounts & access'
                : mode === 'reset'
                  ? 'Reset Password'
                  : current
                    ? 'User Details'
                    : 'New User';
        getElement('breadcrumb-user-list').hidden = mode === 'list';
        getElement('breadcrumb-back').href =
            mode === 'list' ? document.querySelector('#app-breadcrumb ol a').href : '/Admin/Users';
        getElement('breadcrumb-current').textContent =
            mode === 'list'
                ? 'User list'
                : mode === 'reset'
                  ? 'Reset password'
                  : current
                    ? 'Edit user'
                    : 'Add staff account';
        if (mode === 'list') {
            getElement('user-editor').hidden = true;
            getElement('reset-form').hidden = true;
        }
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    }
    function matrix(data, remember = false) {
        rows = structuredClone(data);
        for (const value of rows) {
            value.actions ??=
                value.supportedActions?.filter((a) => {
                    if (a === 'VIEW') {
                        return value.canView;
                    } else {
                        if (a === 'CREATE') {
                            return value.canCreate;
                        } else {
                            if (a === 'DEACTIVATE') {
                                return value.canDelete;
                            } else {
                                if (a === 'EXPORT') {
                                    return value.canExport;
                                } else {
                                    if (a === 'APPROVE') {
                                        return value.canApprove;
                                    } else {
                                        return value.canEdit;
                                    }
                                }
                            }
                        }
                    }
                }) || [];
            for (const f of fields) {
                value[f] = false;
            }
        }
        if (remember) {
            original = structuredClone(rows);
        }
        if (users) return;
        permissionEditor ??= StockFlowPermissionEditor.bind(
            getElement('permission-matrix'),
            editable,
            (data) => {
                return (rows = data);
            },
            JSON.parse((users || roles).dataset.grants || '{}')
        );
        permissionEditor.set(rows);
    }
    let permissionEditor = null;
    let original = [];
    let roleOptions = [];
    document.querySelectorAll('[data-matrix="reset"]').forEach((value) => {
        value.onclick = () => {
            if (editable()) {
                matrix(original);
            }
        };
    });
    async function stores() {
        const data = await requestJson('store-options');
        const selected = current?.storeAccess || {
            storeIds: [],
            warehouseLocationIds: []
        };
        getElement('primary-store').replaceChildren(
            new Option('Choose assigned store', ''),
            ...data.stores.map((store) => {
                return new Option(store.storeName, store.storeId);
            })
        );
        for (const [target, items, key, idKey, nameKey] of [
            ['assigned-stores', data.stores, 'storeIds', 'storeId', 'storeName'],
            [
                'assigned-warehouses',
                data.warehouses,
                'warehouseLocationIds',
                'locationId',
                'locationName'
            ]
        ]) {
            getElement(target).replaceChildren();
            for (const item of items) {
                const label = document.createElement('label');
                label.className = 'col-md-4';
                const check = document.createElement('input');
                check.type = 'checkbox';
                check.value = item[idKey];
                check.checked = selected[key].includes(item[idKey]);
                check.disabled = users.dataset.stores !== 'true';
                check.dataset.name = item[nameKey];
                check.onchange = defaults;
                label.append(check, document.createTextNode(' ' + item[nameKey]));
                getElement(target).append(label);
            }
        }
        defaults(selected.defaultStoreId);
        getElement('primary-store').onchange = () => {
            for (const check of getElement('assigned-stores').querySelectorAll('input')) {
                check.checked = check.value === getElement('primary-store').value;
            }
            defaults(Number(getElement('primary-store').value));
        };
        getElement('default-store').onchange = () => {
            return defaults(Number(getElement('default-store').value));
        };
    }
    function defaults(preferred) {
        let value =
            typeof preferred === 'number' ? preferred : Number(getElement('default-store').value);
        const checked = [...getElement('assigned-stores').querySelectorAll('input:checked')];
        const warehouses =
            getElement('assigned-warehouses').querySelectorAll('input:checked').length;
        if (checked.length === 1) {
            value = Number(checked[0].value);
        }
        getElement('default-store').replaceChildren(new Option('Select default store', ''));
        for (const check of checked) {
            getElement('default-store').add(new Option(check.dataset.name, check.value));
        }
        getElement('default-store').value = String(value || '');
        getElement('default-store').required = checked.length > 0;
        getElement('primary-store').value = getElement('default-store').value;
        getElement('primary-store').disabled =
            users.dataset.stores !== 'true' || !!current?.isTenantOwner;
        getElement('default-store').disabled = users.dataset.stores !== 'true';
        getElement('primary-store').required = !current?.isTenantOwner && !warehouses;
        getElement('store-assignment-help').textContent = current?.isTenantOwner
            ? 'This owner retains access to all stores.'
            : checked.length > 1 || warehouses
              ? `${checked.length} stores and ${warehouses} warehouses assigned. Review additional access below. Staff use the default store.`
              : 'Staff sign in to the assigned store automatically. Role permissions control what they can do.';
    }
    function storePayload() {
        return {
            storeIds: [...getElement('assigned-stores').querySelectorAll('input:checked')].map(
                (value) => {
                    return Number(value.value);
                }
            ),
            warehouseLocationIds: [
                ...getElement('assigned-warehouses').querySelectorAll('input:checked')
            ].map((value) => {
                return Number(value.value);
            }),
            defaultStoreId: getElement('default-store').value
                ? Number(getElement('default-store').value)
                : null
        };
    }
    async function loadRoles() {
        const data = await requestJson('roles');
        roleOptions = data;
        getElement('role').replaceChildren(
            ...(users ? [new Option('Choose a role template', '')] : []),
            ...data
                .filter((value) => {
                    return value.isActive;
                })
                .map((value) => {
                    return new Option(value.roleName, value.roleId);
                })
        );
    }
    async function template() {
        matrix(
            await requestJson(
                getElement('role').value
                    ? `roles/${getElement('role').value}/permissions`
                    : 'modules'
            ),
            true
        );
        if (getElement('selected-role-help')) {
            getElement('selected-role-help').textContent = getElement('role').value
                ? 'The selected role template supplies account permissions.'
                : 'Choose a role template for account access.';
        }
        if (getElement('role-two-factor')) {
            getElement('role-two-factor').checked = !!roleOptions.find((value) => {
                return String(value.roleId) === getElement('role').value;
            })?.requiresTwoFactor;
        }
    }
    function payload() {
        return {
            fullName: getElement('full-name').value,
            username: getElement('username').value,
            mobileNumber: getElement('mobile').value,
            email: getElement('email').value,
            roleId: Number(getElement('role').value),
            isActive: getElement('active').checked,
            mustChangePassword: getElement('must-change').checked,
            expectedVersion: current?.securityVersion || 0,
            useRoleTemplate: true,
            storeAccess: storePayload(),
            permissions: rows,
            ...(!current
                ? {
                      password: getElement('password').value,
                      confirmPassword: getElement('confirm-password').value
                  }
                : {})
        };
    }
    async function edit(id) {
        current = id ? await requestJson('users/' + id) : null;
        await loadRoles();
        getElement('user-editor').reset();
        getElement('reset-form').hidden = true;
        getElement('user-editor').hidden = false;
        getElement('editor-title').textContent = current
            ? 'Edit User: ' + current.username
            : 'Add staff account';
        getElement('save-user').textContent = current ? 'Save Changes' : 'Create User';
        showUserView('editor');
        for (const [field, key] of [
            ['full-name', 'fullName'],
            ['username', 'username'],
            ['mobile', 'mobileNumber'],
            ['email', 'email']
        ]) {
            getElement(field).value = current?.[key] || '';
        }
        getElement('active').checked = current?.isActive ?? true;
        getElement('active').disabled = !!current && users.dataset.status !== 'true';
        getElement('must-change').checked = current?.mustChangePassword ?? true;
        if (current) {
            getElement('role').value = current.roleId;
        }
        getElement('initial-password').hidden = !!current;
        for (const id of ['password', 'confirm-password']) {
            getElement(id).required = !current;
        }
        getElement('save-user').hidden =
            !editable() ||
            (current ? users.dataset.edit !== 'true' : users.dataset.create !== 'true');
        if (current) {
            matrix(current.permissions, true);
            getElement('selected-role-help').textContent =
                'Existing user actions are loaded. Role templates do not overwrite them.';
        } else {
            await template();
        }
        await stores();
        getElement('user-editor').scrollIntoView({
            behavior: 'smooth'
        });
    }
    async function list(pagingRequest) {
        const response = await requestJson(
            `users?term=${encodeURIComponent(getElement('search-term').value.trim())}&offset=${offset}`
        );
        const data=userPager.capture(response,pagingRequest);if(!data)return;
        getElement('user-rows').replaceChildren();
        getElement('users-total').textContent = data.length;
        getElement('users-active').textContent = data.filter((value) => {
            return value.isActive;
        }).length;
        getElement('users-inactive').textContent = data.filter((value) => {
            return !value.isActive;
        }).length;
        getElement('users-roles').textContent = new Set(
            data.map((value) => {
                return value.roleId;
            })
        ).size;
        getElement('user-count').textContent = data.length
            ? `${offset + 1}–${offset + data.length} records shown${getElement('search-term').value ? ' · search results' : ''}`
            : 'No matching users';
        for (const [index, user] of data.entries()) {
            const tr = document.createElement('tr');
            cell(tr, offset + index + 1);
            const name = cell(tr, '');
            name.className = 'admin-user-name';
            const avatar = document.createElement('span');
            avatar.className = 'admin-user-avatar';
            avatar.textContent = (user.username || '?').slice(0, 1).toUpperCase();
            const label = document.createElement('strong');
            label.textContent = user.username;
            name.append(avatar, label);
            cell(tr, user.fullName);
            cell(tr, user.roleName);
            const status = document.createElement('span');
            status.className = 'admin-status ' + (user.isActive ? 'active' : 'inactive');
            status.textContent = user.isActive ? 'Active' : 'Inactive';
            cell(tr, '').append(status);
            cell(tr, user.email || '—');
            cell(tr, user.mobileNumber || '—');
            cell(
                tr,
                user.isTenantOwner ? 'All stores' : user.assignedStores || 'Warehouse / unassigned'
            );
            cell(
                tr,
                user.twoFactorEnabled
                    ? 'Enabled'
                    : user.twoFactorRequired
                      ? 'Required'
                      : 'Not configured'
            );
            cell(tr, user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : 'Never');
            const actions = cell(tr, '');
            actions.className = 'admin-row-actions';
            button(actions, 'View / edit', () => {
                return edit(user.userId);
            });
            if (users.dataset.reset === 'true') {
                button(actions, 'Reset password', async () => {
                    resetUser = await requestJson('users/' + user.userId);
                    getElement('user-editor').hidden = true;
                    getElement('reset-form').reset();
                    getElement('reset-title').textContent = 'Reset password: ' + user.username;
                    getElement('reset-form').hidden = false;
                    showUserView('reset');
                });
            }
            if (users.dataset.resetAuth === 'true') {
                button(actions, 'Reset authenticator', async () => {
                    const reason = prompt(
                        'Record how the user identity was verified (at least 10 characters):'
                    );
                    if (!reason) {
                        return;
                    }
                    if (
                        !await StockFlowDialog.confirm(
                            'I verified this user identity. Reset their authenticator and require enrollment again?'
                        )
                    ) {
                        return;
                    }
                    const code = prompt('Enter your own current authenticator code:');
                    if (!code) {
                        return;
                    }
                    await requestJson(`users/${user.userId}/reset-authenticator`, 'POST', {
                        reason,
                        identityVerified: true,
                        code,
                        expectedVersion: user.securityVersion
                    });
                    showMessage('Authenticator reset and sessions revoked.');
                    await list();
                });
            }
            if (users.dataset.status === 'true') {
                button(actions, user.isActive ? 'Deactivate' : 'Activate', async () => {
                    if (
                        !await StockFlowDialog.confirm(`${user.isActive ? 'Deactivate' : 'Activate'} ${user.username}?`)
                    ) {
                        return;
                    }
                    await requestJson(`users/${user.userId}/status`, 'PUT', {
                        isActive: !user.isActive,
                        expectedVersion: user.securityVersion
                    });
                    showMessage('User status saved.');
                    await list();
                });
            }
            getElement('user-rows').append(tr);
        }
        getElement('previous-users').disabled = offset === 0;
        getElement('next-users').disabled = data.length < 100;
        if (!data.length) {
            const tr = document.createElement('tr');
            cell(tr, 'No users found.').colSpan = 11;
            getElement('user-rows').append(tr);
        }
    }
    if (users) {
        getElement('clear-users').onclick = () => {
            getElement('search-term').value = '';
            offset = 0;
            runAction(list);
        };
        getElement('new-user')?.addEventListener('click', () => {
            return runAction(() => {
                return edit();
            });
        });
        getElement('user-search').onsubmit = (event) => {
            event.preventDefault();
            offset = 0;
            runAction(list);
        };
        getElement('previous-users').onclick = () => {
            offset = Math.max(0, offset - 100);
            runAction(list);
        };
        getElement('next-users').onclick = () => {
            offset += 100;
            runAction(list);
        };
        getElement('cancel-user').onclick = () => {
            getElement('user-editor').hidden = true;
            getElement('user-editor').reset();
            showUserView('list');
        };
        getElement('cancel-reset').onclick = () => {
            getElement('reset-form').hidden = true;
            getElement('reset-form').reset();
            showUserView('list');
        };
        getElement('role').onchange = () => runAction(template);
        getElement('user-editor').onsubmit = (event) => {
            event.preventDefault();
            runAction(async () => {
                await requestJson(
                    current ? 'users/' + current.userId : 'users',
                    current ? 'PUT' : 'POST',
                    payload()
                );
                window.location.assign('/Admin/Users');
            });
        };
        getElement('reset-form').onsubmit = (event) => {
            event.preventDefault();
            runAction(async () => {
                await requestJson(`users/${resetUser.userId}/reset-password`, 'POST', {
                    password: getElement('reset-password').value,
                    confirmPassword: getElement('reset-confirm').value,
                    mustChangePassword: getElement('reset-change').checked,
                    expectedVersion: resetUser.securityVersion
                });
                getElement('reset-form').reset();
                getElement('reset-form').hidden = true;
                showUserView('list');
                showMessage('Password reset. Existing sessions have ended.');
                await list();
            });
        };
        userPager=StockFlowPagination.adapt({host:'.admin-pagination',root:'#user-rows',legacy:{previous:'#previous-users',next:'#next-users',range:'#user-count'},getOffset:()=>offset,setOffset:value=>offset=value,load:list});list=userPager.load;
        runAction(list);
    }
    if (roles) {
        getElement('save-role-security').disabled = !editable();
        getElement('role-two-factor').disabled = !editable();
        getElement('create-role')?.addEventListener('click', () => {
            return runAction(async () => {
                const roleName = prompt('New role name (2–50 characters):');
                if (!roleName) {
                    return;
                }
                const requiresTwoFactor = await StockFlowDialog.confirm('Require an authenticator for this role?');
                const result = await requestJson('roles', 'POST', {
                    roleName,
                    requiresTwoFactor
                });
                await loadRoles();
                getElement('role').value = result.roleId;
                await template();
                showMessage(
                    'Role created with no permissions. Select its default actions and save.'
                );
            });
        });
        getElement('save-role-security').onclick = () => {
            return runAction(async () => {
                await requestJson('roles/' + getElement('role').value + '/security', 'PUT', {
                    requiresTwoFactor: getElement('role-two-factor').checked
                });
                showMessage('Role security policy saved. Affected users must sign in again.');
            });
        };
        getElement('role').onchange = () => {
            return runAction(template);
        };
        getElement('save-role')?.addEventListener('click', () => {
            return runAction(async () => {
                await requestJson(`roles/${getElement('role').value}/permissions`, 'PUT', rows);
                showMessage('Template saved. Existing users retain their individual permissions.');
            });
        });
        runAction(async () => {
            await loadRoles();
            await template();
        });
    }
    async function logs(pagingRequest) {
        const query = new URLSearchParams({
            offset
        });
        for (const [id, key] of [
            ['audit-action', 'action'],
            ['audit-user', 'userId'],
            ['audit-from', 'from'],
            ['audit-to', 'to']
        ]) {
            if (getElement(id).value) {
                query.set(key, getElement(id).value);
            }
        }
        const response = await requestJson('auditlogs?' + query);
        const data=auditPager.capture(response,pagingRequest);if(!data)return;
        getElement('audit-rows').replaceChildren();
        for (const a of data) {
            const tr = document.createElement('tr');
            cell(tr, new Date(a.createdAt).toLocaleString());
            cell(tr, a.username);
            cell(tr, `${a.moduleName} / ${a.actionName}`);
            cell(tr, a.recordId);
            const detail = document.createElement('details');
            const summary = document.createElement('summary');
            const pre = document.createElement('pre');
            summary.textContent = 'View changes';
            pre.textContent = `Before: ${a.oldValues || '—'}\nAfter: ${a.newValues || '—'}\n${a.reason || ''}`;
            pre.style.whiteSpace = 'pre-wrap';
            detail.append(summary, pre);
            cell(tr, '').append(detail);
            getElement('audit-rows').append(tr);
        }
        getElement('previous-audit').disabled = offset === 0;
        getElement('next-audit').disabled = data.length < 100;
    }
    if (audit) {
        getElement('audit-search').onsubmit = (event) => {
            event.preventDefault();
            offset = 0;
            runAction(logs);
        };
        getElement('previous-audit').onclick = () => {
            offset = Math.max(0, offset - 100);
            runAction(logs);
        };
        getElement('next-audit').onclick = () => {
            offset += 100;
            runAction(logs);
        };
        const footer=document.createElement('div');getElement('admin-audit').append(footer);
        auditPager=StockFlowPagination.adapt({host:footer,root:'#audit-rows',legacy:{previous:'#previous-audit',next:'#next-audit'},getOffset:()=>offset,setOffset:value=>offset=value,load:logs});logs=auditPager.load;
        runAction(logs);
    }
})();
