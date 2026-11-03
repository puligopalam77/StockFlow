(() => {
    const id = Number(location.pathname.split('/').pop());
    const getElement = (name) => {
        return document.getElementById(name);
    };
    let warranty;
    let selected;
    let claimOffset = 0;
    let historyOffset = 0;
    const resolve = getElement('resolve-form');
    function selectClaim(claim) {
        selected = claim;
        resolve.reset();
        resolve.hidden = false;
        getElement('resolve-title').textContent =
            'Update ' + claim.claimNumber + ' (' + StockFlowText.label(claim.claimStatus) + ')';
        const statuses =
            claim.claimStatus === 'OPEN'
                ? ['UNDER_REVIEW', 'APPROVED', 'REJECTED', 'CLOSED']
                : claim.claimStatus === 'UNDER_REVIEW'
                  ? ['APPROVED', 'REJECTED', 'CLOSED']
                  : ['CLOSED', 'REJECTED', ...(warranty.productUnitId ? ['REPLACED'] : [])];
        getElement('claim-status').replaceChildren(
            ...statuses
                .filter((value) => {
                    return (
                        !['APPROVED', 'REPLACED'].includes(value) ||
                        resolve.dataset.approve === 'true'
                    );
                })
                .map((value) => {
                    return new Option(StockFlowText.label(value), value);
                })
        );
        updateFields();
        resolve.scrollIntoView({
            block: 'center'
        });
    }
    function updateFields() {
        const status = getElement('claim-status').value;
        getElement('replacement-fields').hidden = status !== 'REPLACED';
        getElement('resolution-type').disabled = status !== 'CLOSED';
        getElement('replacement-unit').required = status === 'REPLACED';
        getElement('replacement-rule').required = status === 'REPLACED';
        getElement('custom-end').required =
            status === 'REPLACED' && getElement('replacement-rule').value === 'CUSTOM';
        getElement('custom-end').disabled = !getElement('custom-end').required;
    }
    async function claims(append = false) {
        if (getElement('claim-results').dataset.view !== 'true') {
            return;
        }
        const items = await Warranty.api(
            '/api/warranty/claims?warrantyId=' + id + '&offset=' + claimOffset
        );
        Warranty.claims(
            items,
            (claim) => {
                if (
                    !resolve ||
                    !['OPEN', 'UNDER_REVIEW', 'APPROVED'].includes(claim.claimStatus) ||
                    warranty.warrantyStatus === 'CANCELLED'
                ) {
                    return Warranty.link(claim.warrantyId);
                }
                const button = document.createElement('button');
                button.className = 'btn btn-sm btn-secondary';
                button.textContent = 'Update';
                button.onclick = () => {
                    return selectClaim(claim);
                };
                return button;
            },
            append
        );
        getElement('more-claims').hidden = items.length < 100;
    }
    async function history(append = false) {
        const items = await Warranty.api(
            '/api/warranty/' + id + '/history?offset=' + historyOffset
        );
        Warranty.table(
            'history-results',
            ['Date', 'Action', 'Previous status', 'New status', 'Notes', 'User'],
            items.map((record) => {
                return [
                    record.createdAt.replace('T', ' '),
                    StockFlowText.label(record.actionName),
                    StockFlowText.label(record.oldStatus),
                    StockFlowText.label(record.newStatus),
                    record.remarks,
                    record.createdByName
                ];
            }),
            append
        );
        getElement('more-history').hidden = items.length < 100;
    }
    async function load() {
        warranty = await Warranty.api('/api/warranty/' + id);
        Warranty.results([warranty]);
        const info = document.createElement('p');
        info.textContent =
            'Coverage: ' +
            StockFlowText.label(warranty.warrantyType || 'Unspecified') +
            ' / ' +
            StockFlowText.label(warranty.warrantyProvider || 'Unspecified');
        getElement('warranty-results').append(info);
        for (const [key, text] of [
            ['parentWarrantyId', 'Original warranty'],
            ['replacementWarrantyId', 'Replacement warranty']
        ]) {
            if (warranty[key]) {
                getElement('warranty-results').append(
                    Warranty.link(warranty[key], text),
                    document.createTextNode(' ')
                );
            }
        }
        if (warranty.replacementWarrantyRule) {
            const rule = document.createElement('p');
            rule.textContent =
                'Replacement rule: ' + StockFlowText.label(warranty.replacementWarrantyRule);
            getElement('warranty-results').append(rule);
        }
        if (getElement('create-claim')) {
            getElement('create-claim').hidden = !warranty.canCreateClaim;
        }
        if (getElement('coverage-section')) {
            getElement('coverage-section').hidden = ['REPLACED', 'CANCELLED'].includes(
                warranty.warrantyStatus
            );
            getElement('coverage-type').value = warranty.warrantyType || '';
            getElement('coverage-provider').value = warranty.warrantyProvider || '';
        }
        claimOffset = 0;
        historyOffset = 0;
        await claims();
        await history();
    }
    if (resolve) {
        getElement('claim-status').onchange = updateFields;
        getElement('replacement-rule').onchange = updateFields;
        getElement('find-units').onclick = () => {
            return Warranty.run(async () => {
                const units = await Warranty.api(
                    '/api/inventory/units?availableOnly=true&productId=' +
                        warranty.productId +
                        '&term=' +
                        encodeURIComponent(getElement('unit-search').value)
                );
                getElement('replacement-unit').replaceChildren(
                    new Option('Select a unit', ''),
                    ...units.map((unit) => {
                        return new Option(
                            unit.serialNumber || unit.barcode || unit.productUnitId,
                            unit.productUnitId
                        );
                    })
                );
            });
        };
        Warranty.mutation(resolve, async (retry) => {
            const status = getElement('claim-status').value;
            const replacing = status === 'REPLACED';
            const payload = retry({
                expectedStatus: selected.claimStatus,
                claimStatus: status,
                resolutionType: replacing
                    ? 'REPLACEMENT'
                    : status === 'REJECTED'
                      ? 'REJECTED'
                      : status === 'CLOSED'
                        ? getElement('resolution-type').value
                        : null,
                resolutionNotes: getElement('resolution-notes').value,
                replacementProductUnitId: replacing
                    ? Number(getElement('replacement-unit').value)
                    : null,
                replacementWarrantyRule: replacing ? getElement('replacement-rule').value : null,
                customWarrantyEndDate:
                    replacing && getElement('replacement-rule').value === 'CUSTOM'
                        ? getElement('custom-end').value
                        : null
            });
            await Warranty.api('/api/warranty/claims/' + selected.warrantyClaimId, {
                method: 'PUT',
                body: JSON.stringify(payload)
            });
            resolve.hidden = true;
            await load();
        });
    }
    if (getElement('create-claim')) {
        Warranty.mutation(getElement('create-claim'), async (retry) => {
            await Warranty.api('/api/warranty/claims', {
                method: 'POST',
                body: JSON.stringify(
                    retry({
                        warrantyId: id,
                        complaint: getElement('complaint').value
                    })
                )
            });
            getElement('create-claim').reset();
            await load();
        });
    }
    if (getElement('coverage-form')) {
        Warranty.mutation(getElement('coverage-form'), async (retry) => {
            await Warranty.api('/api/warranty/' + id + '/coverage', {
                method: 'PUT',
                body: JSON.stringify(
                    retry({
                        warrantyType: getElement('coverage-type').value || null,
                        warrantyProvider: getElement('coverage-provider').value || null,
                        reason: getElement('coverage-reason').value
                    })
                )
            });
            await load();
        });
    }
    getElement('more-claims').onclick = () => {
        return Warranty.run(async () => {
            claimOffset += 100;
            await claims(true);
        });
    };
    getElement('more-history').onclick = () => {
        return Warranty.run(async () => {
            historyOffset += 100;
            await history(true);
        });
    };
    Warranty.run(load);
})();
