(() => {
    let session, library;
    function decoder() {
        if (window.ZXingBrowser) return Promise.resolve(window.ZXingBrowser);
        return library ||= new Promise((resolve, reject) => {
            const script = document.createElement('script'); script.src = '/lib/zxing/zxing-browser.min.js';
            script.onload = () => resolve(window.ZXingBrowser);
            script.onerror = () => { library = null; reject(new Error('Unable to load camera scanner. Check your connection.')); };
            document.head.append(script);
        });
    }
    function stop() {
        const current = session; session = null;
        if (!current) return;
        current.controls?.stop();
        current.video.srcObject?.getTracks().forEach(track => track.stop());
        current.video.srcObject = null;
        if (current.dialog.open) current.dialog.close();
        current.dialog.remove();
        current.input.focus();
    }
    async function scan(input, submit) {
        if (input.disabled || input.readOnly || !input.isConnected) return;
        stop();
        const dialog = document.createElement('dialog'); dialog.className = 'sf-camera-dialog';
        dialog.setAttribute('aria-label', 'Scan barcode with camera');
        const title = document.createElement('h2'); title.textContent = 'Scan barcode';
        const video = document.createElement('video'); video.autoplay = true; video.muted = true; video.playsInline = true;
        const message = document.createElement('p'); message.setAttribute('role', 'status'); message.textContent = 'Point the rear camera at the product barcode or serial label.';
        const result = document.createElement('input'); result.className = 'form-control'; result.readOnly = true; result.hidden = true; result.setAttribute('aria-label', 'Detected code');
        const use = document.createElement('button'); use.type = 'button'; use.className = 'btn btn-primary'; use.textContent = 'Use code'; use.hidden = true;
        const cancel = document.createElement('button'); cancel.type = 'button'; cancel.className = 'btn btn-outline-secondary'; cancel.textContent = 'Cancel'; cancel.onclick = stop;
        dialog.append(title, video, message, result, use, cancel); document.body.append(dialog);
        const current = session = { dialog, video, input, controls: null }; dialog.showModal();
        dialog.addEventListener('cancel', event => { event.preventDefault(); stop(); });
        use.onclick = () => {
            if (session !== current || input.disabled || input.readOnly || !input.isConnected) return;
            const code = result.value; stop(); input.value = code;
            input.dispatchEvent(new Event('input', { bubbles: true })); submit();
        };
        try {
            if (!window.isSecureContext) throw new Error('Camera scanning requires HTTPS or localhost.');
            if (!navigator.mediaDevices?.getUserMedia) throw new Error('This device cannot access the camera. Enter the code manually.');
            const ZXing = await decoder(); if (session !== current) return;
            const reader = new ZXing.BrowserMultiFormatReader();
            const controls = await reader.decodeFromConstraints({ video: { facingMode: { ideal: 'environment' } }, audio: false }, video, (found, error, controls) => {
                if (session !== current) { controls?.stop(); return; }
                if (!found || !use.hidden) return;
                const code = found.getText().trim();
                if (!code || code.length > (input.maxLength > 0 ? input.maxLength : 200) || /[\x00-\x1f]/.test(code)) {
                    message.textContent = 'This label is not a valid product code. Try another label.'; return;
                }
                controls.stop(); video.srcObject?.getTracks().forEach(track => track.stop());
                result.value = code; result.hidden = false; use.hidden = false;
                message.textContent = 'Check the detected code, then select Use code.'; use.focus();
            });
            if (session !== current) controls.stop(); else current.controls = controls;
        } catch (error) {
            if (session !== current) return;
            message.textContent = error.name === 'NotAllowedError' ? 'Camera permission was denied. Enable it in device settings, or enter the code manually.' : error.name === 'NotFoundError' ? 'No camera found. Enter the code manually.' : error.message || 'Unable to start the camera.';
            video.hidden = true;
        }
    }
    window.StockFlowCamera = {
        stop,
        attach(input, submit) {
            if (input.dataset.cameraReady) return;
            input.dataset.cameraReady = 'true';
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary sf-camera-trigger'; button.textContent = 'Camera';
            button.setAttribute('aria-label', 'Scan this code with camera'); button.onclick = () => scan(input, submit);
            input.insertAdjacentElement('afterend', button);
        }
    };
    document.addEventListener('visibilitychange', () => { if (document.hidden) stop(); });
    document.addEventListener('stockflow:device-paused', stop);
    window.addEventListener('pagehide', stop);
})();
