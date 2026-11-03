(() => {
    const select = document.getElementById('chart-period');
    const message = document.getElementById('trend-message');
    let generation = 0;
    let successful = select.value;
    select.addEventListener('change', async () => {
        const current = ++generation;
        const period = select.value;
        message.hidden = false;
        message.textContent = 'Loading stock movements…';
        try {
            const response = await fetch(
                '/api/dashboard/inflow-outflow-trend?period=' + encodeURIComponent(period)
            );
            if (!response.ok) {
                throw new Error(
                    response.status === 401
                        ? 'Sign in to view stock trends.'
                        : 'Unable to load this period. Previous chart retained.'
                );
            }
            const rows = await response.json();
            if (current !== generation) {
                return;
            }
            const max = Math.max(
                4,
                ...rows.flatMap((value) => {
                    return [value.inflow, value.outflow];
                })
            );
            const target = document.getElementById('chart-months');
            target.replaceChildren();
            target.classList.toggle('dense', rows.length > 12);
            rows.forEach((row, index) => {
                const month = document.createElement('div');
                month.className = 'sf-chart-month';
                const pair = document.createElement('div');
                pair.className = 'sf-bar-pair';
                for (const [key, color] of [
                    ['inflow', 'green'],
                    ['outflow', 'pink']
                ]) {
                    const bar = document.createElement('div');
                    bar.className = 'sf-bar ' + color;
                    bar.style.height = (Math.max(0, row[key]) / max) * 100 + '%';
                    bar.title = row.period + ' ' + key + ': ' + row[key].toLocaleString();
                    pair.append(bar);
                }
                const label = document.createElement('span');
                label.textContent =
                    rows.length > 12 && index % 5 !== 0 && index !== rows.length - 1
                        ? ''
                        : row.period;
                const exact = document.createElement('span');
                exact.className = 'visually-hidden';
                exact.textContent =
                    row.period + ': Inflow ' + row.inflow + '; Outflow ' + row.outflow;
                month.append(pair, label, exact);
                target.append(month);
            });
            document.querySelectorAll('#chart-axis span').forEach((label, index) => {
                return (label.textContent = Math.floor((max * (4 - index)) / 4).toLocaleString());
            });
            document
                .querySelector('.sf-bar-chart')
                .setAttribute(
                    'aria-label',
                    'Stock inflow and outflow: ' +
                        select.selectedOptions[0].textContent +
                        '. Exact quantities are available for each period.'
                );
            document.getElementById('trend-empty').hidden = rows.some((value) => {
                return value.inflow !== 0 || value.outflow !== 0;
            });
            successful = period;
            message.hidden = true;
        } catch (error) {
            if (current === generation) {
                select.value = successful;
                message.textContent = error.message;
                message.hidden = false;
            }
        }
    });
})();
