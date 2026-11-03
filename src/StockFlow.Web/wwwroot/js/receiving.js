// Receive goods: choose a supplier, add items, review quantities, and save stock.
(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const workspace = getElement('receiving-workspace');
    getElement('master-data')?.addEventListener('master-record-saved', (event) => {
        const supplier = event.detail;
        const select = getElement('intake-supplier');
        select.add(new Option(supplier.supplierName, supplier.supplierId));
        select.value = String(supplier.supplierId);
        select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    const dialog = getElement('intake-registration');
    const storageKey = workspace.dataset.storageKey;
    function createRequestId() {
        if (typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = [...bytes]
            .map((value) => {
                return value.toString(16).padStart(2, '0');
            })
            .join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
    // Page state: the current step, selected items, and any save that needs a retry.
    let currentStep = 1;
    let cart = [];
    let importedOpeningId = null, importedOpeningFingerprint = null;
    let products = [];
    let baseProducts = [];
    let pendingReceipt = null;
    let isSavingReceipt = false;
    let activeLookups = 0;
    let isSavingProduct = false;
    let isAddingVariant = false;
    let savedProductDetails = null;
    const date = new Date();
    getElement('intake-date').value =
        `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    const showMessage = (error, target = 'receiving-message') => {
        const box = getElement(target);
        box.textContent = error?.message || '';
        box.hidden = !error;
    };
    async function requestJson(url, body) {
        // No body means a lookup (GET). A body means we are saving data (POST).
        let options = {};
        if (body !== undefined) {
            options = {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                },
                body: JSON.stringify(body)
            };
        }

        const response = await fetch(url, options);
        let result = null;
        try {
            result = await response.json();
        } catch {
            // A failed response may not contain readable JSON.
        }
        if (!response.ok) {
            const error = new Error(
                result?.message ||
                    result?.title ||
                    Object.values(result?.errors || {})
                        .flat()
                        .join(' ') ||
                    'Unable to complete this action.'
            );
            error.status = response.status;
            throw error;
        }
        if (result === null) {
            throw new Error('The server response could not be read. Retry the saved receipt.');
        }
        return result;
    }
    const runAction = (action) => {
        return Promise.resolve()
            .then(action)
            .catch((error) => {
                return showMessage(error);
            });
    };
    function updateReceiptType() {
        const opening = getElement('intake-kind').value === 'OPENING_STOCK';
        getElement('intake-supplier-fields').hidden = opening;
        getElement('intake-invoice-fields').hidden = opening;
        getElement('intake-supplier').required = !opening;
        getElement('opening-help').hidden = !opening;
    }
    function showStep(next) {
        currentStep = next;
        for (let index = 1; index <= 3; index++) {
            getElement(`receiving-step-${index}`).hidden = index !== currentStep;
            const label = document.querySelector(`[data-step-label="${index}"]`);
            if (index === currentStep) {
                label.setAttribute('aria-current', 'step');
            } else {
                label.removeAttribute('aria-current');
            }
        }
        getElement('intake-back').hidden = currentStep === 1;
        getElement('intake-next').hidden = currentStep === 3;
        getElement('intake-next').textContent = currentStep === 1 ? 'Add items' : 'Review receipt';
        getElement('intake-save').hidden = currentStep !== 3;
        if (currentStep === 2) {
            getElement('intake-scan').focus();
        }
        if (currentStep === 3) {
            showReceiptReview();
        }
    }
    const getSerialNumbers = (line) => {
        return line.serialText
            .split(/\r?\n/)
            .map((text) => {
                return text.trim();
            })
            .filter(Boolean);
    };
    function addItemToReceipt(product, serial) {
        if (isSavingReceipt || pendingReceipt) {
            throw new Error('Resolve the saved receipt before changing items.');
        }
        if (!product.isActive) {
            throw new Error('This item is inactive. Choose an active product.');
        }
        const existing = cart.find((variant) => {
            return variant.variantId === product.variantId;
        });
        if (existing) {
            if (existing.quantity >= 10000) {
                throw new Error('Maximum quantity reached.');
            }
            existing.quantity++;
            if (serial) {
                existing.serialText += (existing.serialText ? '\n' : '') + serial;
            }
        } else {
            cart.push({
                productId: product.productId,
                variantId: product.variantId,
                suggestBarcodeGeneration: product.suggestBarcodeGeneration !== false,
                name: product.productName,
                packQuantity: product.packQuantity || 1,
                isSerialTracked: product.isSerialTracked,
                quantity: 1,
                purchasePrice: product.purchasePrice || 0,
                taxPercentage: product.taxPercentage || 0,
                serialText: serial || ''
            });
        }
        renderReceiptItems();
    }
    function control(label, type, value, change) {
        const wrap = document.createElement('div');
        wrap.className = 'col-md-4';
        const text = document.createElement('label');
        text.className = 'form-label';
        text.textContent = label;
        const node = document.createElement('input');
        node.type = type;
        node.value = value;
        node.className = 'form-control';
        node.required = true;
        node.setAttribute('aria-label', label);
        node.addEventListener('input', () => {
            return change(node.value);
        });
        wrap.append(text, node);
        return {
            wrap,
            node
        };
    }
    function renderReceiptItems() {
        getElement('intake-lines').replaceChildren();
        cart.forEach((line, index) => {
            const section = document.createElement('section');
            section.className = 'card card-body mb-3';
            section.dataset.variant = line.variantId;
            const heading = document.createElement('div');
            heading.className = 'receiving-line-heading';
            const title = document.createElement('h3');
            title.textContent = line.name;
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-sm btn-outline-secondary';
            remove.textContent = 'Remove';
            remove.addEventListener('click', () => {
                cart.splice(index, 1);
                renderReceiptItems();
            });
            heading.append(title, remove);
            section.append(heading);
            const row = document.createElement('div');
            row.className = 'row g-3';
            for (const [label, field, min, max, increment] of [
                ['Quantity', 'quantity', 1, 10000, 1],
                ['Purchase cost per item', 'purchasePrice', 0, 9999999999.99, 0.01],
                ['Tax %', 'taxPercentage', 0, 100, 0.01]
            ]) {
                const value = control(label, 'number', line[field], (value2) => {
                    return (line[field] = Number(value2));
                });
                value.node.min = min;
                value.node.max = max;
                value.node.step = increment;
                row.append(value.wrap);
            }
            section.append(row);
            if(document.body.dataset.catalogRegistrationEnhancements==='true' && line.packQuantity>1){
                const box=document.createElement('div');box.className='d-flex flex-wrap gap-2 align-items-center mt-2';
                const label=document.createElement('label');label.className='small';label.textContent='Purchase packs ('+line.packQuantity+' stock units per pack)';
                const packs=document.createElement('input');packs.type='number';packs.className='form-control';packs.min=1;packs.max=Math.floor(10000/line.packQuantity);packs.step=1;packs.value=1;packs.style.maxWidth='100px';label.append(packs);
                const convert=document.createElement('button');convert.type='button';convert.className='btn btn-outline-secondary btn-sm';convert.textContent='Convert packs to stock units';
                convert.onclick=()=>runAction(()=>{const units=Number(packs.value)*line.packQuantity;if(!Number.isInteger(Number(packs.value))||Number(packs.value)<1||units>10000)throw new Error('Enter a valid whole pack quantity.');line.quantity=units;renderReceiptItems();});
                const note=document.createElement('small');note.className='text-muted';note.textContent='Purchase cost remains the cost per stock unit. Review the converted quantity and total.';
                box.append(label,convert,note);section.append(box);
            }
            if (line.isSerialTracked) {
                const label = document.createElement('label');
                label.className = 'form-label mt-3';
                label.textContent = 'Serial numbers: one for each item received';
                const text = document.createElement('textarea');
                text.className = 'form-control';
                text.rows = 3;
                text.value = line.serialText;
                text.setAttribute('aria-label', 'Serial numbers for ' + line.name);
                text.addEventListener('input', () => {
                    return (line.serialText = text.value);
                });
                section.append(label, text);
                const scanner = document.createElement('input');
                scanner.className = 'form-control mt-2';
                scanner.placeholder = 'Scan serial and press Enter';
                scanner.setAttribute('aria-label', 'Scan serial for ' + line.name);
                scanner.autocomplete = 'off';
                StockFlowScanner.bind(
                    scanner,
                    (code) => {
                        if (
                            cart.some((value) => {
                                return getSerialNumbers(value).some((text2) => {
                                    return text2.toLowerCase() === code.toLowerCase();
                                });
                            })
                        ) {
                            throw new Error('This serial is already entered.');
                        }
                        if (getSerialNumbers(line).length >= line.quantity) {
                            throw new Error('Serial count already matches quantity.');
                        }
                        line.serialText += (line.serialText ? '\n' : '') + code;
                        text.value = line.serialText;
                    },
                    showMessage
                );
                section.append(scanner);
            }
            getElement('intake-lines').append(section);
        });
    }
    function validateSource() {
        if (!getElement('intake-date').reportValidity()) {
            return false;
        }
        if (
            getElement('intake-kind').value !== 'OPENING_STOCK' &&
            !getElement('intake-supplier').reportValidity()
        ) {
            return false;
        }
        return true;
    }
    function validateItems() {
        if (!cart.length) {
            throw new Error('Add at least one item.');
        }
        if (cart.length > 200) {
            throw new Error('Use at most 200 receipt lines.');
        }
        if (
            ![...getElement('intake-lines').querySelectorAll('input[type="number"]')].every(
                (node) => {
                    return node.reportValidity();
                }
            )
        ) {
            return false;
        }
        const seen = new Set();
        for (const line of cart) {
            const codes = getSerialNumbers(line);
            if (line.isSerialTracked && codes.length !== line.quantity) {
                throw new Error(line.name + ' needs exactly ' + line.quantity + ' serial numbers.');
            }
            for (const code of codes) {
                if (code.length > 150 || seen.has(code.toLowerCase())) {
                    throw new Error('Serials must be unique and at most 150 characters.');
                }
                seen.add(code.toLowerCase());
            }
        }
        return true;
    }
    function showReceiptReview() {
        getElement('intake-review-lines').replaceChildren();
        let total = 0;
        for (const line of cart) {
            const net = Math.round(line.quantity * line.purchasePrice * 100) / 100;
            const amount = net + Math.round(net * line.taxPercentage) / 100;
            total += amount;
            const tr = document.createElement('tr');
            for (const value of [
                line.name,
                line.quantity,
                line.purchasePrice.toFixed(2),
                line.isSerialTracked
                    ? getSerialNumbers(line).length + ' serials'
                    : 'Quantity stock',
                amount.toFixed(2)
            ]) {
                const td = document.createElement('td');
                td.textContent = value;
                tr.append(td);
            }
            getElement('intake-review-lines').append(tr);
        }
        getElement('intake-review-source').textContent =
            getElement('intake-kind').value === 'OPENING_STOCK'
                ? 'Opening stock · ' + getElement('intake-date').value
                : 'Supplier: ' +
                  getElement('intake-supplier').selectedOptions[0]?.textContent +
                  ' · ' +
                  getElement('intake-date').value;
        getElement('intake-total').textContent = 'Total value including tax: ' + total.toFixed(2);
    }
    getElement('intake-kind').addEventListener('change', updateReceiptType);
    getElement('intake-next').addEventListener('click', () => {
        return runAction(() => {
            showMessage(null);
            if (activeLookups) {
                throw new Error('Wait for the barcode lookup to finish.');
            }
            if (currentStep === 1 && validateSource()) {
                showStep(2);
            } else if (currentStep === 2 && validateItems()) {
                showStep(3);
            }
        });
    });
    getElement('intake-back').addEventListener('click', () => {
        return showStep(currentStep - 1);
    });
    const attribute = (product, storageKey2) => {
        return (
            Object.entries(product.attributes || {}).find(([name]) => {
                return name.toLowerCase() === storageKey2;
            })?.[1] || ''
        );
    };
    function variants() {
        const size = getElement('intake-size-filter').value;
        const color = getElement('intake-color-filter').value;
        const groups = new Map();
        getElement('intake-product').replaceChildren(new Option('Choose an item', ''));
        for (const variant of products.filter((value) => {
            return (
                (!size || attribute(value, 'size') === size) &&
                (!color || attribute(value, 'color') === color)
            );
        })) {
            let group = groups.get(variant.productId);
            if (!group) {
                group = document.createElement('optgroup');
                group.label = variant.productName.split(' / ')[0];
                groups.set(variant.productId, group);
                getElement('intake-product').append(group);
            }
            const attrs = [attribute(variant, 'size'), attribute(variant, 'color')]
                .filter(Boolean)
                .join(' / ');
            group.append(
                new Option(
                    variant.productName + (attrs ? ' · ' + attrs : '') + ' · ' + variant.sku,
                    variant.variantId
                )
            );
        }
    }
    async function findProducts() {
        products = await requestJson(
            '/api/products/variants?' +
                new URLSearchParams({
                    term: getElement('intake-search').value,
                    activeOnly: true
                })
        );
        for (const storageKey2 of ['size', 'color']) {
            const values = [
                ...new Set(
                    products
                        .map((value) => {
                            return attribute(value, storageKey2);
                        })
                        .filter(Boolean)
                )
            ].sort();
            getElement('intake-' + storageKey2 + '-filter').replaceChildren(
                new Option('All ' + storageKey2 + 's', ''),
                ...values.map((value) => {
                    return new Option(value, value);
                })
            );
        }
        getElement('intake-variant-filters').hidden = !products.some((value) => {
            return attribute(value, 'size') || attribute(value, 'color');
        });
        variants();
    }
    getElement('intake-size-filter').addEventListener('change', variants);
    getElement('intake-color-filter').addEventListener('change', variants);
    getElement('intake-find').addEventListener('click', () => {
        return runAction(findProducts);
    });
    getElement('intake-add').addEventListener('click', () => {
        return runAction(() => {
            const value = products.find((variant) => {
                return variant.variantId === Number(getElement('intake-product').value);
            });
            if (!value) {
                throw new Error('Find and select an item.');
            }
            addItemToReceipt(value);
        });
    });
    async function findScannedProduct(code) {
        if (dialog.open || isSavingReceipt || pendingReceipt) {
            throw new Error('Finish the current action before scanning another item.');
        }
        activeLookups++;
        try {
            showMessage(null);
            let result;
            try {
                result = await requestJson(
                    '/api/scan?' +
                        new URLSearchParams({
                            code
                        })
                );
            } catch (error) {
                if (error.status === 404 && workspace.dataset.canRegister === 'true') {
                    await openRegistration(false, code);
                    return;
                }
                throw error;
            }
            if (result.productUnitId) {
                throw new Error(
                    'This physical unit is already registered. Scan a model barcode to receive new units.'
                );
            }
            const rows = await requestJson(
                '/api/products/variants?' +
                    new URLSearchParams({
                        productId: result.productId
                    })
            );
            const product = rows.find((variant) => {
                return variant.variantId === result.variantId;
            });
            if (!product) {
                throw new Error('Choose an active product variant.');
            }
            addItemToReceipt(product);
        } finally {
            activeLookups--;
        }
    }
    const submitScan = StockFlowScanner.bind('intake-scan', findScannedProduct, showMessage);
    getElement('intake-lookup').addEventListener('click', submitScan);
    async function openRegistration(asVariant, code = '') {
        if (pendingReceipt || isSavingReceipt) {
            throw new Error('Resolve the saved receipt first.');
        }
        isAddingVariant = asVariant;
        savedProductDetails = null;
        getElement('intake-registration-form').reset();
        showMessage(null, 'intake-registration-message');
        getElement('intake-new-code').value = code;
        getElement('intake-registration-title').textContent = asVariant
            ? 'Add another size / color'
            : 'Add new product';
        getElement('intake-name-label').textContent = asVariant
            ? 'Size / color name (for example Blue / M)'
            : 'Product name';
        getElement('intake-base-product-field').hidden = getElement(
            'intake-variant-attributes'
        ).hidden = !asVariant;
        getElement('intake-category-field').hidden = getElement(
            'intake-has-variants-field'
        ).hidden = asVariant;
        getElement('intake-category').required = !asVariant;
        getElement('intake-new-sku').required = asVariant;
        getElement('intake-tracking').disabled = asVariant;
        if (asVariant) {
            baseProducts = (await requestJson('/api/products?activeOnly=true')).filter((value) => {
                return value.hasVariants;
            });
            if (!baseProducts.length) {
                throw new Error(
                    'No products have variants enabled. Ask an administrator to enable variants in product setup.'
                );
            }
            getElement('intake-base-product').replaceChildren(
                ...baseProducts.map((product) => {
                    return new Option(product.productName, product.productId);
                })
            );
            baseChanged();
        } else {
            const categories = await requestJson('/api/categories?activeOnly=true');
            getElement('intake-category').replaceChildren(
                new Option('Choose category', ''),
                ...categories.map((value) => {
                    return new Option(value.categoryPath || value.categoryName, value.categoryId);
                })
            );
        }
        dialog.showModal();
        getElement('intake-new-name').focus();
    }
    function baseChanged() {
        const value = baseProducts.find((product) => {
            return product.productId === Number(getElement('intake-base-product').value);
        });
        if (value) {
            getElement('intake-tracking').value = value.isSerialTracked ? 'SERIAL' : 'QUANTITY';
            getElement('intake-new-tax').value = value.taxPercentage;
            getElement('intake-new-warranty').value = value.warrantyMonths;
        }
    }
    getElement('intake-base-product').addEventListener('change', baseChanged);
    getElement('intake-code-type').addEventListener('change', () => {
        if (getElement('intake-code-type').value === 'SERIAL' && !isAddingVariant) {
            getElement('intake-tracking').value = 'SERIAL';
        }
    });
    getElement('intake-register')?.addEventListener('click', () => {
        return runAction(() => {
            return openRegistration(false);
        });
    });
    getElement('intake-add-variant')?.addEventListener('click', () => {
        return runAction(() => {
            return openRegistration(true);
        });
    });
    getElement('intake-register-cancel').addEventListener('click', () => {
        if (!isSavingProduct) {
            dialog.close();
        }
    });
    dialog.addEventListener('cancel', (event) => {
        if (isSavingProduct) {
            event.preventDefault();
        }
    });
    getElement('intake-registration-form').addEventListener('submit', async (event) => {
        event.preventDefault();
        if (isSavingProduct) {
            return;
        }
        isSavingProduct = true;
        getElement('intake-registration-fields').disabled = true;
        showMessage(null, 'intake-registration-message');
        try {
            const code = getElement('intake-new-code').value.trim();
            const isSerial = getElement('intake-code-type').value === 'SERIAL';
            const tracked = getElement('intake-tracking').value === 'SERIAL';
            if (isSerial && code && !tracked) {
                throw new Error('An individual serial requires serial tracking.');
            }
            const commercial = {
                purchasePrice: Number(getElement('intake-new-cost').value),
                sellingPrice: Number(getElement('intake-new-price').value),
                taxPercentage: Number(getElement('intake-new-tax').value),
                warrantyMonths: Number(getElement('intake-new-warranty').value)
            };
            if (!savedProductDetails) {
                if (isAddingVariant) {
                    const attributes = {};
                    if (getElement('intake-size').value.trim()) {
                        attributes.Size = getElement('intake-size').value.trim();
                    }
                    if (getElement('intake-color').value.trim()) {
                        attributes.Color = getElement('intake-color').value.trim();
                    }
                    const saved = await requestJson('/api/products/variants', {
                        ...commercial,
                        productId: Number(getElement('intake-base-product').value),
                        variantName: getElement('intake-new-name').value,
                        sku: getElement('intake-new-sku').value,
                        isSerialTracked: tracked,
                        attributes,
                        barcodes: !isSerial && code ? [code] : []
                    });
                    savedProductDetails = {
                        productId: Number(getElement('intake-base-product').value),
                        variantId: saved.variantId
                    };
                } else {
                    const saved = await requestJson('/api/products', {
                        ...commercial,
                        productName: getElement('intake-new-name').value,
                        categoryId: Number(getElement('intake-category').value),
                        trackingType: getElement('intake-tracking').value,
                        hasVariants: getElement('intake-has-variants').checked,
                        sku: getElement('intake-new-sku').value || null,
                        barcode: !isSerial && code ? code : null
                    });
                    savedProductDetails = {
                        productId: saved.productId
                    };
                }
            }
            const variants = await requestJson(
                '/api/products/variants?' +
                    new URLSearchParams({
                        productId: savedProductDetails.productId
                    })
            );
            const product = variants.find((variant) => {
                if (savedProductDetails.variantId) {
                    return variant.variantId === savedProductDetails.variantId;
                } else {
                    return variant.isDefault;
                }
            });
            if (!product) {
                throw new Error(
                    'Product saved. Retry this button to load it; do not register it again.'
                );
            }
            addItemToReceipt(product, isSerial ? code : null);
            dialog.close();
            getElement('intake-scan').focus();
        } catch (error) {
            if (savedProductDetails) {
                error.message =
                    'Product details are saved. Retry this button to add the item to the receipt. ' +
                    error.message;
            }
            showMessage(error, 'intake-registration-message');
        } finally {
            isSavingProduct = false;
            getElement('intake-registration-fields').disabled = false;
            getElement('intake-tracking').disabled = isAddingVariant;
        }
    });
    function updateSaveControls() {
        getElement('receiving-fields').disabled = isSavingReceipt || !!pendingReceipt;
        getElement('receiving-pending').hidden = !pendingReceipt;
        getElement('receiving-retry').disabled = isSavingReceipt;
    }
    async function saveStockReceipt() {
        if (isSavingReceipt) {
            return;
        }
        isSavingReceipt = true;
        updateSaveControls();
        showMessage(null);
        try {
            // A retry sends the same saved body and request ID as the first attempt.
            const result = await requestJson(pendingReceipt.url, pendingReceipt.body);
            if (!result.intakeId) {
                throw new Error('Receipt result was incomplete. Retry the saved receipt.');
            }
            sessionStorage.removeItem(storageKey);
            pendingReceipt = null;
            getElement('receiving-pending').hidden = true;
            getElement('receiving-form').hidden = true;
            getElement('intake-success').hidden = false;
            getElement('intake-result').textContent =
                (result.kind === 'OPENING_STOCK' ? 'Opening stock' : 'Receipt') +
                ' #' +
                result.intakeId +
                ' saved. Stock is now available in this location.';
            if (getElement('intake-label-link')) {
                getElement('intake-label-link').href = '/Labels?intakeId=' + result.intakeId;
                getElement('intake-label-link').hidden = false;
            }
            if (result.purchaseId && workspace.dataset.canPurchases === 'true') {
                getElement('intake-purchase-link').href = '/Purchases/Details/' + result.purchaseId;
                getElement('intake-purchase-link').hidden = false;
            }
        } catch (error) {
            if (error.status === 400 || error.status === 422) {
                sessionStorage.removeItem(storageKey);
                pendingReceipt = null;
            }
            showMessage(error);
        } finally {
            isSavingReceipt = false;
            updateSaveControls();
        }
    }
    getElement('receiving-form').addEventListener('submit', (event) => {
        event.preventDefault();
        runAction(async () => {
            if (isSavingReceipt || pendingReceipt) {
                return;
            }
            if (activeLookups) {
                throw new Error('Wait for scanning to finish.');
            }
            if (!validateSource() || !validateItems()) {
                return;
            }
            const opening = getElement('intake-kind').value === 'OPENING_STOCK';
            const body = {
                requestId: opening && importedOpeningId && importedOpeningFingerprint === JSON.stringify(cart) ? importedOpeningId : createRequestId(),
                locationId: window.StockFlowLocation,
                receivedDate: getElement('intake-date').value,
                supplierId: opening ? null : Number(getElement('intake-supplier').value),
                supplierInvoiceNumber: opening ? null : getElement('intake-invoice').value || null,
                notes: getElement('intake-notes').value || null,
                items: cart.map((variant) => {
                    return {
                        productId: variant.productId,
                        variantId: variant.variantId,
                        quantity: variant.quantity,
                        purchasePrice: variant.purchasePrice,
                        taxPercentage: variant.taxPercentage,
                        serialNumbers: getSerialNumbers(variant)
                    };
                })
            };
            pendingReceipt = {
                url: opening ? '/api/receiving/opening' : '/api/receiving',
                body,
                cart
            };
            try {
                sessionStorage.setItem(storageKey, JSON.stringify(pendingReceipt));
            } catch (error) {
                pendingReceipt = null;
                throw new Error(
                    'Browser storage is unavailable. Enable it before saving so interrupted receipts can be retried safely.'
                );
            }
            await saveStockReceipt();
        });
    });
    getElement('receiving-retry').addEventListener('click', () => {
        return runAction(saveStockReceipt);
    });
    getElement('intake-another').addEventListener('click', () => {
        return location.reload();
    });
    if(getElement('opening-csv-load')){
        const columns=['sku','locationId','quantity','purchasePrice','taxPercentage','serialNumbers'];
        getElement('opening-csv-template').onclick=()=>StockFlowCsv.download('stockflow-opening-stock.csv',columns,[['YOUR-SKU',window.StockFlowLocation,1,0,0,'']]);
        getElement('opening-csv-load').onclick=()=>runAction(async()=>{
            if(cart.length||pendingReceipt||isSavingReceipt||activeLookups)throw new Error('Finish or clear the current receipt before importing.');
            const file=getElement('opening-csv-file').files[0];if(!file)throw new Error('Choose an opening stock CSV first.');if(file.size>2000000)throw new Error('Use a CSV file up to 2 MB.');
            const location=window.StockFlowLocation;if(location<=0)throw new Error('Select one stock location.');
            getElement('opening-csv-load').disabled=true;
            try{
                const rows=StockFlowCsv.parse(await file.text());if(rows.length>200)throw new Error('Use up to 200 items per opening receipt.');
                const available=await requestJson('/api/products/variants?activeOnly=true'),lookup=new Map(available.map(v=>[v.sku.toUpperCase(),v]));
                const seen=new Set(),serials=new Set(),candidate=[];
                for(const row of rows){
                    const sku=(row.sku||'').toUpperCase(),item=lookup.get(sku);
                    if(!item||item.isActive===false)throw new Error('Unknown/inactive SKU: '+sku);
                    if(seen.has(sku))throw new Error('Duplicate SKU in file: '+sku);seen.add(sku);
                    if(!/^\d+$/.test(row.locationId||'')||Number(row.locationId)!==location)throw new Error('Each row must contain the selected shop locationId '+location+'.');
                    if(!/^\d+$/.test(row.quantity||'')||Number(row.quantity)<1||Number(row.quantity)>10000)throw new Error('Enter quantity 1 to 10000 for '+sku+'.');
                    const price=row.purchasePrice||String(item.purchasePrice||0),tax=row.taxPercentage||String(item.taxPercentage||0);
                    if(!/^\d+(\.\d{1,2})?$/.test(price)||Number(price)>9999999999.99||!/^\d+(\.\d{1,2})?$/.test(tax)||Number(tax)>100)throw new Error('Invalid cost or tax for '+sku+'.');
                    const codes=(row.serialNumbers||'').split('|').map(v=>v.trim()).filter(Boolean);
                    if(item.isSerialTracked&&codes.length!==Number(row.quantity)||!item.isSerialTracked&&codes.length)throw new Error('Serial count does not match tracking/quantity for '+sku+'.');
                    for(const code of codes){if(code.length>150||serials.has(code.toUpperCase()))throw new Error('Duplicate/invalid serial '+code);serials.add(code.toUpperCase());}
                    candidate.push({productId:item.productId,variantId:item.variantId,name:item.productName+' / '+item.variantName,isSerialTracked:item.isSerialTracked,quantity:Number(row.quantity),purchasePrice:Number(price),taxPercentage:Number(tax),serialText:codes.join('\n')});
                }
                if(window.StockFlowLocation!==location)throw new Error('Shop changed; reload before importing.');
                const canonical=JSON.stringify([...candidate].sort((a,b)=>a.variantId-b.variantId).map(v=>({productId:v.productId,variantId:v.variantId,quantity:v.quantity,purchasePrice:v.purchasePrice,taxPercentage:v.taxPercentage,serials:v.serialText.split('\n').filter(Boolean).map(s=>s.toUpperCase()).sort()})));
                const importKey=await requestJson('/api/receiving/opening/import-key',{canonical,locationId:location});
                if(!importKey.requestId)throw new Error('Opening import identity could not be prepared. Reload and try again.');
                importedOpeningId=importKey.requestId;
                cart=candidate;importedOpeningFingerprint=JSON.stringify(cart);
                getElement('intake-kind').value='OPENING_STOCK';updateReceiptType();renderReceiptItems();
                getElement('opening-csv-message').textContent=cart.length+' items validated for this shop. Continue to Review & save to confirm. No stock has been posted.';
                showMessage(null);
            }finally{getElement('opening-csv-load').disabled=false;}
        });
    }
    getElement('receiving-form').addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && ['INPUT', 'SELECT'].includes(event.target.tagName)) {
            event.preventDefault();
        }
    });
    runAction(async () => {
        try {
            pendingReceipt = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
        } catch {
            throw new Error(
                'The saved receipt cannot be read. Contact your administrator before re-entering these goods.'
            );
        }
        if (pendingReceipt) {
            cart = pendingReceipt.cart;
            getElement('intake-kind').value = pendingReceipt.body.supplierId
                ? 'RECEIPT'
                : 'OPENING_STOCK';
            getElement('intake-date').value = pendingReceipt.body.receivedDate;
            if (pendingReceipt.body.supplierId) {
                getElement('intake-supplier').add(
                    new Option(
                        'Saved supplier #' + pendingReceipt.body.supplierId,
                        pendingReceipt.body.supplierId,
                        true,
                        true
                    )
                );
            }
            renderReceiptItems();
            showStep(3);
            updateSaveControls();
            return;
        }
        if (!getElement('intake-kind').options.length) {
            throw new Error('You need receiving or opening-stock permission to post stock.');
        }
        if (getElement('intake-kind').querySelector('[value="RECEIPT"]')) {
            const suppliers = await requestJson('/api/suppliers?activeOnly=true');
            for (const supplier of suppliers) {
                getElement('intake-supplier').add(
                    new Option(supplier.supplierName, supplier.supplierId)
                );
            }
        }
        updateReceiptType();
        getElement('receiving-fields').disabled = window.StockFlowLocation <= 0;
    });
})();
