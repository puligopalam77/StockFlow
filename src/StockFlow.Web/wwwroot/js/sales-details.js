Sales.run(async () => {
    const id = location.pathname.split('/').pop();
    const timeZone=document.getElementById('sale-operations')?.dataset.timeZone||'Asia/Kolkata';
    const sale = await Sales.api('/api/sales/' + encodeURIComponent(id));
    const invoiceLink = document.getElementById('view-invoice');
    invoiceLink.href = '/Sales/Invoice/' + sale.saleId;
    invoiceLink.hidden = false;
    document.getElementById('sale-title').textContent = sale.invoiceNumber;
    for (const [label, value] of [
        ['Customer', sale.customerName || 'Walk-in'],
        ['Date', new Date(sale.saleDate).toLocaleString('en-IN',{timeZone})],
        ['Status', StockFlowText.label(sale.status)],
        ...(sale.itemSubtotal != null ? [['Item Subtotal', Sales.money(sale.itemSubtotal)],['Item Discounts', Sales.money(sale.itemDiscountAmount)],['Invoice Discount', Sales.money(sale.invoiceDiscountAmount)],['Taxable Amount', Sales.money(sale.taxableAmount)]] : []),
        [sale.itemSubtotal != null ? 'Net value (excluding GST)' : 'Subtotal (after line discounts)', Sales.money(sale.subtotal)],
        ['Tax', Sales.money(sale.taxAmount)],
        [sale.itemSubtotal != null ? 'Other allowance (after tax)' : 'Invoice discount', Sales.money(sale.discountAmount)],
        ['Total', Sales.money(sale.totalAmount)],
        ['Created by', sale.createdByName],
        ['Notes', sale.notes || '—']
    ]) {
        const dt = document.createElement('dt');
        const dd = document.createElement('dd');
        dt.className = 'col-sm-4';
        dd.className = 'col-sm-8';
        dt.textContent = label;
        dd.textContent = value;
        document.getElementById('sale-header').append(dt, dd);
    }
    for (const item of sale.lines) {
        const tr = document.createElement('tr');
        const warranty = item.warrantyEndDate
            ? `${StockFlowText.label(item.warrantyStatus)}: ${item.warrantyStartDate.slice(0, 10)} to ${item.warrantyEndDate.slice(0, 10)}`
            : '—';
        [
            [item.productName, item.serialNumber, StockFlowVariantIdentity.details(item),item.description,item.mrp?'MRP ₹'+Sales.money(item.mrp):null,item.hsnSac?'HSN/SAC: '+item.hsnSac:null].filter(Boolean).join(' / '),
            item.isManual ? 'Manual Item' : item.sku || '\u2014',
            item.quantity,
            Sales.money(item.sellingPrice),
            Sales.money(item.discountAmount)+(item.discountType==='PERCENTAGE'?' ('+item.discountPercent+'%)':''),
            item.taxPercentage,
            Sales.money(item.lineTotal),
            warranty
        ].forEach((value) => {
            return Sales.cell(tr, value);
        });
        document.getElementById('sale-lines').append(tr);
    }
    for (const payment of sale.payments) {
        const tr = document.createElement('tr');
        [
            payment.paymentDate ? new Date(payment.paymentDate).toLocaleString('en-IN', {timeZone}) : '\u2014',
            payment.paymentMode==='CARD' && ['DEBIT','CREDIT'].includes(payment.cardType) ? (payment.cardType==='DEBIT'?'Debit Card':'Credit Card') : payment.paymentMode==='CREDIT'?'Customer Credit / Pay Later':StockFlowText.label(payment.paymentMode),
            Sales.money(payment.amount),
            payment.referenceNumber
        ].forEach((value) => {
            return Sales.cell(tr, value);
        });
        document.getElementById('sale-payments').append(tr);
    }
    const credit = sale.payments
        .filter((payment) => {
            return payment.paymentMode === 'CREDIT';
        })
        .reduce((sum, value) => {
            return sum + value.amount;
        }, 0);
    const received = sale.payments
        .filter((payment) => {
            return payment.paymentMode !== 'CREDIT';
        })
        .reduce((sum, value) => {
            return sum + value.amount;
        }, 0);
    document.getElementById('sale-payment-summary').textContent =
        `Received: ${Sales.money(received)} · Credit recorded at sale: ${Sales.money(credit)}`;
});
