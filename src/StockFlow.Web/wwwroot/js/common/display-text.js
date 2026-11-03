// Display labels only: saved codes and identifiers keep their original values.
window.StockFlowText = {
    label(value) {
        const labels = {
            DEFAULT: 'Standard model',
            COMPLETED: 'Completed',
            DAMAGED: 'Damaged - not for sale',
            OUTSTANDING: 'Payment still due',
            DRAFT: 'Draft (not finished)',
            PARTIALLY_RECEIVED: 'Partly received',
            IN_STOCK: 'In stock',
            IN_TRANSIT: 'On the way',
            WAITING_FOR_PART: 'Waiting for a part',
            READY: 'Ready for collection',
            INSPECTION: 'Being checked',
            REPAIRING: 'Being repaired',
            FULL_REPLACEMENT: 'Full replacement',
            PRO_RATA: 'Replacement with part-payment credit',
            SERVICE_ONLY: 'Service only',
            NO_FAULT_FOUND: 'No fault found',
            MANUFACTURER_REFERRED: 'Sent to manufacturer',
            MANUFACTURER: 'Manufacturer',
            DEALER: 'Shop / dealer',
            BOTH: 'Manufacturer and shop',
            CONTINUE_ORIGINAL: 'Keep original warranty dates',
            NEW_FULL_WARRANTY: 'Start a new full warranty',
            READY_TO_SEND: 'Ready to send',
            SENT_TO_PROVIDER: 'Sent for repair',
            PROVIDER_RECEIVED: 'Received by repair provider',
            RETURNED_TO_CUSTOMER: 'Returned to customer',
            BANK_TRANSFER: 'Bank transfer',
            BANK: 'Bank payment',
            CREDIT: 'Pay later (credit)',
            UPI: 'UPI',
            SKU: 'Shop item code',
            QUANTITY: 'Count by quantity',
            SERIAL: 'Track each serial number',
            INTERNAL: 'StockFlow barcode',
            ADJUSTMENT_IN: 'Stock added after correction',
            ADJUSTMENT_OUT: 'Stock removed after correction',
            OPENING_STOCK: 'Starting stock',
            CUSTOMER_RETURN: 'Customer return',
            SUPPLIER_RETURN: 'Return to supplier',
            SERVICE_IN: 'Stock returned from repair',
            SERVICE_OUT: 'Parts used for repair',
            TRANSFER_IN: 'Received from another location',
            TRANSFER_OUT: 'Sent to another location',
            SALES_FLOOR: 'Shop floor',
            SERVICE_CENTER: 'Repair centre',
            STOCKROOM: 'Stock room',
            BACKROOM: 'Back room'
        };
        if (value == null || value === '') {
            return '—';
        }
        const code = String(value);
        return (
            labels[code] ||
            code
                .toLowerCase()
                .replaceAll('_', ' ')
                .replace(/^./, (value2) => {
                    return value2.toUpperCase();
                })
        );
    }
};
