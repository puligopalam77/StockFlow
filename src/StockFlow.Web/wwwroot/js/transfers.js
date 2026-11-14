// Move stock between locations: request, approve, send, and receive goods.
(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const workspace = getElement('transfer-workspace');
    const key = workspace.dataset.storageKey;
    let variants = [];
    let cart = [];
    let current = null;
    let rows = [];
    let locations = [];
    let busy = false;
    let pending = null;
    let blocked = false;
    function uuid() {
        if (typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        const value = crypto.getRandomValues(new Uint8Array(16));
        value[6] = (value[6] & 15) | 64;
        value[8] = (value[8] & 63) | 128;
        const h = [...value]
            .map((value2) => {
                return value2.toString(16).padStart(2, '0');
            })
            .join('');
        return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
    }
    const showMessage = (message, error = false) => {
        getElement('transfer-message').className = `alert alert-${error ? 'danger' : 'success'}`;
        getElement('transfer-message').textContent = message;
        getElement('transfer-message').hidden = !message;
    };
    function freeze() {
        getElement('transfer-editor').disabled = busy || !!pending || blocked;
        getElement('transfer-pending').hidden = !pending;
        getElement('transfer-retry').disabled = busy;
    }
    async function requestJson(url, body) {
        const response = await fetch(
            url,
            body === undefined
                ? {}
                : {
                      method: 'POST',
                      headers: {
                          'Content-Type': 'application/json',
                          'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                      },
                      body: JSON.stringify(body)
                  }
        );
        const data = await response.json().catch(() => {
            return null;
        });
        if (!response.ok) {
            const record = new Error(
                data?.message ||
                    Object.values(data?.errors || {})
                        .flat()
                        .join(' ') ||
                    'Transfer action failed.'
            );
            record.status = response.status;
            throw record;
        }
        if (data === null) {
            throw new Error('The transfer result could not be read. Retry the saved action.');
        }
        return data;
    }
    async function runAction(action) {
        if (busy) {
            return;
        }
        busy = true;
        freeze();
        try {
            await action();
        } catch (error) {
            showMessage(error.message, true);
        } finally {
            busy = false;
            freeze();
        }
    }
    const cell = (row, value) => {
        const td = document.createElement('td');
        td.textContent = value;
        row.append(td);
        return td;
    };
    function renderList() {
        const mode = getElement('transfer-filter').value;
        const filtered = rows.filter((t) => {
            return (
                mode === 'all' ||
                (mode === 'incoming' && t.toLocationId === window.StockFlowLocation) ||
                (mode === 'outgoing' && t.fromLocationId === window.StockFlowLocation) ||
                (mode === 'active' && !['RECEIVED', 'CANCELLED'].includes(t.status))
            );
        });
        getElement('transfer-list').replaceChildren();
        for (const t of filtered) {
            const tr = document.createElement('tr');
            for (const value of [
                t.transferNumber,
                t.fromLocation,
                t.toLocation,
                StockFlowText.label(t.status)
            ]) {
                cell(tr, value);
            }
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = 'Open';
            button.onclick = () => {
                return runAction(() => {
                    return detail(t.transferId);
                });
            };
            cell(tr, '').append(button);
            getElement('transfer-list').append(tr);
        }
        getElement('transfer-count').textContent =
            `${filtered.length} shown from the latest ${rows.length} accessible transfers.`;
    }
    let listVersion = 0;
    async function list() {
        const version = ++listVersion;
        const data = await requestJson('/api/transfers');
        if (version !== listVersion) return;
        rows = data;
        renderList();
    }
    async function detail(id) {
        current = await requestJson(`/api/transfers/${id}`);
        getElement('transfer-detail').hidden = false;
        getElement('transfer-title').textContent =
            `${current.transferNumber} · ${StockFlowText.label(current.status)}`;
        getElement('transfer-route').textContent =
            `${current.fromLocation} → ${current.toLocation}`;
        getElement('transfer-lines').replaceChildren();
        getElement('action-notes').value = '';
        let transit = 0;
        const source = window.StockFlowLocation === current.fromLocationId;
        const destination = window.StockFlowLocation === current.toLocationId;
        for (const item of current.items) {
            const outstanding =
                item.dispatchedQuantity -
                item.receivedQuantity -
                item.damagedQuantity -
                item.lostQuantity;
            transit += outstanding;
            const box = document.createElement('fieldset');
            box.className = 'transfer-line border rounded p-3 my-2';
            box.dataset.line = item.transferItemId;
            const legend = document.createElement('legend');
            legend.className = 'fs-6';
            legend.textContent = [item.productName,StockFlowVariantIdentity.summary(item)].filter(Boolean).join(' · ');
            box.append(legend);
            const info = document.createElement('p');
            info.className = 'small';
            info.textContent = `Requested ${item.requestedQuantity} · Sent ${item.dispatchedQuantity} · Received ${item.receivedQuantity} · Damaged ${item.damagedQuantity} · Lost ${item.lostQuantity} · In transit ${outstanding}`;
            box.append(info);
            const actionable =
                (current.status === 'APPROVED' && source) ||
                (['DISPATCHED', 'PARTIALLY_RECEIVED'].includes(current.status) && destination);
            if (actionable) {
                const label = document.createElement('label');
                label.textContent =
                    current.status === 'APPROVED'
                        ? 'Quantity to send'
                        : 'Quantity physically received / resolved (0 skips item)';
                const qty = document.createElement('input');
                qty.type = 'number';
                qty.min = current.status === 'APPROVED' ? 1 : 0;
                qty.max = current.status === 'APPROVED' ? item.requestedQuantity : outstanding;
                qty.step = 1;
                qty.className = 'form-control';
                qty.dataset.quantity = '';
                qty.value =
                    current.status === 'APPROVED'
                        ? item.requestedQuantity
                        : item.isSerialTracked
                          ? 0
                          : outstanding;
                qty.readOnly = current.status === 'APPROVED' || item.isSerialTracked;
                label.append(qty);
                box.append(label);
                if (current.status !== 'APPROVED') {
                    const resolution = document.createElement('select');
                    resolution.className = 'form-select my-2';
                    resolution.setAttribute('aria-label', 'Condition of selected goods');
                    resolution.dataset.resolution = '';
                    resolution.add(new Option('Received in good condition', 'RECEIVED'));
                    resolution.add(new Option('Damaged', 'DAMAGED'));
                    resolution.add(new Option('Lost', 'LOST'));
                    box.append(resolution);
                }
                if (item.isSerialTracked) {
                    let units = item.units.filter((value) => {
                        return value.resolution === 'IN_TRANSIT';
                    });
                    if (current.status === 'APPROVED' && workspace.dataset.canDispatch === 'true') {
                        units = [];
                        for (let offset = 0; ; offset += 100) {
                            const batch = await requestJson(
                                `/api/transfers/${id}/dispatch-units?lineId=${item.transferItemId}&offset=${offset}`
                            );
                            units.push(...batch);
                            if (batch.length < 100) {
                                break;
                            }
                        }
                    }
                    const scanner = document.createElement('input');
                    scanner.className = 'form-control my-2';
                    scanner.placeholder = 'Scan individual unit label or serial';
                    scanner.setAttribute(
                        'aria-label',
                        'Scan transfer serial for ' + item.productName
                    );
                    box.append(scanner);
                    const choices = document.createElement('div');
                    choices.className = 'inventory-unit-choices';
                    box.append(choices);
                    const update = () => {
                        if (current.status !== 'APPROVED') {
                            qty.value = choices.querySelectorAll('input:checked').length;
                        }
                    };
                    for (const unit of units) {
                        const label2 = document.createElement('label');
                        label2.className = 'd-block';
                        const check = document.createElement('input');
                        check.type = 'checkbox';
                        check.value = unit.productUnitId;
                        check.dataset.unit = '';
                        check.addEventListener('change', update);
                        label2.append(check, document.createTextNode(' ' + unit.serialNumber));
                        choices.append(label2);
                    }
                    StockFlowScanner.bind(
                        scanner,
                        (code) => {
                            const found = units.find((value) => {
                                return (value.barcodes || [value.serialNumber]).some((text) => {
                                    return text && text.trim().toLowerCase() === code.toLowerCase();
                                });
                            });
                            if (!found) {
                                throw new Error(
                                    'This serial is not available for this transfer line.'
                                );
                            }
                            const check = choices.querySelector(
                                `input[value="${found.productUnitId}"]`
                            );
                            if (check.checked) {
                                throw new Error('This serial is already selected.');
                            }
                            check.checked = true;
                            update();
                        },
                        (error) => {
                            return showMessage(error.message, true);
                        }
                    );
                }
            }
            getElement('transfer-lines').append(box);
        }
        getElement('transfer-progress').textContent =
            `${transit} units remain in transit. Goods are available at the destination only after receipt.`;
        const allowed = {
            DRAFT: ['request', 'cancel'],
            REQUESTED: ['approve', 'cancel'],
            APPROVED: ['dispatch', 'cancel'],
            DISPATCHED: ['receive', 'discrepancy'],
            PARTIALLY_RECEIVED: ['receive', 'discrepancy']
        };
        for (const value of document.querySelectorAll('[data-action]')) {
            const a = value.dataset.action;
            value.hidden =
                window.StockFlowLocation <= 0 ||
                !(allowed[current.status] || []).includes(a) ||
                (['approve', 'dispatch', 'cancel'].includes(a) && !source) ||
                (['receive', 'discrepancy'].includes(a) && !destination);
        }
        getElement('transfer-history').replaceChildren();
        for (const op of current.operations || []) {
            const paragraph = document.createElement('p');
            paragraph.textContent = new Date(op.date).toLocaleString() + ' · ' + op.action;
            getElement('transfer-history').append(paragraph);
        }
    }
    async function postSaved() {
        if (!pending) {
            return;
        }
        const saved = pending;
        try {
            const result = await requestJson(saved.url, saved.body);
            if (!result?.transferId) {
                throw new Error(
                    'The transfer result could not be confirmed. Retry this saved action.'
                );
            }
            sessionStorage.removeItem(key);
            pending = null;
            await list();
            await detail(result.transferId);
            showMessage(
                'Transfer action saved. Stock moved only for the recorded dispatch or receipt.'
            );
        } catch (record) {
            if ([400, 422].includes(record.status)) {
                sessionStorage.removeItem(key);
                pending = null;
            }
            throw record;
        }
    }
    async function post(url, body) {
        if (pending || blocked) {
            throw new Error('Resolve the saved transfer action first.');
        }
        pending = {
            url,
            body: {
                ...body,
                requestId: uuid()
            }
        };
        try {
            sessionStorage.setItem(key, JSON.stringify(pending));
        } catch {
            pending = null;
            throw new Error(
                'Enable browser storage before posting transfers so interrupted requests can be retried safely.'
            );
        }
        await postSaved();
    }
    function direction() {
        if (!getElement('transfer-create')) {
            return;
        }
        const incoming = getElement('transfer-direction').value === 'request';
        for (const id of ['transfer-from', 'transfer-to']) {
            const own = id === (incoming ? 'transfer-to' : 'transfer-from');
            getElement(id).replaceChildren(
                ...locations
                    .filter((stockLocation) => {
                        if (own) {
                            return stockLocation.locationId === window.StockFlowLocation;
                        } else {
                            return stockLocation.locationId !== window.StockFlowLocation;
                        }
                    })
                    .map((stockLocation) => {
                        return new Option(
                            `${stockLocation.storeName} / ${stockLocation.locationName}`,
                            stockLocation.locationId
                        );
                    })
            );
            getElement(id).disabled = own;
        }
    }
    function renderCart() {
        getElement('transfer-cart').replaceChildren();
        for (const [index, line] of cart.entries()) {
            const paragraph = document.createElement('p');
            paragraph.textContent = `${line.name} × ${line.quantity} `;
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-secondary';
            button.textContent = 'Remove';
            button.onclick = () => {
                cart.splice(index, 1);
                renderCart();
            };
            paragraph.append(button);
            getElement('transfer-cart').append(paragraph);
        }
    }
    getElement('transfer-direction')?.addEventListener('change', direction);
    getElement('transfer-find')?.addEventListener('click', () => {
        return runAction(async () => {
            variants = await requestJson(
                '/api/transfers/variants?' +
                    new URLSearchParams({
                        term: getElement('transfer-search').value,
                        activeOnly: true
                    })
            );
            getElement('transfer-variant').replaceChildren(
                new Option('Choose item', ''),
                ...variants.map((variant) => {
                    return new Option(variant.productName + ' · ' + StockFlowVariantIdentity.details(variant), variant.variantId);
                })
            );
        });
    });
    getElement('transfer-add')?.addEventListener('click', () => {
        if (busy || pending) {
            return;
        }
        const variant = variants.find((variant2) => {
            return variant2.variantId === Number(getElement('transfer-variant').value);
        });
        const quantity = Number(getElement('transfer-quantity').value);
        if (!variant || !Number.isInteger(quantity) || quantity < 1 || quantity > 10000) {
            return showMessage('Choose an item and a whole quantity from 1 to 10,000.', true);
        }
        if (
            cart.some((variant2) => {
                return variant2.variantId === variant.variantId;
            })
        ) {
            return showMessage('This variant is already in the transfer.', true);
        }
        cart.push({
            variantId: variant.variantId,
            quantity,
            name: [variant.productName,StockFlowVariantIdentity.details(variant)].filter(Boolean).join(' · ')
        });
        renderCart();
    });
    getElement('transfer-create')?.addEventListener('submit', (event) => {
        event.preventDefault();
        runAction(async () => {
            if (!cart.length) {
                throw new Error('Add at least one item.');
            }
            await post('/api/transfers', {
                fromLocationId: Number(getElement('transfer-from').value),
                toLocationId: Number(getElement('transfer-to').value),
                items: cart.map(({ variantId, quantity }) => {
                    return {
                        variantId,
                        quantity
                    };
                }),
                notes: getElement('transfer-notes').value || null
            });
            cart = [];
            renderCart();
            getElement('transfer-new').open = false;
        });
    });
    for (const value of document.querySelectorAll('[data-action]')) {
        value.onclick = () => {
            return runAction(async () => {
                const action = value.dataset.action;
                const items = [];
                if (['dispatch', 'receive', 'discrepancy'].includes(action)) {
                    for (const box of document.querySelectorAll('[data-line]')) {
                        const control = box.querySelector('[data-quantity]');
                        if (!control) {
                            continue;
                        }
                        const quantity = Number(control.value);
                        if (
                            !Number.isInteger(quantity) ||
                            quantity < 0 ||
                            quantity > Number(control.max)
                        ) {
                            throw new Error('Enter quantities within the outstanding amount.');
                        }
                        if (!quantity) {
                            continue;
                        }
                        const resolution =
                            box.querySelector('[data-resolution]')?.value || 'RECEIVED';
                        const unitIds = [...box.querySelectorAll('[data-unit]:checked')].map(
                            (value2) => {
                                return Number(value2.value);
                            }
                        );
                        if (box.querySelector('[data-unit]') && unitIds.length !== quantity) {
                            throw new Error('Select exactly one serial for each dispatched unit.');
                        }
                        if (
                            (action === 'receive' && resolution !== 'RECEIVED') ||
                            (action === 'discrepancy' && resolution === 'RECEIVED')
                        ) {
                            throw new Error(
                                'Use Receive for good goods, or select Damaged/Lost and use Record damage / loss.'
                            );
                        }
                        items.push({
                            transferItemId: Number(box.dataset.line),
                            quantity,
                            resolution,
                            unitIds
                        });
                    }
                    if (!items.length) {
                        throw new Error('Enter a quantity or select serials to receive.');
                    }
                }
                if (action === 'discrepancy' && !getElement('action-notes').value.trim()) {
                    throw new Error('Enter the reason for damage or loss.');
                }
                await post(`/api/transfers/${current.transferId}/${action}`, {
                    items,
                    notes: getElement('action-notes').value || null
                });
            });
        };
    }
    getElement('transfer-filter').addEventListener('change', renderList);
    getElement('transfer-refresh').onclick = () => {
        return runAction(list);
    };
    getElement('transfer-retry').onclick = () => {
        return runAction(postSaved);
    };
    runAction(async () => {
        try {
            pending = JSON.parse(sessionStorage.getItem(key) || 'null');
            if (!pending && sessionStorage.getItem('stockflow-transfer-pending')) {
                blocked = true;
                throw new Error(
                    'An older transfer action is unconfirmed. Reconcile it before posting through the new screen.'
                );
            }
        } catch (value) {
            blocked = true;
            throw value;
        }
        await list();
        if (getElement('transfer-create')) {
            getElement('transfer-new').hidden = window.StockFlowLocation <= 0;
            locations = await requestJson('/api/transfers/locations');
            direction();
        }
        const id = Number(new URLSearchParams(location.search).get('id'));
        if (id > 0) {
            await detail(id);
        }
    });
})();
