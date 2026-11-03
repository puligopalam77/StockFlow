window.Sales = {
    async api(url, options = {}) {
        const response = await fetch(url, {
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
            const error = new Error(
                data?.message ||
                    (data?.errors
                        ? Object.values(data.errors).flat().join(' ')
                        : response.status === 401
                          ? 'Sign in to continue.'
                          : response.status === 403
                            ? 'You do not have access to sales.'
                            : 'We could not finish this action. Check saved bills before trying again. If the problem continues, contact the shop owner or administrator.')
            );
            error.status = response.status;
            throw error;
        }
        return data;
    },
    error(error) {
        const box = document.getElementById('sale-message');
        box.textContent = error.message;
        box.hidden = false;
    },
    clear() {
        document.getElementById('sale-message').hidden = true;
    },
    cell(row, value) {
        const cell = document.createElement('td');
        if (value instanceof Node) {
            cell.append(value);
        } else {
            cell.textContent = value ?? '—';
        }
        row.append(cell);
        return cell;
    },
    money(value) {
        return Number(value).toFixed(2);
    },
    async run(action) {
        this.clear();
        try {
            await action();
        } catch (error) {
            this.error(error);
        }
    }
};
