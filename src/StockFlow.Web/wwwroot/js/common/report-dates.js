window.StockFlowReportDates = {
    bind(prefix, changed) {
        const getElement = (suffix) => {
            return document.getElementById(prefix + '-' + suffix);
        };
        const fmt = (d) => {
            return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
        };
        const query = new URLSearchParams(location.search);
        function update() {
            const now = new Date();
            const start = new Date(now);
            if (getElement('period').value === 'week') {
                start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
            }
            if (getElement('period').value === 'month') {
                start.setDate(1);
            }
            const custom = getElement('period').value === 'custom';
            getElement('from').readOnly = getElement('to').readOnly = !custom;
            if (!custom) {
                getElement('from').value = fmt(start);
                getElement('to').value = fmt(now);
            }
            changed?.();
        }
        if (query.has('from') && query.has('to')) {
            getElement('period').value = 'custom';
            getElement('from').value = query.get('from');
            getElement('to').value = query.get('to');
        } else if (['today', 'week', 'month', 'custom'].includes(query.get('period'))) {
            getElement('period').value = query.get('period');
        }
        getElement('period').addEventListener('change', update);
        getElement('from').addEventListener('change', () => {
            return changed?.();
        });
        getElement('to').addEventListener('change', () => {
            return changed?.();
        });
        update();
        return () => {
            const from = getElement('from').value;
            const to = getElement('to').value;
            if (!from || !to || from > to) {
                throw new Error('Choose a start date on or before the end date.');
            }
            return {
                from,
                to
            };
        };
    }
};
