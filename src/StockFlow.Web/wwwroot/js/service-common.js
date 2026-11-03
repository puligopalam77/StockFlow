window.ServiceUI = {
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
                          ? 'Record not found.'
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
        const box = document.getElementById('service-message');
        box.hidden = true;
        try {
            await action();
        } catch (error) {
            box.textContent = error.message;
            box.hidden = false;
        }
    },
    date(value) {
        if (value) {
            return value.slice(0, 10);
        } else {
            return '—';
        }
    },
    time(value) {
        if (value) {
            return value.replace('T', ' ').slice(0, 19);
        } else {
            return '—';
        }
    },
    money(value) {
        return Number(value).toFixed(2);
    },
    link(id) {
        const a = document.createElement('a');
        a.href = '/Service/Details/' + id;
        a.textContent = 'View';
        return a;
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
    async technicians(select, current, currentName) {
        const items = await this.api('/api/service/technicians');
        select.replaceChildren(
            new Option('Unassigned', ''),
            ...items.map((t) => {
                return new Option(t.fullName, t.userId);
            })
        );
        if (
            current &&
            !items.some((t) => {
                return t.userId === current;
            })
        ) {
            select.append(
                new Option(
                    (currentName || 'Previous technician') + ' (inactive; reassign)',
                    current
                )
            );
        }
        select.value = current || '';
    },
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
                            locationId: window.StockFlowLocation,
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
