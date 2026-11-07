// New Sale quick registration: one isolated form; the bill keeps its existing state.
window.StockFlowQuickCustomer = {
    mount(customer, workspace, picker, options = {}) {
        const byId = id => document.getElementById(id);
        const dialog = document.createElement('dialog');
        dialog.id = 'customer-quick-add-dialog';
        dialog.className = 'billing-customer-dialog';
        dialog.setAttribute('aria-labelledby', 'customer-quick-add-title');
        dialog.setAttribute('aria-describedby', 'customer-quick-add-subtitle');
        dialog.innerHTML = `<header class="billing-customer-header">
            <span class="billing-customer-icon" aria-hidden="true"><svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="9" cy="7" r="4"/><path d="M2 21v-3a7 7 0 0 1 14 0v3M19 8v8M15 12h8"/></svg></span>
            <div><h2 id="customer-quick-add-title">Add Customer</h2><p id="customer-quick-add-subtitle">Create a customer without leaving the bill.</p></div>
            <button type="button" class="billing-customer-x" aria-label="Close Add Customer" title="Close"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18"/></svg></button>
        </header>
        <form id="customer-quick-add-form" novalidate data-inline-validation data-skip-unsaved-warning>
            <div id="quick-customer-alert" class="billing-customer-alert" role="alert" hidden></div>
            <div id="quick-customer-matches" class="billing-customer-matches" role="status" aria-live="polite" hidden></div>
            <div class="billing-customer-body"></div>
            <footer class="billing-customer-footer"><button type="button" id="quick-customer-cancel" class="btn btn-dark"><span aria-hidden="true">×</span> Cancel</button></footer>
        </form>`;
        workspace.append(dialog);
        if(options.subtitle)dialog.querySelector('#customer-quick-add-subtitle').textContent=options.subtitle;
        const original = customer;
        customer = document.createElement('div');customer.id = original.id;
        while (original.firstChild) customer.append(original.firstChild);
        original.replaceWith(customer);
        const form = dialog.querySelector('form'), body = dialog.querySelector('.billing-customer-body');
        const save = byId('create-customer');
        save.type = 'submit';save.className = 'btn btn-dark';
        save.innerHTML = '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="m4 12 5 5L20 6"/></svg><span>Save &amp; Select Customer</span>';
        dialog.querySelector('footer').append(save);
        for (const [id, name, label, full] of [
            ['new-customer-name', 'customerName', 'Customer Name *', true],
            ['new-customer-mobile', 'mobileNumber', 'Mobile Number (Optional)', true],
            ['new-customer-gst', 'gstNumber', 'GSTIN (Optional)', false],
            ['new-customer-address', 'address', 'Address (Optional)', true]
        ]) {
            const input = byId(id), field = document.createElement('div');
            field.className = 'billing-customer-field' + (full ? ' billing-customer-wide' : '');
            const oldLabel = document.querySelector('label[for="' + id + '"]');
            oldLabel.textContent = label;oldLabel.className = 'form-label';input.name = name;
            field.append(oldLabel, input);customer.append(field);
        }
        customer.className = 'billing-customer-fields';body.append(customer);
        // Keep the old field container/IDs for the existing integration, not a second form.
        customer.querySelector(':scope > summary')?.remove();
        byId('new-customer-name').required = true;
        const gst = byId('new-customer-gst'), phone = byId('new-customer-mobile');
        byId('new-customer-name').placeholder='Enter customer name';
        phone.placeholder='Enter 10-digit mobile number';phone.autocomplete='tel-national';
        const phoneHelp=document.createElement('p');phoneHelp.id='quick-customer-mobile-help';phoneHelp.className='billing-customer-help';
        phoneHelp.textContent='Used to quickly find the customer for future purchases.';phone.after(phoneHelp);phone.setAttribute('aria-describedby',phoneHelp.id);
        const emailField=document.createElement('div');emailField.className='billing-customer-field billing-customer-wide';
        emailField.innerHTML='<label for="new-customer-email" class="form-label">Email Address (Optional)</label><input id="new-customer-email" name="email" type="email" maxlength="150" autocomplete="email" class="form-control" />';
        const email=emailField.querySelector('input');
        if(options.requiredMobile){
            phone.required=true;document.querySelector('label[for="new-customer-mobile"]').textContent='Mobile Number *';
            const additional=document.createElement('details'),summary=document.createElement('summary');
            additional.id='quick-customer-additional';additional.className='billing-customer-wide billing-customer-additional';summary.textContent='Additional Details (Optional)';additional.append(summary);
            document.querySelector('label[for="new-customer-address"]').textContent='Billing Address (Optional)';
            const optionalFields=document.createElement('div');optionalFields.className='billing-customer-optional-fields';
            optionalFields.append(emailField,byId('new-customer-gst').parentElement,byId('new-customer-address').parentElement);additional.append(optionalFields);customer.append(additional);
        }else customer.append(emailField);
        gst.pattern = '[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]';
        gst.autocapitalize = 'characters';gst.spellcheck = false;
        const open = document.createElement('button');open.type = 'button';open.className = 'btn btn-dark';open.textContent = '+ Add Customer';picker.append(open);
        let busy = false, lookupVersion = 0, lookupTimer, returnFocus, uncertain = false;
        const alert = byId('quick-customer-alert'), matches = byId('quick-customer-matches');
        const closeButtons = [byId('quick-customer-cancel'), dialog.querySelector('.billing-customer-x')];
        const fields = [...customer.querySelectorAll('input,textarea')], touched = new Set();
        const payload = () => ({ customerName: byId('new-customer-name').value.trim(), mobileNumber: phone.value.trim() || null,
            email: email.value.trim() || null, gstNumber: gst.value.trim().toUpperCase() || null, address: byId('new-customer-address').value.trim() || null,
            addressDetails: byId('new-customer-address').sfAddressCombined?.read(),
            useStructuredAddress: byId('new-customer-address').sfAddressCombined?.read()!==undefined?true:undefined });
        function validateGst(){
            gst.setCustomValidity('');
            const value=gst.value.trim().toUpperCase();
            if(!options.requiredMobile||!value||!new RegExp('^'+gst.pattern+'$').test(value))return;
            // Same base-36 checksum as GstRules.ValidGstin on the server.
            const alphabet='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';let factor=2,sum=0;
            for(let index=13;index>=0;index--){const product=alphabet.indexOf(value[index])*factor;sum+=Math.floor(product/36)+product%36;factor=factor===2?1:2;}
            if(alphabet[(36-sum%36)%36]!==value[14])gst.setCustomValidity('GSTIN checksum is invalid. This does not verify GST Portal registration.');
        }
        function error(message) { alert.textContent = message;alert.hidden = !message; }
        function setBusy(value) {
            busy = value;form.setAttribute('aria-busy', String(value));
            fields.forEach(field => field.disabled = value);closeButtons.forEach(button => button.disabled = value);
            matches.querySelectorAll('button').forEach(button => button.disabled = value || button.dataset.inactive === 'true');
            save.disabled = value || uncertain;save.querySelector('span').textContent = value ? 'Saving…' : 'Save & Select Customer';
        }
        function close() { if (!busy) dialog.close(); }
        closeButtons.forEach(button => button.addEventListener('click', close));
        dialog.addEventListener('cancel', event => { event.preventDefault();event.stopPropagation();close(); });
        dialog.addEventListener('close', () => {
            ++lookupVersion;clearTimeout(lookupTimer);
            const target = returnFocus?.isConnected && !returnFocus.closest('details:not([open])') ? returnFocus : byId(options.returnFocusId||'customer-current');
            target?.focus();
        });
        // Prevent bill-level Alt shortcuts while this separate registration is active.
        document.addEventListener('keydown', event => { if (dialog.open && event.altKey) event.stopImmediatePropagation(); }, true);
        open.addEventListener('click', () => {
            if (!workspace.dispatchEvent(new CustomEvent('billing-customer-opening', { cancelable: true }))) return;
            returnFocus = document.activeElement;dialog.showModal();byId('new-customer-name').focus();
        });
        function selectCustomer(record) {
            if (record.isActive === false) return;
            workspace.dispatchEvent(new CustomEvent('billing-customer-selected', { detail: record }));
            returnFocus = byId(options.returnFocusId||'customer-current');
            form.reset();customer.querySelector('details')?.removeAttribute('open');touched.clear();
            fields.forEach(field=>{field.setCustomValidity('');StockFlowValidation.showField(field,'');});
            error('');matches.replaceChildren();matches.hidden = true;uncertain = false;setBusy(false);close();
        }
        async function checkMatches(showErrors = false) {
            const version = ++lookupVersion, data = payload();
            const identifiers = {};
            if (data.mobileNumber && /^[0-9]{10}$/.test(data.mobileNumber)) identifiers.mobileNumber = data.mobileNumber;
            if (data.gstNumber && /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/.test(data.gstNumber)) identifiers.gstNumber = data.gstNumber;
            matches.replaceChildren();matches.hidden = true;
            if (!Object.keys(identifiers).length) return [];
            try {
                const records = await Sales.api('/api/customers/search?' + new URLSearchParams(identifiers));
                if (version !== lookupVersion || !dialog.open) return null;
                const exact = records.filter(record => (data.mobileNumber && record.mobileNumber?.trim() === data.mobileNumber) ||
                    (data.gstNumber && record.gstNumber?.trim().toUpperCase() === data.gstNumber));
                if (!exact.length) return [];
                matches.hidden = false;
                const heading = document.createElement('p');heading.textContent = 'Existing customer found. Select a matching record instead of creating a duplicate.';matches.append(heading);
                for (const record of exact) {
                    const row = document.createElement('div'), text = document.createElement('span'), button = document.createElement('button');
                    text.textContent = [record.customerName, record.mobileNumber, record.gstNumber, record.isActive === false ? 'Inactive — ask an administrator to reactivate' : ''].filter(Boolean).join(' · ');
                    button.type = 'button';button.className = 'btn btn-dark';button.textContent = 'Select Existing Customer';button.disabled = busy || record.isActive === false;
                    button.dataset.inactive = String(record.isActive === false);
                    button.onclick = () => { if (!busy) selectCustomer(record); };row.append(text,button);matches.append(row);
                }
                return exact;
            } catch (failure) {
                if (version !== lookupVersion) return null;
                // Creation permission can exist without customer lookup permission.
                // The opt-in server guard still enforces duplicates in that case.
                if (failure.status === 403) return [];
                if (showErrors) error('Could not check existing customers. ' + failure.message);
                throw failure;
            }
        }
        for (const input of fields) {
            input.addEventListener('blur', () => { if(input===gst)validateGst();touched.add(input);StockFlowValidation.showField(input,StockFlowValidation.fieldMessage(input)); });
            input.addEventListener('input', () => {
                if (!uncertain) error('');
                if (input === gst) { const start = input.selectionStart;input.value = input.value.toUpperCase();input.setSelectionRange(start,start); }
                if(input===gst)validateGst();
                if (touched.has(input)) StockFlowValidation.showField(input,StockFlowValidation.fieldMessage(input));
                if (input === phone || input === gst) {
                    ++lookupVersion;clearTimeout(lookupTimer);matches.hidden = true;
                    lookupTimer = setTimeout(() => checkMatches().catch(() => {}),350);
                }
            });
        }
        form.addEventListener('submit', async event => {
            event.preventDefault();event.stopPropagation();
            if (busy || uncertain) return;
            byId('new-customer-name').value = byId('new-customer-name').value.trim();gst.value = gst.value.trim().toUpperCase();phone.value = phone.value.trim();
            email.value=email.value.trim();validateGst();
            if (!StockFlowValidation.validate(form)) {if(customer.querySelector('details .is-invalid'))customer.querySelector('details').open=true;return;}
            if (!workspace.dispatchEvent(new CustomEvent('billing-customer-opening', { cancelable: true }))) return;
            error('');clearTimeout(lookupTimer);setBusy(true);
            let submitted = false;
            try {
                const existing = await checkMatches(true);
                if (existing === null) return;
                if (existing.length) { error('This mobile number or GSTIN already belongs to a customer. Select the matching record.');return; }
                submitted = true;
                const record = await Sales.api('/api/customers?preventIdentifierDuplicates=true'+(options.requiredMobile?'&requireMobile=true':''),{method:'POST',body:JSON.stringify(payload())});
                if (!Number.isInteger(record?.customerId) || record.customerId <= 0) throw new Error('The customer result could not be confirmed. Search existing customers before trying again.');
                selectCustomer(record);
            } catch (failure) {
                // A name-only uncertain request has no identifier with which the server
                // can safely distinguish a retry from a different person of the same name.
                if (submitted && !payload().mobileNumber && !payload().gstNumber && (!failure.status || failure.status >= 500)) uncertain = true;
                error(failure.message + (uncertain ? ' Check existing customers before creating this customer again.' : ''));
                if (uncertain) {
                    const find = document.createElement('button');find.type = 'button';find.className = 'btn btn-dark';find.textContent = 'Find Existing Customer';
                    find.onclick = () => { if (!busy) { const name = payload().customerName;close();workspace.dispatchEvent(new CustomEvent('billing-customer-find',{detail:name})); } };alert.append(find);
                }
            } finally { setBusy(false); }
        });
    }
};
