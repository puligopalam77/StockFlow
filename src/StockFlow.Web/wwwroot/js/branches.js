(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const requestJson = window.sfBranchApi;
    let stores = [];
    let locations = [];
    let userId = 0;
    const message = (text, error = false) => {
        getElement('branch-message').textContent = text;
        getElement('branch-message').className = `alert alert-${error ? 'danger' : 'success'}`;
    };
    const runAction = async (f) => {
        try {
            await f();
        } catch (error) {
            message(error.message, true);
        }
    };
    const check = (container, id, name, selected) => {
        const label = document.createElement('label');
        label.className = 'd-block';
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.value = id;
        input.checked = selected;
        label.append(input, document.createTextNode(' ' + name));
        container.append(label);
    };
    async function load() {
        const context = await requestJson('/api/branches/context');
        stores = await requestJson('/api/branches/stores');
        locations = context.locations;
        getElement('location-store').replaceChildren(
            new Option('Central warehouse (no retail store)', '')
        );
        for (const store of stores) {
            getElement('location-store').add(new Option(store.storeName, store.storeId));
        }
        getElement('branch-locations').replaceChildren();
        for (const stockLocation of locations) {
            const paragraph = document.createElement('p');
            paragraph.textContent = `${stockLocation.storeName} / ${stockLocation.locationName} (${StockFlowText.label(stockLocation.locationType)})`;
            getElement('branch-locations').append(paragraph);
        }
        if (!context.isOwner) {
            for (const f of document.querySelectorAll('form')) {
                for (const el of f.elements) {
                    el.disabled = true;
                }
            }
            message('Store administration requires the business owner.', true);
        }
    }
    getElement('store-form').onsubmit = (event) => {
        event.preventDefault();
        runAction(async () => {
            await requestJson(
                '/api/branches/stores',
                Object.fromEntries(new FormData(event.target))
            );
            event.target.reset();
            await load();
            message('Store created. Select its main stock location in the top bar.');
        });
    };
    getElement('location-form').onsubmit = (event) => {
        event.preventDefault();
        runAction(async () => {
            const body = Object.fromEntries(new FormData(event.target));
            body.storeId = Number(body.storeId) || null;
            await requestJson('/api/branches/locations', body);
            await load();
            message('Location created. Reload to update the top-bar selector.');
        });
    };
    getElement('load-access').onclick = () => {
        return runAction(async () => {
            userId = Number(getElement('access-user').value);
            if (userId < 1) {
                throw new Error('Enter a user ID.');
            }
            const access = await requestJson(`/api/branches/users/${userId}`);
            getElement('access-stores').replaceChildren();
            getElement('access-warehouses').replaceChildren();
            getElement('default-store').replaceChildren(new Option('First assigned store', ''));
            for (const store of stores) {
                check(
                    getElement('access-stores'),
                    store.storeId,
                    store.storeName,
                    access.storeIds.includes(store.storeId)
                );
                getElement('default-store').add(new Option(store.storeName, store.storeId));
            }
            for (const stockLocation of locations.filter((value) => {
                return !value.storeId && value.locationType === 'WAREHOUSE';
            })) {
                check(
                    getElement('access-warehouses'),
                    stockLocation.locationId,
                    stockLocation.locationName,
                    access.warehouseLocationIds.includes(stockLocation.locationId)
                );
            }
            getElement('default-store').value = access.defaultStoreId || '';
            getElement('access-form').hidden = false;
        });
    };
    getElement('find-access-users').onclick = () => {
        return runAction(async () => {
            const users = await requestJson(
                '/api/admin/users?term=' + encodeURIComponent(getElement('access-search').value)
            );
            getElement('access-user').replaceChildren();
            for (const user of users) {
                getElement('access-user').add(
                    new Option(`${user.fullName} (${user.username})`, user.userId)
                );
            }
            if (!users.length) {
                message('No matching users.', true);
            }
        });
    };
    getElement('access-form').onsubmit = (event) => {
        event.preventDefault();
        runAction(async () => {
            const selected = (id) => {
                return [...getElement(id).querySelectorAll('input:checked')].map((value) => {
                    return Number(value.value);
                });
            };
            await requestJson(
                `/api/branches/users/${userId}`,
                {
                    storeIds: selected('access-stores'),
                    warehouseLocationIds: selected('access-warehouses'),
                    defaultStoreId: Number(getElement('default-store').value) || null
                },
                'PUT'
            );
            message('Access saved. Membership is checked on every request.');
        });
    };
    runAction(load);
})();
