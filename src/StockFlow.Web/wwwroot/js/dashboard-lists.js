(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const route = location.pathname.toLowerCase();
    const movement = route === '/stockinflow' || route === '/stockoutflow';
    const incoming = route === '/stockinflow';
    let offset = 0;let dashboardPager;
    let generation = 0;
    function link(url, title) {
        const a = document.createElement('a');
        a.href = url;
        a.textContent = title;
        return a;
    }
    function table(headings, rows) {
        const wrapper = document.createElement('div');
        wrapper.className = 'table-responsive';
        const table = document.createElement('table');
        table.className = 'table table-striped';
        const head = table.createTHead().insertRow();
        for (const title of headings) {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = title;
            head.append(th);
        }
        const body = table.createTBody();
        for (const values of rows) {
            const tr = body.insertRow();
            for (const value of values) {
                const td = tr.insertCell();
                if (value instanceof Node) {
                    td.append(value);
                } else {
                    td.textContent = value ?? '—';
                }
            }
        }
        if (!rows.length) {
            const td = body.insertRow().insertCell();
            td.colSpan = headings.length;
            td.textContent = 'No records found.';
        }
        wrapper.append(table);
        getElement('dashboard-results').replaceChildren(wrapper);
    }
    async function load(pagingRequest) {
        const current = ++generation;
        getElement('dashboard-message').hidden = true;
        try {
            let url = '/api/dashboard/';
            let headings;
            let render;
            if (movement) {
                const q = new URLSearchParams({
                    incoming,
                    offset,
                    term: getElement('movement-term').value
                });
                for (const name of ['type', 'from', 'to']) {
                    if (getElement('movement-' + name).value) {
                        q.set(name, getElement('movement-' + name).value);
                    }
                }
                url += 'movements?' + q;
                headings = ['Date', 'Product', 'Quantity', 'Movement / reference', 'Party'];
                render = (product) => {
                    return [
                        product.createdAt.replace('T', ' ').slice(0, 19),
                        product.productName,
                        product.quantity,
                        product.referenceUrl
                            ? link(product.referenceUrl, product.movementLabel)
                            : product.movementLabel,
                        product.partyName
                    ];
                };
            } else if (route.startsWith('/inventory/lowstock')) {
                url += 'low-stock?offset=' + offset;
                headings = ['Product', 'Category', 'Current stock', 'Minimum', 'Required'];
                render = (product) => {
                    return [
                        link(
                            '/Inventory?term=' + encodeURIComponent(product.productName),
                            product.productName
                        ),
                        product.categoryName,
                        product.currentStock,
                        product.minimumStockLevel,
                        product.required
                    ];
                };
            } else if (route.startsWith('/users')) {
                url += 'users';
                headings = ['Name', 'Username', 'Role', 'Access'];
                render = (user) => {
                    return [
                        user.fullName,
                        user.username,
                        user.roleName,
                        user.isActive ? 'Active' : 'Inactive'
                    ];
                };
            } else {
                const term = getElement('global-term').value.trim();
                if (term.length < 2) {
                    getElement('dashboard-results').textContent =
                        'Enter at least two characters to search.';
                    return;
                }
                url += 'search?term=' + encodeURIComponent(term);
                headings = ['Type', 'Match', 'Details'];
                render = (value) => {
                    return [value.kind, link(value.url, value.title), value.detail];
                };
            }
            const response = await fetch(url);
            let rows = await response.json().catch(() => {
                return null;
            });
            if (!response.ok) {
                throw new Error(
                    rows?.message ||
                        (response.status === 403
                            ? 'You do not have access to this report.'
                            : 'Unable to load results. Try again.')
                );
            }
            if (current !== generation) {
                return;
            }
            if(dashboardPager){rows=dashboardPager.capture(rows,pagingRequest);if(!rows)return;}
            table(headings, rows.map(render));
            if (getElement('previous')) {
                getElement('previous').disabled = offset === 0;
                getElement('next').disabled = rows.length < 100;
            }
        } catch (error) {
            if (current === generation) {
                getElement('dashboard-message').textContent = error.message;
                getElement('dashboard-message').hidden = false;
            }
        }
    }
    if (movement) {
        const types = incoming
            ? ['PURCHASE', 'CUSTOMER_RETURN', 'ADJUSTMENT_IN', 'SERVICE_IN']
            : [
                  'SALE',
                  'SUPPLIER_RETURN',
                  'DAMAGED',
                  'ADJUSTMENT_OUT',
                  'SERVICE_OUT',
                  'WARRANTY_REPLACEMENT'
              ];
        getElement('movement-type').append(
            ...types.map((t) => {
                return new Option(StockFlowText.label(t), t);
            })
        );
        getElement('movement-filter').onsubmit = (event) => {
            event.preventDefault();
            offset = 0;
            load();
        };
    }
    if (getElement('global-search')) {
        getElement('global-term').value = new URLSearchParams(location.search).get('term') || '';
        getElement('global-search').onsubmit = (event) => {
            event.preventDefault();
            load();
        };
    }
    if (getElement('previous')) {
        getElement('previous').onclick = () => {
            offset = Math.max(0, offset - 100);
            load();
        };
        getElement('next').onclick = () => {
            offset += 100;
            load();
        };
    }
    if(getElement('previous')){const footer=document.createElement('div');getElement('next').parentElement.append(footer);dashboardPager=StockFlowPagination.adapt({host:footer,root:'#dashboard-results',legacy:{previous:'#previous',next:'#next'},getOffset:()=>offset,setOffset:value=>offset=value,load});load=dashboardPager.load;}
    load();
})();
