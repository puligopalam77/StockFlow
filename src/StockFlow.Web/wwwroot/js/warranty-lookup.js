(() => {
    const lookup = (code) => {
        return Warranty.run(async () => {
            document.getElementById('warranty-results').replaceChildren();
            const kind = document.getElementById('lookup-kind').value;
            const data = await Warranty.api(
                '/api/warranty/' + kind + '/' + encodeURIComponent(code)
            );
            Warranty.results(kind === 'serial' ? [data] : data);
        });
    };
    const submit = StockFlowScanner.bind('warrantyScanInput', lookup);
    document.getElementById('lookup-form').addEventListener('submit', (event) => {
        event.preventDefault();
        submit();
    });
    const form = document.getElementById('register-form');
    if (form) {
        Warranty.mutation(form, async () => {
            const result = await Warranty.api(
                '/api/warranty/register/' + document.getElementById('sale-item-id').value,
                {
                    method: 'POST'
                }
            );
            location.href = '/Warranty/Details/' + result.warrantyId;
        });
    }
    const serial = new URLSearchParams(location.search).get('serial');
    if (serial) {
        lookup(serial);
    }
})();
