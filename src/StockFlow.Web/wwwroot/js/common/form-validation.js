// Shared field validation for static forms and fields added by JavaScript.
window.StockFlowValidation = (() => {
    'use strict';

    let nextId = 0;
    const shownForms = new WeakSet();

    function fieldLabel(input) {
        const label = input.labels?.[0];
        // Wrapped labels also contain the input and its feedback. Read their text only.
        const copy = label?.cloneNode(true);
        copy?.querySelectorAll('input, select, textarea, .sf-field-validation, .sf-address-options').forEach((node) => {
            node.remove();
        });
        return (window.StockFlowLanguage?.originalText(label) || copy?.textContent || input.getAttribute('aria-label') || input.name || 'This field')
            .replace(/\s*\*\s*$/, '').trim();
    }

    function prepareField(input) {
        // Disable saved form-entry suggestions on every screen, including fields
        // created inside dialogs after the page loads. Keep password/OTP semantics
        // used by account security and password confirmation validation.
        if (input instanceof HTMLFormElement) {
            input.autocomplete = 'off';
            return;
        }
        if (input.matches('input, select, textarea')) {
            if (!['current-password', 'new-password', 'one-time-code'].includes(input.autocomplete)) {
                input.autocomplete = 'off';
            }
            if (input.form) input.form.autocomplete = 'off';
        }
        if (!(input instanceof HTMLInputElement)) {
            return;
        }
        const identifier = input.name || input.id;
        if (/(?:gst(?:number)?|gstin)$/i.test(identifier) && !input.readOnly) {
            input.maxLength = 15;
            input.pattern = '[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]';
            input.title = 'Enter a valid 15-character GSTIN.';
        }
        if (/(?:mobile(?:number)?|phone(?:number)?)$/i.test(identifier) && !input.readOnly) {
            input.type = 'tel';
            input.inputMode = 'numeric';
            input.maxLength = 10;
            input.pattern = '[0-9]{10}';
            input.title = 'Enter exactly 10 digits, without spaces or a country code.';
        }
    }

    function fieldMessage(input) {
        prepareField(input);
        if (!input.willValidate) {
            return '';
        }
        const label = fieldLabel(input);
        function message(kind, english, limit = '') {
            return window.StockFlowLanguage?.fieldError(kind, { label, limit }, undefined, true, english) || english;
        }
        const value = input.value;
        const text = input.type === 'password' ? value : value.trim();
        if (input.validity.valueMissing || (input.required && !value.trim())) {
            return message('required', `${label} is required.`);
        }
        if (!text && !input.validity.badInput) {
            return '';
        }
        const identifier = input.name || input.id;
        if (/^(customerName|supplierName|categoryName|brandName|full-name)$/i.test(identifier) && !/\p{L}/u.test(text)) {
            return `${label} must contain letters.`;
        }
        if (/contactPerson$/i.test(identifier) && !/^[\p{L}\p{M} .,'’\-]{2,100}$/u.test(text)) {
            return `${label} must use letters and name punctuation (2–100 characters).`;
        }
        if (/alternateMobile$/i.test(identifier)) {
            const primary = input.form?.querySelector('[name="mobileNumber"]');
            if (primary && primary.value === value) {
                return 'Alternate mobile must differ from the primary number.';
            }
        }
        if (input.type === 'password' && /confirm/i.test(identifier)) {
            const password = [...(input.form?.querySelectorAll('input[type="password"]') || [])]
                .find((field) => !/confirm/i.test(field.name || field.id) && field.autocomplete !== 'current-password');
            if (password && password.value !== value) {
                return window.StockFlowLanguage?.text('Passwords do not match.') || 'Passwords do not match.';
            }
        }
        if (input.type === 'tel' && input.pattern === '[0-9]{10}' && !/^[0-9]{10}$/.test(value)) {
            return message('phone', `${label} must contain exactly 10 digits, without spaces or a country code.`);
        }
        if (input.maxLength >= 0 && value.length > input.maxLength) {
            return message('maximum', `${label} must contain no more than ${input.maxLength} characters.`, input.maxLength);
        }
        if (input.minLength > 0 && text.length < input.minLength) {
            return message('minimum', `${label} must contain at least ${input.minLength} characters.`, input.minLength);
        }
        if (input.validity.typeMismatch) {
            return message(input.type === 'email' ? 'email' : 'format', `Enter a valid ${input.type === 'email' ? 'email address' : 'website address'} for ${label}.`);
        }
        if (input.validity.rangeUnderflow) {
            return message('lower', `${label} must be at least ${input.min}.`, input.min);
        }
        if (input.validity.rangeOverflow) {
            return message('upper', `${label} must be no more than ${input.max}.`, input.max);
        }
        if (input.validity.stepMismatch) {
            return message('step', `${label} must use increments of ${input.step || '1'}.`, input.step || '1');
        }
        if (input.validity.badInput) {
            return message('format', `Enter a valid value for ${label}.`);
        }
        if (input.validity.patternMismatch) {
            return message('format', input.title || `Enter ${label} in the correct format.`);
        }
        return input.validity.valid ? '' : input.validationMessage;
    }

    function showField(input, message) {
        if(['product-registration','product-edit'].includes(input.form?.dataset.validationOwner)&&window.ProductRegistrationForm?.field){
            return ProductRegistrationForm.field(input,message);
        }
        if (!input.id) {
            input.id = 'sf-validation-field-' + ++nextId;
        }
        const id = input.id + (input.form?.dataset.validationOwner === 'product-registration' ? '-error' : '-validation-error');
        let feedback = document.getElementById(id);
        if (!feedback && input.form) {
            feedback = [...input.form.querySelectorAll('[data-valmsg-for]')]
                .find((element) => element.getAttribute('data-valmsg-for') === input.name);
            if (feedback) {
                feedback.id = id;
                feedback.classList.add('sf-field-validation');
            }
        }
        if (!feedback && message) {
            feedback = document.createElement('div');
            feedback.id = id;
            feedback.className = 'sf-field-validation text-danger small';
            (input.sfSelect?.wrapper || input.sfAddressPicker?.wrapper || input).after(feedback);
            const describedBy = input.getAttribute('aria-describedby') || '';
            input.setAttribute('aria-describedby', `${describedBy} ${id}`.trim());
        }
        if (feedback) {
            const describedBy = (input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
            if (!describedBy.includes(id)) input.setAttribute('aria-describedby', [...describedBy, id].join(' '));
            feedback.textContent = message;
            feedback.hidden = !message;
        }
        input.classList.toggle('is-invalid', Boolean(message));
        if (message) {
            input.setAttribute('aria-invalid', 'true');
            // Optional sections must expose errors before focus moves to a field.
            for (let parent = input.parentElement; parent; parent = parent.parentElement) {
                if (parent.tagName === 'DETAILS') parent.open = true;
            }
        } else {
            input.removeAttribute('aria-invalid');
        }
        (input.sfSelect || input.sfAddressPicker)?.refresh();
    }

    function validate(container, focus = true) {
        shownForms.add(container);
        const invalid = [];
        for (const input of container.querySelectorAll('input, select, textarea')) {
            const message = fieldMessage(input);
            showField(input, message);
            if (message) {
                invalid.push(input);
            }
        }
        // Validation stays beside each field; operation notifications are independent.
        container.querySelectorAll(':scope > .sf-validation-summary').forEach(summary => summary.remove());
        if (focus && invalid.length) {
            invalid[0].focus();
        }
        return invalid.length === 0;
    }

    // Capture runs before page-specific save handlers, including dynamically created forms.
    document.addEventListener('submit', (event) => {
        if (event.target.noValidate || event.submitter?.formNoValidate) {
            return; // Billing and the shared product wizard perform their own step validation.
        }
        if (!validate(event.target)) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);

    document.addEventListener('invalid', (event) => {
        if (event.target.form && !event.target.form.noValidate) {
            if (event.target.form.hasAttribute('data-inline-validation')) event.preventDefault();
            validate(event.target.form, false);
        }
    }, true);

    document.addEventListener('click', (event) => {
        const button = event.target.closest('button, input[type="submit"]');
        if (button?.type === 'submit' && button.form && !button.form.noValidate && !button.formNoValidate) {
            if (!validate(button.form)) {
                event.preventDefault();
            }
        }
    }, true);

    for (const eventName of ['input', 'change']) {
        document.addEventListener(eventName, (event) => {
            const input = event.target;
            if (!input.matches('input, select, textarea')) {
                return;
            }
            prepareField(input);
            if (input.form && shownForms.has(input.form)) {
                validate(input.form, false);
            }
        });
    }
    document.addEventListener('reset', (event) => {
        shownForms.delete(event.target);
        // Reset values become available after the reset event finishes.
        queueMicrotask(() => {
            for (const input of event.target.querySelectorAll('input, select, textarea')) {
                showField(input, '');
            }
            const summary = event.target.querySelector(':scope > .sf-validation-summary');
            if (summary) {
                summary.hidden = true;
            }
        });
    });
    const observer = new MutationObserver((changes) => {
        for (const change of changes) {
            for (const node of change.addedNodes) {
                if (!(node instanceof Element)) {
                    continue;
                }
                if (node.matches('form, input, select, textarea')) {
                    prepareField(node);
                }
                node.querySelectorAll('form, input, select, textarea').forEach(prepareField);
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.querySelectorAll('form, input, select, textarea').forEach(prepareField);
    function restoreServerFieldErrors() {
        for (const feedback of document.querySelectorAll('[data-valmsg-for].field-validation-error')) {
            const scope = feedback.closest('form') || document;
            const input = [...scope.querySelectorAll('input,select,textarea')]
                .find(field => field.name === feedback.getAttribute('data-valmsg-for'));
            if (input && feedback.textContent.trim()) {
                if (input.form) shownForms.add(input.form);
                showField(input, feedback.textContent.trim());
            }
        }
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', restoreServerFieldErrors, { once: true });
    else restoreServerFieldErrors();
    document.addEventListener('focusin', (event) => {
        if (event.target instanceof Element && event.target.matches('input, select, textarea')) {
            prepareField(event.target);
        }
    }, true);

    // Clear validation state without resetting values or the unsaved-change snapshot.
    function reset(container) {
        shownForms.delete(container);
        container.querySelectorAll('input, select, textarea').forEach(input=>showField(input,''));
        container.querySelectorAll(':scope > .sf-validation-summary').forEach(summary=>summary.hidden=true);
    }
    return { validate, fieldMessage, showField, reset };
})();
