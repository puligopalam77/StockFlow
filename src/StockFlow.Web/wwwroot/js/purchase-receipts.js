(async () => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const id = location.pathname.split('/').filter(Boolean).pop();
    const storage = 'stockflow-receipt-' + id;
    try {
        const [purchase, receipts] = await Promise.all([
            window.StockFlowPurchaseDocument || Purchases.api('/api/purchases/' + id),
            Purchases.api('/api/purchases/' + id + '/receipts')
        ]);
        const container = getElement('receipt-history');
        container.replaceChildren();
        getElement('purchase-receipt-count').textContent = '(' + receipts.length + ')';
        if (receipts.length) {
            const table = document.createElement('table'); table.className = 'table pd-table';
            const head = table.createTHead().insertRow();
            for (const label of ['GRN No.', 'Received date', 'Good / damaged', 'Status', 'Action']) {
                const th = document.createElement('th'); th.scope = 'col'; th.textContent = label; head.append(th);
            }
            const body = table.createTBody();
            receipts.forEach((receipt, index) => {
                const row = body.insertRow(); row.hidden = index >= 3;
                const href = '/StockInflow/Details/PURCHASE/' + receipt.receiptId;
                const link = document.createElement('a'); link.href = href; link.textContent = receipt.receiptNumber;
                Purchases.cell(row, '').append(link);
                Purchases.cell(row, window.StockFlowPurchaseDashboard.date(receipt.receivedDate));
                Purchases.cell(row, receipt.quantity + ' / ' + receipt.damagedQuantity);
                const badge = document.createElement('span'); badge.className = 'pd-status'; badge.dataset.status = receipt.status;
                badge.textContent = StockFlowText.label(receipt.status); Purchases.cell(row, '').append(badge);
                const view = document.createElement('a'); view.href = href; view.className = 'btn btn-outline-secondary'; view.textContent = 'View';
                Purchases.cell(row, '').append(view);
            });
            container.append(table);
            const all = getElement('purchase-receipts-all'); all.hidden = receipts.length <= 3;
            all.onclick = () => {
                const expand = all.textContent === 'View all';
                Array.from(body.rows).forEach((row, index) => row.hidden = !expand && index >= 3);
                all.textContent = expand ? 'Show less' : 'View all';
            };
        }
        if (!receipts.length) {
            getElement('receipt-history').textContent =
                purchase.status === 'COMPLETED'
                    ? 'Legacy purchase: already fully received.'
                    : 'No goods received yet.';
        }
        if (!getElement('receipt-form')) {
            return;
        }
        let pending = JSON.parse(sessionStorage.getItem(storage) || 'null');
        if ((!['DRAFT', 'PARTIALLY_RECEIVED'].includes(purchase.status) || !purchase.items.some(item => item.quantity > item.receivedQuantity + item.damagedQuantity)) && !pending) {
            return;
        }
        getElement('receipt-form').hidden = false;
        const receiveButton = getElement('purchase-receive-open');
        receiveButton.hidden = false;
        receiveButton.textContent = pending ? 'Retry pending receipt' : 'Receive Stock';
        receiveButton.onclick = () => getElement('purchase-receive-dialog').showModal();
        const date = new Date();
        date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
        getElement('receipt-date').value = pending?.receivedDate || date.toISOString().slice(0, 10);
        getElement('receipt-invoice').value = pending?.supplierInvoiceNumber || '';
        getElement('receipt-invoice-date').value = pending?.supplierInvoiceDate || '';
        getElement('receipt-notes').value = pending?.notes || '';
        const rows = [];
        for (const item of purchase.items) {
            const saved = pending?.items.find((value) => {
                return value.purchaseItemId === item.purchaseItemId;
            });
            const remaining = item.quantity - item.receivedQuantity - item.damagedQuantity;
            if (remaining <= 0 && !saved) {
                continue;
            }
            const section = document.createElement('section');
            section.className = 'order-receipt-item';
            const label = document.createElement('label');
            label.textContent = `${item.productName}: expected ${item.quantity}, good ${item.receivedQuantity}, damaged ${item.damagedQuantity}, remaining ${remaining}`;
            const quantity = document.createElement('input');
            quantity.type = 'number';
            quantity.min = '0';
            quantity.max = String(Math.max(remaining, saved?.quantity || 0));
            quantity.value = saved?.quantity || 0;
            quantity.className = 'form-control';
            quantity.id = 'receive-' + item.purchaseItemId;
            label.htmlFor = quantity.id;
            section.append(label, quantity);
            const damaged = document.createElement('input');
            damaged.type = 'number';
            damaged.min = '0';
            damaged.max = String(Math.max(remaining, saved?.damagedQuantity || 0));
            damaged.value = saved?.damagedQuantity || 0;
            damaged.className = 'form-control mt-2';
            damaged.setAttribute('aria-label', 'Damaged quantity for ' + item.productName);
            section.append(damaged);
            const serials = document.createElement('textarea');
            serials.className = 'form-control mt-2';
            serials.placeholder = 'One serial number per good unit';
            serials.setAttribute('aria-label', 'Good serial numbers for ' + item.productName);
            serials.hidden = !item.isSerialTracked;
            serials.value = saved?.serialNumbers.join('\n') || '';
            section.append(serials);
            const damagedSerials = document.createElement('textarea');
            damagedSerials.className = 'form-control mt-2';
            damagedSerials.placeholder = 'One serial number per damaged unit';
            damagedSerials.setAttribute(
                'aria-label',
                'Damaged serial numbers for ' + item.productName
            );
            damagedSerials.hidden = !item.isSerialTracked;
            damagedSerials.value = saved?.damagedSerialNumbers?.join('\n') || '';
            section.append(damagedSerials);
            const batch = document.createElement('input');
            batch.className = 'form-control mt-2';
            batch.maxLength = 100;
            batch.placeholder = 'Batch / lot number';
            batch.value = saved?.batchNumber || item.batchNumber || '';
            section.append(batch);
            const manufacturing = document.createElement('input');
            manufacturing.type = 'date';
            manufacturing.className = 'form-control mt-2';
            manufacturing.setAttribute('aria-label', 'Manufacturing date for ' + item.productName);
            manufacturing.value =
                saved?.manufacturingDate || item.manufacturingDate?.slice(0, 10) || '';
            section.append(manufacturing);
            const expiry = document.createElement('input');
            expiry.type = 'date';
            expiry.className = 'form-control mt-2';
            expiry.setAttribute('aria-label', 'Expiry date for ' + item.productName);
            expiry.value = saved?.expiryDate || item.expiryDate?.slice(0, 10) || '';
            section.append(expiry);
            const caption = document.createElement('p');
            caption.className = 'order-receipt-item-caption';
            caption.textContent = label.textContent;
            section.replaceChildren(caption);
            const fields = document.createElement('div');
            fields.className = 'order-receipt-grid';
            for (const [input, caption, suffix] of [
                [quantity, 'Received (good)', 'good'],
                [damaged, 'Damaged quantity', 'damaged'],
                [batch, 'Batch / lot number', 'batch'],
                [manufacturing, 'Manufacturing date', 'manufactured'],
                [expiry, 'Expiry date', 'expiry'],
                [serials, 'Good unit serials (one per line)', 'serials'],
                [damagedSerials, 'Damaged unit serials (one per line)', 'damaged-serials']
            ]) {
                const field = document.createElement('div');
                field.hidden = input.hidden;
                input.id ||= `receive-${item.purchaseItemId}-${suffix}`;
                input.classList.remove('mt-2');
                const title = document.createElement('label');
                title.htmlFor = input.id;
                title.textContent = caption;
                field.append(title, input);
                fields.append(field);
            }
            section.append(fields);
            getElement('receipt-lines').append(section);
            rows.push({
                item,
                quantity,
                damaged,
                serials,
                damagedSerials,
                batch,
                manufacturing,
                expiry
            });
        }
        function locked() {
            for (const input of getElement('receipt-form').querySelectorAll('input,textarea')) {
                input.disabled = !!pending;
            }
            getElement('receipt-submit').textContent = pending
                ? 'Retry pending receipt'
                : 'Post stock receipt';
        }
        locked();
        getElement('receipt-form').onsubmit = async (event) => {
            event.preventDefault();
            if (getElement('receipt-submit').disabled) return;
            getElement('receipt-submit').disabled = true;
            try {
                for (const value of rows) {
                    if (
                        Number(value.quantity.value) + Number(value.damaged.value) >
                        Number(value.quantity.max)
                    ) {
                        throw new Error(
                            'Good plus damaged quantity cannot exceed the remaining quantity.'
                        );
                    }
                }
                pending ??= {
                    locationId: window.StockFlowLocation,
                    requestId: crypto.randomUUID(),
                    receivedDate: getElement('receipt-date').value,
                    supplierInvoiceNumber: getElement('receipt-invoice').value,
                    supplierInvoiceDate: getElement('receipt-invoice-date').value || null,
                    notes: getElement('receipt-notes').value,
                    items: rows
                        .filter((value) => {
                            return Number(value.quantity.value) + Number(value.damaged.value) > 0;
                        })
                        .map((value) => {
                            return {
                                purchaseItemId: value.item.purchaseItemId,
                                quantity: Number(value.quantity.value),
                                damagedQuantity: Number(value.damaged.value),
                                serialNumbers: value.item.isSerialTracked
                                    ? value.serials.value
                                          .split(/\r?\n/)
                                          .map((text) => {
                                              return text.trim();
                                          })
                                          .filter(Boolean)
                                    : [],
                                damagedSerialNumbers: value.item.isSerialTracked
                                    ? value.damagedSerials.value
                                          .split(/\r?\n/)
                                          .map((text) => {
                                              return text.trim();
                                          })
                                          .filter(Boolean)
                                    : [],
                                unitOfMeasure: value.item.unitOfMeasure,
                                packQuantity: value.item.packQuantity,
                                mrp: value.item.mrp,
                                supplierProductCode: value.item.supplierProductCode,
                                batchNumber: value.batch.value,
                                manufacturingDate: value.manufacturing.value || null,
                                expiryDate: value.expiry.value || null
                            };
                        })
                };
                if (!pending.items.length) {
                    throw new Error('Enter a good or damaged quantity for at least one item.');
                }
                sessionStorage.setItem(storage, JSON.stringify(pending));
                locked();
                await Purchases.api('/api/purchases/' + id + '/receipts', {
                    method: 'POST',
                    body: JSON.stringify(pending)
                });
                sessionStorage.removeItem(storage);
                location.reload();
            } catch (error) {
                if ([400, 401, 403, 409].includes(error.status)) {
                    pending = null;
                    sessionStorage.removeItem(storage);
                }
                Purchases.error(error);
                locked();
                getElement('receipt-submit').disabled = false;
            }
        };
    } catch (error) {
        Purchases.error(error);
    }
})();
