(() => {
    'use strict';
    const badge = document.getElementById('notification-count');
    const bell = document.getElementById('notification-bell');
    if (!badge || !bell) return;
    let pending = false;
    async function refresh() {
        if (pending || document.hidden) return;
        pending = true;
        try {
            const response = await fetch('/api/notifications', { headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(8000) });
            if (!response.ok) { badge.hidden = true; return; }
            const feed = await response.json();
            badge.textContent = feed.unread > 99 ? '99+' : String(feed.unread);
            badge.hidden = feed.unread === 0;
            bell.setAttribute('aria-label', `Notifications, ${feed.unread} unread`);
        } catch { /* Background refresh never blocks the current screen. */ }
        finally { pending = false; }
    }
    refresh();
    setInterval(refresh, 60000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
})();
