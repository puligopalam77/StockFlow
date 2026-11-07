// Application-wide single-select presentation. Original sources/APIs/events remain.
// Existing autocomplete, multi-select listboxes and command menus keep their semantics.
(() => {
    'use strict';
    const modules = [
        ['supplier-address', /^\/(?:Suppliers|Customers|Settings|Admin|Configuration)(?:\/|$)/i],
        ['catalog', /^\/(?:Products|Categories|Brands)(?:\/|$)/i],
        ['purchasing', /^\/(?:Purchases|Receiving|StockInflow)(?:\/|$)/i],
        ['sales-expenses', /^\/(?:Sales|Quotations|Expenses|SavedBills)(?:\/|$)/i],
        ['inventory', /^\/(?:Inventory|Replenishment|Transfers|StockOutflow)(?:\/|$)/i],
        ['remaining', /^\/(?:Reports|Crm|Service|Warranty|EWayBills|CashClosing|Labels|Account)(?:\/|$)/i]
    ];
    const phase = modules.find(([, path]) => path.test(location.pathname))?.[0] || 'shared';
    let queued = false;
    function migrate() {
        queued = false;
        for (const source of document.querySelectorAll('select')) {
            StockFlowSelect.native(source);
            if (source.dataset.sfSelect || source.sfSelect || source.sfAddressPicker || source.hidden ||
                source.multiple || source.size > 1 || source.closest('.product-category-select,[data-entity-picker]') ||
                source.closest('[data-sf-select-native]')) continue;
            source.dataset.sfSelect = 'auto';
            source.dataset.sfSelectPhase = phase;
        }
        StockFlowSelect.scan();
    }
    document.addEventListener('DOMContentLoaded', () => {
        migrate();
        new MutationObserver(() => { if (!queued) { queued = true; queueMicrotask(migrate); } })
            .observe(document.body, { childList: true, subtree: true });
    });
})();
