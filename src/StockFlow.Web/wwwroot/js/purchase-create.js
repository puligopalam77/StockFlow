// Build a stock receipt or purchase order. Stock is added only after completion.
(async () => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const form = getElement('purchase-form');
    const workspace = getElement('purchase-workspace');
    const directReceipt = workspace.dataset.directReceipt === 'true';
    workspace.classList.add('receipt-card-layout');
    const items = [];
    let id = new URLSearchParams(location.search).get('id');
    let products = [];
    let suppliers = [];
    getElement('master-data')?.addEventListener('master-record-saved', (event) => {
        const supplier = event.detail;
        suppliers.push(supplier);
        const select = getElement('supplierId');
        select.add(new Option(supplier.supplierCode + ' - ' + supplier.supplierName, supplier.supplierId));
        select.value = String(supplier.supplierId);
        select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    let sequence = 0;
    let saving = false;
    let scanning = 0;
    let pending = null;
    const key = workspace.dataset.storageKey;
    const canComplete = workspace.dataset.canComplete === 'true';
    const searchInput = getElement('purchase-search');
    const money = (value) => {
        return Math.round((value + Number.EPSILON) * 100) / 100;
    };
    const currency = value => new Intl.NumberFormat('en-IN', {style:'currency',currency:'INR'}).format(value);
    function clearError() {
        getElement('purchase-message').hidden = true;
    }
    function fieldError(input, message) {
        const detail = input.closest('details');
        if (detail) detail.open = true;
        StockFlowValidation.showField(input, message);
        input.focus({ preventScroll: true });
        input.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
    function values(item, damaged = false) {
        return (damaged ? item.damagedSerials : item.serials).value
            .split(/\r?\n/)
            .map((text) => {
                return text.trim();
            })
            .filter(Boolean);
    }
    function totals() {
        let subtotal = 0;
        let tax = 0;
        items.forEach((item, index) => {
            const net =
                money(Number(item.quantity.value || 0) * Number(item.price.value || 0)) -
                Number(item.discount.value || 0);
            const lineTax = money((net * Number(item.tax.value || 0)) / 100);
            subtotal += net;
            tax += lineTax;
            item.total.textContent = currency(net + lineTax);
            item.headerTotal.textContent = `Line total: ${currency(net + lineTax)}`;
            item.itemCaption.textContent = `Item ${index + 1}`;
        });
        const landed =
            Number(getElement('freightAmount').value || 0) +
            Number(getElement('otherLandedCost').value || 0);
        getElement('purchase-items-title').textContent = `Receipt Items (${items.filter(item=>item.select.value).length})`;
        getElement('purchase-totals').replaceChildren(...[
            ['Subtotal',subtotal],['Tax',tax],['Purchase charges',landed],
            ['Total',subtotal + tax + landed - Number(getElement('discountAmount').value || 0)]
        ].map(([label,value])=>{
            const cell=document.createElement('div');
            const caption=document.createElement('span'); caption.textContent=label;
            const amount=document.createElement('strong'); amount.textContent=currency(value);
            cell.append(caption,amount); return cell;
        }));
    }
    function fillOptions(item) {
        const selected = item.select.value;
        const term = searchInput.value.trim().toLowerCase();
        item.select.replaceChildren(new Option('Choose product / size / model', ''));
        products
            .filter((variant) => {
                return (
                    String(variant.variantId) === selected ||
                    !term ||
                    [
                        variant.productName,
                        variant.model,variant.parentProductCode,variant.brandName,variant.categoryName,variant.parentCategoryName,variant.supplierProductCode,...(variant.supplierIdentifiers||[]).map(i=>i.supplierSku),
                        variant.sku,
                        variant.barcode,
                        ...(variant.barcodes || []),
                        ...Object.values(variant.attributes || {})
                    ]
                        .join(' ')
                        .toLowerCase()
                        .includes(term)
                );
            })
            .forEach((variant) => {
                return item.select.add(
                    new Option(
                        variant.productName +
                            (variant.model ? ' | ' + variant.model : '') +
                            (variant.sku?.startsWith('ITEM-') ? '' : ' | ' + variant.sku),
                        variant.variantId
                    )
                );
            });
        item.select.value = selected;
    }
    function remove(item) {
        items.splice(items.indexOf(item), 1);
        item.row.remove();
        totals();
    }
    function mergeMatchingItems() {
        const numericFields = ['price', 'tax', 'pack', 'mrp'];
        const textFields = ['unit', 'supplierCode', 'batch', 'manufactured', 'expiry'];
        for (const item of [...items]) {
            if (!items.includes(item) || !item.select.value ||
                [...item.row.querySelectorAll('input, select, textarea')].some(input => !input.checkValidity())) continue;
            const existing = items.find(candidate => candidate !== item &&
                items.indexOf(candidate) < items.indexOf(item) &&
                candidate.select.value === item.select.value &&
                [...candidate.row.querySelectorAll('input, select, textarea')].every(input => input.checkValidity()) &&
                numericFields.every(key => Number(candidate[key].value) === Number(item[key].value)) &&
                textFields.every(key => candidate[key].value === item[key].value) &&
                ['quantity', 'received', 'damaged'].every(key => Number(candidate[key].value) + Number(item[key].value) <= 10000) &&
                Number(candidate.discount.value) + Number(item.discount.value) <= Number(candidate.discount.max));
            if (!existing) continue;
            // Keep per-line currency rounding unchanged when combining taxable lines.
            const net = value => money(Number(value.quantity.value) * Number(value.price.value)) - Number(value.discount.value);
            const tax = value => money(net(value) * Number(value.tax.value) / 100);
            if (money(tax(existing) + tax(item)) !== money((net(existing) + net(item)) * Number(item.tax.value) / 100)) continue;
            for (const key of ['quantity', 'received', 'damaged']) {
                existing[key].value = Number(existing[key].value) + Number(item[key].value);
            }
            existing.discount.value = money(Number(existing.discount.value) + Number(item.discount.value));
            for (const key of ['serials', 'damagedSerials']) {
                // Preserve every serial: duplicate serial validation must still detect mistakes.
                existing[key].value = [existing[key].value, item[key].value].filter(Boolean).join('\n');
            }
            remove(item);
            existing.render();
        }
        totals();
    }
    function addItem(saved = null, serial = null) {
        if (directReceipt && saved) {
            const existing = items.find(item => Number(item.select.value) === saved.variantId);
            if (existing) {
                if (Number(existing.quantity.value) + Number(saved.quantity || 1) > 10000) throw new Error('Maximum received quantity is 10000.');
                existing.quantity.value = Number(existing.quantity.value) + Number(saved.quantity || 1);
                if (serial) existing.serials.value += (existing.serials.value ? '\n' : '') + serial;
                existing.render(); totals(); return;
            }
        }
        if (saved) {
            items
                .filter((value2) => {
                    return !value2.select.value;
                })
                .forEach(remove);
        }
        const row = document.createElement('section');
        row.className = 'purchase-line';
        const header = document.createElement('div');
        header.className = 'purchase-line-header';
        const itemCaption = document.createElement('span');
        itemCaption.className = 'purchase-item-caption';
        const headerTotal = document.createElement('span');
        headerTotal.className = 'purchase-header-total';
        header.append(itemCaption, headerTotal);
        const imageBox=document.createElement('div');
        imageBox.className='purchase-product-image'; imageBox.hidden=true;
        const image=document.createElement('img');
        image.loading='lazy'; imageBox.append(image);
        const body=document.createElement('div'); body.className='purchase-line-body';
        row.append(header,imageBox,body);
        let imageSource='';
        image.addEventListener('error',()=> {imageBox.hidden=true;row.classList.remove('has-product-image');});
        const grid = document.createElement('div');
        grid.className = 'row g-2 purchase-item-grid';
        body.append(grid);
        const value = ++sequence;
        function field(labelText, type, options = {}, parent = grid, width = 'col-md-3') {
            const wrap = document.createElement('div');
            wrap.className = width;
            const input = document.createElement(
                type === 'select' ? 'select' : type === 'textarea' ? 'textarea' : 'input'
            );
            if (input.tagName === 'INPUT') {
                input.type = type;
            }
            input.id =
                'purchase-line-' + value + '-' + labelText.replace(/[^a-z]/gi, '').toLowerCase();
            input.className = type === 'select' ? 'form-select' : 'form-control';
            Object.assign(input, options);
            const label = document.createElement('label');
            label.className = 'form-label';
            label.htmlFor = input.id;
            label.textContent = labelText;
            wrap.append(label, input);
            parent.append(wrap);
            return input;
        }
        const select = field(
            'Product Variant *',
            'select',
            {
                required: true
            },
            grid,
            'col-md-5'
        );
        const identifiers=document.createElement('small');
        identifiers.className='purchase-product-identifiers';
        select.parentElement.append(identifiers);
        const quantity = field(
            directReceipt ? 'Delivery Quantity *' : 'Expected quantity *',
            'number',
            {
                min: '1',
                max: '10000',
                step: '1',
                value: saved?.quantity ?? 1,
                required: true
            },
            grid,
            'col-md-2'
        );
        const received = field(
            'Accepted Quantity *',
            'number',
            {
                min: '0',
                max: '10000',
                step: '1',
                value:
                    saved?.pendingReceivedQuantity ??
                    saved?.receivedQuantity ??
                    (getElement('purchase-as-order').checked ? 0 : (saved?.quantity ?? 1)),
                required: true
            },
            grid,
            'col-md-2'
        );
        const damaged = field(
            'Damaged Quantity',
            'number',
            {
                min: '0',
                max: '10000',
                step: '1',
                value: saved?.pendingDamagedQuantity ?? 0,
                required: true
            },
            grid,
            'col-md-1'
        );
        const price = field('Unit Price (INR) *', 'number', {
            min: '0',
            max: '9999999999.99',
            step: '0.01',
            value: saved?.purchasePrice ?? 0,
            required: true
        });
        const advanced = document.createElement('section');
        advanced.className = 'purchase-line-options';
        const summary = document.createElement('h3');
        summary.className = 'purchase-options-title';
        summary.textContent = 'Tax & line discount';
        advanced.append(summary);
        const optionsGrid = document.createElement('div');
        optionsGrid.className = 'purchase-line-options-grid';
        advanced.append(optionsGrid);
        body.append(advanced);
        const discount = field(
            'Item Discount (INR)',
            'number',
            {
                min: '0',
                max: '9999999999.99',
                step: '0.01',
                value: saved?.discountAmount ?? 0
            },
            optionsGrid,
            'col-md-6'
        );
        const tax = field(
            'Tax Rate (%)',
            'number',
            {
                min: '0',
                max: '100',
                step: '0.01',
                value: saved?.taxPercentage ?? 0,
                required: true
            },
            optionsGrid,
            'col-md-6'
        );
        const unit = field('Purchase Unit', 'select', {}, optionsGrid, 'col-md-3');
        ['PIECE', 'BOX', 'PAIR', 'SET'].forEach((value2) => {
            return unit.add(new Option(value2[0] + value2.slice(1).toLowerCase(), value2));
        });
        unit.value = saved?.unitOfMeasure || 'PIECE';
        const pack = field(
            'Pack Size',
            'number',
            {
                min: '1',
                max: '10000',
                step: '1',
                value: saved?.packQuantity ?? 1,
                required: true
            },
            optionsGrid,
            'col-md-3'
        );
        const mrp = field(
            'MRP',
            'number',
            {
                min: '0',
                max: '9999999999.99',
                step: '0.01',
                value: saved?.mrp ?? 0
            },
            optionsGrid,
            'col-md-3'
        );
        const supplierCode = field(
            'Supplier SKU',
            'text',
            {
                maxLength: 100,
                value: saved?.supplierProductCode ?? ''
            },
            optionsGrid,
            'col-md-3'
        );
        const batch = field(
            'Batch / lot number',
            'text',
            {
                maxLength: 100,
                value: saved?.batchNumber ?? ''
            },
            optionsGrid,
            'col-md-4'
        );
        const manufactured = field(
            'Manufacturing date',
            'date',
            {
                value: saved?.manufacturingDate?.slice?.(0, 10) || ''
            },
            optionsGrid,
            'col-md-4'
        );
        const expiry = field(
            'Expiry date',
            'date',
            {
                value: saved?.expiryDate?.slice?.(0, 10) || ''
            },
            optionsGrid,
            'col-md-4'
        );
        const serialGrid = document.createElement('div');
        serialGrid.className = 'row g-2 mt-1';
        body.append(serialGrid);
        const serials = field(
            'Serial numbers: one per received unit',
            'textarea',
            {
                rows: 2,
                value: saved?.serialNumbers?.join('\n') ?? serial ?? ''
            },
            serialGrid,
            'col-md-8'
        );
        const scanner = field(
            'Scan serial and press Enter',
            'text',
            {
                autocomplete: 'off',
                maxLength: 150
            },
            serialGrid,
            'col-md-4'
        );
        const damagedSerials = field(
            'Damaged unit serials',
            'textarea',
            {
                rows: 2,
                value: saved?.damagedSerialNumbers?.join('\n') ?? ''
            },
            serialGrid,
            'col-md-8'
        );
        const serialStatus = document.createElement('small'); serialStatus.className = 'receipt-serial-status';
        select.parentElement.append(serialStatus);
        const count = document.createElement('p');
        count.className = 'small text-muted';
        count.setAttribute('aria-live', 'polite');
        serials.parentElement.append(count);
        const footer = document.createElement('div');
        footer.className = 'purchase-line-total';
        const totalLabel=document.createElement('span'); totalLabel.textContent='Line total';
        const total = document.createElement('strong');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm purchase-remove-item';
        button.textContent = 'Remove item';
        footer.append(totalLabel,total);
        header.append(button);
        grid.append(footer);
        const item = {
            row,
            select,
            quantity,
            received,
            damaged,
            price,
            discount,
            tax,
            unit,
            pack,
            mrp,
            supplierCode,
            batch,
            manufactured,
            expiry,
            serials,
            damagedSerials,
            scanner,
            total,
            headerTotal,
            itemCaption,
            serialGrid
        };
        items.push(item);
        getElement('purchase-items').append(row);
        if (saved) {
            select.add(new Option(saved.productName, saved.variantId));
            select.value = saved.variantId;
        }
        fillOptions(item);
        item.render = () => {
            const value2 = products.find((variant) => {
                return variant.variantId === Number(select.value);
            });
            identifiers.textContent=value2 ? [value2.sku ? 'SKU: '+value2.sku : '',value2.barcode ? 'Barcode: '+value2.barcode : ''].filter(Boolean).join(' | ') : '';
            let source='';
            if(value2 && workspace.dataset.canImages==='true') {
                if(value2.hasProductImage) source='/api/products/'+value2.productId+'/image';
                else if(value2.productImageUrl) {
                    try { const url=new URL(value2.productImageUrl); if(['https:','http:'].includes(url.protocol)) source=url.href; } catch { }
                }
            }
            if(source!==imageSource) {
                imageSource=source;
                imageBox.hidden=!source; row.classList.toggle('has-product-image',!!source);
                if(source) {image.alt=value2.productName;image.src=source;}
                else image.removeAttribute('src');
            }
            serialGrid.hidden = !value2?.isSerialTracked;
            serials.parentElement.hidden =
                scanner.parentElement.hidden =
                damagedSerials.parentElement.hidden =
                    serialGrid.hidden;
            received.disabled = damaged.disabled = getElement('purchase-as-order').checked;
            if (directReceipt) { received.value = Math.max(0, Number(quantity.value) - Number(damaged.value)); received.readOnly = true; }
            if (directReceipt) identifiers.textContent += ' | Good qty: ' + received.value;
            count.textContent = `Good serials: ${values(item).length} / ${received.value} · Damaged serials: ${values(item, true).length} / ${damaged.value}`;
        };
        const chips = document.createElement('div'); chips.className='receipt-serial-chips';
        if (directReceipt) serials.parentElement.append(chips);
        function renderSerialChips() {
            if (!directReceipt) return;
            chips.replaceChildren();
            values(item).forEach((serial,index)=>{
                const chip=document.createElement('span'); chip.className='receipt-serial-chip';
                const label=document.createElement('span'); label.textContent=serial;
                const remove=document.createElement('button'); remove.type='button'; remove.textContent='\u00d7'; remove.setAttribute('aria-label','Remove serial '+serial);
                remove.onclick=()=>{ serials.value=values(item).filter((_,position)=>position!==index).join('\n'); item.render(); };
                chip.append(label,remove); chips.append(chip);
            });
        }
        const originalRender = item.render;
        item.render = () => { originalRender(); renderSerialChips(); if (item.visualFallback) item.visualFallback.hidden = !!imageSource && !imageBox.hidden; serialStatus.textContent = directReceipt && !serialGrid.hidden ? values(item).length + ' / ' + received.value + ' good serials entered; ' + Math.max(0, Number(received.value) - values(item).length) + ' more required' : ''; };
        select.onchange = () => {
            if (directReceipt && select.value && items.some(other => other !== item && other.select.value === select.value)) {
                const other = items.find(other => other !== item && other.select.value === select.value);
                const combined = Number(other.quantity.value) + Number(quantity.value);
                if (combined > 10000) { select.value = ''; return Purchases.error(new Error('Maximum received quantity is 10000.')); }
                other.quantity.value = combined; remove(item); other.render(); totals(); return;
            }
            const value2 = products.find((variant) => {
                return variant.variantId === Number(select.value);
            });
            const supplierTerms=value2?.supplierIdentifiers?.find(i=>i.supplierId===Number(getElement('supplierId').value));
            price.value = supplierTerms?.expectedPurchaseCost ?? value2?.purchasePrice ?? 0;
            tax.value = value2?.taxPercentage ?? 0;
            mrp.value = value2?.mrp ?? 0;
            pack.value = supplierTerms?.packConversion ?? value2?.packQuantity ?? 1;
            unit.value = value2?.unitOfMeasure || 'PIECE';
            supplierCode.value = value2?.supplierIdentifiers?.find(i=>i.supplierId===Number(getElement('supplierId').value))?.supplierSku || value2?.supplierProductCode || '';
            serials.value = damagedSerials.value = '';
            item.render();
            totals();
        };
        const scanSerial = StockFlowScanner.bind(
            scanner,
            (code) => {
                if (
                    items.some((value2) => {
                        return values(value2)
                            .concat(values(value2, true))
                            .some((text) => {
                                return text.toLowerCase() === code.toLowerCase();
                            });
                    })
                ) {
                    throw new Error('That serial has already been entered.');
                }
                if (values(item).length >= Number(received.value)) {
                    throw new Error('Good serial count already matches received quantity.');
                }
                serials.value += (serials.value ? '\n' : '') + code;
                item.render();
            },
            Purchases.error
        );
        if (directReceipt) {
            const addSerial=document.createElement('button'); addSerial.type='button'; addSerial.className='btn btn-sm btn-outline-primary mt-2'; addSerial.textContent='+ Add serial';
            addSerial.onclick=scanSerial; scanner.parentElement.append(addSerial);
        }
        [serials, damagedSerials, received, damaged, quantity].forEach((value2) => {
            return value2.addEventListener('input', item.render);
        });
        [quantity, damaged, price, discount, tax].forEach((input) => {
            return input.addEventListener('input', totals);
        });
        button.onclick = () => {
            return remove(item);
        };
        row.addEventListener('focusout', event => {
            if (row.contains(event.relatedTarget)) return;
            // Wait until staff finish editing the line so they can enter a different cost.
            setTimeout(() => {
                if (!directReceipt && !saving && !pending && items.includes(item)) mergeMatchingItems();
            }, 0);
        });
        if (saved) {
            if (
                !products.some((variant) => {
                    return variant.variantId === saved.variantId;
                })
            ) {
                const option = new Option('Inactive item: choose a replacement', saved.variantId);
                option.disabled = true;
                select.add(option);
            }
            select.value = saved.variantId;
        }
        item.render();
        if (saved && !pending && !directReceipt) mergeMatchingItems();
        totals();
    }
    function bulkRows() {
        const rows = getElement('bulk-variants');
        rows.replaceChildren();
        products
            .filter((product) => {
                return product.productId === Number(getElement('bulk-product').value);
            })
            .forEach((variant) => {
                const label = document.createElement('label');
                label.className = 'd-flex flex-wrap gap-2 align-items-center my-2';
                label.textContent =
                    variant.variantName + ' / cost ' + Purchases.money(variant.purchasePrice);
                const quantity = document.createElement('input');
                Object.assign(quantity, {
                    type: 'number',
                    min: '0',
                    max: '10000',
                    value: '0',
                    className: 'form-control'
                });
                quantity.dataset.variantId = variant.variantId;
                quantity.style.maxWidth = '120px';
                label.append(quantity);
                rows.append(label);
            });
    }
    function catalog() {
        const selected = getElement('bulk-product').value;
        const seen = new Set();
        getElement('bulk-product').replaceChildren(new Option('Choose product', ''));
        products.forEach((product) => {
            if (!seen.has(product.productId)) {
                seen.add(product.productId);
                getElement('bulk-product').add(
                    new Option(product.productName.split(' / ')[0], product.productId)
                );
            }
        });
        getElement('bulk-product').value = selected;
        bulkRows();
        items.forEach(fillOptions);
    }
    function mode() {
        const order = !directReceipt && getElement('purchase-as-order').checked;
        getElement('purchase-save').textContent = order
            ? 'Save purchase order'
            : directReceipt ? 'Confirm Receipt' : 'Save stock receipt';
        getElement('purchase-save').value = order ? 'draft' : 'complete';
        getElement('purchase-draft').hidden = directReceipt || order;
        if (directReceipt) getElement('purchase-save').disabled = !canComplete;
        getElement('purchase-title').textContent = id
            ? 'Edit draft'
            : order
              ? 'New purchase order'
              : 'Receive Stock';
        const title = getElement('purchase-title').textContent;
        const breadcrumb = getElement('breadcrumb-current');
        if (breadcrumb) breadcrumb.textContent = title;
        document.title = title + ' - StockFlow';
        const help=getElement('purchase-help');
        if(help){
            help.hidden=!order;
            help.textContent=order?'Record the expected quantities now. Add stock only when each delivery arrives.':'';
        }
        items.forEach((value) => {
            return value.render();
        });
    }
    async function confirmReceipt(body) {
        const dialog = getElement('receipt-confirm');
        if (dialog.open) return false;
        const received = body.items.reduce((sum,item) => sum + item.quantity, 0);
        const damaged = body.items.reduce((sum,item) => sum + item.damagedQuantity, 0);
        const summary = getElement('receipt-confirm-summary'); summary.replaceChildren();
        for (const [label,value] of [
            ['Supplier',suppliers.find(s => s.supplierId === body.supplierId)?.supplierName],
            ['Supplier Invoice',body.supplierInvoiceNumber || 'Not provided'],
            ['Products',body.items.length],['Total Received',received],['Damaged Quantity',damaged],
            ['Added to Sellable Stock',received-damaged],
            ['Purchase Total',getElement('purchase-totals').lastElementChild.lastElementChild.textContent]
        ]) {
            const dt = document.createElement('dt'); dt.textContent = label;
            const dd = document.createElement('dd'); dd.textContent = value; summary.append(dt,dd);
        }
        return new Promise(resolve => {
            let confirmed = false;
            getElement('receipt-confirm-save').onclick = () => { confirmed = true; dialog.close(); };
            getElement('receipt-confirm-cancel').onclick = () => dialog.close();
            dialog.addEventListener('close', () => resolve(confirmed), {once:true}); dialog.showModal();
        });
    }
    function persist(value) {
        const stored = structuredClone(value);
        if (stored?.body) {
            delete stored.body.invoiceAttachmentData;
            delete stored.body.invoiceAttachmentName;
            delete stored.body.invoiceAttachmentContentType;
        }
        sessionStorage.setItem(key, JSON.stringify(stored));
        pending = stored;
        freeze();
    }
    function freeze() {
        getElement('purchase-fields').disabled =
            saving || !!pending || window.StockFlowLocation <= 0;
        getElement('purchase-pending').hidden = !pending;
        getElement('purchase-retry').disabled = saving;
        getElement('purchase-retry').hidden = !!pending && !pending.purchaseId;
        getElement('purchase-checked').hidden = !pending || !!pending.purchaseId;
        if (pending) {
            getElement('purchase-pending-text').textContent = pending.purchaseId
                ? 'Purchase #' +
                  pending.purchaseId +
                  ' is saved. Retry to confirm its result without adding the stock twice.'
                : 'The response was interrupted. Check purchase history to confirm whether the receipt was saved before starting another receipt.';
        }
    }
    async function done(purchaseId) {
        sessionStorage.removeItem(key);
        pending = null;
        if (directReceipt) {
            try {
                const seen=JSON.parse(localStorage.getItem(key+'.counted-receipts') || '[]');
                if (!seen.includes(String(purchaseId))) {
                    const counts=JSON.parse(localStorage.getItem(key+'.product-frequency') || '{}');
                    items.forEach(item=>counts[item.select.value]=(Number(counts[item.select.value]) || 0)+1);
                    localStorage.setItem(key+'.product-frequency',JSON.stringify(counts));
                    localStorage.setItem(key+'.counted-receipts',JSON.stringify([...seen,String(purchaseId)].slice(-100)));
                }
            } catch { /* Local quick picks do not affect the committed receipt. */ }
            form.hidden = true;
            const result = getElement('receipt-success'); result.hidden = false;
            getElement('receipt-view').href = '/Purchases/Details/' + purchaseId;
            getElement('receipt-success-number').textContent = 'Purchase #' + purchaseId;
            const received = items.reduce((sum,item) => sum + Number(item.quantity.value), 0);
            const damaged = items.reduce((sum,item) => sum + Number(item.damaged.value), 0);
            getElement('receipt-success-summary').textContent = received + ' units received. ' + (received-damaged) + ' units added to available stock. ' + damaged + ' units marked damaged. Total value: ' + getElement('purchase-totals').lastElementChild.lastElementChild.textContent;
            result.scrollIntoView({block:'center'});
            try {
                const receipts = await Purchases.api('/api/purchases/' + purchaseId + '/receipts');
                const receipt = receipts[receipts.length - 1];
                if (receipt) {
                    getElement('receipt-success-number').textContent = 'Receipt No: ' + receipt.receiptNumber;
                    getElement('receipt-view').href = '/StockInflow/Details/PURCHASE/' + receipt.receiptId;
                }
            } catch { /* Receipt committed; purchase link remains available. */ }
            return;
        }
        window.StockFlowUnsaved?.markSaved(getElement('purchase-form'));
        location.href = '/Purchases/Details/' + purchaseId;
    }
    async function finish() {
        if (!pending?.purchaseId || saving) {
            return;
        }
        saving = true;
        freeze();
        clearError();
        try {
            const existing = await Purchases.api('/api/purchases/' + pending.purchaseId);
            if (
                existing.status === 'COMPLETED' ||
                (pending.complete && existing.status === 'PARTIALLY_RECEIVED')
            ) {
                done(pending.purchaseId);
                return;
            }
            if (existing.status !== 'DRAFT') {
                throw new Error(
                    'This purchase changed status. Open it from purchase history to review.'
                );
            }
            if (pending.stage === 'edit') {
                await Purchases.api('/api/purchases/' + pending.purchaseId, {
                    method: 'PUT',
                    body: JSON.stringify(pending.body)
                });
            }
            if (pending.complete) {
                await Purchases.api('/api/purchases/' + pending.purchaseId + '/complete', {
                    method: 'POST',
                    body: '{}'
                });
            }
            done(pending.purchaseId);
        } catch (error) {
            Purchases.error(error);
        } finally {
            saving = false;
            freeze();
        }
    }
    getElement('purchase-retry').onclick = finish;
    getElement('purchase-checked').onclick = () => {
        sessionStorage.removeItem(key);
        location.reload();
    };
    try {
        pending = JSON.parse(sessionStorage.getItem(key) || 'null');
        if (pending) freeze();
        let savedPurchase = null;
        if (pending?.purchaseId) {
            savedPurchase = await Purchases.api('/api/purchases/' + encodeURIComponent(id || pending.purchaseId));
            // Opening Edit draft is an explicit return to the saved order, not a
            // request to resume a previous completion attempt. The server still
            // enforces DRAFT status when an edit is subsequently submitted.
            if (id && Number(id) === Number(pending.purchaseId) &&
                pending.stage === 'saved' && savedPurchase.status === 'DRAFT') {
                sessionStorage.removeItem(key);
                pending = null;
            }
        }
        [suppliers, products] = await Promise.all([
            Purchases.api('/api/suppliers?activeOnly=true'),
            Purchases.api('/api/products/variants?activeOnly=true')
        ]);
        getElement('supplierId').replaceChildren(
            new Option('Choose supplier', ''),
            ...suppliers.map((supplier) => {
                return new Option(
                    (supplier.supplierCode ? supplier.supplierCode + ' - ' : '') +
                        supplier.supplierName,
                    supplier.supplierId
                );
            })
        );
        catalog();
        getElement('supplierId').addEventListener('change', () => {
            getElement('paymentTermsDays').value =
                suppliers.find((supplier) => {
                    return supplier.supplierId === Number(getElement('supplierId').value);
                })?.paymentTermsDays ?? '';
        });
        const date = new Date();
        date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
        getElement('purchaseDate').value = date.toISOString().slice(0, 10);
        getElement('receivedDate').value = date.toISOString().slice(0, 10);
        getElement('purchase-as-order').checked = !directReceipt && (!canComplete || new URLSearchParams(location.search).get('mode') === 'order');
        getElement('purchase-as-order').disabled = !canComplete;
        if (id || pending?.purchaseId) {
            const supplier = savedPurchase || await Purchases.api('/api/purchases/' + encodeURIComponent(id));
            form.dataset.editVersion = supplier.editVersion ?? 0;
            window.StockFlowEditingPurchase = supplier;
            if(id){const label=document.createElement('label');label.htmlFor='draft-internal-reference';label.textContent='Internal Reference';const input=document.createElement('input');input.id='draft-internal-reference';input.className='form-control';input.maxLength=100;input.value=supplier.internalReference||'';getElement('purchase-fields').append(label,input);}
            if(id){const wrap=document.createElement('div');wrap.className='col-12';const label=document.createElement('label');label.htmlFor='draft-change-reason';label.textContent='Reason for Change';const input=document.createElement('input');input.id='draft-change-reason';input.className='form-control';input.maxLength=1000;input.minLength=3;input.required=true;wrap.append(label,input);getElement('purchase-fields').append(wrap);}
            if (supplier.status !== 'DRAFT' && !pending) {
                throw new Error('Only draft purchases can be edited.');
            }
            if (
                !suppliers.some((supplier2) => {
                    return supplier2.supplierId === supplier.supplierId;
                })
            ) {
                const option = new Option(
                    'Inactive supplier: choose a replacement',
                    supplier.supplierId
                );
                option.disabled = true;
                getElement('supplierId').add(option);
            }
            getElement('supplierId').value = supplier.supplierId;
            for (const name of [
                'supplierInvoiceNumber',
                'notes',
                'discountAmount',
                'paymentTermsDays',
                'freightAmount',
                'otherLandedCost'
            ]) {
                getElement(name).value = supplier[name] ?? '';
            }
            getElement('purchaseDate').value = supplier.purchaseDate.slice(0, 10);
            getElement('supplierInvoiceDate').value =
                supplier.supplierInvoiceDate?.slice(0, 10) || '';
            getElement('receivedDate').value = supplier.receivedDate?.slice(0, 10) || '';
            getElement('purchase-as-order').checked = !supplier.items.some((item) => {
                return item.pendingReceivedQuantity + item.pendingDamagedQuantity > 0;
            });
            supplier.items.forEach((item) => {
                return addItem(item);
            });
        } else {
            addItem();
        }
        mode();
        totals();
        freeze();
    } catch (error) {
        Purchases.error(error);
        return;
    }
    getElement('purchase-as-order').addEventListener('change', mode);
    const suggestions = document.createElement('div'); suggestions.id = 'purchase-suggestions';
    suggestions.setAttribute('role','group'); suggestions.setAttribute('aria-label','Matching products');
    searchInput.setAttribute('aria-controls','purchase-suggestions'); searchInput.setAttribute('aria-expanded','false');
    searchInput.parentElement.classList.add('purchase-search-field');
    searchInput.parentElement.append(suggestions);
    let suggestionIndex = -1;
    function closeSuggestions() {
        suggestions.replaceChildren(); suggestionIndex=-1; searchInput.setAttribute('aria-expanded','false');
    }
    searchInput.addEventListener('input', () => {
        items.forEach(fillOptions);
        if (!directReceipt) return;
        const term = searchInput.value.trim().toLowerCase(); closeSuggestions();
        if (!term) return;
        products.filter(product => StockFlowVariantIdentity.matches(product,term)).slice(0,20).forEach(product => {
            const button = document.createElement('button'); button.type = 'button';
            const placeholder = document.createElement('span'); placeholder.className='receipt-product-placeholder'; placeholder.setAttribute('aria-hidden','true'); button.append(placeholder);
            let source='';
            if (workspace.dataset.canImages==='true') {
                if (product.hasProductImage) source='/api/products/'+product.productId+'/image';
                else if (product.productImageUrl) {
                    try { const url=new URL(product.productImageUrl); if (['http:','https:'].includes(url.protocol)) source=url.href; } catch { }
                }
            }
            if (source) {
                const image=document.createElement('img'); image.src=source; image.alt=''; image.className='receipt-suggestion-image';
                image.onerror=()=>image.replaceWith(placeholder); placeholder.replaceWith(image);
            }
            const copy=document.createElement('span'); copy.className='receipt-suggestion-copy';
            const name=document.createElement('strong'); name.textContent=product.productName;
            const info=document.createElement('small'); info.textContent=[product.model,product.variantName,product.sku].filter(Boolean).join(' \u00b7 ');
            copy.append(name,info); button.append(copy);
            button.onclick = () => { addItem({...product,quantity:1}); searchInput.value = ''; closeSuggestions(); items.forEach(fillOptions); searchInput.focus(); };
            suggestions.append(button);
        });
        searchInput.setAttribute('aria-expanded',String(suggestions.childElementCount>0));
    });
    searchInput.addEventListener('keydown', event=>{
        const buttons=[...suggestions.querySelectorAll('button')];
        if (!buttons.length) return;
        if (event.key==='Escape') { closeSuggestions(); return; }
        if (['ArrowDown','ArrowUp'].includes(event.key)) {
            event.preventDefault(); suggestionIndex=(suggestionIndex+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;
            buttons.forEach((button,index)=>button.classList.toggle('is-active',index===suggestionIndex));
            buttons[suggestionIndex].scrollIntoView({block:'nearest'});
        }
        if (event.key==='Enter' && suggestionIndex>=0) { event.preventDefault(); event.stopImmediatePropagation(); buttons[suggestionIndex].click(); }
    });
    document.addEventListener('pointerdown',event=>{
        if (event.target!==searchInput && !suggestions.contains(event.target)) closeSuggestions();
    });
    ['discountAmount', 'freightAmount', 'otherLandedCost'].forEach((id) => {
        return getElement(id).addEventListener('input', totals);
    });
    getElement('add-item').onclick = () => {
        const empty = items.find((value) => {
            return !value.select.value;
        });
        if (empty) {
            empty.select.focus();
        } else {
            addItem();
        }
    };
    getElement('bulk-product').onchange = bulkRows;
    getElement('bulk-add').onclick = () => {
        const rows = [...getElement('bulk-variants').querySelectorAll('input')];
        if (
            rows.some((q) => {
                return !q.reportValidity();
            })
        ) {
            return;
        }
        rows.filter((q) => {
            return Number(q.value) > 0;
        }).forEach((q) => {
            const value = products.find((variant) => {
                return variant.variantId === Number(q.dataset.variantId);
            });
            addItem({
                ...value,
                quantity: Number(q.value)
            });
            q.value = '0';
        });
    };
    const lookup = StockFlowScanner.bind(
        'productScanner',
        async (code) => {
            if (saving || pending || getElement('purchase-registration')?.open) {
                throw new Error('Finish the current action before scanning another item.');
            }
            if (directReceipt) closeSuggestions();
            scanning++;
            clearError();
            try {
                let match;
                try {
                    match = await Purchases.api('/api/scan?code=' + encodeURIComponent(code));
                } catch (error) {
                    if (error.status === 404 && workspace.dataset.canRegister === 'true') {
                        location.assign('/Products');
                        return;
                    }
                    if (error.status === 404) {
                        throw new Error(
                            'This code is not registered. Ask a user with product registration access to add it.'
                        );
                    }
                    throw error;
                }
                if (match.productUnitId) {
                    throw new Error(
                        'This unit already exists in inventory. Choose the product model when receiving new units.'
                    );
                }
                const item = products.find((variant) => {
                    return variant.variantId === match.variantId;
                });
                if (!item) {
                    throw new Error(
                        'This item is unavailable. Refresh the item catalog before adding it.'
                    );
                }
                addItem(
                    {
                        ...item,
                        quantity: 1
                    },
                    null
                );
            } finally {
                scanning--;
            }
        },
        Purchases.error
    );
    getElement('purchase-lookup')?.addEventListener('click', lookup);
    const camera = getElement('productScanner')?.parentElement.querySelector('.sf-camera-trigger');
    if (camera) camera.textContent = 'Scan with Camera';
    form.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && event.target.tagName === 'INPUT') {
            event.preventDefault();
        }
    });
    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (saving || pending) {
            return;
        }
        clearError();
        if (scanning || getElement('purchase-registration')?.open) {
            return Purchases.error(
                new Error('Finish scanning or product registration before saving.')
            );
        }
        form.querySelectorAll(':invalid').forEach((input) => {
            let detail = input.closest('details');
            while (detail) {
                detail.open = true;
                detail = detail.parentElement.closest('details');
            }
        });
        if (!form.reportValidity()) {
            return;
        }
        if (!items.length) {
            return Purchases.error(new Error('Add at least one item.'));
        }
        if (
            items.some((value) => {
                return !products.some((variant) => {
                    return variant.variantId === Number(value.select.value);
                });
            })
        ) {
            return Purchases.error(new Error('Choose an active item for every receipt line.'));
        }
        const complete = directReceipt || event.submitter?.value === 'complete';
        if (complete && !canComplete) {
            return Purchases.error(new Error('You do not have permission to post stock.'));
        }
        const seen = new Set();
        for (const item of items) {
            if (directReceipt && Number(item.damaged.value) > Number(item.quantity.value)) return fieldError(item.damaged, 'Damaged quantity cannot exceed received quantity.');
            if (Number(item.discount.value) > money(Number(item.quantity.value) * Number(item.price.value))) return fieldError(item.discount, 'Discount cannot exceed the line base amount.');
            if (
                Number(item.received.value) + Number(item.damaged.value) >
                Number(item.quantity.value)
            ) {
                return fieldError(item.received,
                    `Received (${item.received.value}) plus damaged (${item.damaged.value}) exceeds expected quantity (${item.quantity.value}). Correct the quantities.`);
            }
            if (
                item.expiry.value &&
                item.manufactured.value &&
                item.expiry.value < item.manufactured.value
            ) {
                return fieldError(item.expiry, 'Choose an expiry date on or after the manufacturing date.');
            }
            if (item.serialGrid.hidden) {
                continue;
            }
            const good = values(item);
            const bad = values(item, true);
            for (const [serialList, input] of [[good, item.serials], [bad, item.damagedSerials]]) {
              for (const serial of serialList) {
                if (serial.length > 150) {
                    return fieldError(input, 'Each serial number must contain no more than 150 characters. Enter one serial number per line.');
                }
                if (seen.has(serial.toLowerCase())) {
                    return fieldError(input, `Serial number "${serial}" is repeated in this receipt. Enter a different serial number for each unit.`);
                }
                seen.add(serial.toLowerCase());
              }
            }
            if (
                !getElement('purchase-as-order').checked &&
                (good.length !== Number(item.received.value) ||
                    bad.length !== Number(item.damaged.value))
            ) {
                const goodMismatch = good.length !== Number(item.received.value);
                const input = goodMismatch ? item.serials : item.damagedSerials;
                const expected = goodMismatch ? Number(item.received.value) : Number(item.damaged.value);
                const actual = goodMismatch ? good.length : bad.length;
                return fieldError(input,
                    `Enter exactly ${expected} ${goodMismatch ? 'good' : 'damaged'} unit serial number(s); ${actual} entered. Put each serial number on a separate line (press Enter), without commas.`);
            }
        }
        if (!directReceipt) mergeMatchingItems();
        const file = getElement('invoiceAttachment').files[0];
        let fileData = null;
        if (file) {
            if (file.size > 5000000) {
                return fieldError(getElement('invoiceAttachment'), 'Choose a supplier invoice attachment of 5 MB or smaller.');
            }
            fileData = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => {
                    return resolve(String(reader.result).split(',')[1]);
                };
                reader.onerror = () => {
                    return reject(new Error('Unable to read the invoice attachment.'));
                };
                reader.readAsDataURL(file);
            });
        }
        const body = {
            expectedVersion: id ? Number(form.dataset.editVersion || 0) : null,
            changeReason: id ? getElement('draft-change-reason').value : null,
            internalReference: id ? getElement('draft-internal-reference').value : null,
            supplierId: Number(getElement('supplierId').value),
            locationId: window.StockFlowLocation,
            supplierInvoiceNumber: getElement('supplierInvoiceNumber').value,
            supplierInvoiceDate: getElement('supplierInvoiceDate').value || null,
            purchaseDate: getElement('purchaseDate').value,
            receivedDate:
                getElement('receivedDate').value ||
                (complete ? getElement('purchaseDate').value : null),
            notes: getElement('notes').value,
            paymentTermsDays:
                getElement('paymentTermsDays').value === ''
                    ? null
                    : Number(getElement('paymentTermsDays').value),
            discountAmount: Number(getElement('discountAmount').value || 0),
            freightAmount: Number(getElement('freightAmount').value || 0),
            otherLandedCost: Number(getElement('otherLandedCost').value || 0),
            invoiceAttachmentName: file?.name || null,
            invoiceAttachmentContentType: file?.type || null,
            invoiceAttachmentData: fileData,
            completeImmediately: directReceipt,
            items: items.map((value) => {
                return {
                    productId: products.find((variant) => {
                        return variant.variantId === Number(value.select.value);
                    }).productId,
                    variantId: Number(value.select.value),
                    quantity: Number(value.quantity.value),
                    receivedQuantity: getElement('purchase-as-order').checked
                        ? 0
                        : Number(value.received.value),
                    damagedQuantity: getElement('purchase-as-order').checked
                        ? 0
                        : Number(value.damaged.value),
                    purchasePrice: Number(value.price.value),
                    discountAmount: Number(value.discount.value || 0),
                    taxPercentage: Number(value.tax.value),
                    serialNumbers: value.serialGrid.hidden ? [] : values(value),
                    damagedSerialNumbers: value.serialGrid.hidden ? [] : values(value, true),
                    unitOfMeasure: value.unit.value,
                    packQuantity: Number(value.pack.value),
                    mrp: Number(value.mrp.value || 0),
                    supplierProductCode: value.supplierCode.value,
                    batchNumber: value.batch.value,
                    manufacturingDate: value.manufactured.value || null,
                    expiryDate: value.expiry.value || null
                };


            })
        };
        if(id){
            const old=window.StockFlowEditingPurchase;
            const before=old.items.map(i=>i.productName+': '+i.quantity+' x '+i.purchasePrice+', discount '+i.discountAmount+', tax '+i.taxPercentage+'%').join('\n');
            const next=body.items.map(i=>(products.find(p=>p.variantId===i.variantId)?.productName||i.productId)+': '+i.quantity+' x '+i.purchasePrice+', discount '+i.discountAmount+', tax '+i.taxPercentage+'%').join('\n');
            const keys=['supplierId','purchaseDate','supplierInvoiceNumber','supplierInvoiceDate','paymentTermsDays','discountAmount','freightAmount','otherLandedCost','internalReference','notes'];
            const changed=keys.filter(key=>String(old[key]??'')!==String(body[key]??'')).map(key=>key+': '+(old[key]??'-')+' -> '+(body[key]??'-')).join('\n');
            if(!await StockFlowDialog.confirm('Review Purchase Changes\n\nPrevious items:\n'+before+'\n\nNew items:\n'+next+'\n\n'+changed+'\nAttachment: '+(old.invoiceAttachmentName||'-')+' -> '+(file?.name||old.invoiceAttachmentName||'-')+'\nReason: '+body.changeReason+'\n\nSave these changes?'))return;
        }
        const payable = body.items.reduce((sum,item) => {
            const net = money(item.quantity * item.purchasePrice) - item.discountAmount;
            return sum + net + money(net * item.taxPercentage / 100);
        }, 0) + body.freightAmount + body.otherLandedCost - body.discountAmount;
        if (payable < 0) return fieldError(getElement('discountAmount'), 'Overall discount cannot exceed the purchase value including tax and charges.');
        if (directReceipt && !await confirmReceipt(body)) return;
        saving = true;
        try {
            persist({
                purchaseId: id ? Number(id) : null,
                stage: id ? 'edit' : 'create',
                complete,
                body
            });
            const result = await Purchases.api(
                '/api/purchases' + (id ? '/' + encodeURIComponent(id) : ''),
                {
                    method: id ? 'PUT' : 'POST',
                    body: JSON.stringify(body)
                }
            );
            const purchaseId = id || result?.purchaseId;
            if (!purchaseId) {
                throw new Error(
                    'The order result was incomplete. Check purchase history before trying again.'
                );
            }
            persist({
                purchaseId: Number(purchaseId),
                stage: 'saved',
                complete,
                body
            });
            saving = false;
            if (directReceipt) {
                await done(purchaseId);
            } else if (complete) {
                await finish();
            } else {
                done(purchaseId);
            }
        } catch (error) {
            if (error.status >= 400 && error.status < 500) {
                sessionStorage.removeItem(key);
                pending = null;
            }
            Purchases.error(error);
        } finally {
            saving = false;
            freeze();
        }
    });
})();
