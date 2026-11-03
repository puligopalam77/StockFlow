(() => {
    const input = document.getElementById('scanInput');
    const message = document.getElementById('scanMessage');
    const card = document.getElementById('resultCard');
    function feedback(text, kind) {
        message.className = 'mt-3 alert alert-' + kind;
        message.textContent = text;
    }
    const submit = StockFlowScanner.bind(
        input,
        async (code) => {
            card.hidden = true;
            feedback('Searching for ' + code + '…', 'info');
            document.getElementById('scan-warranty-status').textContent = '';
            document.getElementById('scan-warranty').hidden = true;
            const serviceLink = document.getElementById('scan-service');
            if (serviceLink) {
                serviceLink.hidden = true;
            }
            const response = await fetch('/api/scan?code=' + encodeURIComponent(code));
            if (response.status === 401) {
                throw new Error('Your session expired. Sign in again.');
            }
            if (response.status === 403) {
                throw new Error('You do not have access to scanning.');
            }
            if (response.status >= 500) {
                throw new Error('Lookup is temporarily unavailable. Please try again.');
            }
            const item = await response.json();
            if (!response.ok) {
                throw new Error(item.message || 'Code could not be found.');
            }
            for (const field of [
                'productName',
                'matchType',
                'productCode',
                'sku',
                'serialSource',
                'categoryName',
                'brandName',
                'model',
                'productBarcode',
                'serialNumber',
                'unitBarcode',
                'unitStatus',
                'currentStock',
                'sellingPrice',
                'isSerialTracked'
            ]) {
                document.getElementById(field).textContent =
                    typeof item[field] === 'boolean'
                        ? item[field]
                            ? 'Yes'
                            : 'No'
                        : ['matchType', 'unitStatus'].includes(field)
                          ? StockFlowText.label(item[field])
                          : (item[field] ?? '—');
            }
            document.getElementById('serialSource').textContent=item.productUnitId ? ({MANUFACTURER:'Manufacturer serial',INTERNAL:'Internal unit identifier',LEGACY_UNKNOWN:'Unverified historical source'}[item.serialSource]||'Unverified historical source') : 'Not applicable';
            card.hidden = false;
            const invoiceLink = document.getElementById('scan-invoice');
            invoiceLink.hidden = !item.saleId;
            if (item.saleId) {
                invoiceLink.href = '/Sales/Invoice/' + item.saleId;
            }
            if (
                serviceLink &&
                item.serialNumber &&
                ['SOLD', 'REPLACEMENT_ISSUED'].includes(item.unitStatus)
            ) {
                serviceLink.href =
                    '/Service/Create?serial=' + encodeURIComponent(item.serialNumber);
                serviceLink.hidden = false;
            }
            if (item.productUnitId) {
                const status = document.getElementById('scan-warranty-status');
                try {
                    const response = await fetch('/api/warranty/unit/' + item.productUnitId);
                    if (response.status === 404) {
                        status.textContent = 'No registered warranty for this unit.';
                    } else if (!response.ok) {
                        status.textContent =
                            'Warranty lookup unavailable. Open Warranty lookup to retry.';
                    } else {
                        const warranty = await response.json();
                        status.textContent =
                            StockFlowText.label(warranty.warrantyStatus) +
                            ' — ' +
                            (warranty.isUnderWarranty ? 'Under warranty' : 'Not under warranty') +
                            ' — expires ' +
                            warranty.warrantyEndDate.slice(0, 10);
                        const link = document.getElementById('scan-warranty');
                        link.href = '/Warranty/Details/' + warranty.warrantyId;
                        link.hidden = false;
                    }
                } catch {
                    status.textContent = 'Warranty lookup unavailable. Please retry.';
                }
            }
            feedback(
                !item.isActive
                    ? 'Product is inactive.'
                    : item.unitStatus === 'SOLD'
                      ? 'This serial number has already been sold.'
                      : item.isAvailable
                        ? 'Found and available.'
                        : 'Found, but unavailable. Check the stock and unit status.',
                item.isAvailable ? 'success' : 'warning'
            );
        },
        (error) => {
            card.hidden = true;
            feedback(error.message || 'Unable to look up code.', 'danger');
        }
    );
    document.getElementById('scanButton').onclick = () => {
        submit();
        input.focus();
    };
    const initialCode = new URLSearchParams(location.search).get('code');
    if (initialCode) {
        input.value = initialCode;
        submit();
    }
})();
