(() => {
    'use strict';

    const rows = document.getElementById('sessions-rows');
    const message = document.getElementById('sessions-message');
    const refresh = document.getElementById('sessions-refresh');
    const dialog = document.getElementById('session-revoke-dialog');
    let busy = false;
    function showMessage(value, error = false) {
        message.textContent = value;
        message.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        message.hidden = !value;
    }
    async function requestJson(action, options = {}) {
        const response = await fetch('/account/session/' + action, {
            credentials: 'same-origin',
            cache: 'no-store',
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]')?.content || ''
            }
        });
        if (response.status === 401) {
            location.replace('/Account/Login?reason=ended');
            throw new Error('This login has ended.');
        }
        if (!response.ok) {
            throw new Error(
                response.status === 404
                    ? 'This session has already ended. Refresh the list.'
                    : 'Unable to update sessions. Please try again.'
            );
        }
        if (response.status === 204) {
            return null;
        } else {
            return response.json();
        }
    }
    function device(agent) {
        const browser = /Edg\//.test(agent)
            ? 'Edge'
            : /Firefox\//.test(agent)
              ? 'Firefox'
              : /Chrome\//.test(agent)
                ? 'Chrome'
                : /Safari\//.test(agent)
                  ? 'Safari'
                  : 'Browser';
        const platform = /Android/.test(agent)
            ? 'Android'
            : /iPhone|iPad/.test(agent)
              ? 'iPhone / iPad'
              : /Windows/.test(agent)
                ? 'Windows'
                : /Macintosh/.test(agent)
                  ? 'Mac'
                  : /Linux/.test(agent)
                    ? 'Linux'
                    : 'Unknown device';
        return `${browser} on ${platform}`;
    }
    function confirmRevoke() {
        return new Promise((resolve) => {
            dialog.returnValue = '';
            document.getElementById('session-revoke-cancel').onclick = () => {
                return dialog.close('cancel');
            };
            document.getElementById('session-revoke-confirm').onclick = () => {
                return dialog.close('confirm');
            };
            dialog.addEventListener(
                'close',
                () => {
                    return resolve(dialog.returnValue === 'confirm');
                },
                {
                    once: true
                }
            );
            dialog.showModal();
            document.getElementById('session-revoke-cancel').focus();
        });
    }
    async function load() {
        refresh.disabled = true;
        try {
            const sessions = await requestJson('devices');
            rows.replaceChildren();
            for (const session of sessions) {
                const row = document.createElement('tr');
                const values = [
                    device(session.device) + (session.isCurrent ? ' (this device)' : ''),
                    session.ipAddress,
                    ...[session.createdAt, session.lastActivityAt, session.absoluteExpiresAt].map(
                        (value) => {
                            return new Date(value).toLocaleString();
                        }
                    )
                ];
                for (const value of values) {
                    const cell = document.createElement('td');
                    cell.textContent = value;
                    row.append(cell);
                }
                const cell = document.createElement('td');
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-danger';
                button.textContent = session.isCurrent ? 'Sign out this device' : 'Sign out';
                button.setAttribute(
                    'aria-label',
                    'Sign out ' +
                        device(session.device) +
                        (session.isCurrent ? ' (this device)' : '')
                );
                button.onclick = async () => {
                    if (busy || !(await confirmRevoke())) {
                        return;
                    }
                    busy = true;
                    button.disabled = true;
                    try {
                        await requestJson('revoke', {
                            method: 'POST',
                            body: JSON.stringify({
                                sessionId: session.sessionId
                            })
                        });
                        if (session.isCurrent) {
                            try {
                                localStorage.setItem(
                                    'stockflow.idle.' + session.sessionId + '.ended',
                                    'true'
                                );
                            } catch {}
                            location.replace('/Account/Login?reason=ended');
                            return;
                        }
                        showMessage('Session signed out.');
                        await load();
                    } catch (error) {
                        showMessage(error.message, true);
                    } finally {
                        busy = false;
                        button.disabled = false;
                    }
                };
                cell.append(button);
                row.append(cell);
                rows.append(row);
            }
            if (!sessions.length) {
                const row = document.createElement('tr');
                const cell = document.createElement('td');
                cell.colSpan = 6;
                cell.textContent = 'No active sessions.';
                row.append(cell);
                rows.append(row);
            }
        } catch (error) {
            showMessage(error.message, true);
        } finally {
            refresh.disabled = false;
        }
    }
    refresh.onclick = () => {
        if (!busy) {
            load();
        }
    };
    load();
})();
