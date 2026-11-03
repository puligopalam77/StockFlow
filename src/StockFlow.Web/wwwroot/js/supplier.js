(() => {
    const types = [
        'Manufacturer',
        'Distributor',
        'Wholesaler',
        'Dealer',
        'Importer',
        'Service Provider',
        'Other'
    ];
    function terms(controls, fromRecord = false, record = null) {
        if (fromRecord) {
            const days = record?.paymentTermsDays;
            controls.paymentTermsChoice.value =
                days == null ? '' : [0, 7, 15, 30, 45, 60].includes(days) ? String(days) : 'CUSTOM';
            controls.paymentTermsDays.value = days ?? '';
        }
        const custom = controls.paymentTermsChoice.value === 'CUSTOM';
        controls.paymentTermsDays.parentElement.hidden = !custom;
        controls.paymentTermsDays.required = custom;
        if (!custom) {
            controls.paymentTermsDays.value = controls.paymentTermsChoice.value;
        }
    }
    function tax(controls) {
        controls.state.required =
            controls.countryCode.value === 'IN' && controls.gstNumber.value.trim() !== '';
        controls.gstNumber.setAttribute('aria-describedby', 'supplier-gst-help');
        const patterns = {
            IN: '[1-9][0-9]{5}',
            US: '[0-9]{5}(-[0-9]{4})?',
            CA: '[A-Z][0-9][A-Z] ?[0-9][A-Z][0-9]'
        };
        controls.postalCode.pattern = patterns[controls.countryCode.value] || '[\\p{L}0-9][\\p{L}0-9 \\-]{0,19}';
        controls.postalCode.title = 'Enter a valid PIN / postal code for the selected country.';
    }
    StockFlowMaster.start({
        singular: 'Supplier',
        id: 'supplierId',
        statusSection: 'Status',
        fields: [
            {
                name: 'supplierName',
                label: 'Supplier Name',
                type: 'text',
                required: true,
                minLength: 2,
                maxLength: 150,
                section: 'Basic information'
            },
            {
                name: 'supplierCode',
                label: 'Supplier Code',
                type: 'text',
                readOnly: true,
                default: 'Generated on save'
            },
            {
                name: 'supplierType',
                label: 'Supplier Type',
                type: 'select',
                valueType: 'string',
                options: [
                    ['', 'Not specified'],
                    ...types.map((t) => {
                        return [t.toUpperCase().replaceAll(' ', '_'), t];
                    })
                ]
            },
            {
                name: 'contactPerson',
                label: 'Contact Person',
                type: 'text',
                minLength: 2,
                maxLength: 100
            },
            {
                name: 'mobileNumber',
                label: 'Mobile Number',
                type: 'tel',
                maxLength: 10,
                section: 'Contact information',
                placeholder: '10-digit number'
            },
            {
                name: 'alternateMobile',
                label: 'Alternate Mobile',
                type: 'tel',
                maxLength: 10
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
                maxLength: 500,
                section: 'Address & tax'
            },
            {
                name: 'countryCode',
                label: 'Country',
                type: 'select',
                valueType: 'string',
                required: true,
                default: 'IN',
                options: [['', 'Select country']]
            },
            {
                name: 'state',
                label: 'State / Province',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'city',
                label: 'City',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'postalCode',
                label: 'PIN / postal code',
                type: 'text',
                maxLength: 20
            },
            {
                name: 'gstNumber',
                label: 'GSTIN (India)',
                type: 'text',
                maxLength: 15
            },
            {
                name: 'paymentTermsChoice',
                label: 'When should we pay?',
                type: 'select',
                valueType: 'string',
                transient: true,
                options: [
                    ['', 'Not specified'],
                    ['0', 'Immediate'],
                    ...['7', '15', '30', '45', '60'].map((value) => {
                        return [value, value + ' Days'];
                    }),
                    ['CUSTOM', 'Custom']
                ],
                section: 'Purchasing details'
            },
            {
                name: 'paymentTermsDays',
                label: 'Number of days until payment',
                type: 'number',
                nullable: true,
                min: 0,
                max: 365,
                step: '1'
            },
            {
                name: 'notes',
                label: 'Notes',
                type: 'textarea',
                maxLength: 1000
            }
        ],
        columns: [
            'supplierCode',
            'supplierName',
            'supplierType',
            'mobileNumber',
            'email',
            'countryCode',
            'state',
            'city',
            'gstNumber',
            'paymentTermsDays'
        ],
        columnLabels: {
            countryCode: 'Country',
            paymentTermsDays: 'Payment Terms (Days)'
        },
        async onReady(controls) {
            const response = await fetch('/api/suppliers/options');
            if (!response.ok) {
                throw new Error('Unable to load supplier countries. Refresh to try again.');
            }
            const options = await response.json();
            for (const country of options.countries) {
                controls.countryCode.add(new Option(country.name, country.code));
            }
            const help = document.createElement('small');
            help.id = 'supplier-gst-help';
            help.className = 'text-muted d-block';
            help.textContent =
                'Optional for Indian suppliers. Format validation does not verify GST registration.';
            controls.gstNumber.after(help);
            controls.countryCode.addEventListener('change', () => {
                return tax(controls);
            });
            controls.gstNumber.addEventListener('input', () => {
                return tax(controls);
            });
            controls.gstNumber.addEventListener('blur', () => {
                controls.gstNumber.value = controls.gstNumber.value.trim().toUpperCase();
            });
            controls.paymentTermsChoice.addEventListener('change', () => {
                return terms(controls);
            });
        },
        onOpen(controls, record) {
            // Existing Indian suppliers are stored with +91; edit the local 10 digits.
            for (const name of ['mobileNumber', 'alternateMobile']) {
                if (record?.countryCode === 'IN' && /^\+91[0-9]{10}$/.test(controls[name].value)) {
                    controls[name].value = controls[name].value.slice(3);
                }
            }
            if (record?.countryCode === 'ZZ') {
                controls.countryCode.value = '';
            }
            terms(controls, true, record);
            tax(controls);
        },
        async beforeSave(body, id, requestJson) {
            const matches = await requestJson(
                '/api/suppliers/duplicates?' +
                    new URLSearchParams({
                        name: body.supplierName || '',
                        mobile: body.mobileNumber || '',
                        alternate: body.alternateMobile || '',
                        email: body.email || '',
                        gst: body.gstNumber || '',
                        country: body.countryCode,
                        excludingId: id || ''
                    })
            );
            if (!matches.length) {
                return true;
            }
            const dialog = document.createElement('dialog');
            dialog.className = 'configuration-dialog';
            dialog.setAttribute('aria-labelledby', 'supplier-duplicate-title');
            const title = document.createElement('h2');
            title.id = 'supplier-duplicate-title';
            title.textContent = 'Possible Duplicate Supplier';
            dialog.append(title);
            const text = document.createElement('p');
            text.textContent =
                'A supplier with similar details already exists. Review it before continuing.';
            dialog.append(text);
            for (const match of matches) {
                const details = document.createElement('details');
                details.className = 'supplier-duplicate-details';
                const summary = document.createElement('summary');
                summary.textContent =
                    'View Existing Supplier: ' +
                    (match.supplierCode || '') +
                    ' ' +
                    match.supplierName;
                details.append(summary);
                for (const [label, value] of [
                    ['Mobile', match.mobileNumber],
                    ['Alternate mobile', match.alternateMobile],
                    ['Email', match.email],
                    ['GSTIN', match.gstNumber],
                    ['Address', match.address],
                    ['City', match.city],
                    ['Status', match.isActive ? 'Active' : 'Inactive']
                ]) {
                    const line = document.createElement('div');
                    line.textContent = label + ': ' + (value || 'Not provided');
                    details.append(line);
                }
                dialog.append(details);
            }
            const actions = document.createElement('div');
            actions.className = 'configuration-dialog-actions';
            for (const [label, value, style] of [
                ['Cancel', 'cancel', 'dark'],
                ['Continue Anyway', 'continue', 'primary']
            ]) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-' + style;
                button.textContent = label;
                button.onclick = () => {
                    return dialog.close(value);
                };
                actions.append(button);
            }
            dialog.append(actions);
            document.body.append(dialog);
            dialog.showModal();
            actions.querySelector('button').focus();
            return await new Promise((resolve) => {
                return dialog.addEventListener(
                    'close',
                    () => {
                        const proceed = dialog.returnValue === 'continue';
                        dialog.remove();
                        resolve(proceed);
                    },
                    {
                        once: true
                    }
                );
            });
        }
    });
})();
