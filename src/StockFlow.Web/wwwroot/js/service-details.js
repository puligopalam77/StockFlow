(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const id = Number(location.pathname.split('/').pop());
    let ticket;
    let partsOffset = 0;
    let historyOffset = 0;
    let products = [];
    let generation = 0;
    const edit = getElement('service-edit');
    function summary(item) {
        const target = getElement('service-summary');
        target.replaceChildren();
        const list = document.createElement('dl');
        list.className = 'row';
        for (const [title, value] of [
            ['Status', StockFlowText.label(item.status)],
            ['Customer', item.customerName],
            ['Mobile', item.customerMobile],
            ['Product', item.productName],
            ['Serial', item.serialNumber],
            ['Current warranty', item.warrantyStatus || 'Not registered'],
            ['Under warranty now', item.isUnderWarranty ? 'Yes' : 'No'],
            [
                'Under warranty at receipt',
                item.underWarrantyAtReceipt == null
                    ? 'Not recorded on this older repair request'
                    : item.underWarrantyAtReceipt
                      ? 'Yes'
                      : 'No'
            ],
            ['Complaint', item.complaint],
            ['Diagnosis', item.diagnosis],
            ['Repair details', item.repairDetails],
            ['Technician', item.assignedToName],
            ['Received', ServiceUI.time(item.receivedDate)],
            ['Expected delivery', ServiceUI.date(item.expectedDeliveryDate)],
            ['Delivered', ServiceUI.time(item.deliveredDate)],
            ['Service charge', ServiceUI.money(item.serviceCharge)],
            ['Parts total', ServiceUI.money(item.partsTotal)],
            ['Total charge', ServiceUI.money(item.totalCharge)],
            ['Notes', item.notes]
        ]) {
            const dt = document.createElement('dt');
            const dd = document.createElement('dd');
            dt.className = 'col-sm-4';
            dd.className = 'col-sm-8';
            dd.style.whiteSpace = 'pre-wrap';
            dd.style.overflowWrap = 'anywhere';
            dt.textContent = title;
            dd.textContent = value || '—';
            list.append(dt, dd);
        }
        target.append(list);
        if (item.warrantyId) {
            const a = document.createElement('a');
            a.href = '/Warranty/Details/' + item.warrantyId;
            a.textContent = 'View warranty and claims';
            target.append(a);
        }
    }
    async function parts(append = false) {
        const items = await ServiceUI.api('/api/service/' + id + '/parts?offset=' + partsOffset);
        ServiceUI.table(
            'parts-results',
            ['Part', 'Serial', 'Quantity', 'Unit price', 'Total', 'Issued'],
            items.map((product) => {
                return [
                    product.productName,
                    product.serialNumber,
                    product.quantity,
                    ServiceUI.money(product.unitPrice),
                    ServiceUI.money(product.totalAmount),
                    ServiceUI.time(product.createdAt)
                ];
            }),
            append
        );
        getElement('more-parts').hidden = items.length < 100;
    }
    async function history(append = false) {
        const items = await ServiceUI.api(
            '/api/service/' + id + '/history?offset=' + historyOffset
        );
        ServiceUI.table(
            'history-results',
            ['Date', 'Action', 'Previous', 'New status', 'Remarks', 'User', 'Saved details'],
            items.map((h) => {
                let details = '—';
                if (h.details) {
                    details = document.createElement('details');
                    const label = document.createElement('summary');
                    label.textContent = 'View saved values';
                    const pre = document.createElement('pre');
                    pre.textContent = JSON.stringify(JSON.parse(h.details), null, 2);
                    details.append(label, pre);
                }
                return [
                    ServiceUI.time(h.changedAt),
                    StockFlowText.label(h.actionName),
                    StockFlowText.label(h.oldStatus),
                    StockFlowText.label(h.newStatus),
                    h.remarks,
                    h.changedByName,
                    details
                ];
            }),
            append
        );
        getElement('more-history').hidden = items.length < 100;
    }
    async function load() {
        const current = ++generation;
        if (edit) {
            edit.hidden = true;
        }
        const result = await ServiceUI.api('/api/service/' + id);
        if (current !== generation) {
            return;
        }
        ticket = result;
        getElement('service-title').textContent = ticket.serviceNumber;
        summary(ticket);
        if (edit && !['DELIVERED', 'CANCELLED'].includes(ticket.status)) {
            await ServiceUI.technicians(
                getElement('technician'),
                ticket.assignedTo,
                ticket.assignedToName
            );
            if (current !== generation) {
                return;
            }
            getElement('diagnosis').value = ticket.diagnosis || '';
            getElement('repair').value = ticket.repairDetails || '';
            getElement('charge').value = ticket.serviceCharge;
            getElement('notes').value = ticket.notes || '';
            getElement('expected').value = ticket.expectedDeliveryDate?.slice(0, 10) || '';
            getElement('expected').min = ticket.receivedDate.slice(0, 10);
            getElement('next-status').replaceChildren(
                ...ticket.allowedTransitions
                    .filter((value) => {
                        return value !== 'DELIVERED' || edit.dataset.approve === 'true';
                    })
                    .map((value) => {
                        return new Option(StockFlowText.label(value), value);
                    })
            );
            getElement('part-form').hidden = ![
                'INSPECTION',
                'REPAIRING',
                'WAITING_FOR_PART'
            ].includes(ticket.status);
            edit.hidden = false;
        }
        partsOffset = 0;
        historyOffset = 0;
        await Promise.all([parts(), history()]);
    }
    getElement('reload').onclick = () => {
        return ServiceUI.run(load);
    };
    getElement('more-parts').onclick = () => {
        return ServiceUI.run(async () => {
            partsOffset += 100;
            await parts(true);
        });
    };
    getElement('more-history').onclick = () => {
        return ServiceUI.run(async () => {
            historyOffset += 100;
            await history(true);
        });
    };
    if (edit) {
        ServiceUI.mutation(getElement('details-form'), async (retry) => {
            await ServiceUI.api('/api/service/' + id, {
                method: 'PUT',
                body: JSON.stringify(
                    retry({
                        expectedVersion: ticket.version,
                        diagnosis: getElement('diagnosis').value,
                        repairDetails: getElement('repair').value,
                        serviceCharge: Number(getElement('charge').value),
                        assignedTo: getElement('technician').value
                            ? Number(getElement('technician').value)
                            : null,
                        expectedDeliveryDate: getElement('expected').value || null,
                        notes: getElement('notes').value
                    })
                )
            });
            await load();
        });
        ServiceUI.mutation(getElement('status-form'), async (retry) => {
            await ServiceUI.api('/api/service/' + id + '/status', {
                method: 'PUT',
                body: JSON.stringify(
                    retry({
                        expectedVersion: ticket.version,
                        status: getElement('next-status').value,
                        remarks: getElement('remarks').value
                    })
                )
            });
            await load();
            getElement('remarks').value = '';
        });
        getElement('find-parts').onclick = () => {
            return ServiceUI.run(async () => {
                products = await ServiceUI.api(
                    '/api/products/variants?activeOnly=true&term=' +
                        encodeURIComponent(getElement('part-search').value)
                );
                getElement('part-product').replaceChildren(
                    new Option('Select part', ''),
                    ...products.map((variant) => {
                        return new Option(variant.productName, variant.variantId);
                    })
                );
                partChanged();
            });
        };
        function partChanged() {
            const product = products.find((variant) => {
                return variant.variantId === Number(getElement('part-product').value);
            });
            getElement('part-unit-fields').hidden = !product?.isSerialTracked;
            getElement('part-unit').required = !!product?.isSerialTracked;
            getElement('part-unit').replaceChildren(new Option('Select serial', ''));
            getElement('part-quantity').readOnly = !!product?.isSerialTracked;
            getElement('part-quantity').value = 1;
            getElement('part-price').value = product?.sellingPrice || 0;
        }
        getElement('part-product').onchange = partChanged;
        getElement('find-units').onclick = () => {
            return ServiceUI.run(async () => {
                const variantId = getElement('part-product').value;
                const productId = products.find((variant) => {
                    return String(variant.variantId) === variantId;
                })?.productId;
                if (!productId) {
                    throw new Error('Select a part first.');
                }
                const units = await ServiceUI.api(
                    '/api/inventory/units?availableOnly=true&productId=' +
                        productId +
                        '&variantId=' +
                        variantId +
                        '&term=' +
                        encodeURIComponent(getElement('unit-search').value)
                );
                if (variantId !== getElement('part-product').value) {
                    return;
                }
                getElement('part-unit').replaceChildren(
                    new Option('Select serial', ''),
                    ...units
                        .filter((variant) => {
                            return String(variant.variantId) === variantId;
                        })
                        .map((unit) => {
                            return new Option(
                                unit.serialNumber || unit.barcode,
                                unit.productUnitId
                            );
                        })
                );
            });
        };
        ServiceUI.mutation(getElement('part-form'), async (retry) => {
            const product = products.find((variant) => {
                return variant.variantId === Number(getElement('part-product').value);
            });
            if (!product) {
                throw new Error('Select an inventory part.');
            }
            await ServiceUI.api('/api/service/' + id + '/parts', {
                method: 'POST',
                body: JSON.stringify(
                    retry({
                        expectedVersion: ticket.version,
                        productId: product.productId,
                        variantId: product.variantId,
                        productUnitId: product.isSerialTracked
                            ? Number(getElement('part-unit').value)
                            : null,
                        quantity: Number(getElement('part-quantity').value),
                        unitPrice: Number(getElement('part-price').value)
                    })
                )
            });
            await load();
            getElement('part-form').reset();
            partChanged();
        });
    }
    ServiceUI.run(load);
})();
