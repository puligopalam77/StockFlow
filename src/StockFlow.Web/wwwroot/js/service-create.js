(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    let choices = [];
    let context = null;
    let ready = false;
    let scanGeneration = 0;
    function clear() {
        context = null;
        choices = [];
        for (const name of ['product', 'customer']) {
            getElement(name).disabled = false;
            getElement(name).replaceChildren(new Option('Select ' + name, ''));
        }
        getElement('find-products').disabled = false;
        getElement('find-customers').disabled = false;
        getElement('scan-context').textContent =
            'Scan the serial number on the sold item, or choose a product without serial numbers.';
    }
    async function scan(code) {
        if (!ready) {
            throw new Error(
                'Technician list is still loading. Retry after the form becomes available.'
            );
        }
        const generation = ++scanGeneration;
        clear();
        getElement('create-fields').disabled = true;
        try {
            const match = await ServiceUI.api('/api/scan?code=' + encodeURIComponent(code));
            if (generation !== scanGeneration) {
                return;
            }
            if (!match.productUnitId) {
                if (match.isSerialTracked) {
                    throw new Error(
                        'This is a product barcode. Scan the sold unit serial or unit barcode.'
                    );
                }
                choices = [match];
                getElement('product').replaceChildren(
                    new Option(match.productName, match.variantId)
                );
                getElement('scan-context').textContent =
                    match.productName + ': select the customer for this quantity-only product.';
                return;
            }
            const item = await ServiceUI.api('/api/service/units/' + match.productUnitId);
            if (generation !== scanGeneration) {
                return;
            }
            if (
                item.saleStatus !== 'COMPLETED' ||
                !['SOLD', 'REPLACEMENT_ISSUED'].includes(item.unitStatus)
            ) {
                throw new Error(
                    'This unit is not available for service intake. It must be sold and must not already be in service.'
                );
            }
            context = item;
            getElement('product').replaceChildren(new Option(item.productName, item.variantId));
            getElement('product').disabled = true;
            getElement('find-products').disabled = true;
            if (item.customerId) {
                getElement('customer').replaceChildren(
                    new Option(item.customerName || 'Sale customer', item.customerId)
                );
                getElement('customer').disabled = true;
                getElement('find-customers').disabled = true;
            }
            const warranty = item.warranty;
            getElement('scan-context').textContent = [
                item.productName,
                'Serial: ' + (item.serialNumber || '—'),
                'Customer: ' + (item.customerName || 'Walk-in sale: select customer'),
                'Mobile: ' + (item.customerMobile || '—'),
                'Invoice: ' + item.invoiceNumber,
                'Warranty: ' +
                    (warranty
                        ? StockFlowText.label(warranty.warrantyStatus) +
                          (warranty.isUnderWarranty ? ' (under warranty)' : ' (not under warranty)')
                        : 'Not registered')
            ].join(' | ');
        } finally {
            if (generation === scanGeneration) {
                getElement('create-fields').disabled = !ready;
            }
        }
    }
    const submit = StockFlowScanner.bind('service-scan', (code) => {
        return ServiceUI.run(() => {
            return scan(code);
        });
    });
    getElement('scan-form').onsubmit = (event) => {
        event.preventDefault();
        submit();
    };
    getElement('clear-scan').onclick = () => {
        scanGeneration++;
        clear();
        getElement('create-fields').disabled = !ready;
    };
    getElement('find-products').onclick = () => {
        return ServiceUI.run(async () => {
            const generation = scanGeneration;
            const items = await ServiceUI.api(
                '/api/products/variants?activeOnly=true&term=' +
                    encodeURIComponent(getElement('product-search').value)
            );
            if (generation !== scanGeneration || context) {
                return;
            }
            choices = items;
            getElement('product').replaceChildren(
                new Option('Select quantity-only product', ''),
                ...items
                    .filter((value) => {
                        return !value.isSerialTracked;
                    })
                    .map((variant) => {
                        return new Option(variant.productName, variant.variantId);
                    })
            );
        });
    };
    getElement('find-customers').onclick = () => {
        return ServiceUI.run(async () => {
            const generation = scanGeneration;
            const items = await ServiceUI.api(
                '/api/customers/search?activeOnly=true&term=' +
                    encodeURIComponent(getElement('customer-search').value)
            );
            if (generation !== scanGeneration || context?.customerId) {
                return;
            }
            getElement('customer').replaceChildren(
                new Option('Select customer', ''),
                ...items.map((value) => {
                    return new Option(
                        value.customerName + ' / ' + (value.mobileNumber || ''),
                        value.customerId
                    );
                })
            );
        });
    };
    ServiceUI.mutation(getElement('service-create'), async (retry) => {
        const payload = retry({
            locationId: window.StockFlowLocation,
            customerId: Number(getElement('customer').value),
            productId:
                context?.productId ||
                choices.find((variant) => {
                    return variant.variantId === Number(getElement('product').value);
                })?.productId,
            variantId: Number(getElement('product').value),
            productUnitId: context?.productUnitId || null,
            warrantyId: context?.warranty?.warrantyId || null,
            complaint: getElement('complaint').value,
            assignedTo: getElement('technician').value
                ? Number(getElement('technician').value)
                : null,
            expectedDeliveryDate: getElement('expected').value || null,
            notes: getElement('notes').value
        });
        const result = await ServiceUI.api('/api/service', {
            method: 'POST',
            body: JSON.stringify(payload)
        });
        location.href = '/Service/Details/' + result.serviceRequestId;
    });
    ServiceUI.run(async () => {
        await ServiceUI.technicians(getElement('technician'));
        ready = true;
        getElement('create-fields').disabled = false;
        const code = new URLSearchParams(location.search).get('serial');
        if (code) {
            await scan(code);
        }
    });
})();
