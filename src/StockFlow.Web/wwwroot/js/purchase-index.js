(async () => {
    try {
        const rows = await Purchases.api('/api/purchases');
        const body = document.getElementById('purchase-rows');
        rows.forEach((supplier) => {
            const row = document.createElement('tr');
            [
                supplier.purchaseNumber,
                supplier.supplierName,
                supplier.purchaseDate.slice(0, 10),
                Purchases.money(supplier.totalAmount),
                StockFlowText.label(supplier.status)
            ].forEach((value) => {
                return Purchases.cell(row, value);
            });
            const link = document.createElement('a');
            link.href = '/Purchases/Details/' + supplier.purchaseId;
            link.textContent = 'View';
            const actions = Purchases.cell(row, ''); actions.append(link);
            if (body.dataset.edit === 'true' && supplier.status !== 'CANCELLED') {
                const edit = document.createElement('a'); edit.style.marginLeft='12px'; edit.textContent='Edit';
                edit.href = supplier.status === 'DRAFT' ? '/Purchases/Create?id='+supplier.purchaseId : '/Purchases/Details/'+supplier.purchaseId+'?edit=1';
                actions.append(edit);
            }
            body.append(row);
        });
        if (!rows.length) {
            const row = document.createElement('tr');
            Purchases.cell(row, 'No purchases yet.').colSpan = 6;
            body.append(row);
        }
    } catch (error) {
        Purchases.error(error);
    }
})();
