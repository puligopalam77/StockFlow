// Record payments, returns, delivery updates, stock counts, and repair activity.
(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const money = (value) => {
        return Number(value || 0).toFixed(2);
    };
    async function requestJson(path, options = {}) {
        const response = await fetch(path, {
            credentials: 'same-origin',
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || '',
                ...options.headers
            }
        });
        const data = await response.json().catch(() => {
            return null;
        });
        if (!response.ok) {
            throw new Error(
                data?.message ||
                    (data?.errors
                        ? Object.values(data.errors).flat().join(' ')
                        : response.status === 403
                          ? 'You do not have permission for this action.'
                          : 'We could not finish this action. Check saved records before trying again. If the problem continues, contact the shop owner or administrator.')
            );
        }
        return data;
    }
    function message(error) {
        const target =
            getElement('sale-message') ||
            document.querySelector('.pd-dialog[open] [role="alert"]') ||
            getElement('purchase-message') ||
            getElement('inventory-message') ||
            getElement('service-message') ||
            getElement('warranty-message');
        if (target) {
            target.textContent = error.message;
            target.hidden = false;
            target.scrollIntoView({
                block: 'center'
            });
        }
    }
    function bind(form, build, save) {
        if (!form) {
            return;
        }
        let requestId = '';
        let signature = '';
        let busy = false;
        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            if (busy) {
                return;
            }
            const body = build();
            const next = JSON.stringify(body);
            if (next !== signature) {
                signature = next;
                requestId = crypto.randomUUID();
            }
            body.requestId = requestId;
            body.locationId = window.StockFlowLocation;
            busy = true;
            [...form.elements].forEach((value) => {
                return (value.disabled = true);
            });
            try {
                await save(body);
                signature = '';
                requestId = '';
            } catch (error) {
                message(error);
            } finally {
                busy = false;
                [...form.elements].forEach((value) => {
                    return (value.disabled = false);
                });
            }
        });
    }
    function history(target, rows) {
        if (!target) {
            return;
        }
        target.replaceChildren();
        if (!rows.length) {
            target.textContent = 'No activity recorded.';
            return;
        }
        const table = document.createElement('table');
        table.className = 'table table-sm';
        const head = table.createTHead().insertRow();
        ['Date', 'Activity', 'Status', 'Amount', 'Reference', 'Notes'].forEach((value) => {
            const th = document.createElement('th');
            th.textContent = value;
            head.append(th);
        });
        const body = table.createTBody();
        rows.forEach((record) => {
            const tr = body.insertRow();
            [
                target.id==='sale-operation-history'
                    ? new Date(record.date).toLocaleString('en-IN',{timeZone:getElement('sale-operations').dataset.timeZone||'Asia/Kolkata'})
                    : new Date(record.date).toLocaleString(),
                StockFlowText.label(record.type),
                record.status==='CARD'&&['DEBIT','CREDIT'].includes(record.cardType) ? (record.cardType==='DEBIT'?'Debit Card':'Credit Card') : record.status==='CREDIT'?'Customer Credit / Pay Later':StockFlowText.label(record.status),
                money(record.amount)+(record.refundedAmount!=null?' (refund '+money(record.refundedAmount)+')':''),
                record.reference || '—',
                record.notes || '—'
            ].forEach((value) => {
                const td = tr.insertCell();
                td.textContent = value;
            });
        });
        if(target.id==='sale-operation-history'){
            const scroll=document.createElement('div');scroll.className='table-responsive';scroll.append(table);target.append(scroll);
        }else target.append(table);
    }
    const pathId = () => {
        return Number(location.pathname.split('/').filter(Boolean).pop());
    };
    async function sales() {
        if (!getElement('sale-operations')) {
            return;
        }
        const id = pathId();
        const [sale, ops] = await Promise.all([
            requestJson('/api/sales/' + id),
            requestJson('/api/operations/sales/' + id)
        ]);
        getElement('sale-account-summary').textContent =
            `Invoice ${money(ops.documentTotal)} · Returns ${money(ops.returnedTotal)} · Received ${money(ops.paidTotal)} · Refunded ${money(ops.refundedTotal)} · Outstanding ${money(ops.outstanding)}`;
        history(
            getElement('sale-operation-history'),
            [...ops.payments, ...ops.returns, ...ops.fulfilments, ...ops.tradeIns].sort(
                (a, value) => {
                    return new Date(value.date) - new Date(a.date);
                }
            )
        );
        const returned=getElement('sale-return-history');
        if(returned&&ops.returnedLines?.length){
            const heading=document.createElement('h3');heading.className='h6';heading.textContent='Returned invoice items';returned.replaceChildren(heading);
            const table=document.createElement('table');table.className='table table-sm';
            const header=table.createTHead().insertRow();for(const title of ['Return','Product','SKU','Quantity','Condition']){const th=document.createElement('th');th.textContent=title;header.append(th);}
            const body=table.createTBody();for(const line of ops.returnedLines){const row=body.insertRow();for(const value of [line.returnId,line.productName,line.sku||'\u2014',line.quantity,StockFlowText.label(line.disposition)])row.insertCell().textContent=value;}
            const scroll=document.createElement('div');scroll.className='table-responsive';scroll.append(table);returned.append(scroll);
        }
        if (getElement('customer-payment-amount')) {
            getElement('customer-payment-amount').max = ops.outstanding;
            getElement('customer-payment-amount').value = ops.outstanding || '';
            getElement('sale-collect-payment').hidden = sale.status!=='COMPLETED'||ops.outstanding<=0;
            if(new URLSearchParams(location.search).get('collect')==='1'&&!getElement('sale-collect-payment').hidden){
                getElement('sale-collect-payment').open=true;
                getElement('customer-payment-amount').focus();
            }
        }
        if(new URLSearchParams(location.search).get('return')==='1'&&getElement('sale-return-details'))getElement('sale-return-details').open=true;
        if (getElement('sale-return-item')) {
            getElement('sale-return-item').replaceChildren(
                new Option('Choose invoice item', ''),
                ...sale.lines.map((product) => {
                    return new Option(
                        `${product.productName}${product.serialNumber ? ' / ' + product.serialNumber : ''} · sold ${product.quantity}`,
                        product.saleItemId
                    );
                })
            );
            const disposition=getElement('sale-return-disposition');
            if(disposition){
                const nonStock=new Option('Manual item — no stock posting','NON_STOCK');disposition.add(nonStock);
                getElement('sale-return-item').addEventListener('change',()=>{
                    const manual=sale.lines.find(line=>String(line.saleItemId)===getElement('sale-return-item').value)?.isManual===true;
                    nonStock.hidden=!manual;disposition.value=manual?'NON_STOCK':'RESTOCK';disposition.disabled=manual;
                });nonStock.hidden=true;
            }
        }
        bind(
            getElement('customer-payment-form'),
            () => {
                return {
                    amount: Number(getElement('customer-payment-amount').value),
                    paymentMode: getElement('customer-payment-mode').value.split(':')[0],
                    ...(getElement('customer-payment-mode').value.includes(':')?{cardType:getElement('customer-payment-mode').value.split(':')[1]}:{}),
                    referenceNumber: getElement('customer-payment-reference').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/sales/${id}/payments`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
        bind(
            getElement('sale-return-form'),
            () => {
                return {
                    refundMode: getElement('sale-return-refund-mode').value,
                    refundAmount: Number(getElement('sale-return-refund').value || 0),
                    notes: getElement('sale-return-notes').value,
                    items: [
                        {
                            saleItemId: Number(getElement('sale-return-item').value),
                            quantity: Number(getElement('sale-return-quantity').value),
                            disposition: getElement('sale-return-disposition').value
                        }
                    ]
                };
            },
            async (body) => {
                await requestJson(`/api/operations/sales/${id}/returns`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
        bind(
            getElement('fulfilment-form'),
            () => {
                return {
                    fulfilmentType: getElement('fulfilment-type').value,
                    status: getElement('fulfilment-status').value,
                    scheduledAt: getElement('fulfilment-date').value,
                    address: getElement('fulfilment-address').value,
                    contactNumber: getElement('fulfilment-contact').value,
                    technicianName: getElement('fulfilment-technician').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/sales/${id}/fulfilments`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
        bind(
            getElement('trade-in-form'),
            () => {
                return {
                    itemType: getElement('trade-in-type').value,
                    brandModel: getElement('trade-in-model').value,
                    serialNumber: getElement('trade-in-serial').value,
                    allowanceAmount: Number(getElement('trade-in-allowance').value || 0),
                    status: getElement('trade-in-status').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/sales/${id}/trade-ins`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
    }
    async function purchases() {
        if (!getElement('purchase-settlement')) {
            return;
        }
        const id = pathId();
        const [purchase, ops] = await Promise.all([
            window.StockFlowPurchaseDocument || requestJson('/api/purchases/' + id),
            requestJson('/api/operations/purchases/' + id)
        ]);
        const displayMoney = window.StockFlowPurchaseDashboard?.money || money;
        getElement('purchase-account-summary').textContent =
            `Purchase ${displayMoney(ops.documentTotal)} \u00b7 Returns ${displayMoney(ops.returnedTotal)} \u00b7 Paid ${displayMoney(ops.paidTotal)} \u00b7 Supplier balance ${displayMoney(ops.outstanding)}`;
        history(
            getElement('purchase-operation-history'),
            [...ops.payments, ...ops.returns].sort((a, value) => {
                return new Date(value.date) - new Date(a.date);
            })
        );
        window.dispatchEvent(new CustomEvent('purchase-settlement-loaded', { detail: ops }));
        const payButton = getElement('purchase-pay-open');
        if (payButton) payButton.disabled = ops.outstanding <= 0 || purchase.status === 'CANCELLED';
        const returnButton = getElement('purchase-return-open');
        if (returnButton) returnButton.disabled = !purchase.items.some(item => item.receivedQuantity > 0) || purchase.status === 'CANCELLED';
        if (getElement('supplier-payment-amount')) {
            getElement('supplier-payment-amount').max = ops.outstanding;
            getElement('supplier-payment-amount').value = ops.outstanding || '';
        }
        const itemSelect = getElement('purchase-return-item');
        if (itemSelect) {
            itemSelect.replaceChildren(
                new Option('Choose received item', ''),
                ...purchase.items
                    .filter((value) => {
                        return value.receivedQuantity > 0;
                    })
                    .map((product) => {
                        return new Option(
                            `${product.productName} · received ${product.receivedQuantity}`,
                            product.purchaseItemId
                        );
                    })
            );
            itemSelect.onchange = async () => {
                const line = purchase.items.find((value) => {
                    return value.purchaseItemId === Number(itemSelect.value);
                });
                const wrap = getElement('purchase-return-unit-wrap');
                const select = getElement('purchase-return-unit');
                wrap.hidden = !line?.isSerialTracked;
                getElement('purchase-return-quantity').readOnly = !!line?.isSerialTracked;
                getElement('purchase-return-quantity').value = 1;
                if (line?.isSerialTracked) {
                    const units = await requestJson(
                        `/api/inventory/units?productId=${line.productId}&variantId=${line.variantId}&availableOnly=true`
                    );
                    select.replaceChildren(
                        new Option('Choose serial', ''),
                        ...units.map((unit) => {
                            return new Option(
                                unit.serialNumber || unit.barcode,
                                unit.productUnitId
                            );
                        })
                    );
                }
            };
        }
        bind(
            getElement('supplier-payment-form'),
            () => {
                return {
                    amount: Number(getElement('supplier-payment-amount').value),
                    paymentMode: getElement('supplier-payment-mode').value,
                    referenceNumber: getElement('supplier-payment-reference').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/purchases/${id}/payments`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
        bind(
            getElement('purchase-return-form'),
            () => {
                return {
                    supplierReference: getElement('purchase-return-reference').value,
                    notes: getElement('purchase-return-reason').value,
                    items: [
                        {
                            purchaseItemId: Number(itemSelect.value),
                            productUnitId: getElement('purchase-return-unit-wrap').hidden
                                ? null
                                : Number(getElement('purchase-return-unit').value),
                            quantity: Number(getElement('purchase-return-quantity').value),
                            reason: getElement('purchase-return-reason').value
                        }
                    ]
                };
            },
            async (body) => {
                await requestJson(`/api/operations/purchases/${id}/returns`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
    }
    async function stockCount() {
        const form = getElement('stock-count-form');
        if (!form) {
            return;
        }
        const rows = await requestJson('/api/inventory');
        const select = getElement('stock-count-variant');
        select.replaceChildren(
            new Option('Choose product', ''),
            ...rows.map((variant) => {
                return new Option(
                    `${variant.productName} · system ${variant.currentStock}${variant.isSerialTracked ? ' · serialized' : ''}`,
                    variant.variantId
                );
            })
        );
        select.onchange = () => {
            const row = rows.find((variant) => {
                return variant.variantId === Number(select.value);
            });
            getElement('stock-count-expected').value = row?.currentStock ?? '';
            getElement('stock-count-quantity').value = row?.currentStock ?? '';
        };
        bind(
            form,
            () => {
                const row = rows.find((variant) => {
                    return variant.variantId === Number(select.value);
                });
                return {
                    notes: getElement('stock-count-notes').value,
                    items: [
                        {
                            productId: row?.productId,
                            variantId: row?.variantId,
                            countedQuantity: Number(getElement('stock-count-quantity').value),
                            remarks: getElement('stock-count-notes').value
                        }
                    ]
                };
            },
            async (body) => {
                const result = await requestJson('/api/operations/stock-counts', {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                getElement('stock-count-result').textContent = 'Stock count posted: ' + result.id;
                setTimeout(() => {
                    return location.reload();
                }, 700);
            }
        );
    }
    async function service() {
        const form = getElement('service-payment-form');
        if (!getElement('service-account-summary')) {
            return;
        }
        const id = pathId();
        const ops = await requestJson('/api/operations/service/' + id);
        getElement('service-account-summary').textContent =
            `Charges ${money(ops.documentTotal)} · Collected ${money(ops.paidTotal)} · Outstanding ${money(ops.outstanding)}`;
        history(getElement('service-payment-history'), ops.payments);
        if (getElement('service-payment-amount')) {
            getElement('service-payment-amount').max = ops.outstanding;
            getElement('service-payment-amount').value = ops.outstanding || '';
        }
        bind(
            form,
            () => {
                return {
                    amount: Number(getElement('service-payment-amount').value),
                    paymentMode: getElement('service-payment-mode').value,
                    referenceNumber: getElement('service-payment-reference').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/service/${id}/payments`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                location.reload();
            }
        );
    }
    async function warranty() {
        const form = getElement('warranty-rma-form');
        if (!form) {
            return;
        }
        const warrantyId = pathId();
        const claims = await requestJson(`/api/warranty/claims?warrantyId=${warrantyId}&offset=0`);
        const select = getElement('warranty-rma-claim');
        select.replaceChildren(
            new Option('Choose claim', ''),
            ...claims.map((value) => {
                return new Option(
                    `${value.claimNumber} · ${value.claimStatus}`,
                    value.warrantyClaimId
                );
            })
        );
        async function load() {
            if (!select.value) {
                getElement('warranty-rma-history').textContent =
                    'Choose a claim to view manufacturer activity.';
                return;
            }
            history(
                getElement('warranty-rma-history'),
                await requestJson(`/api/operations/warranty-claims/${select.value}/rmas`)
            );
        }
        select.onchange = () => {
            return load().catch(message);
        };
        bind(
            form,
            () => {
                return {
                    providerReference: getElement('warranty-rma-reference').value,
                    status: getElement('warranty-rma-status').value,
                    chargeAmount: Number(getElement('warranty-rma-charge').value || 0),
                    notes: getElement('warranty-rma-notes').value
                };
            },
            async (body) => {
                await requestJson(`/api/operations/warranty-claims/${select.value}/rmas`, {
                    method: 'POST',
                    body: JSON.stringify(body)
                });
                await load();
                form.reset();
            }
        );
        await load();
    }
    Promise.allSettled([sales(), purchases(), stockCount(), service(), warranty()]).then(
        (results) => {
            const failed = results.find((record) => {
                return record.status === 'rejected';
            });
            if (failed) {
                message(failed.reason);
            }
        }
    );
})();
