// Track inactivity and warn the user before their sign-in expires.
(() => {
    'use strict';

    const settings = document.getElementById('idle-session');
    if (!settings) {
        return;
    }
    const timeout = Number(settings.dataset.timeout) || 15 * 60 * 1000;
    const key = 'stockflow.idle.' + settings.dataset.session;
    let absoluteDeadline =
        Date.now() + (Number(settings.dataset.absoluteRemaining) || 8 * 60 * 60 * 1000);
    const warning = document.getElementById('idle-session-warning');
    const text = document.getElementById('idle-session-warning-text');
    const token = document.querySelector('meta[name="csrf-token"]')?.content || '';
    let deadline = Date.now() + Number(settings.dataset.remaining);
    let stopped = false;
    let dirty = false;
    let sending = false;
    let timer;
    let lastSent = 0;
    let lastInput = 0;
    let lastPublished = 0;
    function sharedDeadline() {
        try {
            const value = Number(localStorage.getItem(key));
            if (Number.isFinite(value) && value > 0) {
                deadline = Math.max(deadline, Math.min(value, Date.now() + timeout));
            }
        } catch {
            /* Private browsing may disable storage. */
        }
        deadline = Math.min(deadline, absoluteDeadline);
    }
    function publish(force = false) {
        if (!force && Date.now() - lastPublished < 1000) {
            return;
        }
        lastPublished = Date.now();
        try {
            localStorage.setItem(key, String(deadline));
        } catch {}
    }
    function ended() {
        try {
            return localStorage.getItem(key + '.ended') === 'true';
        } catch {
            return false;
        }
    }
    function request(action) {
        return fetch('/account/session/' + action, {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers: {
                'X-CSRF-TOKEN': token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                sessionId: settings.dataset.session,
                ageMilliseconds: action === 'activity' ? Math.max(0, Date.now() - lastInput) : 0,
                reason: Date.now() >= absoluteDeadline ? 'absolute' : 'idle'
            }),
            keepalive: true
        });
    }
    function goToLogin(reason = Date.now() >= absoluteDeadline ? 'absolute' : 'idle') {
        // Remove the previous business screen even when the browser is offline.
        document.body.replaceChildren();
        const message = document.createElement('p');
        message.textContent = 'Session expired. Sign in to continue.';
        const link = document.createElement('a');
        let normalLogout = false;
        try { normalLogout = localStorage.getItem(key + '.endedReason') === 'logout'; } catch {}
        if (normalLogout) message.textContent = 'You have signed out. Sign in again to continue.';
        link.href = '/Account/Login' + (normalLogout ? '' : '?reason=' + reason);
        link.textContent = 'Sign in';
        document.body.append(message, link);
        location.replace(link.href);
    }
    async function expire() {
        if (stopped) {
            return;
        }
        if (ended()) {
            stopped = true;
            goToLogin();
            return;
        }
        sharedDeadline();
        if (Date.now() < deadline) {
            return;
        }
        stopped = true;
        clearTimeout(timer);
        try {
            localStorage.setItem(key + '.ended', 'true');
        } catch {}
        // Do not let a slow or offline network postpone locking the screen.
        request('expire').catch(() => {});
        goToLogin();
    }
    async function sendActivity() {
        if (stopped || sending || !dirty) {
            return;
        }
        sharedDeadline();
        if (Date.now() >= deadline) {
            expire();
            return;
        }
        sending = true;
        dirty = false;
        lastSent = Date.now();
        try {
            const response = await request('activity');
            if (stopped) {
                return;
            }
            if (response.status === 401 || response.status === 403 || response.status === 409) {
                deadline = 0;
                stopped = true;
                goToLogin('ended');
                return;
            }
            if (!response.ok) {
                dirty = true;
                return;
            }
            const data = await response.json();
            if (Number.isFinite(data.absoluteRemainingMilliseconds)) {
                absoluteDeadline = Math.min(
                    absoluteDeadline,
                    Date.now() + data.absoluteRemainingMilliseconds
                );
            }
            if (!Number.isFinite(data.remainingMilliseconds) || data.remainingMilliseconds <= 0) {
                deadline = 0;
                expire();
                return;
            }
            // Newer input or input in another tab can arrive while this request is in flight.
            sharedDeadline();
            publish(true);
        } catch {
            dirty = true;
        } finally {
            sending = false;
        }
    }
    function activity(event) {
        if (stopped || !event.isTrusted || document.visibilityState === 'hidden') {
            return;
        }
        sharedDeadline();
        // An input event after sleep/expiry must not revive the expired session.
        if (Date.now() >= deadline) {
            expire();
            return;
        }
        lastInput = Date.now();
        deadline = Math.min(lastInput + timeout, absoluteDeadline);
        dirty = true;
        publish();
        warning.hidden = true;
        clearTimeout(timer);
        if (Date.now() - lastSent >= 20000) {
            sendActivity();
        } else {
            timer = setTimeout(sendActivity, 250);
        } // Send the final input after movement stops.
        check();
    }
    function check() {
        if (stopped) {
            return;
        }
        if (ended()) {
            stopped = true;
            goToLogin();
            return;
        }
        sharedDeadline();
        const remaining = deadline - Date.now();
        if (remaining <= 0) {
            expire();
            return;
        }
        warning.hidden = remaining > 60000;
        const absolute = absoluteDeadline - Date.now() <= 60000;
        document.getElementById('idle-session-continue').hidden = absolute;
        if (!warning.hidden) {
            text.textContent = absolute
                ? `Your maximum login lifetime ends in ${Math.ceil(remaining / 1000)} seconds. You will need to sign in again.`
                : `You will be signed out in ${Math.ceil(remaining / 1000)} seconds due to inactivity. Move the pointer or continue working to stay signed in.`;
        }
        // Retry a failed activity update only while recent user input still exists.
        if (dirty && Date.now() - lastInput < 20000 && Date.now() - lastSent >= 20000) {
            sendActivity();
        }
    }
    for (const name of [
        'pointermove',
        'pointerdown',
        'keydown',
        'input',
        'wheel',
        'touchstart',
        'touchmove'
    ]) {
        document.addEventListener(name, activity, {
            passive: true,
            capture: true
        });
    }
    document.getElementById('idle-session-continue').addEventListener('click', activity);
    window.addEventListener('storage', (event) => {
        if (stopped) {
            return;
        }
        if (event.key === key + '.ended' && event.newValue === 'true') {
            stopped = true;
            goToLogin();
        } else if (event.key === key) {
            check();
        }
    });
    // A normal logout must also close the other tabs for this login session.
    for (const form of document.querySelectorAll('form[action="/Account/Logout"]')) {
        form.addEventListener('submit', () => {
            stopped = true;
            try {
                localStorage.setItem(key + '.endedReason', 'logout');
                localStorage.setItem(key + '.ended', 'true');
            } catch {}
        });
    }
    document.addEventListener('visibilitychange', check);
    window.addEventListener('focus', check);
    window.addEventListener('pageshow', check);
    sharedDeadline();
    publish();
    check();
    setInterval(check, 1000);
})();
