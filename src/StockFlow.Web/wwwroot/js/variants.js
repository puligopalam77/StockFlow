(() => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const permissions = getElement('variant-workspace').dataset;
    let products = [];
    let variants = [];
    let saving = false;
    let loadVersion = 0;
    let labelVariant = null;
    let unitLabel = null;
    let labelReprint = false;
    let itemLabels = [];
    let generatingLabels = false;
    const existingCode = () => labelVariant?.manufacturerBarcode || (labelVariant?.barcodeSource === 'MANUFACTURER' ? labelVariant.barcode : null);
    const useExisting = () => Boolean(existingCode()) && getElement('label-source').value === 'existing';
    const product = () => {
        return products.find((product2) => {
            return String(product2.productId) === getElement('variant-product').value;
        });
    };
    const locationId = Number(
        document.querySelector('meta[name="stockflow-location"]')?.content || 0
    );
    async function requestJson(path, options = {}) {
        const response = await fetch('/api/products' + path, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || ''
            }
        });
        const data = await response.json().catch(() => {
            return {};
        });
        if (!response.ok) {
            throw new Error(
                data.message ||
                    Object.values(data.errors || {})
                        .flat()
                        .join(' ') ||
                    'We could not save this change. Check saved records before trying again. If the problem continues, contact the shop owner or administrator.'
            );
        }
        return data;
    }
    async function runAction(action) {
        const message = getElement('variant-editor')?.open
            ? getElement('variant-editor-message')
            : getElement('unit-label-dialog')?.open
              ? getElement('unit-label-message')
              : getElement('label-dialog').open
                ? getElement('label-message')
                : getElement('variant-message');
        message.textContent = '';
        try {
            await action();
        } catch (error) {
            const target = getElement('variant-editor')?.open
                ? getElement('variant-editor-message')
                : getElement('unit-label-dialog')?.open
                  ? getElement('unit-label-message')
                  : getElement('label-dialog').open
                    ? getElement('label-message')
                    : getElement('variant-message');
            target.textContent = error.message;
        }
    }
    const lines = (id) => {
        return getElement(id)
            .value.split(/\r?\n/)
            .map((text) => {
                return text.trim();
            })
            .filter(Boolean);
    };
    function addAttribute(name = '', value = '') {
        const row = document.createElement('div');
        row.className = 'variant-attribute-row';
        for (const [label, text] of [
            ['Attribute name', name],
            ['Attribute value', value]
        ]) {
            const input = document.createElement('input');
            input.className = 'form-control';
            input.maxLength = 100;
            input.required = true;
            input.placeholder = label;
            input.setAttribute('aria-label', label);
            input.value = text;
            row.append(input);
        }
        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-dark';
        remove.textContent = '×';
        remove.setAttribute('aria-label', 'Remove attribute');
        remove.onclick = () => {
            return row.remove();
        };
        row.append(remove);
        getElement('variant-attributes').append(row);
    }
    function barcodeSource() {
        const manufacturer = getElement('variant-source').value === 'MANUFACTURER';
        getElement('variant-primary-group').hidden = !manufacturer;
        getElement('variant-primary').required = manufacturer;
    }
    function closeEditor() {
        if (!saving) {
            getElement('variant-editor')?.close();
        }
    }
    function edit(variant = {}) {
        const product2 = product();
        if (!product2 || !getElement('variant-form') || saving) {
            return;
        }
        if (!variant.variantId && !product2.hasVariants) {
            throw new Error(
                'Edit the existing size / model, or select the different sizes or models option when editing the product.'
            );
        }
        getElement('variant-form').reset();
        const fields = {
            id: variant.variantId || '',
            name: variant.variantName || '',
            sku: variant.sku || '',
            tracking: String(variant.isSerialTracked ?? product2.isSerialTracked),
            purchase: variant.purchasePrice ?? 0,
            sale: variant.sellingPrice ?? 0,
            tax: variant.taxPercentage ?? product2.taxPercentage ?? 0,
            warranty: variant.warrantyMonths ?? product2.warrantyMonths ?? 0,
            minimum: variant.minimumStockLevel ?? 0,
            'location-minimum': variant.locationMinimumStockLevel ?? '',
            source: variant.barcodeSource || 'INTERNAL',
            primary: variant.primaryBarcode || variant.barcode || '',
            barcodes: ''
        };
        for (const [key, value] of Object.entries(fields)) {
            getElement('variant-' + key).value = value;
        }
        getElement('variant-active').checked = variant.isActive !== false;
        getElement('variant-active').disabled =
            !variant.variantId || permissions.canDisable !== 'true';
        getElement('variant-location-minimum').disabled = locationId <= 0;
        getElement('variant-editor-title').textContent = variant.variantId
            ? 'Edit size / model'
            : 'Add size / model';
        getElement('variant-editor-product').textContent = product2.productName;
        getElement('variant-editor-message').textContent = '';
        getElement('variant-existing-barcodes').textContent = variant.barcodes?.length
            ? 'Saved barcodes: ' + variant.barcodes.join(', ')
            : '';
        getElement('variant-attributes').replaceChildren();
        Object.entries(variant.attributes || {}).forEach(([name, value]) => {
            return addAttribute(name, value);
        });
        barcodeSource();
        getElement('variant-editor').showModal();
        getElement('variant-name').focus();
    }
    function render() {
        getElement('variant-rows').replaceChildren();
        const term = getElement('variant-search').value.trim().toLowerCase();
        const rows = variants.filter((value) => {
            return (
                (getElement('variant-status').value !== 'active' || value.isActive) &&
                [
                    value.variantName,
                    value.sku,
                    value.barcode,
                    ...Object.entries(value.attributes || {}).flat()
                ]
                    .join(' ')
                    .toLowerCase()
                    .includes(term)
            );
        });
        for (const value2 of rows) {
            const tr = document.createElement('tr');
            for (const value of [
                value2.variantName,
                value2.sku,
                value2.barcode || 'Not configured',
                value2.barcodeSource || '—',
                Object.entries(value2.attributes || {})
                    .map(([k, value3]) => {
                        return k + ': ' + value3;
                    })
                    .join(', '),
                value2.isSerialTracked ? 'Serial' : 'Quantity',
                Number(value2.purchasePrice).toFixed(2),
                Number(value2.sellingPrice).toFixed(2),
                value2.taxPercentage,
                value2.warrantyMonths,
                value2.locationMinimumStockLevel ?? value2.minimumStockLevel,
                value2.currentStock,
                value2.isActive ? 'Active' : 'Inactive'
            ]) {
                const td = document.createElement('td');
                td.textContent = value;
                tr.append(td);
            }
            const actions = document.createElement('td');
            if (permissions.canEdit === 'true' && product()?.isActive !== false) {
                actionButton(actions, 'Edit', () => {
                    return edit(value2);
                });
            }
            if (!value2.isSerialTracked && permissions.canLabel === 'true') {
                actionButton(actions, 'Labels', () => {
                    return labels(value2);
                });
            }
            if (!value2.isSerialTracked && permissions.canReprint === 'true') {
                actionButton(actions, 'Reprint', () => {
                    return labels(value2, true);
                });
            } else if (permissions.canEdit === 'true') {
                actionButton(actions, 'Unit label', openUnit);
            }
            tr.append(actions);
            getElement('variant-rows').append(tr);
        }
        if (!rows.length) {
            const tr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = 14;
            td.textContent = product()
                ? 'No matching sizes or models.'
                : 'Choose a product to see its sizes or models.';
            tr.append(td);
            getElement('variant-rows').append(tr);
        }
    }
    function actionButton(container, text, action) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-dark btn-sm me-1';
        button.textContent = text;
        button.onclick = () => {
            return runAction(action);
        };
        container.append(button);
    }
    async function load() {
        const version = ++loadVersion;
        const product2 = product();
        variants = [];
        render();
        if (getElement('variant-new')) {
            getElement('variant-new').disabled =
                !product2?.hasVariants || product2.isActive === false;
        }
        getElement('variant-product-help').textContent = !product2
            ? ''
            : product2.hasVariants
              ? 'Edit the existing size / model, or add another size / model.'
              : 'This product has one size / model. Edit it to set its item code, prices and barcode.';
        getElement('variant-labels').replaceChildren();
        getElement('label-print').hidden = true;
        if (!product2) {
            return;
        }
        const data = await requestJson(
            '/variants?' +
                new URLSearchParams({
                    productId: product2.productId,
                    activeOnly: false
                })
        );
        if (version !== loadVersion) {
            return;
        }
        variants = data;
        render();
    }
    // Code 128 set B, including weighted checksum and quiet zones. No remote label service.
    const patterns =
        '212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 114131 311141 411131 211412 211214 211232 2331112'.split(
            ' '
        );
    function barcode(code) {
        if (!/^[\x20-\x7e]{1,150}$/.test(code)) {
            throw new Error(
                'This barcode cannot be printed as Code 128 B. Generate an internal label.'
            );
        }
        const values = [...code].map((value) => {
            return value.charCodeAt(0) - 32;
        });
        const checksum =
            (104 +
                values.reduce((sum, value, value3) => {
                    return sum + value * (value3 + 1);
                }, 0)) %
            103;
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        let value2 = 10;
        for (const value of [104, ...values, checksum, 106]) {
            patterns[value].split('').forEach((width, index) => {
                const w = Number(width);
                if (index % 2 === 0) {
                    const rect = document.createElementNS(svg.namespaceURI, 'rect');
                    rect.setAttribute('x', value2);
                    rect.setAttribute('width', w);
                    rect.setAttribute('height', 50);
                    svg.append(rect);
                }
                value2 += w;
            });
        }
        svg.setAttribute('viewBox', `0 0 ${value2 + 10} 50`);
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', code);
        return svg;
    }
    function renderLabels(code, name, price, count) {
        const fragment = document.createDocumentFragment();
        for (let index = 0; index < count; index++) {
            const div = document.createElement('div');
            div.className = 'stock-label';
            for (const text of [name, price == null ? '' : Number(price).toFixed(2) + ' + tax']) {
                const line = document.createElement('div');
                line.textContent = text;
                div.append(line);
            }
            div.append(barcode(code));
            const caption = document.createElement('div');
            caption.textContent = code;
            div.append(caption);
            fragment.append(div);
        }
        getElement('variant-labels').replaceChildren(fragment);
        getElement('label-print').hidden = false;
    }
    function renderItemSelection() {
        const existing = useExisting();
        getElement('label-copy-controls').hidden = !existing;
        getElement('label-item-controls').hidden = existing || !itemLabels.length;
        getElement('label-generate').hidden = existing || permissions.canGenerate !== 'true';
        if (existing) {
            const copies = Number(getElement('label-count').value);
            if (!Number.isInteger(copies) || copies < 1 || copies > 500) {
                getElement('variant-labels').replaceChildren();
                getElement('label-print').hidden = true;
                return;
            }
            getElement('label-help').textContent = 'Saved manufacturer barcode: ' + existingCode() + '. This code identifies the size / model and can appear on multiple items. Scan it in New Sale to add the product.';
            getElement('label-message').textContent = '';
            renderLabels(existingCode(), labelVariant.productName, labelVariant.sellingPrice, copies);
            return;
        }
        getElement('label-help').textContent = 'Generate missing barcodes once, attach the printed labels, then scan them in New Sale. Select specific labels or all labels to reprint the same codes.';
        getElement('label-message').textContent = itemLabels.length ? '' : permissions.canGenerate === 'true'
            ? 'No unique barcodes saved yet. Use Generate unique barcodes for stock. Stock must be available at this location.'
            : 'No unique barcodes saved yet. Ask a user with barcode generation access to prepare them.';
        const selected = itemLabels.filter(row => row.selected);
        getElement('label-selection-count').textContent = selected.length + ' of ' + itemLabels.length + ' labels selected. Print up to 500 per batch.';
        getElement('variant-labels').replaceChildren();
        for (const row of selected.slice(0,500)) {
            renderLabels(row.internalBarcode,row.productName,row.sellingPrice,1);
            row.preview = getElement('variant-labels').firstElementChild;
        }
        getElement('variant-labels').replaceChildren(...selected.slice(0,500).map(row=>row.preview));
        getElement('label-print').hidden = !selected.length || selected.length > 500;
    }
    async function labels(variant, reprint = false, generate = false) {
        if (variant.isSerialTracked) throw new Error('Print an individual unit label for serialized stock.');
        const rows = await requestJson('/variants/' + variant.variantId + '/item-labels' + (generate ? '/generate' : ''),generate ? {method:'POST'} : {});
        labelVariant = variant; labelReprint = reprint; unitLabel = null;
        itemLabels = rows.map((row,index)=>({...row,selected:index<500}));
        getElement('label-source-controls').hidden = !existingCode();
        getElement('label-source').value = existingCode() && !generate ? 'existing' : 'unique';
        getElement('label-print').textContent = reprint ? 'Reprint selected labels' : 'Print selected labels';
        getElement('label-help').textContent = 'Each received item has its own saved barcode. Select specific items or all items to print. Reprinting keeps the original codes.';
        const list = getElement('label-item-list'); list.replaceChildren();
        for (const row of itemLabels) {
            const wrap=document.createElement('label');wrap.className='d-block';
            const check=document.createElement('input');check.type='checkbox';check.checked=row.selected;
            check.setAttribute('aria-label','Select '+row.internalBarcode);
            check.onchange=()=>{row.selected=check.checked;renderItemSelection();};row.check=check;
            wrap.append(check,document.createTextNode(' '+row.internalBarcode+' - '+(row.intakeId ? 'Receipt '+row.intakeId : 'Existing stock')+', item '+row.itemNumber));list.append(wrap);
        }
        getElement('label-message').textContent = rows.length ? '' : 'No saved item labels. Receive stock first, then use Labels to prepare its unique barcodes.';
        renderItemSelection();
        if (!getElement('label-dialog').open) getElement('label-dialog').showModal();
    }
    getElement('label-source').onchange = renderItemSelection;
    getElement('label-count').oninput = () => { if (labelVariant && useExisting()) renderItemSelection(); };
    getElement('label-generate').onclick = () => runAction(async () => {
        if (generatingLabels || !labelVariant) return;
        generatingLabels = true;
        getElement('label-generate').disabled = true;
        try { await labels(labelVariant, labelReprint, true); }
        finally { generatingLabels = false; getElement('label-generate').disabled = false; }
    });
    for (const [id,selected] of [['label-select-all',true],['label-select-none',false]]) {
        getElement(id).onclick=()=>{for(const row of itemLabels){row.selected=selected;row.check.checked=selected;}renderItemSelection();};
    }
    function openUnit() {
        getElement('unit-label-message').textContent = '';
        getElement('unit-label-dialog').showModal();
        getElement('unit-serial').focus();
    }
    getElement('label-print').onclick = () => runAction(async () => {
        if(labelVariant){
            if (useExisting()) {
                const copies = Number(getElement('label-count').value);
                if (!Number.isInteger(copies) || copies < 1 || copies > 500) throw new Error('Enter 1 to 500 copies.');
                await requestJson('/variants/'+labelVariant.variantId+'/'+(labelReprint?'reprint':'print'),{method:'POST',body:JSON.stringify({existingBarcode:existingCode(),copies})});
                renderItemSelection();
                window.print();
                return;
            }
            const selected=itemLabels.filter(row=>row.selected);
            if(!selected.length||selected.length>500)throw new Error('Select 1 to 500 item labels.');
            await requestJson('/variants/'+labelVariant.variantId+'/'+(labelReprint?'reprint':'print'),{method:'POST',body:JSON.stringify({labelIds:selected.map(row=>row.labelId)})});
            const fresh=await requestJson('/variants/'+labelVariant.variantId+'/item-labels');
            if(selected.some(row=>!fresh.some(item=>item.labelId===row.labelId&&item.internalBarcode===row.internalBarcode)))throw new Error('Selected labels are no longer available. Reload labels.');
        }
        window.print();
    });
    getElement('label-refresh').onclick = () => {
        return runAction(async () => {
            if (labelVariant) {
                renderItemSelection();
            } else if (unitLabel) {
                renderLabels(unitLabel.code, unitLabel.name, null, 1);
            }
        });
    };
    document.querySelectorAll('[data-close-label]').forEach((button) => {
        return (button.onclick = () => {
            return getElement('label-dialog').close();
        });
    });
    getElement('unit-label-open')?.addEventListener('click', openUnit);
    for (const id of ['unit-label-close', 'unit-label-cancel']) {
        getElement(id)?.addEventListener('click', () => {
            if (!saving) {
                getElement('unit-label-dialog').close();
            }
        });
    }
    getElement('unit-label-dialog')?.addEventListener('cancel', (event) => {
        if (saving) {
            event.preventDefault();
        }
    });
    getElement('unit-label-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        if (saving) {
            return;
        }
        runAction(async () => {
            saving = true;
            const submit = event.target.querySelector('[type=submit]');
            submit.disabled = true;
            try {
                const response = await fetch(
                    '/api/scan?' +
                        new URLSearchParams({
                            code: getElement('unit-serial').value.trim()
                        })
                );
                const unit = await response.json();
                if (!response.ok || !unit.productUnitId) {
                    throw new Error(unit.message || 'Scan an existing physical unit serial.');
                }
                const result = await requestJson(`/variants/units/${unit.productUnitId}/barcode`, {
                    method: 'POST',
                    body: JSON.stringify({
                        manufacturerBarcode: getElement('unit-manufacturer').value,
                        generateInternal: getElement('unit-generate').checked
                    })
                });
                if (!result.barcode) {
                    throw new Error(
                        'Enter a manufacturer unit barcode or choose an internal barcode.'
                    );
                }
                unitLabel = {
                    code: result.barcode,
                    name: unit.productName + ' / ' + unit.serialNumber
                };
                labelVariant = null;
                getElement('label-source-controls').hidden=true;getElement('label-generate').hidden=true;
                getElement('label-copy-controls').hidden=true;getElement('label-item-controls').hidden=true;
                getElement('label-print').textContent='Print label';
                renderLabels(unitLabel.code, unitLabel.name, null, 1);
                getElement('label-help').textContent = 'This barcode identifies one physical unit.';
                getElement('unit-label-dialog').close();
                getElement('label-dialog').showModal();
            } finally {
                saving = false;
                submit.disabled = false;
            }
        });
    });
    getElement('variant-product').onchange = () => {
        return runAction(load);
    };
    getElement('variant-search').oninput = render;
    getElement('variant-status').onchange = render;
    getElement('variant-new')?.addEventListener('click', () => {
        return runAction(() => {
            return edit();
        });
    });
    getElement('attribute-add')?.addEventListener('click', () => {
        if (getElement('variant-attributes').children.length < 30) {
            addAttribute();
        }
    });
    getElement('variant-source')?.addEventListener('change', barcodeSource);
    for (const id of ['variant-close', 'variant-cancel']) {
        getElement(id)?.addEventListener('click', closeEditor);
    }
    getElement('variant-editor')?.addEventListener('cancel', (event) => {
        event.preventDefault();
        closeEditor();
    });
    getElement('variant-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        if (saving) {
            return;
        }
        runAction(async () => {
            const attributes = {};
            const names = new Set();
            for (const row of getElement('variant-attributes').children) {
                const [keyInput, valueInput] = row.querySelectorAll('input');
                const name = keyInput.value.trim();
                const value = valueInput.value.trim();
                if (!name || !value || names.has(name.toLowerCase())) {
                    throw new Error('Use distinct attribute names with one value each.');
                }
                names.add(name.toLowerCase());
                Object.defineProperty(attributes, name, {
                    value,
                    enumerable: true
                });
            }
            const body = {
                productId: Number(getElement('variant-product').value),
                variantName: getElement('variant-name').value,
                sku: getElement('variant-sku').value,
                isSerialTracked: getElement('variant-tracking').value === 'true',
                purchasePrice: Number(getElement('variant-purchase').value),
                sellingPrice: Number(getElement('variant-sale').value),
                taxPercentage: Number(getElement('variant-tax').value),
                warrantyMonths: Number(getElement('variant-warranty').value),
                minimumStockLevel: Number(getElement('variant-minimum').value),
                attributes,
                barcodes: lines('variant-barcodes'),
                isActive: getElement('variant-active').checked,
                barcodeSource: getElement('variant-source').value,
                primaryBarcode:
                    getElement('variant-source').value === 'MANUFACTURER'
                        ? getElement('variant-primary').value
                        : null,
                applyLocationMinimumStock: locationId > 0,
                locationMinimumStockLevel:
                    getElement('variant-location-minimum').value === ''
                        ? null
                        : Number(getElement('variant-location-minimum').value)
            };
            const id = getElement('variant-id').value;
            const submit = event.target.querySelector('[type=submit]');
            saving = true;
            submit.disabled = true;
            try {
                await requestJson('/variants' + (id ? '/' + id : ''), {
                    method: id ? 'PUT' : 'POST',
                    body: JSON.stringify(body)
                });
                saving = false;
                closeEditor();
                await load();
                getElement('variant-message').textContent = 'Size / model saved.';
            } finally {
                saving = false;
                submit.disabled = false;
            }
        });
    });
    runAction(async () => {
        products = await requestJson('?activeOnly=false');
        for (const product2 of products) {
            getElement('variant-product').add(
                new Option(
                    product2.productName + (product2.isActive === false ? ' (inactive)' : ''),
                    product2.productId
                )
            );
        }
        const query = new URLSearchParams(location.search);
        if (
            products.some((product2) => {
                return String(product2.productId) === query.get('productId');
            })
        ) {
            getElement('variant-product').value = query.get('productId');
        } else if (products.length === 1) {
            getElement('variant-product').value = products[0].productId;
        }
        await load();
        const selected = variants.find((variant) => {
            return String(variant.variantId) === query.get('variantId');
        });
        if (selected) {
            getElement('label-count').value = Math.max(
                1,
                Math.min(500, Number(query.get('copies')) || 1)
            );
            await labels(selected);
        }
    });
})();
