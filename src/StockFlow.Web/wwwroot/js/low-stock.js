(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    let version = 0;
    async function load() {
        const current = ++version;
        getElement('low-stock-message').hidden = true;
        try {
            const response = await fetch(
                '/api/inventory?' +
                    new URLSearchParams({
                        lowStockOnly: true,
                        term: getElement('low-stock-term').value
                    })
            );
            const rows = await response.json();
            if (!response.ok) {
                throw new Error(rows.message || 'Unable to load low stock.');
            }
            if (current !== version) {
                return;
            }
            getElement('low-stock-results').replaceChildren();
            for (const item of rows) {
                const tr = document.createElement('tr');
                for (const value of [
                    item.productName,
                    item.currentStock,
                    item.minimumStockLevel,
                    Math.max(0, item.minimumStockLevel - item.currentStock)
                ]) {
                    const td = document.createElement('td');
                    td.textContent = value;
                    tr.append(td);
                }
                const td = document.createElement('td');
                const a = document.createElement('a');
                a.href = '/Inventory?term=' + encodeURIComponent(item.sku || item.productName);
                a.textContent = 'View stock';
                td.append(a);
                tr.append(td);
                getElement('low-stock-results').append(tr);
            }
            getElement('low-stock-summary').textContent = rows.length
                ? `${rows.length} low-stock variants · ${
                      rows.filter((value) => {
                          return value.currentStock === 0;
                      }).length
                  } out of stock.`
                : 'No matching low-stock items.';
        } catch (error) {
            if (current !== version) {
                return;
            }
            getElement('low-stock-message').textContent = error.message;
            getElement('low-stock-message').hidden = false;
        }
    }
    getElement('low-stock-search').onsubmit = (event) => {
        event.preventDefault();
        load();
    };
    load();
})();
