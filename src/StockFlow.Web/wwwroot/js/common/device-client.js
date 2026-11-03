// Android receives a MessagePort in the main frame, addressed to this HTTPS origin.
// Ordinary browsers retain their own printing and download behavior.
(() => {
    if (!navigator.userAgent.includes('StockFlowAndroid/') || window.StockFlowDevice) return;
    let port, readyResolve, waiter, busy = false;
    const ready = new Promise(resolve => readyResolve = resolve);
    const limit = 20 * 1024 * 1024;
    window.StockFlowDevice = { ready };
    window.addEventListener('message', event => {
        if (event.source !== null || event.data !== 'stockflow-device-ready' || !event.ports?.[0]) return;
        port?.close(); port = event.ports[0];
        port.onmessage = event => { if (waiter) { const done = waiter; waiter = null; done(event.data); } };
        port.start(); readyResolve();
    });
    async function connection() {
        await Promise.race([ready, new Promise((_, reject) => setTimeout(() => reject(new Error('Device connection is unavailable. Reopen this screen.')), 10000))]);
    }
    function send(action, values = {}) { port.postMessage(JSON.stringify({ action, ...values })); }
    function exchange(action, values, timeout = 120000) {
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => { waiter = null; reject(new Error('Device operation timed out.')); }, timeout);
            waiter = status => { clearTimeout(timer); status === 'error' || status === 'busy' ? reject(new Error('Unable to save now. Finish the current download and try again.')) : resolve(status); };
            send(action, values);
        });
    }
    window.print = async () => {
        if (busy) { StockFlowNotify.show('warning','Finish the current document operation first.',{key:'device-operation'}); return; }
        busy = true;
        try { await connection(); await exchange('print', {}, 900000); }
        catch (error) { StockFlowNotify.show('error',error.message,{key:'device-operation'}); }
        finally { busy = false; }
    };
    async function saveBlob(anchor) {
        if (busy) { StockFlowNotify.show('warning','Finish the current download first.',{key:'device-operation'}); return; }
        busy = true;
        try {
            const response = await fetch(anchor.href), blob = await response.blob();
            await connection();
            if (blob.size > limit) throw new Error('Maximum file size is 20 MB.');
            await exchange('download-start', { name: anchor.download || 'stockflow-document', mime: blob.type });
            for (let offset = 0; offset < blob.size; offset += 48000) {
                const chunk = new Uint8Array(await blob.slice(offset, offset + 48000).arrayBuffer());
                let text = ''; for (const byte of chunk) text += String.fromCharCode(byte);
                await exchange('download-chunk', { data: btoa(text) });
            }
            await exchange('download-end');
        } catch (error) { if (port) send('download-cancel'); StockFlowNotify.show('error',error.message,{key:'device-operation'}); }
        finally { busy = false; }
    }
    const originalClick = HTMLAnchorElement.prototype.click;
    HTMLAnchorElement.prototype.click = function () {
        if (this.download && this.href.startsWith('blob:')) { void saveBlob(this); return; }
        return originalClick.call(this);
    };
    document.addEventListener('click', event => {
        const anchor = event.target.closest?.('a[download]');
        if (anchor?.href.startsWith('blob:')) { event.preventDefault(); void saveBlob(anchor); }
    });
    window.addEventListener('pagehide', () => { port?.close(); port = null; });
})();
