// Shared, accessible explanations for static forms and dynamically rendered fields.
window.StockFlowCaptionHelp = (() => {
    'use strict';
    const normalize = value => value.replace(/\s*\*\s*$/, '').replace(/\s+/g, ' ').trim().toLowerCase();
    const catalog = new Map();
    for (const [original, caption, help] of window.StockFlowCaptionCatalog || []) {
        if (help) {
            catalog.set(normalize(original), help);
            if (!catalog.has(normalize(caption))) catalog.set(normalize(caption), help);
        }
    }
    const states = new WeakMap();
    let sequence = 0;
    let active;
    let scheduled = false;
    const originalText = node => window.StockFlowLanguage?.originalText(node) || node.textContent;
    function close() {
        if (!active) return;
        if (active.tip.hidePopover && active.tip.matches(':popover-open')) active.tip.hidePopover();
        active.tip.hidden = true;
        active.button.setAttribute('aria-expanded', 'false');
        active.pinned = false;
        active = null;
    }
    function position(state) {
        const rect = state.button.getBoundingClientRect();
        const bounds = state.tip.getBoundingClientRect();
        const viewport = window.visualViewport;
        const left = viewport?.offsetLeft || 0;
        const top = viewport?.offsetTop || 0;
        const width = viewport?.width || innerWidth;
        const height = viewport?.height || innerHeight;
        const x = Math.max(left + 8, Math.min(rect.left, left + width - bounds.width - 8));
        const preferredY = rect.bottom + bounds.height + 8 < top + height
            ? rect.bottom + 6 : rect.top - bounds.height - 6;
        const y = Math.max(top + 8, Math.min(preferredY, top + height - bounds.height - 8));
        state.tip.style.left = `${x}px`;
        state.tip.style.top = `${y}px`;
    }
    function description(state) {
        const hints = state.hints.filter(node => node.isConnected && !node.hidden && !node.closest('[hidden]'));
        return [...new Set([state.help, ...hints.map(originalText)].filter(Boolean))].join(' ');
    }
    function show(state) {
        if (active !== state) close();
        state.tip.textContent = description(state);
        if (!state.tip.textContent) return;
        window.StockFlowLanguage?.refresh(state.tip);
        state.tip.hidden = false;
        if (state.tip.showPopover && !state.tip.matches(':popover-open')) state.tip.showPopover();
        active = state;
        state.button.setAttribute('aria-expanded', 'true');
        position(state);
    }
    function enhance(label) {
        if (label.closest('[data-no-translate], .sf-caption-help, .visually-hidden, svg')) return;
        const control = label.control;
        const text = originalText(label);
        const help = label.dataset.captionHelp || catalog.get(normalize(text));
        // Action buttons already have a clear caption; avoid an extra button beside every action.
        if (label.matches('button,summary')) {
            if (help) { label.title = help; label.setAttribute('aria-description', help); }
            return;
        }
        const previous = states.get(label);
        const host = previous?.host || label.parentElement;
        const hints = [];
        if (label.matches('h1,h2,h3,h4')) {
            const next = previous?.group?.nextElementSibling || label.nextElementSibling;
            if (next?.matches('p') && !next.matches('[role],.alert')
                && (next.matches('.text-muted') || label.closest('.sf-page-heading'))
                && !next.querySelector('a,button,input')) hints.push(next);
        }
        if (control) {
            for (const id of (control.getAttribute('aria-describedby') || '').split(/\s+/)) {
                const node = document.getElementById(id);
                if (node?.matches('.text-muted') && !node.matches('[aria-live],[role]')
                    && !/count|serial-help|next-step|message|error|status/i.test(id)) hints.push(node);
            }
            for (const node of host.querySelectorAll(':scope > small.text-muted, :scope > p.text-muted')) {
                if (!node.matches('[aria-live],[role]') && !/count|serial-help|next-step|message|error|status/i.test(node.id)) hints.push(node);
            }
        }
        if (!help && !hints.length) return;
        let state = states.get(label);
        if (!state || !state.wrapper.isConnected) {
            const wrapper = document.createElement('span');
            wrapper.className = 'sf-caption-help';
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'sf-caption-info';
            button.textContent = 'i';
            button.setAttribute('aria-label', 'Field information');
            button.setAttribute('aria-expanded', 'false');
            const tip = document.createElement('span');
            tip.id = `sf-caption-tip-${++sequence}`;
            tip.className = 'sf-caption-tooltip';
            tip.setAttribute('role', 'tooltip');
            tip.setAttribute('data-i18n', '');
            tip.setAttribute('popover', 'manual');
            tip.hidden = true;
            button.setAttribute('aria-describedby', tip.id);
            button.setAttribute('aria-controls', tip.id);
            wrapper.append(button, tip);
            const group = document.createElement(label.matches('h1,h2,h3,h4') ? 'div' : 'span');
            group.className = 'sf-caption-group';
            if (label.matches('h1,h2,h3,h4')) group.classList.add('sf-caption-heading-group');
            if (control && label.contains(control) && !['checkbox','radio'].includes(control.type))
                group.classList.add('sf-caption-field-group');
            label.before(group);
            group.append(label, wrapper);
            state = { wrapper, group, host, button, tip, help, hints, pinned: false };
            states.set(label, state);
            button.addEventListener('pointerenter', () => show(state));
            button.addEventListener('focus', () => show(state));
            button.addEventListener('click', event => {
                event.preventDefault();
                if (state.pinned) close();
                else { show(state); state.pinned = true; }
            });
            const leave = () => setTimeout(() => {
                if (active === state && !state.pinned && !wrapper.matches(':hover')
                    && !tip.matches(':hover') && document.activeElement !== button) close();
            }, 120);
            wrapper.addEventListener('pointerleave', leave);
            tip.addEventListener('pointerleave', leave);
            button.addEventListener('blur', () => { if (active === state) close(); });
            if (control) {
                const ids = new Set((control.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
                ids.add(tip.id);
                control.setAttribute('aria-describedby', [...ids].join(' '));
            }
        }
        state.help = help;
        state.button.setAttribute('aria-label', text.replace(/\s*\*\s*$/, '').trim() + ' information');
        state.hints = [...new Set(hints)];
        state.hints.forEach(node => node.classList.add('sf-caption-help-source'));
        const explanation = description(state);
        if (state.explanation !== explanation) {
            state.explanation = explanation;
            state.tip.textContent = explanation;
        }
    }
    function refresh(root = document) {
        const selector = 'label,button,summary,h1,h2,h3,h4,[data-caption-help]';
        if (root.matches?.(selector)) enhance(root);
        root.querySelectorAll(selector).forEach(enhance);
        if (active && (!active.button.isConnected || !active.button.getClientRects().length)) close();
    }
    function start() {
        refresh();
        new MutationObserver(records => {
            if (scheduled || records.every(record => (record.target.parentElement || record.target).closest?.('.sf-caption-help'))) return;
            scheduled = true;
            requestAnimationFrame(() => { scheduled = false; refresh(); });
        }).observe(document.body, { childList: true, subtree: true, characterData: true });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && active) {
                close();
                event.preventDefault();
                event.stopPropagation();
            }
        }, true);
        document.addEventListener('close', event => {
            if (active && event.target.contains(active.button)) close();
        }, true);
        document.addEventListener('pointerdown', event => {
            if (active && !active.wrapper.contains(event.target)) close();
        });
        document.addEventListener('scroll', () => { if (active) position(active); }, true);
        window.addEventListener('resize', () => { if (active) position(active); });
        window.visualViewport?.addEventListener('resize', () => { if (active) position(active); });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
    return { refresh, close };
})();
