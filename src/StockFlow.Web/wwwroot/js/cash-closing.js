// Cash totals come from the server. Keep a save request unchanged until confirmed.
(() => {
    const getElement = (id) => document.getElementById(id);
    const storageKey = getElement('cash-workspace').dataset.storageKey;
    let report = null;
    let pending = null;
    let busy = false;
    let loadVersion = 0;
    let showAll = false;
    const money = (amount) => Number(amount).toFixed(2);
    const currency = (value) => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(Number(value || 0));
    const day = (value) => new Date(value.slice(0, 10) + 'T12:00:00').toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
    const amount = (id) => Number(getElement('cash-' + id).value || 0);
    function showMessage(message, error = true) {
        const box = getElement('cash-message');
        box.textContent = window.StockFlowLanguage?.text(message) || message;
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.hidden = !message;
    }
    function controls() {
        getElement('cash-fields').disabled = busy || Boolean(pending) || !report;
        getElement('cash-date').disabled = busy || Boolean(pending);
        getElement('cash-refresh').disabled = busy || Boolean(pending);
        getElement('cash-retry').disabled = busy;
        getElement('cash-pending').hidden = !pending;
        getElement('cash-print').disabled = busy || Boolean(pending) || !report;
        const location = getElement('report-location');
        if (location && location.options.length > 1) {
            const locked = busy || Boolean(pending);
            if (location.disabled !== locked) location.disabled = locked;
        }
    }
    async function requestJson(url, body) {
        const options = body
            ? {
                  method: 'POST',
                  headers: {
                      'Content-Type': 'application/json',
                      'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                  },
                  body: JSON.stringify(body)
              }
            : {};
        const response = await fetch(url, options);
        const result = await response.json().catch(() => null);
        if (!response.ok || !result) {
            const error = new Error(
                result?.message ||
                    result?.title ||
                    Object.values(result?.errors || {})
                        .flat()
                        .join(' ') ||
                    'The result could not be confirmed. Please retry.'
            );
            error.status = response.status;
            throw error;
        }
        return result;
    }
    function preview() {
        if (!report) return;
        const expected =
            amount('opening') + report.totals.netCash + amount('added') - amount('removed');
        const difference = Number(money(amount('counted') - expected));
        getElement('cash-expected').textContent = currency(expected);
        getElement('cash-counted-preview').textContent = currency(amount('counted'));
        getElement('cash-difference').textContent = currency(difference);
        getElement('cash-difference').className = difference < 0 ? 'cash-negative' : difference > 0 ? 'cash-positive' : '';
        getElement('cash-reason-count').textContent = getElement('cash-reason').value.length + '/250';
        getElement('cash-reason').required =
            difference !== 0 ||
            amount('added') !== 0 ||
            amount('removed') !== 0 ||
            report.closings.length > 0;
        getElement('cash-save').textContent = report.closings.length
            ? 'Save updated closing'
            : 'Save cash closing';
    }
    function render() {
        getElement('cash-date').value = report.date.slice(0, 10);
        getElement('cash-zone').textContent = 'Business day time zone: ' + report.timeZone;
        for (const [id, property] of [
            ['sales', 'salesCash'],
            ['service', 'serviceCash'],
            ['refunds', 'cashRefunds'],
            ['suppliers', 'supplierCash'], ['expenses', 'expenseCash']
        ]) {
            getElement('cash-' + id).textContent = currency(report.totals[property]);
            getElement('cash-tile-' + id).textContent = currency(report.totals[property]);
        }
        for (const [id, property, label] of [['sales', 'salesCount', 'invoice'], ['service', 'serviceCount', 'receipt'], ['refunds', 'refundCount', 'refund'], ['suppliers', 'supplierCount', 'payment'], ['expenses','expenseCount','movement']]) {
            const count = report.totals[property];
            getElement('cash-count-' + id).textContent = count == null ? '' : '(' + count + ' ' + label + (count === 1 ? '' : 's') + ')';
        }
        getElement('cash-changed').hidden = !report.changedSinceClosing;
        const last = report.closings[0];
        for (const [id, property] of [
            ['opening', 'openingCash'],
            ['added', 'cashAdded'],
            ['removed', 'cashRemoved'],
            ['counted', 'countedCash']
        ]) {
            getElement('cash-' + id).value = last?.[property] ?? 0;
        }
        getElement('cash-reason').value = '';
        showAll = false;
        renderHistory();
        preview();
    }
    function recordDetails(target, closing) {
        target.replaceChildren();
        const details = document.createElement('dl');
        details.className = 'cash-record-grid';
        for (const [label, value] of [
            ['Location', report.location || getElement('report-location').selectedOptions[0]?.textContent || ''],
            ['Date', day(closing.date)], ['Version', closing.version],
            ['Opening cash', currency(closing.openingCash)], ['Cash added', currency(closing.cashAdded)],
            ['Cash removed', currency(closing.cashRemoved)], ['From sales', currency(closing.totals?.salesCash)],
            ['From repairs', currency(closing.totals?.serviceCash)], ['Refunded to customers', currency(closing.totals?.cashRefunds)],
            ['Paid to suppliers', currency(closing.totals?.supplierCash)], ['Net cash paid for expenses', currency(closing.totals?.expenseCash)], ['Expected cash', currency(closing.expectedCash)],
            ['Counted cash', currency(closing.countedCash)], ['Difference', currency(closing.difference)],
            ['Reason', closing.reason || '—'], ['Saved at', new Date(closing.closedAt).toLocaleString('en-IN', { timeZone: report.timeZone })]
        ]) {
            const term = document.createElement('dt'), valueElement = document.createElement('dd');
            term.textContent = label; valueElement.textContent = value; details.append(term, valueElement);
        }
        target.append(details);
    }
    async function printClosing(closing) {
        if (!report || busy || pending) return;
        if (closing) {
            const target = getElement('cash-print-record');
            const title = document.createElement('h2'); title.textContent = 'Saved cash closing';
            recordDetails(target, closing); target.prepend(title);
            document.body.classList.add('cash-print-saved');
        }
        try {
            await Promise.all([...document.images].map(img => img.decode?.().catch(() => {})));
            await window.print();
        } finally { document.body.classList.remove('cash-print-saved'); }
    }
    function renderHistory() {
        const rows = getElement('cash-history');
        rows.replaceChildren();
        getElement('cash-history-all').hidden = report.closings.length <= 5;
        getElement('cash-history-all').textContent = showAll ? 'Show latest 5' : 'View all →';
        for (const closing of showAll ? report.closings : report.closings.slice(0, 5)) {
            const row = document.createElement('tr');
            for (const [label, text] of [
                ['Version', closing.version], ['Date', day(closing.date)],
                ['Opening cash', currency(closing.openingCash)], ['Cash added', currency(closing.cashAdded)],
                ['Cash removed', currency(closing.cashRemoved)], ['Counted cash', currency(closing.countedCash)],
                ['Difference', currency(closing.difference)], ['Reason', closing.reason || '—'],
                ['Saved at', new Date(closing.closedAt).toLocaleString('en-IN', { timeZone: report.timeZone })]
            ]) {
                const cell = document.createElement('td');
                cell.dataset.label = label;
                cell.textContent = text;
                row.append(cell);
            }
            const actions = document.createElement('td'); actions.className = 'no-print cash-history-actions'; actions.dataset.label = 'Actions';
            for (const [label, action] of [['View', () => { recordDetails(getElement('cash-record-details'), closing); getElement('cash-record-dialog').showModal(); }], ['Print', () => printClosing(closing)]]) {
                const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-secondary'; button.textContent = label; button.setAttribute('aria-label', label + ' closing version ' + closing.version); button.onclick = action; actions.append(button);
            }
            row.append(actions);
            rows.append(row);
        }
        if (!report.closings.length) {
            const row = document.createElement('tr'), cell = document.createElement('td');
            cell.colSpan = 10; cell.textContent = 'No saved closings for this date.'; row.append(cell); rows.append(row);
        }
    }
    async function load() {
        const version = ++loadVersion;
        report = null;
        controls();
        try {
            const date = getElement('cash-date').value;
            const result = await requestJson(
                '/api/cashclosing' + (date ? '?date=' + encodeURIComponent(date) : '')
            );
            if (version !== loadVersion) return;
            report = result;
            render();
            return true;
        } catch (error) {
            showMessage(error.message);
            return false;
        } finally {
            controls();
        }
    }
    function requestId() {
        if (crypto.randomUUID) return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = [...bytes].map((value) => value.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
    async function postSaved() {
        if (!pending || busy) return;
        busy = true;
        controls();
        try {
            const saved = await requestJson('/api/cashclosing', pending);
            if (!saved.closingId)
                throw new Error('The closing result could not be confirmed. Retry saved closing.');
            sessionStorage.removeItem(storageKey);
            pending = null;
            const refreshed = await load();
            if (refreshed) showMessage('Cash closing saved.', false);
            else
                showMessage(
                    'Cash closing saved, but the latest totals could not be loaded. Refresh cash totals before continuing.'
                );
        } catch (error) {
            if ([400, 422].includes(error.status)) {
                sessionStorage.removeItem(storageKey);
                pending = null;
            }
            showMessage(error.message);
        } finally {
            busy = false;
            controls();
        }
    }
    getElement('cash-form').addEventListener('input', preview);
    getElement('cash-form').addEventListener('submit', (event) => {
        event.preventDefault();
        if (!report || busy || pending) return;
        const body = {
            requestId: requestId(),
            locationId: window.StockFlowLocation,
            date: report.date.slice(0, 10),
            expectedVersion: report.closings[0]?.version || 0,
            fingerprint: report.totals.fingerprint,
            openingCash: amount('opening'),
            cashAdded: amount('added'),
            cashRemoved: amount('removed'),
            countedCash: amount('counted'),
            reason: getElement('cash-reason').value.trim()
        };
        try {
            sessionStorage.setItem(storageKey, JSON.stringify(body));
        } catch {
            showMessage(
                'This browser cannot keep a retry copy. Enable browser storage before saving.'
            );
            return;
        }
        pending = body;
        postSaved();
    });
    getElement('cash-date').addEventListener('change', load);
    getElement('cash-refresh').onclick = load;
    getElement('cash-retry').onclick = postSaved;
    getElement('cash-print').onclick = () => printClosing();
    getElement('cash-history-all').onclick = () => { showAll = !showAll; renderHistory(); };
    const locationSelect = getElement('report-location');
    new MutationObserver(controls).observe(locationSelect, { childList: true });
    locationSelect.addEventListener('change', event => {
        if (busy || pending) {
            event.stopImmediatePropagation();
            const store = document.querySelector('meta[name="stockflow-store"]')?.content;
            locationSelect.value = window.StockFlowLocation + ':' + store;
        }
    }, true);
    try {
        pending = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
        if (pending) getElement('cash-date').value = pending.date;
    } catch {
        showMessage(
            'The saved closing could not be read. Ask the shop owner to check closing history before re-entering it.'
        );
        busy = true;
    }
    controls();
    if (!pending && !busy) load();
})();
