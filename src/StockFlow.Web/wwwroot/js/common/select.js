// Selection UI only: native controls remain the submission and validation source.
window.StockFlowSelect = window.StockFlowSelect || (() => {
    'use strict';
    const instances = new Set();
    let serial = 0, opened;
    const normalize = value => String(value || '').normalize('NFKD').replace(/[\u0300-\u036f]/g, '').trim().toLowerCase().replace(/\s+/g, ' ');
    const element = (tag, className, text) => {
        const result = document.createElement(tag);
        if (className) result.className = className;
        if (text != null) result.textContent = text;
        return result;
    };
    function create(source, settings = {}) {
        source.sfSelect?.destroy();
        const address = settings.address === true, lifecycle = new AbortController();
        const id = source.id || (source.id = 'sf-select-' + ++serial);
        const labels = [...(source.labels || [])];
        const label = settings.label || source.getAttribute('aria-label') || labels.map(l => {
            const copy = l.cloneNode(true); copy.querySelectorAll('input,select,textarea,button,.sf-select').forEach(n => n.remove());
            return copy.textContent.trim();
        }).join(' ') || source.name || 'Selection';
        const fieldName = label.replace(/\s*\(Optional\)\s*/ig, '').replace(/\s*\*\s*$/, '').trim();
        const emptyOption = source instanceof HTMLSelectElement ? [...source.options].find(o => o.value === '')?.textContent?.trim() : '';
        // A filter's "All ..." and other meaningful null choices remain meaningful.
        // "Not specified" belongs in the popup, rather than looking like a chosen entity.
        const meaningfulEmpty = emptyOption && !/^(?:not specified|none|n\/a|[-—–]+)$/i.test(emptyOption);
        const placeholder = settings.placeholder || source.dataset.placeholder || (meaningfulEmpty ? emptyOption : 'Select ' + fieldName);
        const original = { hidden: source.hidden, tabIndex: source.getAttribute('tabindex'), label: source.getAttribute('aria-label'), focus: source.focus, value: Object.getOwnPropertyDescriptor(source, 'value') };
        if (!original.label) source.setAttribute('aria-label', label);
        const host = element('div', 'sf-select' + (address ? ' sf-address-picker' : '') + (settings.hostClass ? ' ' + settings.hostClass : ''));
        source.before(host); host.append(source); source.hidden = true; source.tabIndex = -1;
        const input = element(settings.button ? 'button' : 'input', 'form-control sf-select-trigger' + (address ? ' sf-address-search' : '') + (settings.triggerClass ? ' ' + settings.triggerClass : ''));
        input.type = settings.button ? 'button' : 'text'; input.id = settings.triggerId || id + '-search'; input.autocomplete = 'off';
        input.dataset.sfAddressTransient = 'true'; input.dataset.sfAddressUi = 'true';
        input.setAttribute('role', 'combobox'); input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-haspopup', 'listbox'); input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-label', label); input.placeholder = placeholder;
        const labelStates = labels.map(l => ({ label: l, htmlFor: l.htmlFor }));
        for (const l of labels) if (l.htmlFor === id) l.htmlFor = input.id;
        const panel = element('div', 'sf-select-panel' + (settings.panelClass ? ' ' + settings.panelClass : ''));
        panel.id = id + '-panel'; panel.hidden = true;
        if (typeof panel.showPopover === 'function') panel.setAttribute('popover', 'manual');
        const search = element('input', 'form-control sf-select-query');
        search.type = 'search'; search.id = id + '-query'; search.autocomplete = 'off'; search.placeholder = 'Search ' + fieldName;
        search.setAttribute('aria-label', search.placeholder);
        search.setAttribute('role', 'combobox'); search.setAttribute('aria-autocomplete', 'list'); search.setAttribute('aria-expanded', 'false');
        search.dataset.sfAddressTransient = 'true'; search.dataset.sfAddressUi = 'true';
        const list = element('div', 'sf-select-options' + (address ? ' sf-address-options' : '') + (settings.resultsClass ? ' ' + settings.resultsClass : ''));
        list.id = (address ? input.id : id) + '-options'; list.setAttribute('role', 'listbox'); list.setAttribute('aria-label', label);
        input.setAttribute('aria-controls', list.id); search.setAttribute('aria-controls', list.id);
        const status = element('div', 'sf-select-status');
        status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
        panel.append(search, list, status);
        if (settings.addAction) {
            const footer = element('div', 'sf-select-footer'), add = element('button', 'btn btn-sm btn-outline-secondary', settings.addLabel || 'Add New');
            add.type = 'button'; if (settings.addId) add.id = settings.addId;
            add.onclick = () => { close(); settings.addAction(); }; footer.append(add); panel.append(footer);
        }
        host.append(input, panel);
        let options = [], activeIndex = -1, destroyed = false, lastQuery = '';
        const values = () => {
            const rows = (settings.values ? settings.values() : [...source.options].filter(o => !o.hidden).map(o => ({
                value: o.value, label: o.textContent, disabled: o.disabled || o.parentElement?.disabled,
                aliases: o.dataset.search || '', group: o.parentElement?.tagName === 'OPTGROUP' ? o.parentElement.label : ''
            }))).map(o => ({ ...o, value: String(o.value ?? ''), label: String(o.label ?? '') }));
            if (settings.values && !source.required && !rows.some(o => o.value === '')) rows.unshift({ value: '', label: 'Not specified' });
            return rows;
        };
        const locked = () => source.disabled || source.readOnly || source.hasAttribute('readonly');
        function refresh() {
            if (destroyed) return;
            const selected = values().find(o => o.value === String(source.value));
            if (panel.hidden) {
                const caption = source.value ? selected?.label || (source instanceof HTMLSelectElement ? source.selectedOptions[0]?.textContent : source.value) || '' : '';
                if (settings.button) { const text = caption || input.placeholder; if (input.textContent !== text) input.textContent = text; } else input.value = caption;
            }
            if (input.disabled !== source.disabled) input.disabled = source.disabled;
            if (input.readOnly !== locked()) input.readOnly = locked();
            if (input.getAttribute('aria-disabled') !== String(locked())) input.setAttribute('aria-disabled', String(locked()));
            if (input.getAttribute('aria-required') !== String(source.required)) input.setAttribute('aria-required', String(source.required));
            for (const name of ['aria-invalid', 'aria-describedby']) {
                const value = source.getAttribute(name);
                if (input.getAttribute(name) !== value) { if (value) input.setAttribute(name, value); else input.removeAttribute(name); }
            }
            input.classList.toggle('is-invalid', source.classList.contains('is-invalid') || source.getAttribute('aria-invalid') === 'true');
            if (locked() && !panel.hidden) close();
        }
        function position() {
            const viewport = window.visualViewport, left = viewport?.offsetLeft || 0, top = viewport?.offsetTop || 0;
            const width = viewport?.width || innerWidth, height = viewport?.height || innerHeight;
            const box = input.getBoundingClientRect(), panelWidth = Math.min(Math.max(box.width, Math.min(260, width - 16)), width - 16);
            const below = top + height - box.bottom - 8, above = box.top - top - 8;
            const flip = below < 220 && above > below, room = Math.max(72, flip ? above : below);
            const panelHeight = Math.min(330, room);
            panel.style.width = panelWidth + 'px';
            panel.style.left = Math.max(left + 8, Math.min(box.left, left + width - panelWidth - 8)) + 'px';
            panel.style.maxHeight = panelHeight + 'px';
            panel.style.top = (flip ? Math.max(top + 8, box.top - Math.min(panel.scrollHeight, panelHeight) - 4) : Math.max(top + 8, Math.min(box.bottom + 4, top + height - 80))) + 'px';
        }
        function highlight(index, scroll = true) {
            const buttons = [...list.querySelectorAll('[role=option]')];
            if (!buttons.length) return;
            activeIndex = Math.max(0, Math.min(index, buttons.length - 1));
            buttons.forEach((button, i) => button.classList.toggle('sf-select-active', i === activeIndex));
            const option = buttons[activeIndex];
            input.setAttribute('aria-activedescendant', option.id); search.setAttribute('aria-activedescendant', option.id);
            if (scroll) option.scrollIntoView({ block: 'nearest' });
        }
        function render(query = '') {
            lastQuery = query; list.replaceChildren(); activeIndex = -1;
            const term = normalize(query);
            options = values().filter(o => (!source.required || o.value !== '') && (!term || normalize([o.label, o.value, o.aliases, o.group].join(' ')).includes(term)));
            // This is a local selector, never a replacement for paginated entity search.
            const visible = options.slice(0, 150);
            for (const [index, option] of visible.entries()) {
                const button = element('button', 'sf-select-option', option.group ? option.group + ' → ' + option.label : option.label);
                button.type = 'button'; button.tabIndex = -1; button.id = id + '-option-' + index;
                button.dataset.value = option.value;
                button.setAttribute('role', 'option'); button.setAttribute('aria-selected', String(option.value === String(source.value)));
                button.setAttribute('aria-label', button.textContent);
                button.disabled = !!option.disabled; button.setAttribute('aria-disabled', String(!!option.disabled));
                button.onmousedown = event => event.preventDefault();
                button.onclick = () => select(option);
                button.onpointermove = () => { if (!button.disabled) highlight(index, false); };
                list.append(button);
            }
            if (!visible.length) list.append(element('p', 'sf-select-empty', 'No matching results. Try a different name or code.'));
            status.textContent = options.length > visible.length ? 'Showing first 150 matches. Type to narrow results.' : '';
            input.removeAttribute('aria-activedescendant'); search.removeAttribute('aria-activedescendant');
            const selectedIndex = visible.findIndex(o => o.value === String(source.value) && !o.disabled);
            if (selectedIndex >= 0) highlight(selectedIndex, false);
        }
        function close(focus = false) {
            if (panel.matches(':popover-open')) panel.hidePopover();
            panel.hidden = true; input.setAttribute('aria-expanded', 'false'); search.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant'); search.removeAttribute('aria-activedescendant');
            search.value = ''; lastQuery = ''; if (opened === api) opened = null;
            refresh(); if (focus && input.isConnected && !input.disabled) input.focus();
        }
        function open(focusSearch = true) {
            if (locked()) return;
            if (opened && opened !== api) opened.close();
            opened = api; search.value = ''; render();
            panel.hidden = false; input.setAttribute('aria-expanded', 'true'); search.setAttribute('aria-expanded', 'true'); position();
            if (panel.showPopover && !panel.matches(':popover-open')) panel.showPopover();
            position(); list.querySelector('[aria-selected=true]')?.scrollIntoView({ block: 'nearest' });
            if (focusSearch) search.focus({ preventScroll: true });
        }
        function select(option) {
            if (locked() || option.disabled || (source.required && !option.value)) return;
            if (source instanceof HTMLSelectElement && ![...source.options].some(o => o.value === option.value)) source.add(new Option(option.label, option.value));
            const changed = source.value !== option.value;
            source.value = option.value; close(true);
            if (changed || settings.notifyReselect) {
                source.dispatchEvent(new Event('input', { bubbles: true }));
                source.dispatchEvent(new Event('change', { bubbles: true }));
                settings.onSelect?.(option);
            }
        }
        const enabledIndexes = () => [...list.querySelectorAll('[role=option]')].flatMap((b, i) => b.disabled ? [] : [i]);
        function navigate(event) {
            if (event.isComposing || event.keyCode === 229) return;
            if (event.key === 'Escape' && !panel.hidden) { event.preventDefault(); event.stopPropagation(); close(true); return; }
            if (event.key === 'Tab') { close(); return; }
            if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
                if (panel.hidden && ['Home', 'End'].includes(event.key)) return;
                event.preventDefault(); if (panel.hidden) open(false);
                const enabled = enabledIndexes(), current = enabled.indexOf(activeIndex);
                const next = event.key === 'Home' ? 0 : event.key === 'End' ? enabled.length - 1 : current < 0 ? (event.key === 'ArrowDown' ? 0 : enabled.length - 1) : (current + (event.key === 'ArrowDown' ? 1 : -1) + enabled.length) % enabled.length;
                if (enabled.length) highlight(enabled[next]);
            } else if (event.key === 'Enter' || (event.key === ' ' && event.target === input && panel.hidden)) {
                event.preventDefault();
                if (panel.hidden) open(); else {
                    const button = list.querySelectorAll('[role=option]')[activeIndex];
                    if (button && !button.disabled) button.click();
                    else if (settings.allowFree?.() && lastQuery.trim()) select({ value: lastQuery.trim(), label: lastQuery.trim() });
                }
            }
        }
        input.addEventListener('keydown', navigate); search.addEventListener('keydown', navigate);
        input.addEventListener('click', () => { if (panel.hidden) open(); });
        input.addEventListener('input', () => { const query = input.value; if (panel.hidden) open(false); search.value = query; render(query); position(); });
        search.addEventListener('input', () => { render(search.value); position(); });
        input.addEventListener('change', () => { if (settings.allowFree?.()) select({ value: input.value.trim(), label: input.value.trim() }); });
        search.addEventListener('change', () => { if (settings.allowFree?.()) select({ value: search.value.trim(), label: search.value.trim() }); });
        host.addEventListener('focusout', () => queueMicrotask(() => { if (!host.contains(document.activeElement)) close(); }));
        document.addEventListener('pointerdown', event => { if (!panel.hidden && !host.contains(event.target)) close(); }, { signal: lifecycle.signal });
        const reposition = () => { if (!panel.hidden) position(); };
        window.addEventListener('resize', reposition, { signal: lifecycle.signal });
        window.visualViewport?.addEventListener('resize', reposition, { signal: lifecycle.signal });
        document.addEventListener('scroll', reposition, { capture: true, signal: lifecycle.signal });
        source.addEventListener('change', refresh, { signal: lifecycle.signal });
        source.addEventListener('invalid', event => { event.preventDefault(); window.StockFlowValidation?.showField(source, source.validationMessage); input.focus(); }, { signal: lifecycle.signal });
        const nativeValue = Object.getOwnPropertyDescriptor(source instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype, 'value');
        const descriptor = original.value || nativeValue;
        Object.defineProperty(source, 'value', { configurable: true, get() { return descriptor.get.call(this); }, set(value) { descriptor.set.call(this, value); refresh(); } });
        const observer = new MutationObserver(() => { refresh(); if (!panel.hidden) { render(lastQuery); position(); } });
        observer.observe(source, { subtree: true, attributes: true, childList: true, characterData: true });
        // Native reset also resets the unnamed search inputs after dispatching reset.
        source.form?.addEventListener('reset', () => setTimeout(() => { if (!destroyed) { close(); refresh(); } }, 0), { signal: lifecycle.signal });
        source.focus = options => input.focus(options);
        const api = { source, input, search, wrapper: host, panel, list, refresh, open, close,
            destroy() {
                if (destroyed) return; close(); destroyed = true; lifecycle.abort(); observer.disconnect();
                source.hidden = original.hidden;
                if (original.tabIndex == null) source.removeAttribute('tabindex'); else source.setAttribute('tabindex', original.tabIndex);
                source.focus = original.focus;
                if (original.label == null) source.removeAttribute('aria-label'); else source.setAttribute('aria-label', original.label);
                if (original.value) Object.defineProperty(source, 'value', original.value); else delete source.value;
                for (const entry of labelStates) if (entry.label.htmlFor === input.id) entry.label.htmlFor = entry.htmlFor;
                if (host.parentNode) { host.before(source); host.remove(); }
                delete source.sfSelect; if (source.sfAddressPicker === api) delete source.sfAddressPicker;
                instances.delete(api);
            }
        };
        source.sfSelect = api; if (address) source.sfAddressPicker = api; instances.add(api); refresh();
        return api;
    }
    function native(source) { if (!source.classList.contains('sf-standard-select')) source.classList.add('sf-standard-select'); }
    function scan(root = document) {
        for (const api of instances) if (!api.source.isConnected) api.destroy();
        for (const source of root.querySelectorAll('select[data-sf-select]')) {
            if (source.sfSelect || source.closest('.product-category-select') || source.sfAddressPicker) continue;
            native(source);
            if (source.dataset.sfSelect === 'native' || source.multiple || source.size > 1 || source.hidden) continue;
            if (source.dataset.sfSelect === 'search' || source.dataset.sfSelect === 'auto') create(source);
        }
    }
    let queued = false;
    const schedule = () => { if (!queued) { queued = true; queueMicrotask(() => { queued = false; scan(); }); } };
    document.addEventListener('DOMContentLoaded', () => {
        scan();
        new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
    });
    return { create, native, scan, normalize, closeAll() { opened?.close(); } };
})();
