window.StockFlowPermissionEditor = (() => {
    'use strict';

    const catalog = {
        DASHBOARD: ['Dashboard & scanning', 'Dashboard', 'Shop overview'],
        SCAN: [
            'Dashboard & scanning',
            'Scan products / serials',
            'Scanner lookup used while receiving and billing'
        ],
        SEARCH: ['Dashboard & scanning', 'Search', 'Search permitted records'],
        STOCK_INFLOW: [
            'Receive Stock',
            'Receive Stock',
            'Guided receiving and stock inflow history'
        ],
        PURCHASES: [
            'Receive Stock',
            'Purchase orders & deliveries',
            'Advanced orders, partial deliveries and purchase records'
        ],
        SALES: [
            'Sell / Billing',
            'Sell / Billing',
            'Counter billing, walk-in or named customers and sales lists'
        ],
        INVOICES: ['Sell / Billing', 'Invoices', 'Print completed invoices'],
        CUSTOMERS: [
            'Setup',
            'Customers',
            'Configuration customer records, billing lookup and quick registration'
        ],
        INVENTORY: [
            'Inventory & transfers',
            'Stock on hand',
            'Stock checking, serials and movement history'
        ],
        STOCK_ADJUSTMENT: [
            'Inventory & transfers',
            'Stock adjustments / opening stock',
            'Authorize opening stock, counted corrections or damage'
        ],
        STOCK_OUTFLOW: [
            'Inventory & transfers',
            'Stock leaving the shop',
            'Review stock leaving the location'
        ],
        STOCK_TRANSFER: [
            'Inventory & transfers',
            'Stock transfers',
            'Requests, approval, dispatch, receipt and damage/loss'
        ],
        REPORTS: [
            'Reports',
            'Daily / weekly / monthly reports',
            'Period reports and authorized store comparisons; stock also requires Inventory view and financials require Sales view'
        ],
        PRODUCTS: ['Setup', 'Products', 'Product registration'],
        PRODUCT_VARIANTS: [
            'Setup',
            'Product configurations / variants',
            'Pricing, barcodes, capacities and specifications within Product Registration'
        ],
        BARCODES: [
            'Setup',
            'Barcodes & labels',
            'Existing/internal identifiers, optional printing and reprinting'
        ],
        CATEGORIES: ['Setup', 'Categories', 'Product category setup'],
        BRANDS: ['Setup', 'Brands', 'Brand setup'],
        SUPPLIERS: ['Setup', 'Suppliers', 'Supplier lookup and registration'],
        COMPANY_SETTINGS: ['Setup', 'Company / invoice settings', 'Business and invoice details'],
        WARRANTY_LOOKUP: ['Service & Warranty', 'Check warranty', 'Check coverage'],
        WARRANTY_CLAIMS: ['Service & Warranty', 'Warranty claims', 'Record and resolve claims'],
        EXPIRING_WARRANTIES: ['Service & Warranty', 'Expiring warranties', 'Review expiry lists'],
        SERVICE: ['Service & Warranty', 'Service history', 'Repairs, assignments and delivery'],
        PENDING_SERVICES: ['Service & Warranty', 'Pending services', 'Pending repair list'],
        USER_MANAGEMENT: [
            'Administration & Security',
            'Users',
            'Create accounts and manage user actions'
        ],
        ROLE_MANAGEMENT: [
            'Administration & Security',
            'Role templates',
            'Defaults for new users; existing users retain their saved actions'
        ],
        STORES: [
            'Administration & Security',
            'Stores & assignments',
            'Store setup and user store access'
        ],
        SECURITY: [
            'Administration & Security',
            'Security policy',
            'Password and authenticator policies'
        ],
        AUDIT_LOGS: [
            'Administration & Security',
            'Audit logs',
            'Review security and operational changes'
        ]
    };
    const names = {
        VIEW: 'Open this screen',
        CREATE: 'Create',
        EDIT: 'Edit',
        UPDATE: 'Update',
        DEACTIVATE: 'Deactivate',
        EXPORT: 'Export',
        APPROVE: 'Approve',
        DISPATCH: 'Send goods',
        RECEIVE: 'Receive goods',
        CANCEL: 'Cancel',
        ADJUST: 'Correct stock / enter starting stock',
        SCAN: 'Use scanner',
        GENERATE: 'Create StockFlow barcode',
        PRINT: 'Print',
        REPRINT: 'Reprint',
        RESET_PASSWORD: 'Reset password',
        RESET_AUTHENTICATOR: 'Reset authenticator',
        MANAGE_PERMISSIONS: 'Manage permissions',
        MANAGE: 'Change security policy',
        MANAGE_ACCESS: 'Assign stores',
        ASSIGN: 'Assign service',
        DELIVER: 'Deliver service',
        RESOLVE: 'Resolve claim'
    };
    const sensitive = new Set([
        'DEACTIVATE',
        'APPROVE',
        'DISPATCH',
        'RECEIVE',
        'CANCEL',
        'ADJUST',
        'RESET_PASSWORD',
        'RESET_AUTHENTICATOR',
        'MANAGE_PERMISSIONS',
        'MANAGE',
        'MANAGE_ACCESS'
    ]);
    const workflows = {
        'Receive stock': [
            ['STOCK_INFLOW', 'VIEW'],
            ['STOCK_INFLOW', 'RECEIVE']
        ],
        'Receive with scanner': [
            ['STOCK_INFLOW', 'VIEW'],
            ['STOCK_INFLOW', 'RECEIVE'],
            ['SCAN', 'VIEW'],
            ['SCAN', 'SCAN']
        ],
        'Bill with scanner': [
            ['SALES', 'VIEW'],
            ['SALES', 'CREATE'],
            ['SCAN', 'VIEW'],
            ['SCAN', 'SCAN']
        ],
        'Print invoice': [
            ['INVOICES', 'VIEW'],
            ['INVOICES', 'PRINT']
        ],
        'Customer lookup': [['CUSTOMERS', 'VIEW']],
        'Quick customer creation': [
            ['CUSTOMERS', 'VIEW'],
            ['CUSTOMERS', 'CREATE']
        ],
        'Register unknown item': [
            ['PRODUCTS', 'VIEW'],
            ['PRODUCTS', 'CREATE']
        ],
        'Print received labels': [
            ['BARCODES', 'VIEW'],
            ['BARCODES', 'PRINT']
        ],
        'Post opening stock': [
            ['STOCK_ADJUSTMENT', 'VIEW'],
            ['STOCK_ADJUSTMENT', 'ADJUST']
        ],
        'Check stock': [['INVENTORY', 'VIEW']],
        'Dispatch transfer': [
            ['STOCK_TRANSFER', 'VIEW'],
            ['STOCK_TRANSFER', 'DISPATCH']
        ],
        'Receive transfer': [
            ['STOCK_TRANSFER', 'VIEW'],
            ['STOCK_TRANSFER', 'RECEIVE']
        ],
        'Stock period report': [
            ['REPORTS', 'VIEW'],
            ['INVENTORY', 'VIEW']
        ],
        'Sales period report': [
            ['REPORTS', 'VIEW'],
            ['SALES', 'VIEW']
        ]
    };
    const info = (value) => {
        return [
            value.parentModuleName || value.moduleName,
            value.moduleName,
            value.description || ''
        ];
    };
    const title = (code) => {
        return (
            rowsForTitles.find((value) => {
                return value.moduleCode === code;
            })?.moduleName ||
            catalog[code]?.[1] ||
            code
        );
    };
    let rowsForTitles = [];
    function bind(target, editable, changed, grants = {}) {
        let rows = [];
        let query = '';
        let assignedOnly = false;
        const openGroups = new Set();
        const openModules = new Set();
        target.classList.add('permission-editor');
        const toolbar = document.createElement('div');
        toolbar.className = 'permission-toolbar';
        const search = document.createElement('input');
        search.type = 'search';
        search.className = 'form-control';
        search.placeholder = 'Find a module or action';
        search.setAttribute('aria-label', 'Find module permissions');
        const onlyLabel = document.createElement('label');
        const only = document.createElement('input');
        only.type = 'checkbox';
        onlyLabel.append(only, document.createTextNode(' Assigned only'));
        const count = document.createElement('p');
        count.className = 'permission-count';
        count.setAttribute('role', 'status');
        const groups = document.createElement('div');
        groups.className = 'permission-groups';
        const workflow = document.createElement('details');
        const workflowTitle = document.createElement('summary');
        const workflowBody = document.createElement('div');
        workflow.className = 'permission-workflows';
        workflowTitle.textContent = 'Check workflow access';
        workflow.append(workflowTitle, workflowBody);
        toolbar.append(search, onlyLabel);
        target.replaceChildren(toolbar, count, groups, workflow);
        function checks() {
            workflowBody.replaceChildren();
            for (const [name, needs] of Object.entries(workflows)) {
                const paragraph = document.createElement('p');
                const missing = needs.filter(([code, action]) => {
                    return !rows
                        .find((value) => {
                            return value.moduleCode === code;
                        })
                        ?.actions.includes(action);
                });
                paragraph.textContent =
                    name +
                    ': ' +
                    (missing.length
                        ? 'Missing ' +
                          missing
                              .map(([code, action]) => {
                                  return title(code) + ' / ' + names[action];
                              })
                              .join(', ')
                        : 'Ready');
                workflowBody.append(paragraph);
            }
        }
        function refreshCounts() {
            count.textContent = `${
                rows.filter((value) => {
                    return value.actions.length;
                }).length
            } of ${
                rows.filter((value) => {
                    return value.supportedActions?.length;
                }).length
            } screens assigned · ${rows.reduce((value, value2) => {
                return value + value2.actions.length;
            }, 0)} actions`;
            for (const section of groups.querySelectorAll('.permission-group')) {
                section.querySelector('summary').textContent =
                    section.dataset.group +
                    ' · ' +
                    rows.filter((value) => {
                        return info(value)[0] === section.dataset.group && value.actions.length;
                    }).length +
                    ' assigned';
            }
            checks();
        }
        function draw() {
            groups.replaceChildren();
            const grouped = new Map();
            let visible = 0;
            for (const value of rows) {
                if (!value.supportedActions?.length) {
                    continue;
                }
                const [group, title, description] = info(value);
                const searchable = [
                    title,
                    description,
                    value.moduleName,
                    value.moduleCode,
                    ...value.supportedActions.map((a) => {
                        return names[a] || a;
                    })
                ]
                    .join(' ')
                    .toLowerCase();
                if (
                    (query && !searchable.includes(query)) ||
                    (assignedOnly && !value.actions.length)
                ) {
                    continue;
                }
                visible++;
                if (!grouped.has(group)) {
                    const section = document.createElement('details');
                    section.dataset.group = group;
                    section.className = 'permission-group';
                    section.open = !!query || openGroups.has(group);
                    const summary = document.createElement('summary');
                    const enabled = rows.filter((value2) => {
                        return info(value2)[0] === group && value2.actions.length;
                    }).length;
                    summary.textContent = group + ' · ' + enabled + ' assigned';
                    section.append(summary);
                    section.ontoggle = () => {
                        if (section.open) {
                            openGroups.add(group);
                        } else {
                            openGroups.delete(group);
                        }
                    };
                    grouped.set(group, section);
                    groups.append(section);
                }
                const row = document.createElement('fieldset');
                row.className = 'permission-module';
                row.dataset.module = value.moduleCode;
                const legend = document.createElement('legend');
                legend.textContent = title;
                const hint = document.createElement('p');
                hint.className = 'permission-hint';
                hint.textContent = description;
                row.append(legend, hint);
                const actionDetails = document.createElement('details');
                actionDetails.open = !!query || openModules.has(value.moduleCode);
                actionDetails.className = 'permission-actions';
                const actionTitle = document.createElement('summary');
                actionTitle.textContent =
                    'Actions · ' +
                    value.actions.filter((a) => {
                        return a !== 'VIEW';
                    }).length +
                    ' selected';
                actionDetails.append(actionTitle);
                actionDetails.ontoggle = () => {
                    if (actionDetails.open) {
                        openModules.add(value.moduleCode);
                    } else {
                        openModules.delete(value.moduleCode);
                    }
                };
                const viewInput = make('VIEW', row);
                if (value.supportedActions.length > 1) {
                    for (const a of value.supportedActions.filter((a) => {
                        return a !== 'VIEW';
                    })) {
                        make(a, actionDetails);
                    }
                    row.append(actionDetails);
                }
                grouped.get(group).append(row);
                function make(action, parent) {
                    const label = document.createElement('label');
                    label.className =
                        'permission-action' + (sensitive.has(action) ? ' sensitive' : '');
                    const input = document.createElement('input');
                    input.type = 'checkbox';
                    input.dataset.action = action;
                    input.checked = value.actions.includes(action);
                    input.disabled = !editable() || !grants[value.moduleCode]?.includes(action);
                    if (editable() && !grants[value.moduleCode]?.includes(action)) {
                        label.title = 'You cannot grant an action you do not have.';
                    }
                    input.setAttribute('aria-label', value.moduleName + ': ' + action);
                    label.append(input, document.createTextNode(' ' + (names[action] || action)));
                    parent.append(label);
                    input.onchange = () => {
                        value.actions = input.checked
                            ? [...new Set([...value.actions, action])]
                            : value.actions.filter((a) => {
                                  return a !== action;
                              });
                        if (action === 'VIEW' && !input.checked) {
                            value.actions = [];
                        } else if (input.checked && !value.actions.includes('VIEW')) {
                            value.actions.unshift('VIEW');
                        }
                        viewInput.checked = value.actions.includes('VIEW');
                        for (const control of row.querySelectorAll('[data-action]')) {
                            control.checked = value.actions.includes(control.dataset.action);
                        }
                        actionTitle.textContent =
                            'Actions · ' +
                            value.actions.filter((a) => {
                                return a !== 'VIEW';
                            }).length +
                            ' selected';
                        changed?.(rows);
                        refreshCounts();
                    };
                    return input;
                }
            }
            if (!visible) {
                const paragraph = document.createElement('p');
                paragraph.textContent = 'No screens match this search.';
                groups.append(paragraph);
            }
            refreshCounts();
        }
        search.oninput = () => {
            query = search.value.trim().toLowerCase();
            draw();
        };
        only.onchange = () => {
            assignedOnly = only.checked;
            draw();
        };
        return {
            set(data) {
                rows = data;
                rowsForTitles = data;
                draw();
            },
            clearFilter() {
                query = '';
                assignedOnly = false;
                search.value = '';
                only.checked = false;
                draw();
            }
        };
    }
    return {
        bind,
        catalog
    };
})();
