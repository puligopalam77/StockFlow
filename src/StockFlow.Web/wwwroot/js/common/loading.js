// Shared loading presentation. GET observers never replay a request or change a write.
window.StockFlowLoading = (() => {
    'use strict';
    const states = new Map(), buttonStates = new WeakMap(), activatedButtons = new Set();
    let scopeVersion = 0;
    const scope = () => [scopeVersion, document.body?.dataset.businessProfile,
        document.querySelector('meta[name="stockflow-location"]')?.content].join('|');
    const node = (tag, className, text) => {
        const element = document.createElement(tag); element.className = className;
        if (text) element.textContent = text; return element;
    };
    function skeleton(kind = 'table', { rows = 6, columns = 5 } = {}) {
        const host = node('div', 'sf-skeleton sf-skeleton-' + kind);
        host.setAttribute('aria-hidden', 'true');
        const count = kind === 'table' ? rows : kind === 'dashboard' ? 6 : kind === 'card' ? 4 : 3;
        host.style.setProperty('--sf-skeleton-columns', String(columns));
        for (let i = 0; i < count; i++) {
            const row = node('div', 'sf-skeleton-row');
            for (let j = 0; j < (kind === 'table' ? columns : 3); j++) row.append(node('span', 'sf-skeleton-line'));
            host.append(row);
        }
        return host;
    }
    function inline(label = 'Loading results…') {
        const host = node('span', 'sf-inline-loader', label);
        host.setAttribute('role', 'status'); host.setAttribute('aria-live', 'polite'); return host;
    }
    function region(element, options = {}) {
        if (typeof element === 'string') element = document.querySelector(element);
        if (!element) return null;
        if (element.matches('tbody,table')) element = element.closest('.table-responsive,.cih-table-wrap') || element.closest('table').parentElement;
        if (states.has(element)) { Object.assign(states.get(element).options, options); return states.get(element); }
        let table = element.querySelector('table'); const body = table?.tBodies[0];
        const state = { element, options, active: 0, loaded: options.initialLoaded ?? !!(body
            ? [...body.rows].some(row => row.cells.length > 1)
            : element.children.length && !/loading/i.test(element.textContent)), serial: 0, failure: false };
        element.classList.add('sf-loading-region');
        const feedback = node('div', 'sf-loading-feedback'); feedback.hidden = true;
        const indicator = inline(); feedback.append(indicator); element.append(feedback);
        let locked = [];
        const headerHeight = () => options.kind && options.kind !== 'table' ? 0 : table?.tHead?.getBoundingClientRect().height || 0;
        function position() {
            const header = headerHeight();
            feedback.style.left = (element.scrollLeft + element.clientWidth / 2) + 'px';
            feedback.style.top = (element.scrollTop + header + Math.max(0, element.clientHeight - header) / 2) + 'px';
        }
        function stableColumns() {
            if (!table?.tHead || getComputedStyle(table).display !== 'table' || table.querySelector('colgroup')) return;
            const widths = [...table.tHead.rows[0].cells].map(cell => cell.getBoundingClientRect().width);
            const total = widths.reduce((sum,width) => sum + width,0);
            if (!total || widths.some(width => !width)) return;
            const group = document.createElement('colgroup'); group.dataset.sfLoadingColumns = 'true';
            for (const width of widths) { const col = document.createElement('col'); col.style.width = (width / total * 100) + '%'; group.append(col); }
            table.prepend(group); table.classList.add('sf-loading-table');
        }
        const observer = new ResizeObserver(() => { if (state.active || !feedback.hidden) position(); });
        observer.observe(element); element.addEventListener('scroll', position, {passive:true});
        function clean() {
            state.placeholder?.remove(); state.placeholder = null;
            for (const [part,inert] of locked) part.inert = inert;
            locked = [];
            element.classList.remove('sf-loading-initial', 'sf-loading-refresh');
            feedback.classList.remove('sf-loading-error');
            element.removeAttribute('aria-busy'); feedback.hidden = true;
            element.style.minHeight = state.previousMinHeight || '';
        }
        state.begin = () => {
            if (state.active++ === 0) {
                // Another dependent read can start before the previous paint cleanup.
                if (element.hasAttribute('aria-busy')) clean();
                if (!feedback.isConnected) element.append(feedback);
                table = element.querySelector('table');
                stableColumns();
                const module = element.closest('[data-module]')?.dataset.module;
                const label = options.label || (module ? 'Loading ' + module + '\u2026' : 'Loading results\u2026');
                state.failure = false; state.cancelled = false;feedback.classList.remove('sf-loading-error');feedback.replaceChildren(inline(label)); feedback.removeAttribute('role'); feedback.hidden = false;
                state.previousMinHeight = element.style.minHeight;
                element.setAttribute('aria-busy', 'true');
                const height = element.getBoundingClientRect().height;
                const header = headerHeight();
                if (!state.loaded) {
                    const rowHeight = Number.parseFloat(getComputedStyle(document.body).getPropertyValue('--sf-ui-table-row-height')) || 44;
                    const rows = Math.max(5, Math.min(8, Number(options.rows) || 6));
                    const kind = options.kind || (table ? 'table' : 'card');
                    element.style.minHeight = Math.max(height, kind === 'table' ? header + rows * rowHeight : 160) + 'px';
                    state.placeholder = skeleton(kind, { rows, columns: table?.tHead?.rows[0]?.cells.length || options.columns || 5 });
                    const widths = [...(table?.tHead?.rows[0]?.cells || [])].map(cell => cell.getBoundingClientRect().width);
                    if (widths.length && widths.every(width => width > 0)) state.placeholder.style.setProperty('--sf-skeleton-grid', widths.map(width => width + 'fr').join(' '));
                    state.placeholder.style.top = header + 'px'; element.append(state.placeholder);
                    element.classList.add('sf-loading-initial');
                } else {
                    element.style.minHeight = height + 'px'; element.classList.add('sf-loading-refresh');
                }
                const parts = table ? [...element.querySelectorAll('tbody')] : [...element.children].filter(part => part !== feedback && part !== state.placeholder);
                locked = parts.map(part => [part,part.inert]);
                parts.forEach(part => { part.inert = true; });
                position();
            }
            const serial = ++state.serial; let ended = false;
            return error => {
                if (ended) return; ended = true;
                if (serial === state.serial) {state.failure = !!error && error.name !== 'AbortError';state.cancelled = error?.name === 'AbortError';}
                if (--state.active > 0) return;
                // Render and body-consumption continuations finish before the next paint.
                requestAnimationFrame(() => {
                    if (state.active) return;
                    clean();
                    if (!state.failure) { if(!state.cancelled)state.loaded = true; return; }
                    feedback.replaceChildren(node('span', '', 'Unable to load data. '));
                    feedback.setAttribute('role', 'alert');
                    const retry = node('button', 'sf-loading-retry', 'Retry'); retry.type = 'button';
                    retry.onclick = async () => { if (options.retry) return options.retry(); if (window.StockFlowUnsaved && !await StockFlowUnsaved.confirmNavigation()) return; location.reload(); };
                    feedback.append(retry); feedback.classList.add('sf-loading-error');feedback.hidden = false;
                });
            };
        };
        state.dispose = () => { clean(); observer.disconnect(); element.removeEventListener('scroll',position); feedback.remove(); states.delete(element); };
        states.set(element, state); return state;
    }
    function button(element) {
        if (!element || buttonStates.has(element)) return () => {};
        const disabled = element.disabled, busy = element.getAttribute('aria-busy');
        const width = element.getBoundingClientRect().width, minWidth = element.style.minWidth;
        element.style.minWidth = width + 'px'; element.classList.add('sf-button-loading');
        element.setAttribute('aria-busy', 'true'); element.disabled = true;
        const end = () => {
            element.classList.remove('sf-button-loading'); element.style.minWidth = minWidth;
            if (busy === null) element.removeAttribute('aria-busy'); else element.setAttribute('aria-busy', busy);
            element.disabled = disabled; buttonStates.delete(element);
        };
        buttonStates.set(element, end); return end;
    }
    function updateTable(host, content) {
        const previous = host.querySelector('table'), next = content.matches('table') ? content : content.querySelector('table');
        if (previous?.tBodies[0] && (!next || previous.tHead?.textContent === next.tHead?.textContent)) {
            if (next) previous.tBodies[0].replaceChildren(...next.tBodies[0].children);
            else { const row = document.createElement('tr'),cell = row.insertCell(); cell.colSpan = previous.tHead?.rows[0]?.cells.length || 1;cell.append(content);previous.tBodies[0].replaceChildren(row); }
        } else host.replaceChildren(content);
    }
    const excluded = /\/(?:notifications|session|sessions|context|antiforgery|readiness|permissions|options|schema|sku-prefix-suggestion|identifier-reservations|search|duplicates)(?:\/|$)/i;
    function target(url) {
        // SKU expansion owns its cell-level loading state, not the whole list.
        if (/^\/api\/products\/[^/]+\/(?:skus|image)(?:\/|$)/.test(url.pathname)) return null;
        if (document.querySelector('#admin-roles') && /\/api\/admin\/(roles|modules)(?:\/|$)/.test(url.pathname)) return document.querySelector('#permission-matrix');
        if (document.querySelector('#quotations[data-mode=list]') && /^\/api\/quotations(?:\/options)?$/.test(url.pathname)) {
            const table = document.querySelector('#quote-list-table'); if (table) table.hidden = false; return table;
        }
        if (!document.body || excluded.test(url.pathname) || !url.pathname.startsWith('/api/')) return null;
        const drawer = document.querySelector('#saved-bills-drawer[open] #saved-bills-list');
        if (drawer && /draft|saved|hold/i.test(url.pathname)) return drawer;
        if (document.querySelector('dialog[open],.master-form-page')) return null;
        if (/dashboard\/inflow-outflow-trend/.test(url.pathname)) return document.querySelector('.sf-bar-chart');
        const report = document.querySelector('[data-sf-loading-report]');
        if (report && /\/api\/(reports|operations\/reports|expenses\/report)/.test(url.pathname)) return report;
        if (/\/api\/stock-receipts(?:\/items|\/suppliers)?$/.test(url.pathname)) return document.querySelector(url.pathname.endsWith('/items') ? '#receipt-item-results' : '#receipt-results');
        if (/\/api\/dashboard\//.test(url.pathname) && document.querySelector('#dashboard-results')) return document.querySelector('#dashboard-results');
        if (/\/branches\/summary$/.test(url.pathname)) return document.querySelector('#store-summary');
        if (/\/branches\/comparison$/.test(url.pathname)) return document.querySelector('#comparison-rows');
        const main = document.querySelector('.sf-list-screen'); if (!main) return null;
        const bodies = [...main.querySelectorAll('table tbody')].filter(body => !body.closest('dialog,form,#editor') && body.closest('table').getClientRects().length);
        const tokens = url.pathname.toLowerCase().split(/[^a-z]+/).filter(token => token.length > 3);
        const body = bodies.sort((a, b) => {
            const score = value => tokens.filter(token => value.includes(token) || value.includes(token.replace(/s$/, ''))).length;
            return score(b.id) - score(a.id);
        })[0];
        return body || main.querySelector('[data-sf-loading],.sf-card-grid,.bank-accounts-list,.bank-account-list,#bank-accounts,#bank-list');
    }
    const nativeFetch = window.fetch.bind(window);
    window.fetch = async (input, options = {}) => {
        const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, location.href);
        const method = (options.method || input.method || 'GET').toUpperCase();
        const element = url.origin === location.origin && method === 'GET' ? target(url) : null;
        const owner = element ? region(element) : null;
        if (!owner) return nativeFetch(input, options);
        const end = owner.begin(), startedIn = scope(); let consumed = false;
        try {
            const signal = options.signal || input.signal;
            const bounded = typeof AbortSignal.any === 'function' ? AbortSignal.any([...(signal ? [signal] : []), AbortSignal.timeout(60000)]) : signal;
            const response = await nativeFetch(input, { ...options, ...(bounded ? {signal:bounded} : {}) });
            const failed = !response.ok;
            for (const name of ['json', 'text', 'blob', 'arrayBuffer']) {
                const read = response[name].bind(response);
                response[name] = async (...args) => {
                    consumed = true;
                    try {
                        const result = await read(...args);
                        if (startedIn !== scope()) throw new DOMException('Shop context changed.', 'AbortError');
                        return result;
                    } catch (error) { end(error); throw error; }
                    finally { end(failed); }
                };
            }
            requestAnimationFrame(() => { if (!consumed) end(failed); });
            return response;
        } catch (error) { end(error); throw error; }
    };
    function scopeChange() {
        scopeVersion++;
        document.body.classList.add('sf-scope-changing');
        if (!document.querySelector('.sf-scope-loader')) {
            const message = inline('Loading shop data…'); message.classList.add('sf-scope-loader'); document.body.append(message);
        }
        for (const state of states.values()) {
            state.element.querySelectorAll('tbody').forEach(body => body.replaceChildren()); state.loaded = false;
        }
    }
    function syncButtons(root = document) {
        for (const element of root.querySelectorAll('button[aria-busy=true],form[data-saving=true] button[type=submit],form[aria-busy=true] button[type=submit],button[data-sf-busy=true]')) element.classList.add('sf-button-loading');
        for (const element of root.querySelectorAll('.sf-button-loading')) {
            if (!buttonStates.has(element) && element.getAttribute('aria-busy') !== 'true' && !element.closest('form[data-saving=true],form[aria-busy=true]') && !activatedButtons.has(element) && element.dataset.sfBusy !== 'true') element.classList.remove('sf-button-loading');
        }
    }
    document.addEventListener('click', event => {
        const action = event.target.closest('button'); if (action && !action.disabled) activatedButtons.add(action);
    }, true);
    function syncActivated() {
        for (const action of activatedButtons) {
            if (!action.isConnected || !action.disabled) { activatedButtons.delete(action); if (!buttonStates.has(action)) action.classList.remove('sf-button-loading'); }
            else action.classList.add('sf-button-loading');
        }
    }
    // A document navigation precedes all destination-page API reads. Keep that
    // wait visible without intercepting navigation or bypassing discard guards.
    function clearNavigation() {
        document.querySelector('.sf-navigation-loader')?.remove();
        document.body?.classList.remove('sf-navigation-pending');
    }
    function navigationDestination(url) {
        return url.origin === location.origin && /^https?:$/.test(url.protocol)
            && !/^\/api\//i.test(url.pathname)
            && !/(?:^|\/)(?:export|download|print)(?:\/|$)|\.(?:csv|pdf|xlsx?|zip)$/i.test(url.pathname);
    }
    function showNavigation() {
        document.body?.classList.add('sf-navigation-pending');
    }
    document.addEventListener('click', event => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest('a[href]');
        if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
        const url = new URL(link.href, location.href);
        if (!navigationDestination(url) || (url.pathname === location.pathname && url.search === location.search && url.hash !== location.hash)) return;
        // Let all application handlers finish, including canceled navigation.
        queueMicrotask(() => { if (!event.defaultPrevented) showNavigation(); });
    });
    if (window.navigation) {
        window.navigation.addEventListener('navigate', event => {
            if (event.hashChange || event.downloadRequest != null || event.formData) return;
            if (!navigationDestination(new URL(event.destination.url))) return;
            queueMicrotask(() => { if (!event.defaultPrevented) showNavigation(); });
        });
        for (const type of ['navigateerror','navigatesuccess']) window.navigation.addEventListener(type, clearNavigation);
    }
    document.addEventListener('keydown', event => { if (event.key === 'Escape') clearNavigation(); });
    document.addEventListener('DOMContentLoaded', () => {
        syncButtons();
        new MutationObserver(changes => {
            if (changes.some(change => ['aria-busy', 'data-saving', 'data-sf-busy','disabled'].includes(change.attributeName))) {syncActivated();syncButtons();}
        }).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['aria-busy', 'data-saving', 'data-sf-busy','disabled'] });
    });
    window.addEventListener('pageshow', event => {
        clearNavigation();
        if (event.persisted && document.body?.classList.contains('sf-scope-changing')) { location.reload(); return; }
        document.body?.classList.remove('sf-scope-changing'); document.querySelector('.sf-scope-loader')?.remove();
    });
    return { region, inline, button, updateTable, InlineLoader: inline, ButtonLoader: button, scopeChange,
        TableSkeleton: options => skeleton('table', options), CardSkeleton: options => skeleton('card', options),
        DashboardSkeleton: options => skeleton('dashboard', options), ReportSkeleton: options => skeleton('report', options) };
})();
