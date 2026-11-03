(async () => {
    const getElement = (id) => {
        return document.getElementById(id);
    };
    const message = getElement('security-message');
    async function requestJson(method = 'GET', body) {
        const options = {
            method,
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || ''
            }
        };
        if (body) {
            options.body = JSON.stringify(body);
        }
        const response = await fetch('/api/admin/security/policy', options);
        if (!response.ok) {
            throw Error('Unable to read or save security policy. Check your access.');
        }
        if (response.status === 204) {
            return null;
        } else {
            return response.json();
        }
    }
    try {
        const securityPolicy = await requestJson();
        getElement('policy-length').value = securityPolicy.minimumPasswordLength;
        getElement('policy-2fa').checked = securityPolicy.requireTwoFactor;
        getElement('policy-attempts').value = securityPolicy.maxLoginAttempts;
        getElement('policy-lockout').value = securityPolicy.lockoutMinutes;
    } catch (error) {
        message.textContent = error.message;
    }
    getElement('security-policy').onsubmit = async (event) => {
        event.preventDefault();
        const button = event.submitter;
        button.disabled = true;
        try {
            await requestJson('PUT', {
                minimumPasswordLength: Number(getElement('policy-length').value),
                requireTwoFactor: getElement('policy-2fa').checked,
                maxLoginAttempts: Number(getElement('policy-attempts').value),
                lockoutMinutes: Number(getElement('policy-lockout').value)
            });
            location.assign('/Account/Login');
        } catch (error) {
            message.textContent = error.message;
            button.disabled = false;
        }
    };
})();
