(() => {
    let offset = 0;
    let load = pagingRequest => {
        return Warranty.run(async () => {
            const response = await Warranty.api(
                '/api/warranty/expiring?days=' +
                    document.getElementById('expiry-days').value +
                    '&offset=' +
                    offset
            );
            const items=warrantyPager.capture(response,pagingRequest);if(!items)return;
            Warranty.results(items);
            document.getElementById('previous').disabled = offset === 0;
            document.getElementById('next').disabled = items.length < 100;
        });
    };
    document.getElementById('expiry-days').onchange = () => {
        offset = 0;
        load();
    };
    document.getElementById('previous').onclick = () => {
        offset = Math.max(0, offset - 100);
        load();
    };
    document.getElementById('next').onclick = () => {
        offset += 100;
        load();
    };
    const footer=document.createElement('div');document.getElementById('next').parentElement.append(footer);
    const warrantyPager=StockFlowPagination.adapt({host:footer,root:document.getElementById('warranty-message').parentElement,legacy:{previous:'#previous',next:'#next'},getOffset:()=>offset,setOffset:value=>offset=value,load});load=warrantyPager.load;
    load();
})();
