// Presentation only: keep the single launcher in the shared header, even when
// older module docks request it. Chat state and handlers stay on the same nodes.
(() => {
    const launcher = document.getElementById('help-open');
    const profile = document.getElementById('profile-menu');
    const actions = profile?.parentElement;
    const panel = document.getElementById('help-panel');
    if (!launcher || !actions || !panel) return;
    function mount() {
        if (launcher.parentElement !== actions || launcher.nextElementSibling !== profile) actions.insertBefore(launcher, profile);
        if (panel.parentElement !== document.body) document.body.append(panel);
        launcher.removeAttribute('data-inline');
        for (const property of ['top','left','right','bottom','--sf-stockbot-clearance']) launcher.style.removeProperty(property);
    }
    function panelViewport() {
        const viewport = window.visualViewport, top = viewport?.offsetTop || 0, height = viewport?.height || innerHeight;
        panel.style.setProperty('--sf-help-viewport-top', top + 'px');
        panel.style.setProperty('--sf-help-viewport-height', height + 'px');
        panel.style.setProperty('--sf-help-keyboard-inset', Math.max(0, innerHeight - top - height) + 'px');
    }
    mount();
    new MutationObserver(mount).observe(document.body, { childList:true, subtree:true });
    addEventListener('resize', panelViewport);
    window.visualViewport?.addEventListener('resize', panelViewport);
    window.visualViewport?.addEventListener('scroll', panelViewport, { passive:true });
    panelViewport();
})();
