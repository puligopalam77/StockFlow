// Reuse the same form and list behaviour for products, categories, brands, and contacts.
window.StockFlowMaster = {
    async start(config) {
        const root = document.getElementById('master-data');
        const screen=config.inlineEditor&&!window.StockFlowMasterScreens?.page?null:window.StockFlowMasterScreens;
        const createOnly = root.dataset.createOnly === 'true';
        const isProductRegistration = root.dataset.module === 'products';
        const productUpdateKey='stockflow.product-updated.'+document.body.dataset.businessProfile+'.'+(window.StockFlowLocation||'');
        const search = document.getElementById('master-search');
        if (search) search.value = new URLSearchParams(location.search).get('search') ?? search.value;
        const endpoint = '/api/' + root.dataset.module;
        const canEdit = root.dataset.canEdit === 'true';
        const canDisable = root.dataset.canDisable === 'true';
        const canHistory = root.dataset.canSales === 'true';
        const form = document.getElementById('master-form');
        const editor = document.getElementById('editor');
        const fields = document.getElementById('form-fields');
        const tbody = document.getElementById('master-rows');
        window.StockFlowLoading?.region(tbody,{retry:()=>load()});
        const message = document.getElementById('master-message');
        const list = document.getElementById('master-list');
        const pageTitle = document.getElementById('master-page-title');
        const managementButton = document.getElementById('show-records');
        const isDialog = editor instanceof HTMLDialogElement;
        const editorMessage = document.getElementById('editor-message');
        let saving = false;
        let editorVersion = 0;
        function setEditorVisible(visible) {
            if (isDialog) {
                if (visible && !editor.open) {
                    editor.showModal();
                } else if (!visible && editor.open) {
                    editor.close();
                }
                return;
            }
            if(screen?.page){editor.hidden=!visible;return;}
            editor.hidden = !visible;
            if (!list) {
                return;
            }
            list.hidden = visible;
            const title = visible
                ? editingId === null
                    ? 'Add New Product'
                    : 'Edit Product'
                : 'Product Management';
            pageTitle.textContent = title;
            const breadcrumb = document.getElementById('breadcrumb-current');
            if (breadcrumb) {
                breadcrumb.textContent = title;
            }
            document
                .getElementById('add-record')
                ?.setAttribute('aria-pressed', String(visible && editingId === null));
            managementButton?.setAttribute('aria-pressed', String(!visible));
        }
        function closeEditor() {
            if (saving) {
                return;
            }
            ++editorVersion;
            if(screen?.page){screen.back();return;}
            setEditorVisible(false);
            if (createOnly||config.inlineEditor) document.getElementById('add-record')?.focus();
            managementButton?.focus();
        }
        let editingId = null;
        let editingActive = true;
        let loadVersion = 0;
        const controls = {};
        function show(text, error = false) {
            for(const target of [message,editorMessage].filter(Boolean)){target.replaceChildren();target.hidden=true;}
            StockFlowNotify.show(error?'error':'success',text,{key:'master-result'});
        }
        function styleAction(button, action) {
            if (!isDialog) {
                return;
            }
            button.className = 'btn configuration-row-action me-1';
            button.setAttribute('aria-label', action + ' ' + config.singular);
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            svg.setAttribute('aria-hidden', 'true');
            const path = document.createElementNS(svg.namespaceURI, 'path');
            path.setAttribute(
                'd',
                action === 'Edit'
                    ? 'M15 5l4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15z'
                    : 'M12 2v10M6 5a9 9 0 1 0 12 0'
            );
            svg.append(path);
            button.replaceChildren(svg);
        }
        let defaultConfirmation;
        function confirmDeactivation(options={}) {
            const dialog = document.getElementById('deactivate-dialog');
            if (!dialog) {
                return StockFlowDialog.confirm(options.message||'Stop using this record for new entries? Its saved history will stay.',options);
            }
            const title=dialog.querySelector('h2'),copy=dialog.querySelector('p'),action=document.getElementById('confirm-deactivate');
            defaultConfirmation??={title:title.textContent,message:copy?.textContent,action:action.textContent};
            title.textContent=options.title||defaultConfirmation.title;if(copy)copy.textContent=options.message||defaultConfirmation.message;action.textContent=options.action||defaultConfirmation.action;
            return new Promise((resolve) => {
                dialog.returnValue = '';
                document.getElementById('cancel-deactivate').onclick = () => {
                    return dialog.close('cancel');
                };
                document.getElementById('confirm-deactivate').onclick = () => {
                    return dialog.close('confirm');
                };
                dialog.addEventListener(
                    'close',
                    () => {
                        return resolve(dialog.returnValue === 'confirm');
                    },
                    {
                        once: true
                    }
                );
                dialog.showModal();
                document.getElementById('cancel-deactivate').focus();
            });
        }
        StockFlowMaster.confirmChange=confirmDeactivation;
        async function requestJson(url, options = {}) {
            const response = await fetch(url, {
                credentials: 'same-origin',
                ...options,
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN':
                        document.querySelector('meta[name="csrf-token"]')?.content || '',
                    ...options.headers
                }
            });
            if (response.status === 401) {
                throw new Error('Your session has expired. Sign in again.');
            }
            if (response.status === 403) {
                throw new Error('You do not have permission for this action.');
            }
            const content =
                response.status === 204
                    ? null
                    : await response.json().catch(() => {
                          return null;
                      });
            if (!response.ok) {
                const error=new Error(
                    content?.message ||
                        (content?.errors
                            ? Object.values(content.errors).flat().join(' ')
                            : 'Unable to complete the request. Please try again.')
                );
                error.status=response.status;error.fields=content?.errors;throw error;
            }
            return content;
        }
        const labelFor = (name) => {
            return (
                config.fields.find((f) => {
                    return f.name === name;
                })?.label ||
                name.replace(/([A-Z])/g, ' $1').replace(/^./, (value) => {
                    return value.toUpperCase();
                })
            );
        };
        for (const field of [
            ...config.fields,
            ...(canDisable
                ? [
                      {
                          name: 'isActive',
                          label: config.statusLabel || 'Active',
                          type: 'checkbox',
                          section: config.statusSection
                      }
                  ]
                : [])
        ]) {
            if (field.section) {
                const heading = document.createElement('h3');
                heading.className = 'col-12 registration-section';
                heading.textContent = field.section;
                fields.append(heading);
            }
            const group = document.createElement('div');
            group.className = isProductRegistration ? 'col-md-6 col-lg-4' : 'col-md-6';
            if (isDialog && field.type === 'textarea') {
                group.className = 'col-12';
            }
            const label = document.createElement('label');
            const fieldId = createOnly ? 'quick-supplier-' + field.name : field.name;
            label.htmlFor = fieldId;
            label.className = 'form-label';
            label.textContent = field.label + (field.required ? ' *' : '');
            const input = document.createElement(
                field.type === 'select'
                    ? 'select'
                    : field.type === 'textarea'
                      ? 'textarea'
                      : 'input'
            );
            if (input.tagName === 'INPUT') {
                input.type = field.type;
            }
            input.id = fieldId;
            input.name = field.name;
            input.className =
                field.type === 'checkbox'
                    ? 'form-check-input d-block'
                    : field.type === 'select'
                      ? 'form-select'
                      : 'form-control';
            for (const attribute of [
                'required',
                'minLength',
                'maxLength',
                'min',
                'max',
                'step',
                'readOnly',
                'placeholder',
                'pattern',
                'title',
                'inputMode',
                'accept'
            ]) {
                if (field[attribute] !== undefined) {
                    input[attribute] = field[attribute];
                }
            }
            if (field.options) {
                field.options.forEach(([value, text]) => {
                    return input.add(new Option(text, value));
                });
            }
            group.append(label, input);
            if (field.help) {
                const help = document.createElement('small');
                help.id = fieldId + '-help';
                help.className = 'text-muted d-block mt-1';
                help.textContent = field.help;
                input.setAttribute('aria-describedby', help.id);
                group.append(help);
            }
            fields.append(group);
            controls[field.name] = input;
        }
        try {
            await config.onReady?.(controls, {openEditor});
        } catch (error) {
            show(error.message, true);
            return;
        }
        if (config.submitText) {
            form.querySelector('[type="submit"]').textContent = config.submitText;
        }
        const header = document.createElement('tr');
        for (const text of [
            ...config.columns.map((name) => {
                return config.columnLabels?.[name] || labelFor(name);
            }),
            'Status',
            ...(canEdit || canDisable || canHistory || config.detailLink ? ['Actions'] : [])
        ]) {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = text;
            header.append(th);
        }
        const listHead = document.getElementById('master-head');
        if (!listHead?.children.length) listHead?.append(header);
        async function openEditor(id = null) {
            if(screen&&!screen.page){screen.navigate(id===null?'Create':'Edit',id);return;}
            if (saving) {
                return;
            }
            const version = ++editorVersion;
            let loading;
            if(isProductRegistration&&id!==null){form.querySelector('[type=submit]').disabled=true;loading=document.createElement('p');loading.className='text-muted';loading.setAttribute('role','status');loading.textContent='Loading product details…';root.prepend(loading);root.setAttribute('aria-busy','true');}
            try {
                if (id === null && config.onCreate) {
                    await config.onCreate({
                        api: requestJson,
                        onSaved: async () => {
                            if(!isProductRegistration)show('Record saved.');
                            await load();
                        }
                    });
                    return;
                }
                const record = id === null ? null : await requestJson(endpoint + '/' + id);
                if (version !== editorVersion) {
                    return;
                }
                editingId = id;
                editingActive = record?.isActive ?? true;
                form.reset();
                for (const field of config.fields) {
                    const input = controls[field.name];
                    if (field.lookup) {
                        const options = await requestJson(
                            '/api/' + field.lookup + '?activeOnly=true'
                        );
                        input.stockFlowLookupOptions = options;
                        if (version !== editorVersion) {
                            return;
                        }
                        input.replaceChildren(
                            new Option(field.required ? 'Select ' + field.label : 'None', '')
                        );
                        options.forEach((option) => {
                            return input.add(
                                new Option(
                                    option[field.text] || option.categoryName || '',
                                    option[field.value]
                                )
                            );
                        });
                        if (
                            record?.[field.name] &&
                            !options.some((option) => {
                                return option[field.value] === record[field.name];
                            })
                        ) {
                            const inactive = new Option(
                                'Inactive selection — choose an active replacement',
                                record[field.name]
                            );
                            inactive.disabled = true;
                            input.add(inactive);
                        }
                    }
                    if (field.type === 'checkbox') {
                        input.checked = record?.[field.name] ?? false;
                    } else {
                        input.value =
                            record?.[field.name] ??
                            field.default ??
                            (field.type === 'number' ? 0 : '');
                    }
                }
                await config.onOpen?.(controls, record);
                if (version !== editorVersion) {
                    return;
                }
                if (controls.isActive) {
                    controls.isActive.checked = editingActive;
                    controls.isActive.disabled = id === null;
                }
                document.getElementById('editor-title').textContent =
                    (id === null ? 'Add ' : 'Edit ') +
                    (isDialog ? config.singular : config.singular.toLowerCase());
                message.classList.add('d-none');
                editorMessage?.classList.add('d-none');
                setEditorVisible(true);
                (config.initialFocus?.()||(isProductRegistration&&screen?.mode==='edit'?controls.productName:controls[config.fields[0].name])).focus();
            } catch (error) {
                show(error.message, true);
            } finally {
                loading?.remove();if(version===editorVersion)root.removeAttribute('aria-busy');
            }
        }
        if (isProductRegistration) document.addEventListener('stockflow:catalog-imported',()=>load().catch(error=>show(error.message,true)));
        let listController;
        async function load() {
            if (createOnly||screen?.page) return;
            const version = ++loadVersion;
            listController?.abort();listController=new AbortController();
            const params = new URLSearchParams(config.listQuery?.() ?? {
                term: document.getElementById('master-search').value,
                activeOnly: document.getElementById('status-filter').value
            });
            try {
                const records = await requestJson(endpoint + '?' + params,{signal:listController.signal});
                if (version !== loadVersion) {
                    return;
                }
                if(config.renderList){await config.renderList({records,openEditor,requestJson,reload:load,show});return;}
                tbody.replaceChildren();
                for (const record of records) {
                    const row = document.createElement('tr');
                    for (const name of config.columns) {
                        const cell = document.createElement('td');
                        if (
                            config.descriptionTooltip &&
                            name === config.singular.toLowerCase() + 'Name' &&
                            record.description
                        ) {
                            cell.title = record.description;
                            cell.tabIndex = 0;
                            cell.setAttribute(
                                'aria-label',
                                (record[name] || '') + ': ' + record.description
                            );
                        }
                        cell.textContent =
                            typeof record[name] === 'boolean'
                                ? record[name]
                                    ? 'Yes'
                                    : 'No'
                                : name.includes('Price')
                                  ? Number(record[name]).toFixed(2)
                                  : (record[name] ?? '');
                        row.append(cell);
                    }
                    const status = document.createElement('td');
                    status.textContent = record.productStatus==='DISCONTINUED'?'Discontinued':record.productStatus==='INACTIVE'?'Inactive':record.isActive
                        ? config.activeStatus || 'Active'
                        : config.inactiveStatus || 'Inactive';
                    if(status.textContent==='Active'||status.textContent==='Inactive'){
                        const badge=document.createElement('span');badge.className='sf-status-badge';badge.dataset.active=String(status.textContent==='Active');badge.textContent=status.textContent;status.replaceChildren(badge);
                    }
                    row.append(status);
                    if (canEdit || canDisable || canHistory || config.detailLink) {
                        const actions = document.createElement('td');
                        if (config.detailLink) {
                            const link = document.createElement('a');
                            link.className = 'btn btn-sm btn-outline-secondary me-2';
                            link.textContent = 'Sizes, models & labels';
                            link.href =
                                '/Products/Variants?productId=' +
                                encodeURIComponent(record[config.id]);
                            actions.append(link);
                        }
                        if (canHistory) {
                            const history = document.createElement('a');
                            history.className = 'btn btn-sm btn-outline-secondary me-2';
                            history.textContent = 'Sales history';
                            history.href =
                                '/Sales?customerId=' + encodeURIComponent(record[config.id]);
                            actions.append(history);
                        }
                        if (canEdit) {
                            const edit = document.createElement('button');
                            edit.type = 'button';
                            edit.className = 'btn btn-sm btn-outline-primary me-2';
                            edit.textContent = 'Edit';
                            styleAction(edit, 'Edit');
                            edit.onclick = () => {
                                return openEditor(record[config.id]);
                            };
                            actions.append(edit);
                        }
                        if (canDisable && record.isActive) {
                            const disable = document.createElement('button');
                            disable.type = 'button';
                            disable.className = 'btn btn-sm btn-outline-danger';
                            disable.textContent = 'Deactivate';
                            styleAction(disable, 'Deactivate');
                            disable.onclick = async () => {
                                if (!(await confirmDeactivation())) {
                                    return;
                                }
                                disable.disabled = true;
                                try {
                                    await requestJson(endpoint + '/' + record[config.id], {
                                        method: 'DELETE'
                                    });
                                    show('Record deactivated.');
                                    await load();
                                } catch (error) {
                                    show(error.message, true);
                                    disable.disabled = false;
                                }
                            };
                            actions.append(disable);
                        }
                        row.append(actions);
                    }
                    tbody.append(row);
                    row.dataset.recordId = record[config.id];
                }
                if (!records.length) {
                    const row = document.createElement('tr');
                    const cell = document.createElement('td');
                    cell.colSpan = header.children.length;
                    cell.textContent = 'No matching records.';
                    row.append(cell);
                    tbody.append(row);
                }
                await config.onLoaded?.({records, rows:[...tbody.children], openEditor, isCurrent:()=>version===loadVersion});
            } catch (error) {
                if (version === loadVersion) {
                    show(error.message, true);
                }
            }
        }
        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            if (saving) {
                return;
            }
            if (!form.reportValidity()) {
                return;
            }
            const body = {};
            for (const field of config.fields) {
                if (field.readOnly || field.transient) {
                    continue;
                }
                const input = controls[field.name];
                const value = input.value.trim();
                body[field.name] =
                    field.type === 'checkbox'
                        ? input.checked
                        : field.type === 'number' ||
                            (field.type === 'select' && field.valueType !== 'string')
                          ? value === '' && field.nullable
                              ? null
                              : Number(value)
                          : value || (field.required ? '' : null);
            }
            if (editingId !== null) {
                body.isActive = controls.isActive?.checked ?? editingActive;
            }
            const submit = form.querySelector('[type="submit"]');
            submit.disabled = true;
            saving = true;
            form.dataset.saving='true';
            config.onSaving?.(true);
            const endButtonLoading=window.StockFlowLoading?.button(submit);
            const dismissButtons = [
                document.getElementById('cancel-edit'),
                document.getElementById('close-editor')
            ].filter(Boolean);
            dismissButtons.forEach((button) => {
                return (button.disabled = true);
            });
            try {
                if (config.beforeSave && !(await config.beforeSave(body, editingId, requestJson))) {
                    return;
                }
                const result = await requestJson(
                    endpoint + (editingId === null ? '' : '/' + editingId),
                    {
                        method: editingId === null ? 'POST' : 'PUT',
                        body: JSON.stringify(body)
                    }
                );
                window.StockFlowUnsaved?.markSaved(form);
                await config.onSaved?.(result, body);
                if (config.continueToVariants) {
                    location.assign(
                        '/Products/Variants?productId=' +
                            encodeURIComponent(editingId ?? result.productId)
                    );
                    return;
                }
                saving = false;
                const productUpdated=isProductRegistration&&editingId!==null;
                if(productUpdated&&screen?.page){try{sessionStorage.setItem(productUpdateKey,String(Date.now()));}catch{}}
                closeEditor();
                if (createOnly) {
                    root.dispatchEvent(new CustomEvent('master-record-saved', { detail: result }));
                }
                if(productUpdated)show('Product updated successfully');
                else if(!isProductRegistration)show('Record saved.');
                await load();
            } catch (error) {
                show(error.message, true);
                config.onSaveError?.(error);
            } finally {
                saving = false;
                form.dataset.saving='false';
                config.onSaving?.(false);
                submit.disabled = false;
                dismissButtons.forEach((button) => {
                    return (button.disabled = false);
                });
            }
        });
        document.getElementById('add-record')?.addEventListener('click', () => {
            return openEditor();
        });
        document.getElementById('cancel-edit').onclick = closeEditor;
        document.getElementById('close-editor')?.addEventListener('click', closeEditor);
        if (isDialog) {
            editor.addEventListener('cancel', (event) => {
                event.preventDefault();
                closeEditor();
            });
        }
        managementButton?.addEventListener('click', () => {
            closeEditor();
            message.classList.add('d-none');
        });
        let timer;
        search?.addEventListener('input', () => {
            clearTimeout(timer);
            if (!config.localFilter) { ++loadVersion; listController?.abort(); }
            timer = setTimeout(config.localFilter || load, 250);
        });
        document.getElementById('status-filter')?.addEventListener('change', () => { clearTimeout(timer); (config.localFilter || load)(); });
        if (createOnly) document.getElementById('add-record').disabled = false;
        if(screen?.page){await openEditor(screen.id);screen.ready();}
        else {
            await load();
            if(isProductRegistration){try{const updated=Number(sessionStorage.getItem(productUpdateKey));sessionStorage.removeItem(productUpdateKey);if(updated&&Date.now()-updated<60000)show('Product updated successfully');}catch{}}
        }
    }
};
