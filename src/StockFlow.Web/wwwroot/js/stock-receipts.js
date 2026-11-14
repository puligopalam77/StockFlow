(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const money = (value) => {
        return new Intl.NumberFormat('en-IN', {
            style: 'currency',
            currency: 'INR'
        }).format(Number(value || 0));
    };
    const number = (value) => {
        return new Intl.NumberFormat('en-IN').format(Number(value || 0));
    };
    const date = (value) => {
        if (value) {
            return new Intl.DateTimeFormat('en-IN', {
                dateStyle: 'medium',
                timeStyle: 'short'
            }).format(new Date(value));
        } else {
            return '—';
        }
    };
    let tab = 'receipts';
    let page = 1;let receiptPager;
    let total = 0;
    let loading = 0;
    async function requestJson(url) {
        const response = await fetch(url, {
            headers: {
                Accept: 'application/json'
            }
        });
        const body = await response.json().catch(() => {
            return null;
        });
        if (!response.ok) {
            throw new Error(body?.message || 'Unable to load stock receipt history.');
        }
        return body;
    }
    function message(error) {
        getElement('receipt-history-message').textContent = error.message;
        getElement('receipt-history-message').hidden = false;
    }
    function query() {
        const values = {
            term: getElement('receipt-term').value.trim(),
            type: getElement('receipt-type').value,
            supplierId: getElement('receipt-supplier').value,
            from: getElement('receipt-from').value,
            to: getElement('receipt-to').value,
            page,
            pageSize: getElement('receipt-page-size').value
        };
        const result = new URLSearchParams();
        Object.entries(values).forEach(([key, value]) => {
            if (value !== '') {
                result.set(key, value);
            }
        });
        return result;
    }
    function cell(row, value, className) {
        const td = row.insertCell();
        if (className) {
            td.className = className;
        }
        if (value instanceof Node) {
            td.append(value);
        } else {
            td.textContent = value ?? '—';
        }
        return td;
    }
    function link(row) {
        const value = document.createElement('a');
        value.href = `/StockInflow/Details/${encodeURIComponent(row.source)}/${row.sourceId}`;
        value.textContent = row.receiptNumber;
        value.className = 'receipt-number';
        return value;
    }
    function table(headings, rows, render) {
        if (!rows.length) {
            const empty = document.createElement('p');
            empty.className = 'receipt-empty';
            empty.textContent = 'No stock receipts match these filters.';
            return empty;
        }
        const wrap = document.createElement('div');
        wrap.className = 'table-responsive';
        const result = document.createElement('table');
        result.className = 'table table-hover receipt-table';
        const header = result.createTHead().insertRow();
        headings.forEach((text) => {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = text;
            header.append(th);
        });
        const body = result.createTBody();
        rows.forEach((item) => {
            return render(body.insertRow(), item);
        });
        wrap.append(result);
        return wrap;
    }
    function renderReceipts(data) {
        getElement('summary-receipts').textContent = number(data.summary.receipts);
        getElement('summary-units').textContent = number(data.summary.unitsReceived);
        getElement('summary-value').textContent = money(data.summary.purchaseValue);
        getElement('summary-adjustments').textContent = number(data.summary.adjustments);
        const result = table(
            [
                'Date / time',
                'Receipt no.',
                'Type',
                'Supplier / party',
                'Items',
                'Qty',
                'Value',
                'Status',
                'Actions'
            ],
            data.rows,
            (tr, row) => {
                cell(tr, date(row.occurredAt));
                cell(tr, link(row));
                const type = document.createElement('span');
                type.className = 'receipt-type';
                type.textContent = row.typeLabel;
                cell(tr, type);
                cell(tr, row.partyName);
                cell(tr, number(row.itemCount));
                cell(tr, number(row.quantity));
                cell(tr, row.value == null ? '—' : money(row.value));
                const status = document.createElement('span');
                status.className =
                    'receipt-status' + (row.status === 'REVERSED' ? ' reversed' : '');
                status.textContent = row.status === 'REVERSED' ? 'Reversed' : 'Completed';
                cell(tr, status);
                const view = link(row);
                view.textContent = 'View';
                view.className = 'btn btn-outline-secondary btn-sm receipt-view';
                view.setAttribute('aria-label', `View receipt ${row.receiptNumber}`);
                cell(tr, view);
                tr.classList.add('receipt-clickable-row');
                tr.addEventListener('click', event => {
                    if (event.target.closest('a,button,input,select') || window.getSelection()?.toString()) return;
                    location.assign(view.href);
                });
            }
        );
        const host=getElement('receipt-results');if(window.StockFlowLoading)StockFlowLoading.updateTable(host,result);else host.replaceChildren(result);
    }
    function renderItems(data) {
        const result = table(
            ['Date / time', 'Product / variant', 'Quantity', 'Movement type', 'Reference', 'Party'],
            data.rows,
            (tr, row) => {
                cell(tr, date(row.occurredAt));
                cell(tr, row.productName);
                cell(tr, number(row.quantity));
                const type = document.createElement('span');
                type.className = 'receipt-type';
                type.textContent = row.typeLabel;
                cell(tr, type);
                cell(tr, link(row));
                cell(tr, row.partyName);
            }
        );
        const host=getElement('receipt-item-results');if(window.StockFlowLoading)StockFlowLoading.updateTable(host,result);else host.replaceChildren(result);
    }
    for (const id of ['receipt-results','receipt-item-results']) window.StockFlowLoading?.region(getElement(id),{kind:'table',columns:id==='receipt-results'?9:6,retry:()=>load()});
    async function load(pagingRequest) {
        const request = ++loading;
        const form = getElement('receipt-history-filter');
        const apply = form.querySelector('[type="submit"]');
        getElement('receipt-history-message').hidden = true;
        form.setAttribute('aria-busy', 'true');
        apply.disabled = true;
        apply.textContent = 'Searching...';
        try {
            const responseData = await requestJson(
                `/api/stock-receipts${tab === 'items' ? '/items' : ''}?${query()}`
            );
            const data=receiptPager.capture(responseData,pagingRequest);if(!data)return;
            if (request !== loading) {
                return;
            }
            total = data.totalCount;
            if (tab === 'receipts') {
                renderReceipts(data);
            } else {
                renderItems(data);
            }

        } catch (error) {
            if (request === loading) {
                message(error);
            }
        } finally {
            if (request === loading) {
                form.removeAttribute('aria-busy');
                apply.disabled = false;
                apply.textContent = 'Apply';
            }
        }
    }
    function selectTab(next) {
        tab = next;
        page = 1;
        const receipts = next === 'receipts';
        getElement('receipts-tab').classList.toggle('active', receipts);
        getElement('receipts-tab').setAttribute('aria-selected', String(receipts));
        getElement('items-tab').classList.toggle('active', !receipts);
        getElement('items-tab').setAttribute('aria-selected', String(!receipts));
        getElement('receipts-panel').hidden = !receipts;
        getElement('items-panel').hidden = receipts;
        load();
    }
    async function initialize() {
        try {
            const suppliers = await requestJson('/api/stock-receipts/suppliers');
            getElement('receipt-supplier').append(
                ...suppliers.map((item) => {
                    return new Option(item.supplierName, item.supplierId);
                })
            );
        } catch (error) {
            message(error);
        }
        getElement('receipt-history-filter').onsubmit = (event) => {
            event.preventDefault();
            page = 1;
            load();
        };
        getElement('receipt-reset').onclick = () => {
            getElement('receipt-history-filter').reset();
            page = 1;
            load();
        };
        getElement('receipt-page-size').onchange = () => {
            page = 1;
            load();
        };
        getElement('receipts-tab').onclick = () => {
            return selectTab('receipts');
        };
        getElement('items-tab').onclick = () => {
            return selectTab('items');
        };
        load();
    }
    receiptPager=StockFlowPagination.adapt({host:'.receipt-pagination',root:'#stock-receipt-history',legacy:{size:'#receipt-page-size',range:'#receipt-range'},getOffset:()=>(page-1)*Number(getElement('receipt-page-size').value),setOffset:(value,request)=>{page=request.page;getElement('receipt-page-size').value=String(request.size);},load});load=receiptPager.load;
    initialize();
})();
