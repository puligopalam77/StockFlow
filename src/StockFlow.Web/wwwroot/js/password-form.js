document.querySelectorAll('[data-password-toggle]').forEach((button) => {
    button.addEventListener('click', () => {
        const input = document.getElementById(button.dataset.passwordToggle);
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(show));
        const label = input.labels[0].textContent.toLowerCase();
        button.setAttribute('aria-label', (show ? 'Hide ' : 'Show ') + label);
    });
});
