// Keep API requests tied to the signed-in stock location. Reports have their own location filter.
(() => {
    'use strict';

    const locationId = Number(
        document.querySelector('meta[name="stockflow-location"]')?.content || 0
    );
    window.StockFlowLocation = locationId;
    const original = window.fetch.bind(window);
    window.fetch = (input, options = {}) => {
        const url = new URL(typeof input === 'string' ? input : input.url, location.href);
        const method = (options.method || input.method || 'GET').toUpperCase();
        if (url.origin === location.origin && !['GET', 'HEAD'].includes(method)) {
            const headers = new Headers(options.headers || input.headers);
            headers.set('X-StockFlow-Location', String(locationId));
            options = {
                ...options,
                headers
            };
        }
        return original(input, options);
    };
    window.sfBranchApi = async (url, body, method = 'POST') => {
        const response = await fetch(
            url,
            body === undefined
                ? {}
                : {
                      method,
                      headers: {
                          'Content-Type': 'application/json',
                          'X-CSRF-TOKEN':
                              document.querySelector('meta[name="csrf-token"]')?.content || ''
                      },
                      body: JSON.stringify(body)
                  }
        );
        const data = response.status === 204 ? null : await response.json();
        if (!response.ok) {
            throw new Error(
                data?.message ||
                    data?.title ||
                    Object.values(data?.errors || {})
                        .flat()
                        .join(' ') ||
                    'Request failed.'
            );
        }
        return data;
    };
    document.addEventListener('DOMContentLoaded', async () => {
        const select = document.getElementById('report-location');
        if (!select) {
            return;
        }
        try {
            const context = await window.sfBranchApi('/api/branches/context');
            if (!context.canReviewStores) {
                select.disabled = true;
                return;
            }
            select.replaceChildren();
            const add = (label, loc, store) => {
                const option = new Option(label, `${loc}:${store}`);
                select.add(option);
            };
            add('All authorized stores / warehouses', 0, 0);
            const stores = new Map(
                context.locations
                    .filter((value) => {
                        return value.storeId;
                    })
                    .map((store) => {
                        return [store.storeId, store.storeName];
                    })
            );
            for (const [id, name] of stores) {
                add(`${name} / all locations`, 0, id);
            }
            for (const store of context.locations) {
                add(
                    `${store.storeName} / ${store.locationName}`,
                    store.locationId,
                    store.storeId || 0
                );
            }
            select.value = `${Number(context.locationId)}:${Number(context.storeId)}`;
            select.disabled = false;
            const originalSelection=select.value;
            select.addEventListener('change', async () => {
                if(window.StockFlowUnsaved&&!await StockFlowUnsaved.confirmNavigation()){select.value=originalSelection;return;}
                select.disabled = true;
                try {
                    const [locationId, storeId] = select.value.split(':').map(Number);
                    await window.sfBranchApi('/api/branches/context', {
                        locationId,
                        storeId
                    });
                    window.StockFlowUnsaved?.allowNavigation();
                    window.StockFlowLoading?.scopeChange();
                    location.reload();
                } catch (error) {
                    select.value=originalSelection;
                    StockFlowNotify.show('error',error.message,{key:'branch-change'});
                    select.disabled = false;
                }
            });
        } catch (error) {
            select.title = error.message;
            select.disabled = true;
        }
    });
})();
