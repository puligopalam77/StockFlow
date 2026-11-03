// Voice turns into text and uses the same authenticated question form as typing.
(() => {
    const panel = document.getElementById('help-panel');
    if (!panel) return;
    const mic = document.getElementById('help-voice-start');
    const input = document.getElementById('help-ai-question');
    const form = document.getElementById('help-ai-form');
    const send = document.getElementById('help-ai-submit');
    const status = document.getElementById('help-voice-status');
    const consent = document.getElementById('help-voice-consent');
    const read = document.getElementById('help-voice-read');
    const auto = document.getElementById('help-voice-auto');
    const stop = document.getElementById('help-voice-stop');
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const synthesis = window.speechSynthesis;
    let language = document.getElementById('language-choice')?.value || 'en';
    let recognition = null;
    let timer = null;
    let onlineAllowed = false;
    let starting = false;
    let generation = 0;
    let speechGeneration = 0;
    let recorder = null, stream = null, recordingTimer = null, upload = null;
    let recordedWave = null, recordingDraft = '', recordVersion = 0;
    const cancel = document.getElementById('help-voice-cancel');
    const retry = document.createElement('button');
    retry.type = 'button'; retry.textContent = 'Retry transcription'; retry.hidden = true;
    retry.className = 'sf-voice-retry'; status.after(retry);

    function releaseMic() { stream?.getTracks().forEach(track => track.stop()); stream = null; clearInterval(recordingTimer); }
    function recordingControls(active) {
        mic.setAttribute('aria-pressed', String(active)); mic.title = active ? 'Stop recording' : 'Record question';
        mic.setAttribute('aria-label', mic.title); cancel.hidden = !active; stop.hidden = !active;
        stop.textContent = active ? 'Stop recording' : 'Stop voice'; send.disabled = active;
        input.readOnly = active;
    }
    function cancelRecording() {
        const wasRecording = starting || recorder || upload || input.readOnly;
        recordVersion++; upload?.abort(); upload = null; recordedWave = null; retry.hidden = true;
        if (wasRecording) mic.disabled = false;
        starting = false;
        const active = recorder; recorder = null; if (active?.state === 'recording') active.stop();
        releaseMic(); if (wasRecording) recordingControls(false);
    }
    async function waveFrom(blob) {
        const Audio = window.AudioContext || window.webkitAudioContext;
        const context = new Audio();
        let decoded;
        try { decoded = await context.decodeAudioData(await blob.arrayBuffer()); } finally { await context.close(); }
        const length = Math.min(960000, Math.ceil(decoded.duration * 16000));
        if (!length || !Number.isFinite(length)) throw new Error('The recording was empty. Record your question again.');
        const Offline = window.OfflineAudioContext || window.webkitOfflineAudioContext;
        if (!Offline) throw new Error('This browser cannot prepare microphone audio. Update the browser and try again.');
        const offline = new Offline(1, length, 16000);
        const source = offline.createBufferSource(); source.buffer = decoded; source.connect(offline.destination); source.start();
        const samples = (await offline.startRendering()).getChannelData(0);
        const wave = new ArrayBuffer(44 + samples.length * 2), view = new DataView(wave);
        const text = (offset, value) => [...value].forEach((c, i) => view.setUint8(offset + i, c.charCodeAt(0)));
        text(0, 'RIFF'); view.setUint32(4, wave.byteLength - 8, true); text(8, 'WAVE'); text(12, 'fmt ');
        view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
        view.setUint32(24, 16000, true); view.setUint32(28, 32000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
        text(36, 'data'); view.setUint32(40, samples.length * 2, true);
        let power = 0;
        samples.forEach((value, i) => { power += value * value; const sample = Math.max(-1, Math.min(1, value)); view.setInt16(44 + i * 2, sample * (sample < 0 ? 32768 : 32767), true); });
        if (Math.sqrt(power / samples.length) < .002) throw new Error('No speech was detected. Check the microphone and record again.');
        return new Blob([wave], {type: 'audio/wav'});
    }
    async function transcribe(version) {
        retry.hidden = true; recordingControls(true); stop.hidden = true; mic.disabled = true;
        message('Transcribing your recording…'); upload = new AbortController();
        try {
            const body = new FormData(); body.append('audio', recordedWave, 'question.wav');
            const response = await fetch('/api/help/voice', {method: 'POST', headers: {'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content}, body, signal: upload.signal});
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Transcription failed. Retry your recording.');
            if (version !== recordVersion || !panel.open) return;
            if (!result.text?.trim()) throw new Error('No speech was detected. Record again.');
            input.value = [recordingDraft, result.text.trim()].filter(Boolean).join(' ');
            input.dispatchEvent(new Event('input', {bubbles: true})); recordedWave = null;
            message(input.value.length > 500 ? 'Recording transcribed. Shorten the question to 500 characters before sending.' : 'Review the transcribed question, correct any words, then select Send.');
            input.focus();
        } catch (error) {
            if (version !== recordVersion) return;
            message(error.message); retry.hidden = !recordedWave;
        } finally { if (version === recordVersion) { upload = null; mic.disabled = false; recordingControls(false); } }
    }
    async function recordQuestion() {
        if (recorder?.state === 'recording') { recorder.stop(); return; }
        if (starting || upload || send.disabled || !panel.open) return;
        if (!window.isSecureContext) { message('Open StockFlow using HTTPS to use the microphone on this device. Plain HTTP over a shop Wi-Fi address blocks microphone access.'); return; }
        if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) { return startListening(); }
        stopSpeaking(); cancelListening(); consent.hidden = true; starting = true;
        const version = ++recordVersion;
        recordingDraft = input.value.trim(); recordedWave = null; retry.hidden = true;
        message('Allow microphone access to record your question.');
        try {
            const acquired = await navigator.mediaDevices.getUserMedia({audio: {echoCancellation: true, noiseSuppression: true}, video: false});
            if (version !== recordVersion || !panel.open) { acquired.getTracks().forEach(track => track.stop()); return; }
            stream = acquired;
            const type = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/mp4;codecs=mp4a.40.2', 'audio/ogg;codecs=opus', 'audio/webm'].find(value => MediaRecorder.isTypeSupported?.(value));
            const active = new MediaRecorder(stream, type ? {mimeType: type} : undefined), chunks = [];
            recorder = active; recordingControls(true); const began = Date.now();
            active.ondataavailable = event => { if (event.data.size) chunks.push(event.data); };
            active.onerror = () => { cancelRecording(); message('The microphone stopped. Record your question again.'); };
            active.onstop = async () => {
                if (version !== recordVersion || !panel.open) return;
                releaseMic(); recorder = null;
                try { message('Preparing your recording…'); recordedWave = await waveFrom(new Blob(chunks, {type: active.mimeType})); if (version === recordVersion) await transcribe(version); }
                catch (error) { if (version === recordVersion) { recordingControls(false); message(error.message); } }
            };
            active.start(250);
            for (const track of stream.getAudioTracks()) track.addEventListener('ended', () => {
                if (version === recordVersion && active.state === 'recording') active.stop();
            }, {once: true});
            const tick = () => { const seconds = Math.floor((Date.now() - began) / 1000); message('Recording ' + String(Math.floor(seconds / 60)).padStart(2, '0') + ':' + String(seconds % 60).padStart(2, '0') + ' — tap Stop to transcribe.'); if (seconds >= 60 && active.state === 'recording') active.stop(); };
            tick(); recordingTimer = setInterval(tick, 250);
        } catch (error) { releaseMic(); recordingControls(false); message(error.name === 'NotAllowedError' ? 'Microphone access was denied. Allow the microphone in browser settings, then try again.' : error.name === 'NotFoundError' ? 'No microphone is available. Connect a microphone and try again.' : 'The microphone could not start. Check that another app is not using it.'); }
        finally { starting = false; }
    }

    function message(text) { status.textContent = text; status.hidden = !text; }
    function locale() { return language === 'en' ? 'en-IN' : language === 'ur' ? 'ur-IN' : language + '-IN'; }
    function stopSpeaking() { speechGeneration++; synthesis?.cancel(); stop.hidden = !recognition; }
    function cancelListening() {
        generation++;
        starting = false;
        const active = recognition;
        recognition = null;
        clearTimeout(timer);
        active?.abort();
        mic.setAttribute('aria-pressed', 'false');
        mic.title = 'Speak question';
        stop.hidden = true;
    }

    async function startListening() {
        if (recognition) { recognition.stop(); return; }
        if (starting || send.disabled || !panel.open) return;
        if (!Recognition || !window.isSecureContext) {
            message('Voice input is unavailable here. Use HTTPS or localhost and a browser that supports speech recognition. You can still type.');
            return;
        }
        starting = true;
        const request = ++generation;
        const speech = new Recognition();
        const selectedLocale = locale();
        let local = false;
        try {
            if ('processLocally' in speech && typeof Recognition.available === 'function')
                local = await Recognition.available({langs: [selectedLocale], processLocally: true}) === 'available';
        } catch { /* The browser may not provide an installed language pack. */ }
        if (request !== generation || !panel.open) return;
        starting = false;
        if (!local && !onlineAllowed) {
            consent.hidden = false;
            message('Choose whether to use your browser speech service, or continue typing.');
            return;
        }
        consent.hidden = true;
        if ('processLocally' in speech) speech.processLocally = local;
        speech.lang = selectedLocale;
        speech.continuous = false;
        speech.interimResults = true;
        speech.maxAlternatives = 1;
        const draft = input.value.trim();
        let finalText = '';
        let failed = false;
        recognition = speech;
        stopSpeaking();
        mic.setAttribute('aria-pressed', 'true');
        mic.title = 'Finish speaking';
        stop.hidden = false;
        message('Listening… Say your question. Tap the microphone again to finish.');
        speech.onresult = event => {
            if (request !== generation || !panel.open) return;
            finalText = '';
            let interim = '';
            for (let index = 0; index < event.results.length; index++) {
                if (event.results[index].isFinal) finalText += event.results[index][0].transcript + ' ';
                else interim += event.results[index][0].transcript + ' ';
            }
            const text = [draft, finalText.trim(), interim.trim()].filter(Boolean).join(' ');
            input.value = text;
            if (text.length > 500) { failed = true; speech.abort(); message('Keep your question within 500 characters. Edit it, then send.'); }
        };
        speech.onerror = event => {
            if (request !== generation || !panel.open) return;
            failed = true;
            input.value = [draft, finalText.trim()].filter(Boolean).join(' ');
            const errors = {
                'not-allowed': 'Microphone access was denied. Allow it in browser settings, or type your question.',
                'service-not-allowed': 'The browser speech service is blocked. You can still type.',
                'audio-capture': 'No microphone is available. Connect one, or type your question.',
                'no-speech': 'No speech was detected. Try again, or type your question.',
                'network': 'The browser speech service could not connect. Try again, or type.',
                'language-not-supported': 'Voice recognition does not support the selected language here. You can still type.'
            };
            message(errors[event.error] || 'Voice recognition stopped. You can still type your question.');
        };
        speech.onend = () => {
            if (request !== generation || !panel.open) return;
            recognition = null;
            clearTimeout(timer);
            mic.setAttribute('aria-pressed', 'false');
            mic.title = 'Speak question';
            stop.hidden = true;
            if (failed) return;
            input.value = [draft, finalText.trim()].filter(Boolean).join(' ');
            if (!finalText.trim() || input.value.length < 3 || input.value.length > 500) {
                message('Say a question with 3–500 characters, or type it.');
                return;
            }
            input.dispatchEvent(new Event('input', {bubbles:true}));
            message('Review the recognized question, correct any words, then select Send.');
            input.focus();
        };
        try {
            speech.start();
            timer = setTimeout(() => speech.stop(), 20000);
        } catch {
            cancelListening();
            message('Voice recognition could not start. Try again, or type your question.');
        }
    }

    async function speakReply() {
        if (!synthesis || !panel.open) return;
        if (starting || recorder || upload) cancelRecording();
        const reply = document.getElementById('help-ai-answer');
        if (reply.hidden || reply.getAttribute('aria-busy') === 'true') { message('Ask a question first, then read its reply.'); return; }
        cancelListening(); stopSpeaking();
        const speaking = speechGeneration;
        if (!synthesis.getVoices().length && synthesis.addEventListener) {
            message('Loading the voices on this device…');
            await new Promise(resolve => {
                let timeout;
                const done = () => { clearTimeout(timeout); synthesis.removeEventListener('voiceschanged', done); resolve(); };
                synthesis.addEventListener('voiceschanged', done);
                timeout = setTimeout(done, 2000);
            });
            if (speaking !== speechGeneration || !panel.open) return;
        }
        const voices = synthesis.getVoices().filter(voice => voice.localService);
        const voice = voices.find(item => item.lang.toLowerCase().startsWith(language + '-')) ||
            voices.find(item => item.lang.toLowerCase().startsWith('en'));
        if (!voice) { message('No installed local voice is available. Read the answer on screen.'); return; }
        const utterance = new SpeechSynthesisUtterance(reply.innerText.trim().slice(0, 6000));
        utterance.voice = voice;
        utterance.lang = voice.lang;
        utterance.onend = () => { if (speaking === speechGeneration) stop.hidden = true; };
        utterance.onerror = () => { if (speaking === speechGeneration) { stop.hidden = true; message('Tap Read reply to start playback on this device. If it still cannot play, check the device volume and installed speech voices.'); } };
        stop.hidden = false;
        message(voice.lang.toLowerCase().startsWith(language) ? 'Reading the reply…' : 'Reading with an installed English voice.');
        synthesis.speak(utterance);
    }

    mic.addEventListener('click', recordQuestion);
    document.addEventListener('stockflow:device-paused', () => { cancelRecording(); cancelListening(); stopSpeaking(); });
    cancel.addEventListener('click', () => { cancelRecording(); message('Recording cancelled. Your typed question is unchanged.'); });
    retry.addEventListener('click', () => transcribe(recordVersion));
    document.getElementById('help-voice-allow').addEventListener('click', () => { onlineAllowed = true; startListening(); });
    read.addEventListener('click', speakReply);
    stop.addEventListener('click', () => { if (recorder?.state === 'recording') { recorder.stop(); return; } cancelListening(); stopSpeaking(); message('Voice stopped.'); });
    document.addEventListener('stockflow:help-sending', () => { cancelRecording(); cancelListening(); stopSpeaking(); mic.disabled = true; });
    document.addEventListener('stockflow:help-answer', () => { mic.disabled = false; message(''); if (auto.checked) speakReply(); });
    document.addEventListener('stockflow:language-ready', event => {
        language = event.detail.language;
        cancelRecording(); cancelListening(); stopSpeaking(); consent.hidden = true; message('');
    });
    auto.addEventListener('change', () => { if (!auto.checked) stopSpeaking(); });
    panel.addEventListener('close', () => { cancelRecording(); cancelListening(); stopSpeaking(); consent.hidden = true; message(''); });
    window.addEventListener('pagehide', () => { cancelRecording(); cancelListening(); stopSpeaking(); });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden && recorder?.state === 'recording') recorder.stop();
        if (document.hidden) stopSpeaking();
    });
    if (!synthesis) { auto.disabled = true; read.disabled = true; }
    else synthesis.getVoices(); // Allow browsers to populate installed voices before the first reply.
})();
