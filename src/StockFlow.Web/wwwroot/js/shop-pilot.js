// This is an operator's checklist, not an automatic certification of shop hardware.
(() => {
    const getElement = (id) => document.getElementById(id);
    const workspace = getElement('pilot-workspace');
    const storageKey = workspace.dataset.storageKey;
    getElement('pilot-operations-load').onclick = async () => {
        const output = getElement('pilot-operations');
        output.textContent = 'Checking operational evidence…';
        try {
            const response = await fetch('/api/database/operations', { credentials: 'same-origin', cache: 'no-store' });
            if (response.status === 401 || response.status === 403) throw new Error('Administrator review access is required.');
            const data = await response.json();
            if (!data.recovery) throw new Error('Operational status is unavailable.');
            const states = {
                NotConfigured: 'Recovery evidence is not configured.', Unavailable: 'Recovery evidence is unavailable.',
                InvalidEvidence: 'Recovery evidence is invalid; review it with the backup operator.',
                StaleEvidence: 'Recovery evidence is stale; check the scheduled collector and backup job.',
                ArchiveOnly: 'Archive checksum checked; no matching database restore-test evidence.',
                DatabaseRestoreTestRecorded: 'Matching isolated database restore-test evidence recorded.'
            };
            const date = value => value ? new Date(value).toLocaleString() : 'Unknown';
            const migrations = data.migrationCapabilities || {};
            output.textContent = `Database: ${data.database}. ${states[data.recovery.status] || 'Recovery status unknown.'} ` +
                `Backup: ${date(data.recovery.backupUtc)}. Restore test: ${date(data.recovery.restoreTestUtc)}. ` +
                (data.recovery.backupOverdue ? 'BACKUP OVERDUE. ' : '') +
                `082 capability: ${migrations.BillingPolicy082 === true ? 'detected' : 'not detected or unknown'}. ` +
                `083 capability: ${migrations.Quotations083 === true ? 'detected' : 'not detected or unknown'}.`;
        } catch (error) {
            output.textContent = error.message || 'Operational status is unavailable.';
        }
    };
    const metadata = ['store', 'operator', 'build', 'workstation', 'scanner', 'printer', 'backup'];
    const checks = [
        ['legal', 'Legal business and shop identity', 'Review company details and the selected shop. Record the review date and authorized reviewer; a saved name alone is not verification.'],
        ['gst', 'GST applicability and setup review', 'Review GST readiness and effective shop mapping in GST Settings. Record reviewed evidence, or the authorized decision that GST is not applicable.'],
        ['contacts', 'Customers and suppliers ready', 'Review existing records and reconcile any manual migration. General contact/opening-balance import is not implemented; do not claim an import completed without source evidence.'],
        ['catalog', 'Product catalog ready', 'Reconcile successful catalog import rows, errors and duplicate SKUs against the source, or record authorized review of the existing catalog. Product records do not prove opening stock.'],
        ['opening', 'Opening stock reconciled', 'Compare opening intake evidence and exact SKU/location quantities with source counts. Record and resolve differences; an intake alone is not reconciliation.'],
        [
            'receiving',
            'Receive stock',
            'Receive a known item and an unknown barcode. Check quantities, cost and models. Test serials only for the Electrical profile.'
        ],
        [
            'scanner',
            'Physical scanner',
            'Scan leading-zero codes and ten rapid scans. Check every item is added once. Test both receiving and billing.'
        ],
        [
            'labels',
            'Physical labels and printer',
            'Print and scan an item label. Reprint and check the same code is kept. Test unique serial labels only for Electrical.'
        ],
        [
            'billing',
            'Billing and invoice printing',
            'Complete walk-in and named-customer bills using cash and UPI. Print the actual invoice or thermal receipt.'
        ],
        [
            'competition',
            'Two-counter stock protection',
            'At two counters, try to sell the last quantity item and the same serial. Only one sale must succeed.'
        ],
        [
            'recovery',
            'Interrupted connection',
            'Interrupt a save response, reload and retry the saved request. Confirm one receipt or invoice, not two.'
        ],
        [
            'access',
            'Staff access',
            'Use staff accounts assigned to this shop and profile. Check forbidden actions, other-store access and access revocation. Test repair accounts only for Electrical.'
        ],
        [
            'closing',
            'Reports and cash closing',
            'Reconcile recorded stock and collections. Count cash, explain a difference and verify closing history.'
        ],
        [
            'backup',
            'Backup and recovery',
            'Run a backup and restore it into a separate test database. Keep the checksum manifest and restore-test result.'
        ],
        [
            'language',
            'Staff understanding',
            'Ask staff to receive stock, create a bill and correct an invalid field using their chosen language.'
        ]
    ];
    let signedAt = null;
    function message(text, error = false) {
        const box = getElement('pilot-message');
        box.textContent = window.StockFlowLanguage?.text(text) || text;
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.hidden = !text;
    }
    for (const [id, title, instruction] of checks) {
        const row = document.createElement('section');
        row.className = 'pilot-check';
        const heading = document.createElement('h2');
        heading.className = 'h5';
        heading.textContent = title;
        const help = document.createElement('p');
        help.textContent = instruction;
        help.dataset.i18n = instruction;
        const label = document.createElement('label');
        label.htmlFor = 'pilot-status-' + id;
        label.textContent = 'Result';
        const status = document.createElement('select');
        status.id = label.htmlFor;
        status.className = 'form-select';
        for (const [value, text] of [
            ['PENDING', 'Pending'],
            ['PASS', 'Passed'],
            ['FAIL', 'Failed']
        ]) {
            const option = new Option(text, value);
            option.dataset.i18n = text;
            status.add(option);
        }
        const evidenceLabel = document.createElement('label');
        evidenceLabel.htmlFor = 'pilot-evidence-' + id;
        evidenceLabel.textContent = 'Evidence / issue';
        const evidence = document.createElement('textarea');
        evidence.id = evidenceLabel.htmlFor;
        evidence.maxLength = 1000;
        evidence.rows = 2;
        evidence.className = 'form-control';
        row.append(heading, help, label, status, evidenceLabel, evidence);
        getElement('pilot-checks').append(row);
    }
    function snapshot() {
        const details = Object.fromEntries(
            metadata.map((name) => [name, getElement('pilot-' + name).value.trim()])
        );
        const results = checks.map(([id, title]) => ({
            id,
            title,
            status: getElement('pilot-status-' + id).value,
            evidence: getElement('pilot-evidence-' + id).value.trim()
        }));
        return {
            schemaVersion: 2,
            profile: workspace.dataset.profile,
            scopeKey: storageKey,
            recordedAt: new Date().toISOString(),
            signedAt,
            details,
            checks: results
        };
    }
    function progress() {
        const passed = checks.filter(
            ([id]) => getElement('pilot-status-' + id).value === 'PASS' && getElement('pilot-evidence-' + id).value.trim()
        ).length;
        getElement('pilot-progress').textContent = `${passed} / ${checks.length} checks passed.`;
    }
    function save() {
        signedAt = null; // Any edit requires a fresh sign-off.
        message('');
        try {
            localStorage.setItem(storageKey, JSON.stringify(snapshot()));
        } catch {
            message(
                'Browser storage is unavailable. Export the checklist before leaving this page.',
                true
            );
        }
        progress();
    }
    getElement('pilot-form').addEventListener('input', save);
    getElement('pilot-form').addEventListener('change', save);
    getElement('pilot-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const results = snapshot().checks;
        if (results.some((check) => check.status !== 'PASS' || !check.evidence)) {
            message('Every check must pass and include evidence before sign-off.', true);
            return;
        }
        signedAt = new Date().toISOString();
        try {
            localStorage.setItem(storageKey, JSON.stringify(snapshot()));
        } catch {
            message('Sign-off could not be saved in this browser. Export the checklist now.', true);
            return;
        }
        message('Pilot sign-off recorded. Export and keep this checklist with the owner.');
    });
    getElement('pilot-export').onclick = () => {
        const blob = new Blob([JSON.stringify(snapshot(), null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'stockflow-pilot-checklist.json';
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    getElement('pilot-print').onclick = () => window.print();
    try {
        const saved = JSON.parse(localStorage.getItem(storageKey) || 'null');
        if (saved?.schemaVersion === 2 && saved.scopeKey === storageKey && saved.profile === workspace.dataset.profile) {
            for (const name of metadata)
                getElement('pilot-' + name).value = saved.details?.[name] || '';
            for (const check of saved.checks || []) {
                if (!checks.some(([id]) => id === check.id)) continue;
                getElement('pilot-status-' + check.id).value = ['PASS', 'FAIL'].includes(
                    check.status
                )
                    ? check.status
                    : 'PENDING';
                getElement('pilot-evidence-' + check.id).value = check.evidence || '';
            }
            signedAt = saved.signedAt || null;
            if (signedAt)
                message(
                    'A signed checklist is saved on this browser. Export a copy for the owner.'
                );
        }
    } catch {
        message(
            'The local checklist could not be read. Start fresh and export your results.',
            true
        );
    }
    progress();
})();
