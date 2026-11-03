(() => {
    'use strict';
    const get = id => document.getElementById(id);
    const query = new URLSearchParams(location.search);
    const kind = query.has('receiptId') ? 'receipt' : 'intake';
    const id = query.get(kind === 'receipt' ? 'receiptId' : 'intakeId');
    const detail = Boolean(id);
    get('barcode-lots').hidden = detail;
    get('barcode-lot-detail').hidden = !detail;
    get('label-load').hidden = true;
    if (detail) get('barcode-lot-title').textContent = (kind === 'receipt' ? 'Purchase receipt' : 'Received stock') + ' #' + id;
    let lots = [];
    function render() {
        const term = get('barcode-lot-search').value.trim().toLowerCase();
        const filter = get('barcode-lot-kind').value;
        const purchaseId = query.get('purchaseId');
        const rows = lots.filter(lot => (!filter || lot.kind === filter) && (!purchaseId || String(lot.purchaseId) === purchaseId) &&
            [lot.lotNumber,lot.purchaseNumber,lot.supplierName].join(' ').toLowerCase().includes(term));
        get('barcode-lot-rows').replaceChildren();
        for (const lot of rows) {
            const row = document.createElement('tr');
            for (const value of [lot.lotNumber,lot.receivedDate?.slice(0,10),lot.purchaseNumber || 'Opening / received stock',lot.supplierName || '-',lot.items,lot.quantity]) {
                const cell = document.createElement('td'); cell.textContent = value; row.append(cell);
            }
            const cell = document.createElement('td'),link = document.createElement('a');
            link.href = '/Labels?' + new URLSearchParams({[lot.kind === 'receipt' ? 'receiptId' : 'intakeId']:lot.lotId});
            link.textContent = 'View barcodes';cell.append(link);row.append(cell);get('barcode-lot-rows').append(row);
        }
        if (!rows.length) {
            const row = document.createElement('tr'),cell = document.createElement('td');cell.colSpan = 7;
            cell.textContent = lots.length ? 'No received lots match your filters.' : 'No received lots in this location yet. Save received stock first.';
            row.append(cell);get('barcode-lot-rows').append(row);
        }
        get('barcode-lot-count').textContent = rows.length + ' received lots';
        get('barcode-all-lots').hidden = !purchaseId;
    }
    get('barcode-lot-search').addEventListener('input',render);
    get('barcode-lot-kind').addEventListener('change',render);
    fetch('/api/labels/lots').then(async response => {
        const result = await response.json();
        if (!response.ok) throw new Error(result.message || 'Unable to load received lots.');
        lots = result;
        if (detail) {
            const lot = lots.find(lot => lot.kind === kind && String(lot.lotId) === id);
            if (lot) get('barcode-lot-title').textContent = lot.lotNumber + (lot.purchaseNumber ? ' / ' + lot.purchaseNumber : '');
        } else render();
    }).catch(error => {
        if (!detail) { get('label-message').textContent = error.message; get('label-message').hidden = false; }
    });
})();
