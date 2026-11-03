(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const pending = location.pathname.toLowerCase().endsWith('/pending');
    let offset = 0;
    let generation = 0;
    if (pending) {
        for (const option of [...getElement('status').options]) {
            if (['DELIVERED', 'CANCELLED'].includes(option.value)) {
                option.remove();
            }
        }
    }
    getElement('status').value = new URLSearchParams(location.search).get('status') || '';
    async function load(pagingRequest) {
        const current = ++generation;
        const query = new URLSearchParams({
            term: getElement('term').value,
            status: getElement('status').value,
            pendingOnly: pending,
            offset
        });
        for (const name of ['from', 'to']) {
            if (getElement(name).value) {
                query.set(name, getElement(name).value);
            }
        }
        const response = await ServiceUI.api('/api/service?' + query);
        const items = servicePager.capture(response,pagingRequest);if(!items)return;
        if (current !== generation) {
            return;
        }
        ServiceUI.table(
            'service-results',
            [
                'Number',
                'Customer',
                'Mobile',
                'Product',
                'Serial',
                'Status',
                'Technician',
                'Received',
                'Expected',
                'Delivered',
                'Total charge',
                'Details'
            ],
            items.map((product) => {
                return [
                    product.serviceNumber,
                    product.customerName,
                    product.customerMobile,
                    product.productName,
                    product.serialNumber,
                    StockFlowText.label(product.status),
                    product.assignedToName,
                    ServiceUI.time(product.receivedDate),
                    ServiceUI.date(product.expectedDeliveryDate),
                    ServiceUI.time(product.deliveredDate),
                    ServiceUI.money(product.totalCharge),
                    ServiceUI.link(product.serviceRequestId)
                ];
            })
        );
        getElement('previous').disabled = offset === 0;
        getElement('next').disabled = items.length < 100;
    }
    getElement('service-search').onsubmit = (event) => {
        event.preventDefault();
        offset = 0;
        ServiceUI.run(load);
    };
    getElement('previous').onclick = () => {
        offset = Math.max(0, offset - 100);
        ServiceUI.run(load);
    };
    getElement('next').onclick = () => {
        offset += 100;
        ServiceUI.run(load);
    };
    const footer=document.createElement('div');getElement('next').parentElement.append(footer);
    const servicePager=StockFlowPagination.adapt({host:footer,root:'#service-results',legacy:{previous:'#previous',next:'#next'},getOffset:()=>offset,setOffset:value=>offset=value,load});load=servicePager.load;
    ServiceUI.run(load);
})();
