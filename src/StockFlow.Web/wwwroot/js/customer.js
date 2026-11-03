StockFlowMaster.start({
    singular: 'Customer',
    id: 'customerId',
    fields: [
        {
            name: 'customerName',
            label: 'Customer Name',
            type: 'text',
            maxLength: 150,
            minLength: 2,
            required: true
        },
        {
            name: 'mobileNumber',
            label: 'Mobile Number',
            type: 'tel',
            maxLength: 10,
            pattern: '[0-9]{10}',
            help: 'Optional. Enter exactly 10 digits, without a country code.'
        },
        {
            name: 'email',
            label: 'Email',
            type: 'email',
            maxLength: 150
        },
        {
            name: 'address',
            label: 'Address',
            type: 'textarea',
            maxLength: 1000
        },
        {
            name: 'gstNumber',
            label: 'GST Number',
            type: 'text',
            maxLength: 15,
            pattern: '[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]',
            title: 'Enter a valid 15-character GSTIN.'
        }
    ],
    columns: ['customerName', 'mobileNumber', 'email', 'gstNumber'],
    onLoaded({records,rows}) {
        if(!document.getElementById('customer-crm-link'))return;
        for(let i=0;i<rows.length;i++){
            const name=rows[i].querySelector('td');if(!name)continue;
            const a=document.createElement('a');a.textContent=records[i].customerName;a.href='/Crm?customerId='+encodeURIComponent(records[i].customerId);name.replaceChildren(a);
        }
    }
});
