(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    const canGenerate = getElement('label-workspace').dataset.canGenerate === 'true';
    const receiptLot = new URLSearchParams(location.search).has('receiptId');
    const labelsUrl = id => receiptLot ? `/api/labels/lots/receipt/${id}` : `/api/receiving/${id}/labels`;
    let rows = [];
    let intakeId = null;
    let busy = false;
    const showMessage = (error) => {
        getElement('label-message').textContent = error?.message || '';
        getElement('label-message').hidden = !error;
    };
    async function requestJson(url, body) {
        const response = await fetch(
            url,
            body === undefined
                ? {}
                : {
                      method: 'POST',
                      headers: {
                          'Content-Type': 'application/json',
                          'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                      },
                      body: JSON.stringify(body)
                  }
        );
        const result = await response.json().catch(() => {
            return null;
        });
        if (!response.ok || result === null) {
            throw new Error(result?.message || 'Unable to load or prepare labels. Try again.');
        }
        return result;
    }
    function freeze(value) {
        busy = value;
        for (const node of document.querySelectorAll(
            '.label-controls input,.label-controls select,.label-controls button'
        )) {
            node.disabled = value;
        }
    }
    async function runAction(action) {
        if (busy) {
            return;
        }
        freeze(true);
        showMessage(null);
        try {
            await action();
        } catch (error) {
            showMessage(error);
        } finally {
            freeze(false);
        }
    }
    const identity = (row) => {
        return row.labelId ? `item:${row.labelId}` : `${row.variantId}:${row.unitId || 0}`;
    };
    function normalize(items) {
        const map = new Map();
        for (const item of items) {
            const key = identity(item);
            if (map.has(key)) {
                map.get(key).quantity += item.quantity;
            } else {
                map.set(key, {
                    ...item,
                    key,
                    copies: Math.min(item.quantity, 500),
                    selected: false,
                    source: item.labelId ? 'internal' : item.existingBarcode ? 'existing' : 'internal'
                });
            }
        }
        return [...map.values()];
    }
    function invalidate() {
        getElement('label-preview-area').replaceChildren();
        getElement('label-count').textContent = '';
    }
    function field(label, node) {
        const wrap = document.createElement('div');
        const text = document.createElement('label');
        text.className = 'form-label';
        text.textContent = label;
        node.setAttribute('aria-label', label);
        wrap.append(text, node);
        return wrap;
    }
    function render() {
        getElement('label-items').replaceChildren();
        for (const row of rows) {
            const card = document.createElement('section');
            card.className = 'card card-body mb-3';
            card.dataset.labelKey = row.key;
            const heading = document.createElement('h2');
            heading.className = 'h5';
            heading.textContent = row.productName;
            const detail = document.createElement('p');
            detail.className = 'small text-muted';
            detail.textContent = row.unitId
                ? `Serial: ${row.serialNumber} · Individual unit`
                : `SKU: ${row.sku} · ${row.quantity} received${row.variantDetails ? ' · ' + row.variantDetails : ''}`;
            const controls = document.createElement('div');
            controls.className = 'label-item-controls';
            const check = document.createElement('input');
            check.type = 'checkbox';
            check.checked = row.selected;
            check.addEventListener('change', () => {
                row.selected = check.checked;
                invalidate();
            });
            controls.append(field('Include in printing', check));
            const choice = document.createElement('select');
            choice.className = 'form-select';
            choice.add(new Option('Use existing barcode', 'existing'));
            choice.add(new Option('Use saved internal barcode', 'internal'));
            if (!row.unitId && !row.labelId) choice.options[1].disabled = true;
            if(row.labelId){choice.options[0].disabled=!row.existingBarcode;choice.disabled=!row.existingBarcode;}
            choice.value = row.source;
            choice.addEventListener('change', () => {
                row.source = choice.value;
                invalidate();
                render();
            });
            controls.append(field('Barcode choice', choice));
            const copies = document.createElement('input');
            copies.type = 'number';
            copies.className = 'form-control';
            copies.min = 1;
            copies.max = row.unitId || row.labelId ? 1 : 500;
            copies.step = 1;
            copies.value = row.copies;
            copies.addEventListener('input', () => {
                row.copies = Number(copies.value);
                invalidate();
            });
            controls.append(field('Label copies', copies));
            card.append(heading, detail, controls);
            const code = row.source === 'existing' ? row.existingBarcode : row.internalBarcode;
            const status = document.createElement('p');
            status.className = 'mt-3 mb-0';
            status.textContent = code
                ? `Identifier: ${code}`
                : row.source === 'existing'
                  ? 'No existing barcode is saved. Register a model barcode when receiving, or generate an internal label.'
                  : 'No internal barcode yet.';
            card.append(status);
            if (row.source === 'internal' && canGenerate && row.unitId) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-outline-primary mt-2 align-self-start';
                button.textContent = row.internalBarcode
                    ? 'Reuse internal barcode'
                    : 'Generate internal barcode';
                button.addEventListener('click', () => {
                    return runAction(async () => {
                        const result = await requestJson(
                            row.unitId
                                ? `/api/products/variants/units/${row.unitId}/barcode`
                                : `/api/products/variants/${row.variantId}/barcode`,
                            row.unitId
                                ? {
                                      generateInternal: true
                                  }
                                : {}
                        );
                        row.internalBarcode = result.barcode;
                        row.selected = true;
                        invalidate();
                        render();
                    });
                });
                card.append(button);
            }
            if (row.unitId && canGenerate) {
                const input = document.createElement('input');
                input.className = 'form-control mt-2';
                input.maxLength = 150;
                input.placeholder = 'Scan a unique existing unit barcode (optional)';
                input.setAttribute('aria-label', 'Existing unit barcode for ' + row.serialNumber);
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-outline-secondary mt-2 align-self-start';
                button.textContent = 'Save existing unit barcode';
                button.addEventListener('click', () => {
                    return runAction(async () => {
                        const code = input.value.trim();
                        if (!code) {
                            throw new Error(
                                'Scan a unique barcode identifying this physical unit.'
                            );
                        }
                        await requestJson(`/api/products/variants/units/${row.unitId}/barcode`, {
                            manufacturerBarcode: code,
                            generateInternal: false
                        });
                        const fresh = await requestJson(labelsUrl(intakeId));
                        const item = fresh.find((value) => {
                            return identity(value) === row.key;
                        });
                        if (!item) {
                            throw new Error(
                                'This unit is no longer available in the current location.'
                            );
                        }
                        row.existingBarcode = item.existingBarcode;
                        row.source = 'existing';
                        invalidate();
                        render();
                    });
                });
                const extra = document.createElement('details');
                const summary = document.createElement('summary');
                summary.textContent = 'Attach an existing unit label (optional)';
                extra.className = 'mt-3';
                extra.append(summary, input, button);
                card.append(extra);
            }
            getElement('label-items').append(card);
        }
    }
    async function load() {
        const id = Number(getElement('label-intake').value);
        if (!Number.isSafeInteger(id) || id < 1) {
            throw new Error('Enter a valid saved receipt number.');
        }
        invalidate();
        rows = [];
        intakeId = null;
        render();
        const items = await requestJson(labelsUrl(id));
        rows = normalize(items);
        intakeId = id;
        render();
        if (!rows.length) {
            throw new Error('No saved labels yet. Use Prepare unique item labels for this receipt.');
        }
        history.replaceState(null, '', `/Labels?${receiptLot ? 'receiptId' : 'intakeId'}=${id}`);
    }
    function preview() {
        const selected = rows.filter((value) => {
            return value.selected;
        });
        if (!selected.length) {
            throw new Error(
                'Select the items that need labels. Existing labels can be used without printing replacements.'
            );
        }
        const count = selected.reduce((sum, row) => {
            return sum + row.copies;
        }, 0);
        if (count > 500) {
            throw new Error(
                'Print at most 500 labels per batch. Reduce copies or select fewer items.'
            );
        }
        const fragment = document.createDocumentFragment();
        for (const row of selected) {
            if (
                !Number.isInteger(row.copies) ||
                row.copies < 1 ||
                row.copies > (row.unitId || row.labelId ? 1 : 500)
            ) {
                throw new Error(
                    'Enter 1–500 copies for quantity stock, or one label per individual unit.'
                );
            }
            const code = row.source === 'existing' ? row.existingBarcode : row.internalBarcode;
            if(!row.unitId&&!row.labelId&&!(row.source==='existing'&&row.existingBarcode))throw new Error('Generate unique item labels, or use the saved manufacturer barcode.');
            if (!code) {
                throw new Error(
                    `Choose an existing identifier or generate a barcode for ${row.productName}.`
                );
            }
            const barcode = StockFlowBarcode.svg(code);
            for (let index = 0; index < row.copies; index++) {
                const label = document.createElement('article');
                label.className = 'stock-label';
                const name = document.createElement('strong');
                name.textContent = row.productName;
                const detail = document.createElement('div');
                detail.className = 'label-detail';
                detail.textContent = row.unitId
                    ? `Serial: ${row.serialNumber}`
                    : row.variantDetails || `SKU: ${row.sku}`;
                const price = document.createElement('div');
                price.className = 'label-detail';
                price.textContent = 'Price before tax: ' + row.sellingPrice.toFixed(2);
                const text = document.createElement('div');
                text.className = 'label-code';
                text.textContent = code;
                label.append(name, detail, price, barcode.cloneNode(true), text);
                fragment.append(label);
            }
        }
        getElement('label-preview-area').replaceChildren(fragment);
        getElement('label-count').textContent = `${count} label${count === 1 ? '' : 's'} ready.`;
    }
    async function print(action) {
        preview();
        if (!intakeId) {
            throw new Error('Load a receipt first.');
        }
        // Re-read stored identifiers before printing; neither printing nor reprinting generates new identifiers.
        const fresh = await requestJson(`${labelsUrl(intakeId)}/${action}`, {});
        for (const row of rows.filter((value) => {
            return value.selected;
        })) {
            const item = fresh.find((value) => {
                return identity(value) === row.key;
            });
            if (!item) {
                throw new Error(
                    'A selected item is no longer available in this location. Reload the receipt.'
                );
            }
            Object.assign(row, {
                existingBarcode: item.existingBarcode,
                internalBarcode: item.internalBarcode,
                sellingPrice: item.sellingPrice
            });
        }
        preview();
        const thermal = getElement('label-layout').value === 'thermal';
        document.body.classList.toggle('label-thermal', thermal);
        let style = getElement('label-page-size');
        if (!style) {
            style = document.createElement('style');
            style.id = 'label-page-size';
            document.head.append(style);
        }
        style.textContent = thermal
            ? '@page {size:63mm 38mm;margin:0}'
            : '@page {size:A4;margin:9mm}';
        window.print();
    }
    getElement('label-load').addEventListener('submit', (event) => {
        event.preventDefault();
        runAction(load);
    });
    getElement('label-generate-items')?.addEventListener('click',()=>runAction(async()=>{
        const id=Number(getElement('label-intake').value);
        if(!Number.isSafeInteger(id)||id<1)throw new Error('Enter a saved receipt number first.');
        await requestJson(`${labelsUrl(id)}/generate`,{});
        await load();
    }));
    for(const [id,selected] of [['label-select-all',true],['label-select-none',false]])
        getElement(id).addEventListener('click',()=>{rows.forEach(row=>row.selected=selected);invalidate();render();});
    getElement('label-preview').addEventListener('click', () => {
        return runAction(preview);
    });
    getElement('label-print')?.addEventListener('click', () => {
        return runAction(() => {
            return print('print');
        });
    });
    getElement('label-reprint')?.addEventListener('click', () => {
        return runAction(() => {
            return print('reprint');
        });
    });
    getElement('label-layout').addEventListener('change', invalidate);
    const id = new URLSearchParams(location.search).get(receiptLot ? 'receiptId' : 'intakeId');
    if (id) {
        getElement('label-intake').value = id;
        runAction(load);
    }
})();
