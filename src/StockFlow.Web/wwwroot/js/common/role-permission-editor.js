// Presentation only: every screen points to an existing module/action permission.
// The posted catalog remains one row per permission module, including unseen grants.
window.StockFlowRolePermissionEditor = (() => {
    'use strict';
    const screen = (code, group, title, route, subgroup = '', actions = null) =>
        ({ code, group, title, route, subgroup, actions });
    const screens = [
        screen('DASHBOARD', 'Home', 'Home', '/'),
        screen('STOCK_INFLOW', 'Purchases', 'Receive Stock', '/StockInflow'),
        screen('PURCHASES', 'Purchases', 'Purchase Orders', '/Purchases'),
        screen('SUPPLIERS', 'Purchases', 'Suppliers', '/Suppliers'),
        screen('SALES', 'Sales', 'New Sale', '/Sales/Create', '', ['VIEW', 'CREATE']),
        screen('SALES', 'Sales', 'Sales History', '/Sales'),
        screen('INVOICES', 'Sales', 'Invoices', null),
        screen('QUOTATIONS', 'Sales', 'Quotations', '/Quotations'),
        screen('EWAY_BILLS', 'Sales', 'E-Way Bills', '/EWayBills'),
        screen('INVENTORY', 'Inventory', 'Stock Overview', '/Inventory'),
        screen('INVENTORY', 'Inventory', 'Low Stock', '/Inventory/LowStock', '', ['VIEW']),
        screen('STOCK_ADJUSTMENT', 'Inventory', 'Stock Adjustments / Opening Stock', null, 'Stock Overview'),
        screen('REPLENISHMENT', 'Inventory', 'Replenishment Planning', '/Replenishment'),
        screen('STOCK_TRANSFER', 'Inventory', 'Stock Transfers', '/Transfers'),
        screen('STOCK_OUTFLOW', 'Inventory', 'Stock Out', '/StockOutflow'),
        screen('SCAN', 'Inventory', 'Scan & Lookup', '/Scan'),
        screen('SEARCH', 'Inventory', 'Stock History', '/Search'),
        screen('PRODUCTS', 'Inventory', 'Products', '/Products', 'Product Catalog'),
        screen('PRODUCT_VARIANTS', 'Inventory', 'Product Configurations / Variants', null, 'Product Catalog'),
        screen('CATEGORIES', 'Inventory', 'Categories', '/Categories', 'Product Catalog'),
        screen('BRANDS', 'Inventory', 'Brands', '/Brands', 'Product Catalog'),
        screen('BARCODES', 'Inventory', 'Barcode Labels', '/Labels', 'Product Catalog'),
        screen('EXPENSES', 'Expenses', 'Expenses', '/Expenses'),
        screen('REPORTS', 'Cash in Hand', 'Cash in Hand', '/CashInHand', '', ['VIEW']),
        screen('REPORTS', 'Reports', 'Business Summary', '/Reports/Activity'),
        screen('SALES', 'Reports', 'Sales Reports', '/Sales?fromReports=1', '', ['VIEW']),
        screen('REPORTS', 'Reports', 'Product Performance', '/Reports/ProductSales', '', ['VIEW']),
        screen('REPORTS', 'Reports', 'Daily Cash Closing', '/CashClosing', '', ['VIEW']),
        screen('EXPENSES', 'Reports', 'Expense Reports', '/Expenses/Report', '', ['VIEW']),
        screen('GST_REPORTS', 'Reports', 'GST Reports', '/Reports/Gst'),
        screen('STOCK_INFLOW', 'Reports', 'Received Stock History', '/StockInflow?fromReports=1', 'More Reports', ['VIEW']),
        screen('STOCK_OUTFLOW', 'Reports', 'Stock Out History', '/StockOutflow?fromReports=1', 'More Reports', ['VIEW']),
        screen('EXPIRING_WARRANTIES', 'Reports', 'Warranty Expiry', '/Warranty/Expiring?fromReports=1', 'More Reports', ['VIEW']),
        screen('WARRANTY_CLAIMS', 'Reports', 'Warranty Claims', '/Warranty/Claims?fromReports=1', 'More Reports', ['VIEW']),
        screen('PENDING_SERVICES', 'Reports', 'Open Repairs', '/Service/Pending?fromReports=1', 'More Reports', ['VIEW']),
        screen('SERVICE', 'Reports', 'Completed Repairs', '/Service?status=DELIVERED&fromReports=1', 'More Reports', ['VIEW']),
        screen('SERVICE', 'Service & Warranty', 'New Repair', '/Service/Create', '', ['VIEW', 'CREATE']),
        screen('SERVICE', 'Service & Warranty', 'Repair History', '/Service'),
        screen('PENDING_SERVICES', 'Service & Warranty', 'Open Repairs', '/Service/Pending'),
        screen('WARRANTY_LOOKUP', 'Service & Warranty', 'Warranty Lookup', '/Warranty/Lookup'),
        screen('WARRANTY_CLAIMS', 'Service & Warranty', 'Warranty Claims', '/Warranty/Claims'),
        screen('EXPIRING_WARRANTIES', 'Service & Warranty', 'Warranty Expiry', '/Warranty/Expiring'),
        screen('CRM', 'Customers', 'Customer Overview', '/Crm'),
        screen('CRM', 'Customers', 'Enquiries & Follow-ups', '/Crm/Enquiries'),
        screen('CUSTOMERS', 'Customers', 'Customers', '/Customers'),
        screen('SECURITY', 'Settings', 'Security', '/Admin/Security'),
        screen('BANK_ACCOUNTS', 'Settings', 'Bank Accounts', '/Settings/BankAccounts', 'Business Setup'),
        screen('COMPANY_SETTINGS', 'Settings', 'Business Details', '/Settings/Company?section=business', 'Business Setup'),
        screen('COMPANY_SETTINGS', 'Settings', 'Invoice Settings', '/Settings/Company?section=billing', 'Business Setup'),
        screen('COMPANY_SETTINGS', 'Settings', 'Shop Profile', '/Settings/BusinessProfile', 'Business Setup'),
        screen('GST_SETTINGS', 'Settings', 'GST Settings', '/Admin/Gst', 'Business Setup'),
        screen('STORES', 'Settings', 'Stores & Locations', '/Admin/Stores', 'Business Setup'),
        screen('USER_MANAGEMENT', 'Settings', 'Users', '/Admin/Users', 'Staff & Access'),
        screen('ROLE_MANAGEMENT', 'Settings', 'Roles & Permissions', '/Admin/Roles', 'Staff & Access'),
        screen('EWAY_BILLS', 'Settings', 'E-Way Bill Provider', '/EWayBills/Provider', 'Integrations', ['VIEW', 'CONFIGURE_PROVIDER']),
        screen('COMPANY_SETTINGS', 'Settings', 'Notifications', '/Configuration/Notifications'),
        screen('COMPANY_SETTINGS', 'Settings', 'WhatsApp Settings', '/Configuration/Notifications/WhatsApp', '', ['VIEW', 'EDIT']),
        screen('AUDIT_LOGS', 'Settings', 'Change history', '/Admin/AuditLogs')
    ];
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    function routeKey(route) {
        const u = new URL(route, location.origin);
        // All report links are rendered with this marker, including their main entries.
        const reporting = u.searchParams.get('fromReports') === '1' &&
            !u.pathname.startsWith('/Reports') && !['/CashClosing', '/Expenses/Report'].includes(u.pathname);
        return u.pathname.replace(/\/$/, '') + (u.searchParams.get('section') || '') +
            (reporting ? ':report' : '') + (u.searchParams.get('status') || '');
    }
    function navigationEntries(rows) {
        const links = [...document.querySelectorAll('.sf-shop-nav a[href]')];
        const definitions = [...screens];
        // The existing informational bank setup link uses company VIEW, not bank access.
        if (!rows.some(r => r.moduleCode === 'BANK_ACCOUNTS') && links.some(a => a.getAttribute('href') === '/Settings/BankAccountsSetup'))
            definitions.push(screen('COMPANY_SETTINGS', 'Settings', 'Bank Accounts', '/Settings/BankAccountsSetup', 'Business Setup', ['VIEW']));
        const entries = definitions.filter(s => rows.some(r => r.moduleCode === s.code && r.supportedActions?.length)).map(s => {
            const entry = { ...s }, link = s.route && links.find(a => routeKey(a.getAttribute('href')) === routeKey(s.route));
            if (link) {
                entry.title = link.querySelector('.sf-nav-label')?.textContent.trim() || link.textContent.trim();
                const ancestors = []; let parent = link.parentElement;
                while (parent && !parent.matches('.sf-shop-nav')) {
                    if (parent.matches('details')) {
                        const summary = parent.querySelector(':scope > summary');
                        const label = summary?.querySelector('.sf-nav-label') || summary?.querySelector('span:not(.sf-nav-chevron):not(.sf-icon)');
                        if (summary) ancestors.unshift((label?.textContent || [...summary.childNodes].filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.textContent).join('') || summary.textContent).trim());
                    }
                    parent = parent.parentElement;
                }
                if (ancestors.length) { entry.group = ancestors[0]; entry.subgroup = ancestors.slice(1).join(' / '); }
                else { entry.group = entry.title; entry.subgroup = ''; }
            }
            return entry;
        });
        // Keep every existing grant editable, including catalog extensions without sidebar links.
        for (const row of rows.filter(r => r.supportedActions?.length)) {
            if (!entries.some(e => e.code === row.moduleCode)) entries.push(screen(row.moduleCode, 'Settings', row.moduleName, row.route, 'Additional Permissions'));
        }
        return entries;
    }
    function bind(target, editable, changed, grants = {}) {
        let rows = [], query = '', assignedOnly = false, initialized = false;
        const expanded = new Set(), subgroupExpanded = new Set();
        target.classList.add('permission-editor');
        const toolbar = node('div', null, 'permission-toolbar'), search = node('input'), onlyLabel = node('label'), only = node('input');
        search.type = 'search'; search.className = 'form-control'; search.placeholder = 'Find a module, submenu, screen or action'; search.setAttribute('aria-label', 'Find module permissions');
        only.type = 'checkbox'; onlyLabel.append(only, document.createTextNode(' Assigned only')); toolbar.append(search, onlyLabel);
        const count = node('p', null, 'permission-count'), groups = node('div', null, 'permission-groups'); count.setAttribute('role', 'status');
        const note = node('p', 'Screen names follow the navigation menu. Screens sharing an existing permission stay linked; changing one updates the others. Shop scope, owner restrictions and feature availability still apply.', 'roles-help');
        target.replaceChildren(toolbar, note, count, groups);
        const controls = node('div', null, 'role-accordion-controls');
        for (const [label, open] of [['Expand All', true], ['Collapse All', false]]) {
            const button = node('button', label, 'btn btn-outline-secondary'); button.type = 'button'; button.onclick = () => {
                for (const section of groups.querySelectorAll('.permission-group, .role-permission-subgroup')) {
                    section.open = open; const state = section.matches('.permission-group') ? expanded : subgroupExpanded;
                    if (open) state.add(section.dataset.key); else state.delete(section.dataset.key);
                }
            }; controls.append(button);
        }
        target.closest('.role-permission-card').querySelector('.role-section-heading').append(controls);
        const rowFor = e => rows.find(r => r.moduleCode === e.code);
        const actionsFor = e => rowFor(e).supportedActions.filter(a => !e.actions || e.actions.includes(a));
        const pairs = entries => [...new Map(entries.flatMap(e => actionsFor(e).map(a => [e.code + ':' + a, [rowFor(e), a]]))).values()];
        const allowed = (r, a) => editable() && grants[r.moduleCode]?.includes('VIEW') && grants[r.moduleCode]?.includes(a);
        function change(entries, enabled, clearScreen = false) {
            for (const [row, action] of pairs(entries)) {
                if (!allowed(row, action)) continue;
                if (enabled) row.actions = [...new Set([...(row.actions || []), 'VIEW', action])];
                else if (clearScreen && action === 'VIEW') row.actions = [];
                else row.actions = row.actions.filter(a => a !== action);
            }
            // VIEW is required for every other action by the existing security contract.
            for (const row of rows) if (!row.actions.includes('VIEW')) row.actions = [];
            changed?.(rows); draw();
        }
        function bulk(entries, label, onSelect) {
            const input = node('input'); input.type = 'checkbox'; input.setAttribute('aria-label', label);
            const all = pairs(entries), available = all.filter(([r, a]) => !editable() || allowed(r, a));
            const selected = available.filter(([r, a]) => r.actions.includes(a)).length;
            input.checked = available.length > 0 && selected === available.length;
            input.indeterminate = selected > 0 && selected < available.length;
            input.disabled = !editable() || !available.length;
            input.onclick = e => e.stopPropagation(); input.onkeydown = e => e.stopPropagation();
            input.onchange = () => { const enabled = input.checked; onSelect?.(enabled); change(entries, enabled); restoreFocus(label); };
            return input;
        }
        function restoreFocus(label) { [...groups.querySelectorAll('input')].find(i => i.getAttribute('aria-label') === label)?.focus(); }
        function disclosure(section, key, state) {
            section.dataset.key = key;
            section.open = !!query || state.has(key);
            const heading = section.querySelector(':scope > summary');
            heading.onclick = e => {
                if (e.target.closest('input')) return;
                e.preventDefault(); section.open = !section.open;
                if (section.open) state.add(key); else state.delete(key);
            };
        }
        function drawScreen(entry, parent, entries) {
            const row = rowFor(entry), field = node('fieldset', null, 'permission-module'); field.dataset.module = entry.code; if (entry.route) field.dataset.route = entry.route;
            const legend = node('legend'), label = node('label', null, 'permission-action permission-screen-select');
            const required = entry.actions || ['VIEW'];
            const input = node('input'); input.type = 'checkbox'; input.dataset.action = 'VIEW'; input.setAttribute('aria-label', entry.title + ': screen access');
            input.checked = required.every(a => row.actions.includes(a)); input.disabled = !required.every(a => allowed(row, a));
            input.onchange = () => { const enabled = input.checked; change([{ ...entry, actions: required }], enabled, true); restoreFocus(entry.title + ': screen access'); };
            label.append(input, document.createTextNode(' ' + entry.title)); legend.append(label); field.append(legend);
            const linked = entries.filter(e => e !== entry && e.code === entry.code).map(e => [e.group, e.subgroup, e.title].filter(Boolean).join(' / '));
            if (linked.length) field.append(node('p', 'Shared permissions with: ' + linked.join('; '), 'permission-hint'));
            if (!entry.route) field.append(node('p', 'Controls within the existing screen; no separate navigation entry.', 'permission-hint'));
            const actions = node('div', null, 'permission-actions');
            for (const action of actionsFor(entry).filter(a => a !== 'VIEW')) {
                const actionLabel = node('label', null, 'permission-action'), actionInput = node('input'); actionInput.type = 'checkbox'; actionInput.dataset.action = action;
                actionInput.checked = row.actions.includes(action); actionInput.disabled = !allowed(row, action); const accessible = entry.title + ': ' + action; actionInput.setAttribute('aria-label', accessible);
                actionInput.onchange = () => { change([{ ...entry, actions: [action] }], actionInput.checked); restoreFocus(accessible); };
                const name = StockFlowPermissionEditor.actionNames[action] || action.toLowerCase().replaceAll('_', ' ').replace(/^./, c => c.toUpperCase());
                actionLabel.append(actionInput, document.createTextNode(' ' + name)); actions.append(actionLabel);
            }
            field.append(actions); parent.append(field);
        }
        function draw() {
            groups.replaceChildren(); const entries = navigationEntries(rows), grouped = new Map();
            for (const entry of entries) { if (!grouped.has(entry.group)) grouped.set(entry.group, []); grouped.get(entry.group).push(entry); }
            const order = ['Home', 'Purchases', 'Sales', 'Inventory', 'Expenses', 'Cash in Hand', 'Reports', 'Service & Warranty', 'Customers', 'Settings'];
            count.textContent = rows.reduce((n, r) => n + r.actions.length, 0) + ' permissions selected';
            for (const [name, groupEntries] of [...grouped].sort((a, b) => (order.indexOf(a[0]) < 0 ? 99 : order.indexOf(a[0])) - (order.indexOf(b[0]) < 0 ? 99 : order.indexOf(b[0])))) {
                const visible = groupEntries.filter(e => (!assignedOnly || rowFor(e).actions.length) && (!query || [e.group, e.subgroup, e.title, ...actionsFor(e).map(a => StockFlowPermissionEditor.actionNames[a] || a)].join(' ').toLowerCase().includes(query)));
                if (!visible.length) continue;
                const all = pairs(groupEntries), selected = all.filter(([r, a]) => r.actions.includes(a)).length;
                const section = node('details', null, 'permission-group' + (selected ? ' role-module-selected' : '')); section.dataset.group = name;
                const heading = node('summary'), check = bulk(groupEntries, 'Select all screens in ' + name, enabled => { if (enabled) { expanded.add(name); for (const e of groupEntries) if (e.subgroup) subgroupExpanded.add(name + '/' + e.subgroup); } }); check.dataset.moduleGroup = name;
                const identity = node('span', null, 'role-module-identity'); identity.append(node('strong', name), node('small', groupEntries.length + (groupEntries.length === 1 ? ' screen' : ' screens')));
                const icon = node('span', null, 'role-module-icon'), svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'), use = document.createElementNS(svg.namespaceURI, 'use');
                use.setAttribute('href', '/images/icons.svg#' + ({ Home: 'home', Purchases: 'truck', Sales: 'cart', Inventory: 'box', Expenses: 'document', Reports: 'chart', Settings: 'settings', Customers: 'users', 'Cash in Hand': 'cash', 'Service & Warranty': 'tools' }[name] || 'grid')); svg.setAttribute('aria-hidden', 'true'); svg.append(use); icon.append(svg);
                const badge = node('span', selected + '/' + all.length, 'role-module-count'), chevron = node('span', null, 'role-module-chevron'); chevron.setAttribute('aria-hidden', 'true'); badge.setAttribute('aria-label', selected + ' of ' + all.length + ' permissions selected');
                heading.append(check, icon, identity, badge, chevron); section.append(heading);
                const subgroups = new Map();
                for (const entry of visible) {
                    let parent = section;
                    if (entry.subgroup) {
                        if (!subgroups.has(entry.subgroup)) {
                            const nested = node('details', null, 'role-permission-subgroup'), summary = node('summary');
                            summary.append(bulk(groupEntries.filter(e => e.subgroup === entry.subgroup), 'Select all screens in ' + name + ' / ' + entry.subgroup, enabled => { if (enabled) subgroupExpanded.add(name + '/' + entry.subgroup); }), node('strong', entry.subgroup));
                            nested.append(summary); section.append(nested); disclosure(nested, name + '/' + entry.subgroup, subgroupExpanded); subgroups.set(entry.subgroup, nested);
                        }
                        parent = subgroups.get(entry.subgroup);
                    }
                    drawScreen(entry, parent, entries);
                }
                if (!initialized && selected) { expanded.add(name); for (const e of groupEntries) if (e.subgroup && rowFor(e).actions.length) subgroupExpanded.add(name + '/' + e.subgroup); }
                disclosure(section, name, expanded); groups.append(section);
            }
            initialized = true;
            if (!groups.childElementCount) groups.append(node('p', 'No screens match this search.'));
        }
        search.oninput = () => { query = search.value.trim().toLowerCase(); draw(); };
        only.onchange = () => { assignedOnly = only.checked; draw(); };
        return { set(data) { rows = data; draw(); }, clearFilter() { query = ''; assignedOnly = false; search.value = ''; only.checked = false; draw(); } };
    }
    return { bind, navigationEntries };
})();
