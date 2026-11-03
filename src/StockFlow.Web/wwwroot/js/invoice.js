(() => {
    const print = async () => {
        await Promise.all(Array.from(document.querySelectorAll('.invoice-paper img')).map(image => image.decode().catch(() => {})));
        return window.print();
    };
    document.getElementById('invoice-print-layout')?.addEventListener('change', (event) => {
        const thermal = event.target.value === 'thermal';
        document.body.classList.toggle('invoice-thermal', thermal);
        let style = document.getElementById('invoice-page-format');
        if (!style) {
            style = document.createElement('style');
            style.id = 'invoice-page-format';
            document.head.append(style);
        }
        style.textContent = thermal
            ? '@page { size:auto; margin:3mm; }'
            : '@page { size:A4 portrait; margin:12mm 10mm; }';
    });
    document.getElementById('print-invoice')?.addEventListener('click', print);
    document.getElementById('save-invoice-pdf')?.addEventListener('click', print);
    if(new URLSearchParams(location.search).get('print')==='1') {
        if(document.readyState==='complete') print();
        else window.addEventListener('load',print,{once:true});
    }
})();
