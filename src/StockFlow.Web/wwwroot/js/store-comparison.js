(() => {
    window
        .sfBranchApi('/api/branches/summary')
        .then((rows) => {
            for (const item of rows) {
                const tr = document.createElement('tr');
                for (const key of [
                    'storeName',
                    'locationName',
                    'currentStock',
                    'externalInflow',
                    'externalOutflow',
                    'transferIn',
                    'transferOut',
                    'salesTotal'
                ]) {
                    const td = document.createElement('td');
                    td.textContent = item[key] ?? '—';
                    tr.append(td);
                }
                document.getElementById('store-summary').append(tr);
            }
        })
        .catch((error) => {
            document.getElementById('comparison-message').textContent = error.message;
        });
    const load = async () => {
        try {
            const rows = await window.sfBranchApi(
                '/api/branches/comparison?term=' +
                    encodeURIComponent(document.getElementById('comparison-term').value)
            );
            const body = document.getElementById('comparison-rows');
            body.replaceChildren();
            for (const item of rows) {
                const tr = document.createElement('tr');
                for (const key of [
                    'productName',
                    'variantName',
                    'storeName',
                    'locationName',
                    'locationType',
                    'currentStock'
                ]) {
                    const td = document.createElement('td');
                    td.textContent = item[key];
                    tr.append(td);
                }
                body.append(tr);
            }
            document.getElementById('comparison-message').textContent =
                `${rows.length} location balances`;
        } catch (error) {
            document.getElementById('comparison-message').textContent = error.message;
        }
    };
    document.getElementById('comparison-form').onsubmit = (event) => {
        event.preventDefault();
        load();
    };
    load();
})();
