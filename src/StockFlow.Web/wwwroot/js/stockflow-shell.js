(() => {
    const profile = document.getElementById('profile-menu');
    const logoutDialog = document.getElementById('logout-confirmation');
    document.getElementById('profile-logout')?.addEventListener('click', () => {
        profile.open = false;
        logoutDialog.showModal();
    });
    for (const id of ['logout-cancel', 'logout-close']) {
        document.getElementById(id)?.addEventListener('click', () => {
            return logoutDialog.close();
        });
    }
    logoutDialog?.addEventListener('close', () => {
        return profile.querySelector('summary').focus();
    });
    for (const [trigger, id] of [
        ['open-profile-details', 'profile-details-dialog'],
        ['open-change-password', 'change-password-dialog']
    ]) {
        const dialog = document.getElementById(id);
        document.getElementById(trigger)?.addEventListener('click', () => {
            profile.open = false;
            dialog.showModal();
        });
        dialog?.querySelectorAll('[data-close-account]').forEach((button) => {
            return button.addEventListener('click', () => {
                return dialog.close();
            });
        });
        dialog?.addEventListener('close', () => {
            dialog.querySelector('form')?.reset();
            const message = dialog.querySelector('[role="alert"]');
            if (message) {
                message.hidden = true;
                message.textContent = '';
            }
            profile.querySelector('summary').focus();
        });
    }
    const passwordForm = document.getElementById('profile-password-form');
    let savingPassword = false;
    const passwordDialog = document.getElementById('change-password-dialog');
    passwordDialog?.addEventListener('cancel', (event) => {
        if (savingPassword) {
            event.preventDefault();
        }
    });
    passwordForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (savingPassword) {
            return;
        }
        const message = document.getElementById('profile-password-message');
        message.hidden = true;
        if (
            passwordForm.elements['Input.Password'].value !==
            passwordForm.elements['Input.ConfirmPassword'].value
        ) {
            message.textContent = 'Passwords do not match.';
            message.hidden = false;
            return;
        }
        savingPassword = true;
        const controls = [...passwordDialog.querySelectorAll('button')];
        controls.forEach((button) => {
            return (button.disabled = true);
        });
        try {
            const response = await fetch(passwordForm.action, {
                method: 'POST',
                body: new FormData(passwordForm),
                headers: {
                    Accept: 'application/json'
                }
            });
            if (response.redirected || response.status === 401) {
                window.location.assign('/Account/Login');
                return;
            }
            const result = await response.json().catch(() => {
                return null;
            });
            if (!response.ok) {
                throw new Error(result?.message || 'Unable to change password. Please try again.');
            }
            passwordForm.reset();
            window.location.assign('/Account/Login');
        } catch (error) {
            message.textContent = error.message;
            message.hidden = false;
        } finally {
            savingPassword = false;
            controls.forEach((button) => {
                return (button.disabled = false);
            });
        }
    });
    document.addEventListener('click', (event) => {
        if (profile?.open && !profile.contains(event.target)) {
            profile.open = false;
        }
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && profile?.open) {
            profile.open = false;
            profile.querySelector('summary').focus();
        }
    });
    document.addEventListener('focusin', (event) => {
        if (profile?.open && !profile.contains(event.target)) {
            profile.open = false;
        }
    });
    const toggle = document.getElementById('sidebar-toggle');
    const sidebar = document.getElementById('app-sidebar');
    const navigation = sidebar?.querySelector('.sf-nav');
    if (navigation) {
        const scrollKey = 'stockflow.sidebar.scrollTop';
        // Full-page navigation (including /Configuration redirects) recreates the menu.
        // Keep its position within this tab independently of the page content.
        try {
            const saved = Number(sessionStorage.getItem(scrollKey));
            if (Number.isFinite(saved) && saved >= 0) {
                navigation.scrollTop = saved;
            }
        } catch {
            /* Storage may be disabled by the browser. */
        }
        const saveScroll = () => {
            try {
                sessionStorage.setItem(scrollKey, String(navigation.scrollTop));
            } catch {
                /* Navigation still works without persistence. */
            }
        };
        navigation.addEventListener('scroll', saveScroll, {
            passive: true
        });
        navigation.addEventListener('click', saveScroll);
        window.addEventListener('pagehide', saveScroll);
    }
    const backdrop = document.getElementById('sidebar-backdrop');
    const mobile = window.matchMedia('(max-width: 991px)');
    function setMenu(open) {
        document.body.classList.toggle('sf-nav-open', open && mobile.matches);
        document.body.classList.toggle('sf-nav-collapsed', !open && !mobile.matches);
        if (toggle) {
            toggle.setAttribute('aria-expanded', String(open));
        }
        if (backdrop) {
            backdrop.hidden = !(open && mobile.matches);
        }
        if (sidebar) {
            sidebar.inert = !open;
        }
    }
    if (sidebar) {
        setMenu(!mobile.matches);
        toggle?.addEventListener('click', () => {
            return setMenu(toggle.getAttribute('aria-expanded') !== 'true');
        });
        document.getElementById('sidebar-close')?.addEventListener('click', () => {
            setMenu(false);
            toggle.focus();
        });
        backdrop?.addEventListener('click', () => {
            return setMenu(false);
        });
        mobile.addEventListener('change', () => {
            return setMenu(!mobile.matches);
        });
        sidebar.addEventListener('click', (event) => {
            if (mobile.matches && event.target.closest('a')) {
                setMenu(false);
            }
        });
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && mobile.matches) {
                setMenu(false);
                toggle?.focus();
            }
            if (event.key === 'Tab' && document.body.classList.contains('sf-nav-open')) {
                const focusable = [
                    ...sidebar.querySelectorAll('a,button:not([disabled]),summary')
                ].filter((element) => {
                    return element.getClientRects().length > 0;
                });
                const first = focusable[0];
                const last = focusable[focusable.length - 1];
                if (event.shiftKey && document.activeElement === first) {
                    event.preventDefault();
                    last.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault();
                    first.focus();
                }
            }
        });
        toggle?.addEventListener('click', () => {
            if (mobile.matches && document.body.classList.contains('sf-nav-open')) {
                sidebar.querySelector('a')?.focus();
            }
        });
    }
    const password = document.getElementById('Input_Password');
    const forgotLink = document.getElementById('forgot-password-link');
    const usernameInput = document.getElementById('Input_Username');
    function updateForgotLink() {
        if (!forgotLink || !usernameInput) return;
        const destination = new URL(forgotLink.href, window.location.origin);
        const username = usernameInput.value.trim();
        if (username) destination.searchParams.set('username', username);
        else destination.searchParams.delete('username');
        forgotLink.href = destination.pathname + destination.search;
    }
    usernameInput?.addEventListener('input', updateForgotLink);
    // Read again on click to include usernames supplied by browser autofill.
    forgotLink?.addEventListener('click', updateForgotLink);
    updateForgotLink();
    const visibility = document.getElementById('password-toggle');
    visibility?.addEventListener('click', () => {
        const show = password.type === 'password';
        password.type = show ? 'text' : 'password';
        visibility.setAttribute('aria-pressed', String(show));
        visibility.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
    });
})();
