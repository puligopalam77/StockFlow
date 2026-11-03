(() => {
    'use strict';
    const root = document.getElementById('whatsapp-settings');
    if (!root || !root.dataset.appId) return;
    const status = document.getElementById('whatsapp-signup-status');
    const buttons = [document.getElementById('whatsapp-connect'), document.getElementById('whatsapp-coexistence')].filter(Boolean);
    let sdk, attempt, prepared;
    const say = text => { status.textContent = text; };
    const post = async (path, body) => {
        const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const response = await fetch('/api/configuration/whatsapp/' + path, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf || '' }, body: JSON.stringify(body)
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.message || 'Could not complete WhatsApp setup. Refresh the page and try again.');
        return result;
    };
    const loadSdk = () => sdk ||= new Promise((resolve, reject) => {
        const init = () => {
            window.FB.init({ appId: root.dataset.appId, autoLogAppEvents: false, xfbml: false, version: root.dataset.graphVersion });
            resolve();
        };
        if (window.FB) { init(); return; }
        window.fbAsyncInit = init;
        const script = document.createElement('script'); script.async = true;
        script.src = 'https://connect.facebook.net/en_US/sdk.js';
        script.onerror = () => reject(new Error('Meta signup could not load. Check your internet connection and browser privacy settings.'));
        document.head.appendChild(script);
    });
    function finish(error) {
        if (attempt) clearTimeout(attempt.timeout);
        attempt = null;
        buttons.forEach(b => b.disabled = false);
        if (error) say(error);
        prepare(true);
    }
    async function complete() {
        const current = attempt;
        if (!current || current.saving || !current.code || !current.wabaId || !current.phoneId) return;
        current.saving = true;
        say('Confirming account access with Meta…');
        try {
            const pin = document.getElementById('whatsapp-registration-pin').value.trim();
            const result = await post('complete', { ticket: current.ticket, code: current.code, wabaId: current.wabaId, phoneId: current.phoneId, registrationPin: pin || null, businessApp: current.businessApp });
            say(result.message); finish(); window.location.reload();
        } catch (error) { finish(error.message); }
    }
    window.addEventListener('message', event => {
        if (!attempt || !['https://www.facebook.com', 'https://web.facebook.com'].includes(event.origin)) return;
        let data;
        try { data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch { return; }
        if (data?.type !== 'WA_EMBEDDED_SIGNUP') return;
        if (['FINISH', 'FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING'].includes(data.event)) {
            if (!/^\d{5,30}$/.test(data.data?.waba_id || '') || !/^\d{5,30}$/.test(data.data?.phone_number_id || '')) return;
            attempt.wabaId = data.data.waba_id; attempt.phoneId = data.data.phone_number_id; complete();
        } else if (['CANCEL', 'ERROR'].includes(data.event)) finish('WhatsApp signup was not completed. You can start again.');
    });
    async function prepare(keepStatus = false) {
        buttons.forEach(b => b.disabled = true);
        if (!keepStatus) say('Preparing secure Meta signup…');
        try {
            await loadSdk();
            prepared = { ...await post('signup', {}), preparedAt: Date.now() };
            buttons.forEach(b => b.disabled = false);
            if (!keepStatus) say('');
        } catch (error) {
            sdk = undefined; prepared = null;
            buttons.forEach(b => b.disabled = false); say(error.message);
        }
    }
    function begin(useBusinessApp) {
        if (attempt) return;
        // Open the popup directly in the click gesture. Waiting for a network request
        // here makes browsers block FB.login as an unsolicited popup.
        if (!prepared || Date.now() - prepared.preparedAt > 9 * 60 * 1000) { prepare(); return; }
        buttons.forEach(b => b.disabled = true); say('Opening Meta signup…');
        try {
            const data = prepared; prepared = null;
            if (useBusinessApp && !data.coexistenceEnabled) throw new Error('Business App onboarding is not enabled for this Meta integration.');
            attempt = { ticket: data.ticket, businessApp: useBusinessApp };
            attempt.timeout = setTimeout(() => finish('Signup timed out. Start Connect WhatsApp again.'), 5 * 60 * 1000);
            const extras = { setup: {}, sessionInfoVersion: '3' };
            if (useBusinessApp) extras.featureType = 'whatsapp_business_app_onboarding';
            window.FB.login(response => {
                if (!attempt) return;
                if (!response.authResponse?.code) { finish('Meta authorization was not completed. You can start again.'); return; }
                attempt.code = response.authResponse.code; complete();
            }, { config_id: data.configurationId, response_type: 'code', override_default_response_type: true, extras });
        } catch (error) { finish(error.message); }
    }
    document.getElementById('whatsapp-connect')?.addEventListener('click', () => begin(false));
    document.getElementById('whatsapp-coexistence')?.addEventListener('click', () => begin(true));
    prepare();
})();
