(() => {
    if (window.StockFlowSupplierDirectory?.start()) return;
    const types = [
        'Manufacturer',
        'Distributor',
        'Wholesaler',
        'Dealer',
        'Importer',
        'Service Provider',
        'Other'
    ];
    let supplierControls,currentSupplier=null,duplicateSequence=0,duplicateTimer;
    const element=(tag,copy)=>{const e=document.createElement(tag);if(copy)e.textContent=copy;return e;};
    function clearField(input){input.removeAttribute('aria-invalid');document.getElementById(input.id+'-server-error')?.remove();const ids=(input.getAttribute('aria-describedby')||'').split(/\s+/).filter(id=>id&&id!==input.id+'-server-error');if(ids.length)input.setAttribute('aria-describedby',ids.join(' '));else input.removeAttribute('aria-describedby');}
    function clearErrors(){Object.values(supplierControls||{}).forEach(clearField);}
    function fieldErrors(error){clearErrors();const errors=Object.entries(error.fields||{});if(!errors.length){const signals=[['supplierName',/supplier name/i],['district',/district/i],['addressLine1',/address/i],['countryCode',/country/i],['alternateMobile',/alternate/i],['mobileNumber',/mobile|phone/i],['gstNumber',/GSTIN/i],['state',/state/i],['postalCode',/postal|PIN/i],['email',/email/i],['contactPerson',/contact person/i],['paymentTermsDays',/payment/i]];const key=signals.find(([,r])=>r.test(error.message))?.[0];if(key)errors.push([key,[error.message]]);}let first;for(const [key,values]of errors){const name=Object.keys(supplierControls).find(n=>n.toLowerCase()===key.split('.').at(-1).toLowerCase()),input=supplierControls[name==='address'&&supplierControls.useStructuredAddress.checked?'addressLine1':name];if(!input)continue;const note=element('small',(Array.isArray(values)?values:[values]).join(' '));note.className='supplier-field-error';note.id=input.id+'-server-error';note.setAttribute('role','alert');input.setAttribute('aria-invalid','true');input.setAttribute('aria-describedby',[input.getAttribute('aria-describedby'),note.id].filter(Boolean).join(' '));(input.sfAddressPicker?.wrapper||input).after(note);input.sfAddressPicker?.refresh();first??=input;}if(first){reveal(first);first.focus();}}
    function duplicateQuery(){return new URLSearchParams({name:supplierControls.supplierName.value,mobile:supplierControls.mobileNumber.value,alternate:supplierControls.alternateMobile.value,email:supplierControls.email.value,gst:supplierControls.gstNumber.value,country:supplierControls.countryCode.value,excludingId:currentSupplier?.supplierId||''});}
    function duplicateHint(){clearTimeout(duplicateTimer);const mine=++duplicateSequence;duplicateTimer=setTimeout(async()=>{const box=document.getElementById('supplier-duplicate-hint');try{const response=await fetch('/api/suppliers/duplicates?'+duplicateQuery(),{credentials:'same-origin'});if(!response.ok)throw Error();const rows=await response.json();if(mine!==duplicateSequence)return;box.replaceChildren();box.hidden=!rows.length;if(rows.length){box.append(element('strong','Possible existing supplier found'),element('p','Similar details are a review signal, not proof of duplication.'));for(const row of rows){const a=element('a',(row.supplierCode||'')+' '+row.supplierName);a.href='/Suppliers/View?id='+encodeURIComponent(row.supplierId);a.target='_blank';a.rel='noopener';box.append(a);}}}catch{if(mine===duplicateSequence){box.hidden=false;box.textContent='Duplicate preview unavailable. Review will be retried when saving.';}}},350);}
    function reveal(input) {
        for (let parent = input.parentElement; parent; parent = parent.parentElement) {
            if (parent.tagName === 'DETAILS') parent.open = true;
        }
    }
    function modernize(controls) {
        supplierControls = controls;
        const root = document.getElementById('master-data'), editor = document.getElementById('editor'), fields = document.getElementById('form-fields');
        root.classList.add('supplier-workspace');
        editor.classList.add('supplier-modern');
        const groups = [
            ['Supplier Details', 'users', ['supplierName', 'supplierCode', 'supplierType', 'contactPerson', 'mobileNumber']],
            ['Contact & Address (Optional)', 'chat', ['alternateMobile', 'email', 'address', 'useStructuredAddress', 'addressLine1', 'addressLine2', 'state', 'district', 'city', 'countryCode', 'postalCode']],
            ['GST Details (Optional)', 'document', ['gstNumber']],
            ['Purchasing Preferences (Optional)', 'truck', ['paymentTermsChoice', 'paymentTermsDays']],
            ['Notes (Optional)', 'document', ['notes']],
            ['Supplier Status', 'shield', ['isActive']]
        ];
        const cards = [], additional = element('div');
        additional.className = 'supplier-additional';
        additional.append(element('h3', 'Additional Details (Optional)'));
        for (const [index, [title, icon, names]] of groups.entries()) {
            const optional = index > 0 && index < 5;
            const section = element(optional ? 'details' : 'section');
            section.className = 'supplier-card' + (optional ? ' supplier-optional' : index === 0 ? ' supplier-basic' : ' supplier-status');
            const heading = element(optional ? 'summary' : 'h3');
            heading.innerHTML = '<svg aria-hidden="true" class="supplier-section-icon"><use href="/images/icons.svg#' + icon + '" /></svg>';
            heading.append(document.createTextNode(title));
            if(optional){heading.setAttribute('aria-expanded','false');section.addEventListener('toggle',()=>heading.setAttribute('aria-expanded',String(section.open)));}
            const grid = element('div');
            grid.className = 'supplier-fields';
            for (const name of names) {
                const input = controls[name];
                if (!input) continue;
                const group = input.closest('.col-md-6,.col-12');
                group.className = 'supplier-field' + (['address', 'addressLine1', 'addressLine2', 'useStructuredAddress', 'notes'].includes(name) ? ' supplier-wide' : '') + (['addressLine1', 'addressLine2'].includes(name) ? ' supplier-address-line' : '');
                if (input.tagName === 'TEXTAREA') input.rows = 2;
                grid.append(group);
            }
            if (grid.children.length) {
                section.append(heading, grid);
                if (optional) additional.append(section);
                else cards.push(section);
            }
        }
                fields.replaceChildren(cards[0], additional, ...cards.slice(1));
        const code = controls.supplierCode, locked = element('div');
        locked.className = 'supplier-locked';
        locked.innerHTML = '<svg aria-hidden="true" viewBox="0 0 24 24"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>';
        code.before(locked);locked.append(code);code.disabled=true;code.tabIndex=-1;
        const legacy=element('pre');legacy.className='supplier-legacy-address';
        controls.address.hidden=true;controls.address.after(legacy);
        controls.useStructuredAddress.addEventListener('change',()=>addressMode(controls));
        const hints = element('aside');
        hints.id = 'supplier-duplicate-hint';
        hints.className = 'supplier-duplicate-hint';
        hints.hidden = true;
        hints.setAttribute('role', 'status');
        fields.before(hints);
        const header = document.querySelector('.master-screen-heading');
        if (header) {
            const subtitle = element('p', window.StockFlowMasterScreens?.mode === 'edit' ? 'Update supplier details and purchasing information.' : 'Add a supplier you purchase products from.');
            subtitle.className = 'supplier-subtitle';
            header.append(subtitle);
        }
        // Reveal collapsed fields before native validation tries to focus them.
        document.getElementById('master-form').addEventListener('invalid', event => reveal(event.target), true);
        for (const input of Object.values(controls)) input.addEventListener('input', () => clearField(input));
        for (const key of ['supplierName', 'mobileNumber', 'alternateMobile', 'email', 'gstNumber', 'countryCode']) controls[key].addEventListener('input', duplicateHint);
        const launcher = document.querySelector('.sf-help-open');
        if (launcher && !root.dataset.createOnly) {
            const dock = element('div');
            dock.className = 'supplier-assistant-dock';
            editor.after(dock);
            dock.append(launcher);
        }
    }
    function addressMode(controls) {
        for(const key of ['addressLine1','addressLine2']) controls[key].disabled=!controls.useStructuredAddress.checked;
    }
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
                placeholder: 'Enter supplier or business name',
                section: 'Basic information'
            },
            {
                name: 'supplierCode',
                label: 'Supplier Code',
                type: 'text',
                readOnly: true,
                default: 'Generated automatically on save'
            },
            {
                name: 'supplierType',
                label: 'Supplier Type (Optional)',
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
                label: 'Contact Person (Optional)',
                placeholder: 'Person you usually contact',
                type: 'text',
                minLength: 2,
                maxLength: 100
            },
            {
                name: 'mobileNumber',
                label: 'Mobile Number (Optional)',
                type: 'tel',
                maxLength: 10,
                section: 'Contact information',
                placeholder: '10-digit mobile number'
            },
            {
                name: 'alternateMobile',
                label: 'Alternate Mobile (Optional)',
                type: 'tel',
                maxLength: 10
            },
            {
                name: 'email',
                label: 'Email (Optional)',
                type: 'email',
                maxLength: 150
            },
            {
                name: 'address',
                label: 'Saved Address',
                readOnly: true,
                type: 'textarea',
                maxLength: 500,
                section: 'Address & tax'
            },
            {
                name: 'useStructuredAddress', label: 'Replace saved address with address lines', type: 'checkbox'
            },
            {
                name: 'addressLine1', label: 'Address Line 1', type: 'text', maxLength: 250,
                placeholder: 'Building number, street or area'
            },
            {
                name: 'addressLine2', label: 'Address Line 2', type: 'text', maxLength: 250,
                placeholder: 'Landmark, floor or locality (optional)'
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
                label: 'State / Union Territory',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'district',
                label: 'District',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'city',
                label: 'City / Town',
                type: 'text',
                maxLength: 100
            },
            {
                name: 'postalCode',
                label: 'PIN / Postal Code',
                type: 'text',
                maxLength: 20
            },
            {
                name: 'gstNumber',
                label: 'GSTIN (Optional)',
                type: 'text',
                maxLength: 15
            },
            {
                name: 'paymentTermsChoice',
                label: 'Default Payment Terms (Optional)',
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
                label: 'Notes (Optional)',
                placeholder: 'Delivery instructions or helpful supplier details',
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
            modernize(controls);
            if(window.StockFlowMasterScreens&&!StockFlowMasterScreens.page){try{const saved=JSON.parse(sessionStorage.getItem('stockflow.supplier-saved')||'null');sessionStorage.removeItem('stockflow.supplier-saved');if(saved?.id){const response=await fetch('/api/suppliers/'+saved.id,{credentials:'same-origin'});if(response.ok){const record=await response.json(),note=element('section');note.className='supplier-success';note.setAttribute('role','status');note.append(element('strong','Supplier saved: '+record.supplierCode),element('p',record.supplierName));const c=saved.context;if(c){const href=StockFlowSupplierNavigation.editUrl(c.productId,c.variantId,c.from);if(href){const link=element('a','Return to Product Edit');link.href=href;link.className='btn btn-dark';note.append(link,element('p','Select this supplier explicitly and enter its item code. No product association or planning policy has been saved.'));}}document.getElementById('master-data').prepend(note);}}}catch{}}

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
            help.title = 'Format checks do not confirm government registration.';
            help.className = 'text-muted d-block';
            help.textContent =
                'Enter GSTIN if the supplier has one.';
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
            currentSupplier=record;clearErrors();++duplicateSequence;clearTimeout(duplicateTimer);document.getElementById('supplier-duplicate-hint').hidden=true;const submit=document.querySelector('#master-form [type=submit]');submit.textContent=record?'Save Changes':'Save Supplier';
            // Existing Indian suppliers are stored with +91; edit the local 10 digits.
            for (const name of ['mobileNumber', 'alternateMobile']) {
                if (record?.countryCode === 'IN' && /^\+91[0-9]{10}$/.test(controls[name].value)) {
                    controls[name].value = controls[name].value.slice(3);
                }
            }
            if (record?.countryCode === 'ZZ') {
                controls.countryCode.value = '';
            }
            for (const section of document.querySelectorAll('.supplier-optional')) {
                section.open = !!record && [...section.querySelectorAll('input,select,textarea')].some(input =>
                    !['countryCode', 'paymentTermsChoice', 'useStructuredAddress'].includes(input.name) && record[input.name] != null && String(record[input.name]).trim() !== '');
            }
            const legacy=!!record?.address&&!record?.useStructuredAddress;
            controls.address.closest('.supplier-field').hidden=!legacy;
            controls.address.nextElementSibling.textContent=legacy?record.address:'';
            controls.useStructuredAddress.closest('.supplier-field').hidden=!legacy;
            controls.useStructuredAddress.checked=!legacy;
            addressMode(controls);
            document.querySelector('.supplier-status')?.toggleAttribute('hidden',!record);
            if(controls.isActive)controls.isActive.closest('.supplier-field').querySelector('label').textContent='Active / Inactive';
            terms(controls, true, record);
            tax(controls);
        },
        onSaving(saving){const form=document.getElementById('master-form');form.setAttribute('aria-busy',String(saving));form.querySelector('[type=submit]').textContent=saving?'Saving...':currentSupplier?'Save Changes':'Save Supplier';},
        onSaveError:fieldErrors,
        onSaved(result){if(document.getElementById('master-data').dataset.createOnly==='true')return;try{sessionStorage.setItem('stockflow.supplier-saved',JSON.stringify({id:result?.supplierId||currentSupplier?.supplierId,context:window.StockFlowSupplierNavigation?.supplierReturn()}));}catch{}},
        async beforeSave(body, id, requestJson) {
            clearErrors();
            if(body.useStructuredAddress) body.address=null;
            else {body.address=currentSupplier?.address||null;body.addressLine1=null;body.addressLine2=null;}
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
            dialog.className = 'configuration-dialog supplier-duplicate-dialog';
            dialog.setAttribute('aria-labelledby', 'supplier-duplicate-title');
            const title = document.createElement('h2');
            title.id = 'supplier-duplicate-title';
            title.textContent = 'Possible existing supplier found';
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
                ['Continue Adding', 'continue', 'primary']
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
            dialog.addEventListener('keydown',event=>{if(event.key!=='Tab')return;const buttons=[...dialog.querySelectorAll('summary,button:not([disabled]),a[href]')];if(event.shiftKey&&document.activeElement===buttons[0]){event.preventDefault();buttons.at(-1).focus();}else if(!event.shiftKey&&document.activeElement===buttons.at(-1)){event.preventDefault();buttons[0].focus();}});
            dialog.showModal();
            actions.querySelector('button').focus();
            return await new Promise((resolve) => {
                return dialog.addEventListener(
                    'close',
                    () => {
                        const proceed = dialog.returnValue === 'continue';
                        dialog.remove();
                        resolve(proceed);if(!proceed)requestAnimationFrame(()=>document.querySelector('#master-form [type=submit]')?.focus());
                    },
                    {
                        once: true
                    }
                );
            });
        }
    });
})();
