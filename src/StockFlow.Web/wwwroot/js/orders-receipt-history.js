(() => {
    const host = document.getElementById('orders-receipt-history');
    const tabs = [...host.querySelectorAll('[data-history-tab]')];
    function select(name) {
        if (!tabs.some(tab => tab.dataset.historyTab === name)) return;
        tabs.forEach(tab => {
            const active = tab.dataset.historyTab === name;
            tab.setAttribute('aria-selected', String(active));
            tab.tabIndex = active ? 0 : -1;
            document.getElementById(`history-${tab.dataset.historyTab}-panel`).hidden = !active;
        });
        const url = new URL(location.href);
        url.searchParams.set('view', name);
        history.replaceState(null, '', url);
    }
    tabs.forEach((tab,index) => {
        tab.addEventListener('click', () => select(tab.dataset.historyTab));
        tab.addEventListener('keydown', event => {
            if (!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
            event.preventDefault();
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
            select(tabs[next].dataset.historyTab); tabs[next].focus();
        });
    });
    const requested = new URLSearchParams(location.search).get('view');
    select(tabs.some(tab=>tab.dataset.historyTab===requested) ? requested : host.dataset.defaultTab);
})();
