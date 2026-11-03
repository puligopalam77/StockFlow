window.Warranty = {
    async api(path, options = {}) {
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
                        : response.status === 404
                          ? 'No warranty found.'
                          : response.status === 401
                            ? 'Sign in to continue.'
                            : response.status === 403
                              ? 'You do not have permission for this action.'
                              : 'We could not finish this action. Check saved records before trying again. If the problem continues, contact the shop owner or administrator.')
            );
        }
        return data;
    },
    async run(action) {
        document.getElementById('warranty-message').hidden = true;
        try {
            await action();
        } catch (error) {
            const box = document.getElementById('warranty-message');
            box.textContent = error.message;
            box.hidden = false;
        }
    },
    link(id, text = 'View') {
        const a = document.createElement('a');
        a.href = '/Warranty/Details/' + id;
        a.textContent = text;
        return a;
    },
    date(value) {
        if (value) {
            return value.slice(0, 10);
        } else {
            return '—';
        }
    },
    status(value) {
        const badge = document.createElement('span');
        const color =
            {
                ACTIVE: 'success',
                CLAIMED: 'warning text-dark',
                REPLACED: 'info text-dark',
                CANCELLED: 'danger',
                EXPIRED: 'secondary'
            }[value] || 'secondary';
        badge.className = 'badge bg-' + color;
        badge.textContent = StockFlowText.label(value);
        return badge;
    },
    table(target, headings, rows, append = false) {
        const container = document.getElementById(target);
        if (!append) {
            container.replaceChildren();
        }
        if (!rows.length) {
            if (!append) {
                container.textContent = 'No records found.';
            }
            return;
        }
        const wrapper = document.createElement('div');
        wrapper.className = 'table-responsive';
        const table = document.createElement('table');
        table.className = 'table table-striped';
        const head = table.createTHead().insertRow();
        for (const title of headings) {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = title;
            head.append(th);
        }
        const body = table.createTBody();
        for (const values of rows) {
            const row = body.insertRow();
            for (const value of values) {
                const td = row.insertCell();
                if (value instanceof Node) {
                    td.append(value);
                } else {
                    td.textContent = value ?? '—';
                }
            }
        }
        wrapper.append(table);
        container.append(wrapper);
    },
    results(items) {
        this.table(
            'warranty-results',
            [
                'Product',
                'Brand / model',
                'Serial / Unit ID',
                'Customer',
                'Mobile',
                'Invoice',
                'Sale date',
                'Start',
                'Expiry',
                'Status',
                'Under warranty',
                'Days left',
                'Claims',
                'Details'
            ],
            items.map((w) => {
                return [
                    w.productName,
                    [w.brandName, w.model].filter(Boolean).join(' / '),
                    w.serialNumber ? (w.serialSource==='MANUFACTURER'?'Manufacturer Serial: ':w.serialSource==='INTERNAL'?'Internal Unit ID: ':'Source unverified: ')+w.serialNumber : null,
                    w.customerName,
                    w.customerMobile,
                    w.invoiceNumber,
                    this.date(w.saleDate),
                    this.date(w.warrantyStartDate),
                    this.date(w.warrantyEndDate),
                    this.status(w.warrantyStatus),
                    w.isUnderWarranty ? 'Yes' : 'No',
                    w.remainingDays,
                    w.claimCount,
                    this.link(w.warrantyId)
                ];
            })
        );
    },
    claims(items, action, append = false) {
        this.table(
            'claim-results',
            [
                'Number',
                'Date',
                'Product',
                'Serial / Unit ID',
                'Customer',
                'Complaint',
                'Status',
                'Resolution',
                'Notes',
                'Action'
            ],
            items.map((product) => {
                return [
                    product.claimNumber,
                    this.date(product.claimDate),
                    product.productName,
                    product.serialNumber,
                    product.customerName,
                    product.complaint,
                    StockFlowText.label(product.claimStatus),
                    StockFlowText.label(product.resolutionType),
                    product.resolutionNotes,
                    action ? action(product) : this.link(product.warrantyId)
                ];
            }),
            append
        );
    },
    // Retain the request ID for an unchanged payload after a lost response.
    mutation(form, action) {
        let busy = false;
        let previous = '';
        let requestId = '';
        form.addEventListener('submit', (event) => {
            event.preventDefault();
            if (busy) {
                return;
            }
            this.run(async () => {
                busy = true;
                const buttons = [...form.querySelectorAll('button')];
                buttons.forEach((value) => {
                    return (value.disabled = true);
                });
                try {
                    await action((payload) => {
                        const signature = JSON.stringify(payload);
                        if (signature !== previous) {
                            previous = signature;
                            requestId = crypto.randomUUID();
                        }
                        return {
                            ...payload,
                            requestId
                        };
                    });
                    previous = '';
                    requestId = '';
                } finally {
                    busy = false;
                    buttons.forEach((value) => {
                        return (value.disabled = false);
                    });
                }
            });
        });
    }
};
