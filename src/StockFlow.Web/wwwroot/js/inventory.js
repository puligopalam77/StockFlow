// Show current stock and stock history, and save authorized stock corrections.
(() => {
    'use strict';

    const getElement = (id) => {
        return document.getElementById(id);
    };
    getElement('inventory-search').value = new URLSearchParams(location.search).get('term') || '';
    let productId = '';
    let variantId = '';
    let historyOffset = 0;let historyPager,unitPager;
    let unitsOffset = 0;
    let products = [];
    let pending = null;
    let posting = false;
    let blocked = false;
    let adjustmentUnits = [];
    let balanceRows = [], balanceSort = 'productName', balanceAscending = true;
    let balanceVersion = 0;
    let historyVersion = 0;
    let unitVersion = 0;
    const storageKey = getElement('inventory-workspace').dataset.storageKey;
    async function requestJson(path, options) {
        const response = await fetch('/api/inventory' + path, options);
        if (!response.ok) {
            const body = await response.json().catch(() => {
                return {};
            });
            const error = new Error(
                body.message ||
                    Object.values(body.errors || {})
                        .flat()
                        .join(' ') ||
                    `Request failed (${response.status}). Check saved stock changes before trying again. If the problem continues, contact the shop owner or administrator.`
            );
            error.status = response.status;
            throw error;
        }
        return response.json();
    }
    function table(id, rows, fields) {
        const target = getElement(id);
        target.replaceChildren();
        for (const row of rows) {
            const tr = document.createElement('tr');
            for (const [index,field] of fields.entries()) {
                const td = document.createElement('td');
                const value = field(row);
                if (value instanceof Node) {
                    td.append(value);
                } else {
                    td.textContent = value ?? '—';
                }
                if(id==='balances')td.dataset.label=['Product','Category / brand','Item code / barcode','Available stock','Low-stock limit','Status','Details'][index];
                tr.append(td);
            }
            target.append(tr);
        }
        if (!rows.length) {
            const tr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = fields.length;
            td.textContent = 'No records found.';
            tr.append(td);
            target.append(tr);
        }
    }
    function stockState(row){return [!row.isActive?'Inactive':'',row.currentStock===0?'Out of stock':row.isLowStock?'Low stock':'',row.hasDiscrepancy?'Mismatch: '+row.availableUnits+' physical units':''].filter(Boolean).join(' / ')||'In stock';}
    function productCell(row){const cell=document.createElement('div');cell.className='stock-product';let src=row.hasProductImage?'/api/products/'+row.productId+'/image':row.productImageUrl;if(src){try{const url=new URL(src,location.origin);if(!['http:','https:'].includes(url.protocol))src=null;}catch{src=null;}}
        const placeholder=()=>{const n=document.createElement('span');n.className='stock-product-placeholder';const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'),use=document.createElementNS(svg.namespaceURI,'use');svg.setAttribute('class','sf-icon');svg.setAttribute('aria-hidden','true');use.setAttribute('href','/images/icons.svg#box');svg.append(use);n.append(svg);return n;};
        if(src){const img=document.createElement('img');img.src=src;img.alt='';img.loading='lazy';img.onerror=()=>img.replaceWith(placeholder());cell.append(img);}else cell.append(placeholder());
        const description=document.createElement('div'),name=document.createElement('strong'),variant=document.createElement('small');const split=row.productName.lastIndexOf(' / ');name.textContent=split<0?row.productName:row.productName.slice(0,split);variant.textContent=[row.variantName||(split<0?'':row.productName.slice(split+3)),StockFlowVariantIdentity.summary(row),StockFlowVariantIdentity.unit(row)].filter(Boolean).join(' · ');description.append(name,variant);cell.append(description);return cell;}
    document.querySelectorAll('.stock-current th').forEach((th,index)=>{const key=['productName','category','code','currentStock','minimumStockLevel','status',''][index];if(!key)return;const button=document.createElement('button');button.type='button';button.className='stock-sort';button.textContent=th.textContent+' \u2195';th.replaceChildren(button);button.onclick=()=>{balanceAscending=balanceSort===key?!balanceAscending:true;balanceSort=key;document.querySelectorAll('.stock-current th').forEach(n=>n.removeAttribute('aria-sort'));th.setAttribute('aria-sort',balanceAscending?'ascending':'descending');renderBalances();};if(index===0)th.setAttribute('aria-sort','ascending');});
    async function balances() {
        const version = ++balanceVersion;
        const rows = await requestJson(
            '?' +
                new URLSearchParams({
                    term: getElement('inventory-search').value,
                    lowStockOnly: getElement('low-stock').checked,
                    includeInactive: getElement('inactive').checked
                })
        );
        if (version !== balanceVersion) {
            return;
        }
        balanceRows = rows;
        renderBalances();
    }
    function renderBalances() {
        const rows = [...balanceRows].sort((a,b)=>{const av=balanceSort==='category'?a.categoryName+' / '+(a.brandName||''):balanceSort==='code'?[a.sku,a.barcode].filter(Boolean).join(' / '):balanceSort==='status'?stockState(a):a[balanceSort],bv=balanceSort==='category'?b.categoryName+' / '+(b.brandName||''):balanceSort==='code'?[b.sku,b.barcode].filter(Boolean).join(' / '):balanceSort==='status'?stockState(b):b[balanceSort];return (typeof av==='number'?av-bv:String(av||'').localeCompare(String(bv||'')))*(balanceAscending?1:-1);});
        getElement('inventory-summary').textContent = rows.length+' items found';
        for(const [id,value]of [['items',rows.length],['units',rows.reduce((n,r)=>n+Number(r.currentStock),0)],['low',rows.filter(r=>r.isLowStock).length],['out',rows.filter(r=>r.currentStock===0).length]])getElement('stock-total-'+id).textContent=value.toLocaleString('en-IN');
        table('balances', rows, [
            (product) => {
                return productCell(product);
            },
            (value) => {
                return [value.categoryName, value.brandName].filter(Boolean).join(' / ');
            },
            (value) => {
                return [value.sku, value.barcode].filter(Boolean).join(' / ');
            },
            (value) => {
                return value.currentStock;
            },
            (value) => {
                return value.minimumStockLevel;
            },
            (value) => {
                const span=document.createElement('span');span.className='stock-status '+(!value.isActive?'inactive':value.hasDiscrepancy?'mismatch':value.currentStock===0?'out':value.isLowStock?'low':'');span.textContent=stockState(value);return span;
            },
            (variant) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = 'View';
                button.addEventListener('click', () => {
                    return runAction(async () => {
                        getElement('inventory-details').hidden = false;
                        getElement('inventory-unit-section').hidden = !variant.isSerialTracked;
                        productId = String(variant.productId);
                        variantId = String(variant.variantId);
                        historyOffset = unitsOffset = 0;
                        getElement('detail-title').textContent =
                            'Stock change history — ' + variant.productName;
                        await Promise.all([history(), units()]);
                        getElement('inventory-details').scrollIntoView({
                            behavior: 'smooth'
                        });
                    });
                });
                return button;
            }
        ]);
    }
    async function history(pagingRequest) {
        const version = ++historyVersion;
        const query = new URLSearchParams({
            offset: historyOffset
        });
        if (productId) {
            query.set('productId', productId);
        }
        if (variantId) {
            query.set('variantId', variantId);
        }
        if (getElement('history-from').value) {
            query.set('from', getElement('history-from').value);
        }
        if (getElement('history-to').value) {
            query.set('to', getElement('history-to').value);
        }
        const response = await requestJson('/history?' + query);
        const rows=historyPager.capture(response,pagingRequest);if(!rows)return;
        if (version !== historyVersion) {
            return;
        }
        table('history', rows, [
            (record) => {
                return new Date(record.createdAt).toLocaleString();
            },
            (product) => {
                return [product.productName, product.serialNumber].filter(Boolean).join(' / ');
            },
            (value) => {
                return StockFlowText.label(value.transactionType);
            },
            (value) => {
                return (
                    ([
                        'PURCHASE',
                        'CUSTOMER_RETURN',
                        'ADJUSTMENT_IN',
                        'SERVICE_IN',
                        'TRANSFER_IN'
                    ].includes(value.transactionType)
                        ? '+'
                        : '−') + value.quantity
                );
            },
            (value) => {
                const text = [value.referenceType, value.referenceId]
                    .filter((value2) => {
                        return value2 != null;
                    })
                    .join(' #');
                if (
                    value.referenceType === 'TRANSFER' &&
                    getElement('inventory-workspace').dataset.canTransfers === 'true'
                ) {
                    const link = document.createElement('a');
                    link.href = '/Transfers?id=' + value.referenceId;
                    link.textContent = text;
                    return link;
                }
                return text;
            },
            (value) => {
                return [value.remarks, value.createdByName].filter(Boolean).join(' / ');
            }
        ]);
        getElement('history-prev').disabled = historyOffset === 0;
        getElement('history-next').disabled = rows.length < 100;
    }
    async function units(pagingRequest) {
        const version = ++unitVersion;
        const query = new URLSearchParams({
            offset: unitsOffset,
            term: getElement('unit-search').value,
            availableOnly: getElement('available-only').checked
        });
        if (productId) {
            query.set('productId', productId);
        }
        if (variantId) {
            query.set('variantId', variantId);
        }
        const response = await requestJson('/units?' + query);
        const rows=unitPager.capture(response,pagingRequest);if(!rows)return;
        if (version !== unitVersion) {
            return;
        }
        table('units', rows, [
            (unit) => {
                return unit.productUnitId;
            },
            (product) => {
                return product.productName;
            },
            (value) => {
                return value.serialNumber;
            },
            (value) => {
                return value.barcode;
            },
            (value) => {
                return StockFlowText.label(value.unitStatus);
            }
        ]);
        getElement('units-prev').disabled = unitsOffset === 0;
        getElement('units-next').disabled = rows.length < 100;
    }
    async function runAction(action) {
        getElement('inventory-message').textContent = '';
        try {
            await action();
        } catch (error) {
            getElement('inventory-message').textContent = error.message;
        }
    }
    getElement('show-history').addEventListener('click', () => {
        getElement('inventory-details').hidden = false;
        getElement('inventory-unit-section').hidden = false;
        productId = variantId = '';
        historyOffset = unitsOffset = 0;
        getElement('detail-title').textContent = 'Stock change history';
        runAction(async () => {
            await Promise.all([history(), units()]);
        });
    });
    getElement('balance-filter').addEventListener('submit', (event) => {
        event.preventDefault();
        runAction(balances);
    });
    getElement('history-filter').addEventListener('submit', (event) => {
        event.preventDefault();
        historyOffset = 0;
        runAction(history);
    });
    getElement('unit-filter').addEventListener('submit', (event) => {
        event.preventDefault();
        unitsOffset = 0;
        runAction(units);
    });
    getElement('all-products').addEventListener('click', () => {
        return runAction(async () => {
            getElement('inventory-unit-section').hidden = false;
            productId = variantId = '';
            historyOffset = unitsOffset = 0;
            getElement('detail-title').textContent = 'Stock change history';
            await Promise.all([history(), units()]);
        });
    });
    for (const kind of ['history', 'units']) {
        for (const direction of ['prev', 'next']) {
            getElement(`${kind}-${direction}`).addEventListener('click', () => {
                return runAction(async () => {
                    const change = direction === 'next' ? 100 : -100;
                    if (kind === 'history') {
                        historyOffset = Math.max(0, historyOffset + change);
                        await history();
                    } else {
                        unitsOffset = Math.max(0, unitsOffset + change);
                        await units();
                    }
                });
            });
        }
    }
    function uuid() {
        if (typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        const value = crypto.getRandomValues(new Uint8Array(16));
        value[6] = (value[6] & 15) | 64;
        value[8] = (value[8] & 63) | 128;
        const h = [...value]
            .map((value2) => {
                return value2.toString(16).padStart(2, '0');
            })
            .join('');
        return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
    }
    function freeze() {
        if (!getElement('adjustment-editor')) {
            return;
        }
        getElement('adjustment-editor').disabled =
            posting || !!pending || blocked || window.StockFlowLocation <= 0;
        getElement('adjustment-pending').hidden = !pending;
        getElement('adjustment-retry').disabled = posting;
    }
    async function adjustmentFields() {
        const selected = products.find((variant) => {
            return String(variant.variantId) === getElement('adjust-product').value;
        });
        const incoming = getElement('adjust-type').value === 'ADJUSTMENT_IN';
        const tracked = selected?.isSerialTracked;
        getElement('serial-fields').hidden = !tracked || !incoming;
        getElement('unit-fields').hidden = !tracked || incoming;
        getElement('adjust-quantity').readOnly = tracked && !incoming;
        getElement('adjust-units').replaceChildren();
        adjustmentUnits = [];
        if (tracked && !incoming) {
            for (let offset = 0; ; offset += 100) {
                const batch = await requestJson(
                    '/units?' +
                        new URLSearchParams({
                            variantId: selected.variantId,
                            availableOnly: true,
                            offset
                        })
                );
                adjustmentUnits.push(...batch);
                if (batch.length < 100) {
                    break;
                }
            }
            for (const unit of adjustmentUnits) {
                const label = document.createElement('label');
                label.className = 'd-block';
                const check = document.createElement('input');
                check.type = 'checkbox';
                check.value = unit.productUnitId;
                check.addEventListener('change', () => {
                    return (getElement('adjust-quantity').value =
                        getElement('adjust-units').querySelectorAll('input:checked').length);
                });
                label.append(check, document.createTextNode(' ' + unit.serialNumber));
                getElement('adjust-units').append(label);
            }
            getElement('adjust-quantity').value = 0;
        }
    }
    async function postAdjustment() {
        if (posting || !pending) {
            return;
        }
        posting = true;
        freeze();
        getElement('adjustment-message').textContent = 'Posting…';
        try {
            const receipt = await requestJson('/adjustments', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                },
                body: JSON.stringify(pending.body)
            });
            if (!receipt?.adjustmentId) {
                throw new Error('The adjustment result is incomplete. Retry the saved adjustment.');
            }
            sessionStorage.removeItem(storageKey);
            pending = null;
            getElement('adjustment-message').textContent =
                `Adjustment #${receipt.adjustmentId} posted.`;
            getElement('adjust-reason').value = getElement('adjust-serials').value = '';
            await balances();
            if (!getElement('inventory-details').hidden) {
                await Promise.all([history(), units()]);
            }
            await adjustmentFields();
        } catch (error) {
            if ([400, 422].includes(error.status)) {
                sessionStorage.removeItem(storageKey);
                pending = null;
            }
            getElement('adjustment-message').textContent = error.message;
        } finally {
            posting = false;
            freeze();
        }
    }
    if (getElement('adjustment-form')) {
        let changing = false;
        const fields = async () => {
            if (changing || pending) {
                return;
            }
            changing = true;
            getElement('adjustment-editor').disabled = true;
            try {
                await adjustmentFields();
            } catch (error) {
                getElement('adjustment-message').textContent = error.message;
            } finally {
                changing = false;
                freeze();
            }
        };
        getElement('adjust-product').addEventListener('change', fields);
        getElement('adjust-type').addEventListener('change', fields);
        StockFlowScanner.bind(
            'adjust-unit-scan',
            (code) => {
                const unit = adjustmentUnits.find((value) => {
                    return (value.barcodes || [value.serialNumber, value.barcode]).some((value) => {
                        return value && value.trim().toLowerCase() === code.toLowerCase();
                    });
                });
                if (!unit) {
                    throw new Error('This unit is not available in the selected variant.');
                }
                const check = getElement('adjust-units').querySelector(
                    `input[value="${unit.productUnitId}"]`
                );
                if (check.checked) {
                    throw new Error('This serial is already selected.');
                }
                check.checked = true;
                getElement('adjust-quantity').value =
                    getElement('adjust-units').querySelectorAll('input:checked').length;
            },
            (error) => {
                return (getElement('adjustment-message').textContent = error.message);
            }
        );
        getElement('adjustment-retry').onclick = postAdjustment;
        getElement('adjustment-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            if (posting || pending || changing || blocked) {
                return;
            }
            const selected = products.find((variant) => {
                return variant.variantId === Number(getElement('adjust-product').value);
            });
            if (!selected) {
                return;
            }
            const body = {
                locationId: window.StockFlowLocation,
                requestId: uuid(),
                productId: selected.productId,
                variantId: selected.variantId,
                transactionType: getElement('adjust-type').value,
                quantity: Number(getElement('adjust-quantity').value),
                reason: getElement('adjust-reason').value.trim(),
                serialNumbers: getElement('serial-fields').hidden
                    ? []
                    : getElement('adjust-serials')
                          .value.split(/\r?\n/)
                          .map((text) => {
                              return text.trim();
                          })
                          .filter(Boolean),
                productUnitIds: getElement('unit-fields').hidden
                    ? []
                    : [...getElement('adjust-units').querySelectorAll('input:checked')].map(
                          (value) => {
                              return Number(value.value);
                          }
                      )
            };
            pending = {
                body
            };
            try {
                sessionStorage.setItem(storageKey, JSON.stringify(pending));
            } catch {
                pending = null;
                getElement('adjustment-message').textContent =
                    'Enable browser storage before posting stock adjustments.';
                return;
            }
            await postAdjustment();
        });
        getElement('adjustment-form').addEventListener('keydown', (event) => {
            if (event.key === 'Enter' && event.target.tagName === 'INPUT') {
                event.preventDefault();
            }
        });
    }
    historyPager=StockFlowPagination.adapt({host:getElement('history-prev').parentElement,root:getElement('history').closest('table'),legacy:{previous:'#history-prev',next:'#history-next'},getOffset:()=>historyOffset,setOffset:value=>historyOffset=value,load:history});history=historyPager.load;
    unitPager=StockFlowPagination.adapt({host:getElement('units-prev').parentElement,root:getElement('units').closest('table'),legacy:{previous:'#units-prev',next:'#units-next'},getOffset:()=>unitsOffset,setOffset:value=>unitsOffset=value,load:units});units=unitPager.load;
    runAction(async () => {
        if (getElement('adjust-product')) {
            try {
                pending = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
                if (pending && pending.body.locationId !== window.StockFlowLocation) {
                    throw new Error('The saved adjustment belongs to a different location.');
                }
                if (!pending && sessionStorage.getItem('stockflow-pending-adjustment')) {
                    throw new Error(
                        'An older stock correction needs to be checked by the administrator before you save another correction.'
                    );
                }
            } catch (value) {
                blocked = true;
                freeze();
                throw value;
            }
            products = await requestJson('');
            for (const variant of products) {
                getElement('adjust-product').add(
                    new Option(variant.productName, variant.variantId)
                );
            }
            if (pending) {
                getElement('adjustment-message').textContent =
                    'A saved adjustment awaits confirmation. Retry its exact request; do not enter the quantity again.';
            }
            freeze();
        }
        await balances();
    });
})();
