// Save the business language directly from the header, without opening another page.
(() => {
    const choice = document.getElementById('language-choice');
    if (!choice) return;
    const notice = document.getElementById('language-message');
    let savedLanguage = 'en';
    choice.replaceChildren();
    for (const [code, name] of window.StockFlowLanguages.languages) {
        choice.add(new Option(name, code));
    }

    // Keep the existing company settings permissions for this business-wide choice.
    const canSave = choice.dataset.canSave === 'true';
    choice.disabled = !canSave;
    if (!canSave) choice.title = 'Your administrator chooses the language for this business.';

    document.addEventListener('stockflow:language-ready', (event) => {
        savedLanguage = event.detail.language;
        choice.value = savedLanguage;
    });

    choice.addEventListener('change', async () => {
        const selectedLanguage = choice.value;
        choice.disabled = true;
        notice.hidden = true;
        try {
            const response = await fetch('/api/screenlanguages', {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content
                },
                body: JSON.stringify({ screen: 'application', language: selectedLanguage, captions: {} })
            });
            if (!response.ok) {
                const body = await response.json().catch(() => null);
                throw new Error(body?.message || 'Could not save the language. Please try again.');
            }
            savedLanguage = selectedLanguage;
            await window.StockFlowLanguage.setLanguage(selectedLanguage);
            // Other open tabs read the same saved setting automatically.
            try { localStorage.setItem('stockflow-language-changed', String(Date.now())); } catch {}
        } catch (error) {
            choice.value = savedLanguage;
            notice.textContent = error.message;
            notice.hidden = false;
        } finally {
            choice.disabled = !canSave;
        }
    });
})();
