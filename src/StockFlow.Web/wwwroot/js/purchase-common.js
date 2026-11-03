window.Purchases = {
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
        const data =
            response.status === 204
                ? null
                : await response.json().catch(() => {
                      return null;
                  });
        if (!response.ok) {
            const error = new Error(
                response.status === 401
                    ? 'Your session expired. Sign in again.'
                    : response.status === 403
                      ? 'You do not have access to purchases.'
                      : data?.message ||
                        (data?.errors
                            ? Object.values(data.errors).flat().join(' ')
                            : 'We could not finish this action. Check saved receipts before trying again. If the problem continues, contact the shop owner or administrator.')
            );
            error.status = response.status;
            throw error;
        }
        return data;
    },
    error(error) {
        const box = document.querySelector('.pd-dialog[open] [role="alert"]') || document.getElementById('purchase-message');
        box.textContent = error.message;
        box.hidden = false;
    },
    cell(row, text) {
        const cell = document.createElement('td');
        cell.textContent = text ?? '';
        row.append(cell);
        return cell;
    },
    money(value) {
        return Number(value).toFixed(2);
    }
};
