(() => {
    const root = document.querySelector('.sf-notification-console');
    if (!root) return;
    root.querySelectorAll('.sf-notification-tabs a').forEach(link => link.addEventListener('click', () => {
        root.querySelectorAll('.sf-notification-tabs a').forEach(a => a.classList.toggle('active', a === link));
    }));
    root.querySelectorAll('.sf-notification-template-table input[maxlength]').forEach(input => {
        const counter = input.parentElement.querySelector('.sf-template-counter');
        const update = () => { if (counter) counter.textContent = `${input.value.length}/${input.maxLength}`; };
        input.addEventListener('input', update); update();
    });
    root.querySelectorAll('[data-edit-template]').forEach(button => button.addEventListener('click', () => {
        document.querySelector(`[name="Input.Rules[${button.dataset.editTemplate}].Title"]`)?.focus();
    }));
    document.getElementById('notification-staff-search')?.addEventListener('input', event => {
        const text = event.target.value.trim().toLocaleLowerCase();
        root.querySelectorAll('[data-staff-name]').forEach(row => { row.hidden = !row.dataset.staffName.toLocaleLowerCase().includes(text); });
    });
    const dialog = document.getElementById('notification-preview-dialog');
    document.getElementById('notification-preview-button')?.addEventListener('click', () => {
        const index = document.getElementById('notification-preview-event').value;
        const sample = field => (document.querySelector(`[name="Input.Rules[${index}].${field}"]`)?.value || '')
            .replaceAll('{reference}', 'DOC-2026-000001').replaceAll('{status}', 'COMPLETED');
        document.getElementById('notification-preview-title').textContent = sample('Title');
        document.getElementById('notification-preview-body').textContent = sample('Body');
        dialog.showModal();
    });
    document.getElementById('notification-preview-close')?.addEventListener('click', () => dialog.close());
})();
