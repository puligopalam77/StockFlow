// Local help only: the panel reads the reviewed guides embedded in this page.
(() => {
    const panel = document.getElementById('help-panel');
    if (!panel) return;
    const articles = JSON.parse(document.getElementById('help-data').textContent);
    const search = document.getElementById('help-search');
    const results = document.getElementById('help-results');
    const questions = document.getElementById('help-questions');
    const articleSection = document.getElementById('help-article');
    const content = document.getElementById('help-content');
    const opener = document.getElementById('help-open');
    let pack = {};
    let selectedArticle = null;
    let revision = 0;
    const packs = new Map();
    const status = document.getElementById('help-language-status');
    let aiAnswer = null;
    let lastQuestion = '';
    let feedbackId = null;
    let retryAllowed = false;
    const conversationId = crypto.randomUUID?.() || 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, character => {
        const value = Math.floor(Math.random() * 16);
        return (character === 'x' ? value : (value & 3) | 8).toString(16);
    });
    let stockAnswer = null;
    const currentPath = location.pathname.toLowerCase();
    function screenMatch(article) {
        return Math.max(0, ...article.screens.filter(screen => currentPath === screen.toLowerCase() ||
            currentPath.startsWith(screen.toLowerCase() + '/')).map(screen => screen.length));
    }
    const related = articles.filter(article => screenMatch(article) > 0).sort((left, right) => screenMatch(right) - screenMatch(left));
    const screenQuestions = {
        billing: ['How much quantity sold today?', 'How much amount collected today?'],
        products: ['How many products?', 'What is the difference between a barcode and a serial?'],
        'receiving-stock': ['How do I record a partial delivery?', 'How do I enter damaged goods?'],
        returns: ['How do I refund a customer?', 'How do I record an exchange?'],
        warranty: ['How many warranties are expiring?', 'How do I find warranty by invoice?']
    };
    const workflowQuestions = currentPath.startsWith('/settings/company')
        ? ['How do I change the business logo?', 'How do I set invoice prefixes and payment terms?', 'How do I print the saved logo on invoices?']
        : currentPath.startsWith('/cashclosing')
        ? ['How do I save daily cash closing?', 'How do I calculate the cash closing difference?', 'How do I print a saved cash closing?']
        : currentPath.startsWith('/inventory')
        ? ['How do I check current stock?', 'How do I correct stock quantity?', 'How do I count stock in the shop?']
        : currentPath.startsWith('/reports')
        ? ['How do I use Shop summary?', 'How do I view Product sales & profit?', 'How do I find More reports?']
        : currentPath.startsWith('/stockinflow')
        ? ['How do I find stock receipt history?', 'How do I view stock receipt details?', 'How do I filter item movements?']
        : currentPath === '/sales'
        ? ['How do I search sales invoices?', 'How do I export sales invoices?', 'How much sales today?'] : null;
    function askSuggested(question) {
        if (document.getElementById('help-ai-submit').disabled) return;
        document.getElementById('help-ai-question').value = question;
        document.getElementById('help-ai-form').requestSubmit();
    }

    function plain(text) {
        return text.replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
            .replaceAll('**\\***', '*').replaceAll('**', '').replaceAll('`', '')
            .replace(/\s+/g, ' ').trim();
    }
    function translation(text) { return pack[plain(text).toLowerCase()] || ''; }
    function bilingual(text) {
        const translated = translation(text);
        return translated && translated !== text ? text + ' (' + translated + ')' : (window.StockFlowLanguage?.text(text) || text);
    }

    function button(label, action, className = 'btn btn-outline-secondary btn-sm', translate = true) {
        const element = document.createElement('button');
        element.type = 'button';
        element.className = className;
        element.textContent = translate ? bilingual(label) : label;
        element.addEventListener('click', action);
        return element;
    }

    // Build text nodes instead of inserting HTML from an article.
    function inline(parent, text) {
        const tokens = text.replaceAll('**\\***', '*').split(/(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\))/g);
        for (const token of tokens) {
            if (token.startsWith('**') && token.endsWith('**')) {
                const strong = document.createElement('strong');
                strong.textContent = token.slice(2, -2);
                parent.append(strong);
            } else if (token.startsWith('`') && token.endsWith('`')) {
                const code = document.createElement('code');
                code.textContent = token.slice(1, -1);
                parent.append(code);
            } else {
                const link = token.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
                const target = link && articles.find(article => link[2] === article.id + '.md');
                if (target) parent.append(button(link[1], () => openArticle(target), 'sf-help-article-link', false));
                else parent.append(document.createTextNode(link ? link[1] : token));
            }
        }
        const translated = translation(text);
        if (translated && translated !== plain(text)) {
            const span = document.createElement('span');
            span.className = 'sf-help-translation';
            const wording = document.createElement('bdi');
            wording.textContent = translated;
            span.append(document.createTextNode(' ('), wording, document.createTextNode(')'));
            parent.append(span);
        }
    }

    function render(markdown, destination = content) {
        // Article and AI passage rendering share the same safe text renderer.
        renderInto(destination, markdown);
    }
    function renderInto(content, markdown) {
        content.replaceChildren();
        const lines = markdown.replaceAll('\r', '').split('\n');
        let list = null;
        for (let index = 0; index < lines.length; index++) {
            const line = lines[index].trim();
            if (!line || line.startsWith('# ')) { list = null; continue; }
            if (line.startsWith('|')) {
                const wrapper = document.createElement('div');
                wrapper.className = 'sf-help-table';
                const table = document.createElement('table');
                let header = true;
                while (index < lines.length && lines[index].trim().startsWith('|')) {
                    const cells = lines[index].trim().split('|').slice(1, -1).map(cell => cell.trim());
                    if (!cells.every(cell => /^:?-+:?$/.test(cell))) {
                        const row = document.createElement('tr');
                        for (const cell of cells) {
                            const element = document.createElement(header ? 'th' : 'td');
                            if (header) element.scope = 'col';
                            inline(element, cell);
                            row.append(element);
                        }
                        table.append(row);
                        header = false;
                    }
                    index++;
                }
                index--;
                wrapper.append(table);
                content.append(wrapper);
                list = null;
                continue;
            }
            const step = line.match(/^\d+\. (.*)$/);
            if (step) {
                if (!list) { list = document.createElement('ol'); content.append(list); }
                const item = document.createElement('li');
                inline(item, step[1]);
                list.append(item);
            } else {
                list = null;
                const heading = line.match(/^##+ (.*)$/);
                const element = document.createElement(heading ? 'h4' : 'p');
                inline(element, heading ? heading[1] : line);
                content.append(element);
            }
        }
    }

    function openArticle(article, focus = true) {
        document.getElementById('help-library').hidden = false;
        document.getElementById('help-library').open = true;
        selectedArticle = article;
        articleSection.hidden = false;
        results.hidden = true;
        questions.hidden = true;
        document.getElementById('help-count').hidden = true;
        const title = document.getElementById('help-article-title');
        title.textContent = bilingual(article.title);
        render(article.content);
        const links = document.getElementById('help-links');
        links.replaceChildren();
        // These links have already been filtered by the server's normal permissions.
        for (const link of article.links) {
            const anchor = document.createElement('a');
            anchor.className = 'btn btn-primary btn-sm';
            anchor.href = link.url;
            anchor.textContent = bilingual(link.label);
            links.append(anchor);
        }
        if (focus) title.focus();
    }

    function showResults() {
        selectedArticle = null;
        articleSection.hidden = true;
        results.hidden = false;
        questions.hidden = search.value.trim().length > 0;
        const count = document.getElementById('help-count');
        count.hidden = false;
        const words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        const matching = articles.filter(article => {
            const translatedLines = article.content.split(/\r?\n/).flatMap(line => {
                if (line.trim().startsWith('|')) return line.split('|').map(translation);
                return [translation(line.trim().replace(/^#+\s+|^\d+\.\s+/, ''))];
            });
            const text = (article.title + ' ' + article.question + ' ' + article.content + ' ' +
                translation(article.title) + ' ' + translation(article.question) + ' ' + translatedLines.join(' ')).toLowerCase();
            return words.every(word => text.includes(word));
        });
        results.replaceChildren(...matching.map(article => button(article.title, () => openArticle(article))));
        const countCaption = translation('{count} help articles');
        count.textContent = matching.length ? matching.length + ' help articles' +
            (countCaption ? ' (' + countCaption.replaceAll('{count}', matching.length) + ')' : '') :
            bilingual('No matching articles. Try another word or ask your administrator.');
    }

    const suggestions = [...related, articles.find(article => article.id === 'common-problems')];
    if (!related.length) suggestions.unshift(articles.find(article => article.id === 'billing'), articles.find(article => article.id === 'receiving-stock'));
    const fixedCaptions = new Map();
    for (const id of ['help-back', 'help-ai-support', 'help-ai-support-note', 'help-stock-check', 'help-browse']) {
        const element = document.getElementById(id);
        if (element) fixedCaptions.set(element, element.textContent);
    }
    const searchLabel = panel.querySelector('label[for="help-search"]');
    fixedCaptions.set(searchLabel, searchLabel.textContent);
    const accessNote = articleSection.querySelector('p.small');
    fixedCaptions.set(accessNote, accessNote.textContent);
    function refreshHelp() {
        // Session expiry removes the business screen while translations may still finish.
        if (!panel.isConnected) return;
        for (const [element, english] of fixedCaptions) element.textContent = bilingual(english);
        search.placeholder = bilingual('Try barcode, payment or warranty');
        document.getElementById('help-close').setAttribute('aria-label', bilingual('Close help'));
        questions.replaceChildren(...suggestions.map(article => button(article.question, () => openArticle(article))));
        const primary = related[0];
        if (workflowQuestions) {
            document.getElementById('help-suggested').replaceChildren(...workflowQuestions.map(question =>
                button(question, () => askSuggested(question), 'sf-help-suggestion')));
        } else if (primary && screenQuestions[primary.id]) {
            document.getElementById('help-suggested').replaceChildren(
                button(primary.question, () => answerSuggested(primary), 'sf-help-suggestion'),
                ...screenQuestions[primary.id].map(question => button(question, () => askSuggested(question), 'sf-help-suggestion')));
        } else {
            const starters = [...new Set([...related, ...articles])].slice(0, 3);
            document.getElementById('help-suggested').replaceChildren(...starters.map(article =>
                button(article.question, () => answerSuggested(article), 'sf-help-suggestion')));
        }
        document.getElementById('help-context').textContent = bilingual(related.length ? 'Questions for this screen' : 'Common shop questions');
        const scroll = panel.scrollTop;
        if (selectedArticle) openArticle(selectedArticle, false);
        else showResults();
        panel.scrollTop = scroll;
        if (aiAnswer) showAiAnswer();
        if (stockAnswer) showStockAnswer();
    }

    function showAiAnswer() {
        const destination = document.getElementById('help-ai-answer');
        destination.hidden = false;
        destination.replaceChildren();
        const message = document.createElement('p');
        message.textContent = bilingual(aiAnswer.message);
        message.style.whiteSpace = "pre-line";
        destination.append(message);
        for (const section of aiAnswer.sections || []) {
            const heading = document.createElement('h4');
            heading.textContent = bilingual(section.title);
            const body = document.createElement('div');
            render(section.content, body);
            const article = articles.find(item => item.id === section.articleId);
            destination.append(heading, body);
            if (article) destination.append(button('Source: ' + bilingual(article.title), () => openArticle(article), 'sf-assistant-source', false));
        }
        // The only clickable sources are local guide buttons and server-approved screen links.
        for (const link of aiAnswer.links || []) {
            if (!articles.some(article => article.links.some(item => item.url === link.url.split('?')[0]))) continue;
            const anchor = document.createElement('a');
            anchor.href = link.url;
            anchor.className = 'btn btn-outline-primary btn-sm';
            anchor.textContent = bilingual(link.label);
            destination.append(anchor);
        }
        const tools = document.getElementById('help-answer-tools');
        tools.replaceChildren();
        tools.hidden = true;
        if (retryAllowed && lastQuestion) {
            const retry = button('Retry', () => askSuggested(lastQuestion), 'sf-help-retry');
            retry.disabled = true;
            setTimeout(() => {retry.disabled = false;}, 3000);
            tools.append(retry);
            tools.hidden = false;
        }
        if (aiAnswer.answerId) {
            tools.hidden = false;
            const feedback = document.createElement('span');
            tools.append(feedback);
            if (feedbackId === aiAnswer.answerId) feedback.textContent = bilingual('Thank you for your feedback.');
            else for (const helpful of [true, false]) feedback.append(button(helpful ? 'Helpful' : 'Not helpful', async () => {
                const id = aiAnswer.answerId;
                feedback.querySelectorAll('button').forEach(item => item.disabled = true);
                try {
                    const response = await fetch('/api/help/questions/feedback', {
                        method:'POST', headers:{'Content-Type':'application/json','X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]').content},
                        body:JSON.stringify({answerId:id,helpful})
                    });
                    if (!response.ok) throw new Error('Feedback could not be recorded.');
                    feedbackId = id;
                    if (aiAnswer.answerId === id) feedback.textContent = bilingual('Thank you for your feedback.');
                } catch {
                    if (aiAnswer.answerId === id) {feedback.textContent = bilingual('Feedback could not be recorded.');}
                }
            }, 'sf-help-feedback'));
        }
    }
    // Suggested questions use approved instructions directly and also work without AI.
    function answerSuggested(article) {
        if (document.getElementById('help-ai-submit').disabled) return;
        const parts = article.content.split(/^## /m);
        const firstSection = parts[1] || parts[0];
        const lines = firstSection.trim().split('\n');
        const title = parts.length > 1 ? lines.shift().trim() : article.title;
        const bubble = document.getElementById('help-user-question');
        bubble.textContent = bilingual(article.question);
        bubble.hidden = false;
        document.getElementById('help-library').hidden = true;
        aiAnswer = {status:'answered', message:'',
            sections:[{articleId:article.id, title, content:lines.join('\n')}], links:article.links};
        retryAllowed = false;
        showAiAnswer();
        document.getElementById('help-ai-answer').scrollIntoView({block:'nearest'});
        document.dispatchEvent(new Event('stockflow:help-answer'));
    }
    document.getElementById('help-ai-support').addEventListener('click', () => {
        document.getElementById('help-ai-support-note').hidden = false;
    });
    document.getElementById('help-ai-form').addEventListener('submit', async event => {
        event.preventDefault();
        const submit = document.getElementById('help-ai-submit');
        if (submit.disabled) return;
        const question = document.getElementById('help-ai-question').value.trim();
        const error = document.getElementById('help-question-error');
        const input = document.getElementById('help-ai-question');
        if (question.length < 3 || question.length > 500) {
            error.textContent = bilingual('Enter a question with 3–500 characters.');
            error.hidden = false;
            input.setAttribute('aria-invalid', 'true');
            input.setAttribute('aria-describedby', 'help-question-error');
            input.focus();
            return;
        }
        error.hidden = true;
        input.removeAttribute('aria-invalid');
        input.removeAttribute('aria-describedby');
        lastQuestion = question;
        retryAllowed = false;
        document.getElementById('help-answer-tools').hidden = true;
        document.dispatchEvent(new Event('stockflow:help-sending'));
        document.getElementById('help-library').hidden = true;
        panel.querySelector('.sf-assistant-shortcuts').hidden = true;
        const stockPanel = panel.querySelector('.sf-help-stock');
        if (stockPanel) stockPanel.hidden = true;
        const questionBubble = document.getElementById('help-user-question');
        questionBubble.textContent = question;
        questionBubble.hidden = false;
        document.getElementById('help-ai-question').value = '';
        // Clear the previous reply so only the answer to this question is shown.
        aiAnswer = null;
        const answer = document.getElementById('help-ai-answer');
        answer.hidden = false;
        answer.textContent = bilingual('Checking your question…');
        answer.setAttribute('aria-busy', 'true');
        answer.scrollIntoView({block:'nearest'});
        submit.disabled = true;
        panel.querySelectorAll('#help-suggested button').forEach(button => button.disabled = true);
        document.getElementById('help-ai-support-note').hidden = true;
        try {
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 120000);
            let response;
            try {
            response = await fetch('/api/help/questions', {
                method: 'POST',
                signal:controller.signal,
                headers: {'Content-Type':'application/json', 'X-CSRF-TOKEN':document.querySelector('meta[name="csrf-token"]').content},
                body: JSON.stringify({question, conversationId})
            });
            } finally {clearTimeout(timeout);}
            const result = await response.json().catch(() => null);
            if (!response.ok) {
                retryAllowed = response.status === 429 || response.status >= 500;
                aiAnswer = {status:'unavailable',message:result?.message || 'The question could not be answered. Please retry.',sections:[],links:[],answerId:result?.answerId};
                if (response.status === 429) aiAnswer.message = result?.message || 'Please wait a moment, then retry.';
            } else {
            if (!result) throw new Error('No response was received. Please retry.');
            aiAnswer = result;
            retryAllowed = result.status === 'unavailable';
            }
        } catch (error) {
            retryAllowed = true;
            aiAnswer = {status:'unavailable', message:error.name === 'AbortError' ? 'The request took too long. Please retry.' : 'The connection failed. Please retry; no total was calculated.', sections:[], links:[]};
        } finally {
            submit.disabled = false;
            panel.querySelectorAll('#help-suggested button').forEach(button => button.disabled = false);
            answer.removeAttribute('aria-busy');
            showAiAnswer();
            document.dispatchEvent(new Event('stockflow:help-answer'));
        }
    });
    document.getElementById('help-ai-question').addEventListener('input', () => {
        document.getElementById('help-question-error').hidden = true;
        document.getElementById('help-ai-question').removeAttribute('aria-invalid');
        document.getElementById('help-ai-question').removeAttribute('aria-describedby');
    });

    function showStockAnswer() {
        const destination = document.getElementById('help-stock-result');
        if (!destination) return;
        destination.hidden = false;
        destination.replaceChildren();
        const note = document.createElement('p');
        if (stockAnswer.error) {
            note.textContent = bilingual(stockAnswer.error);
            destination.append(note);
            return;
        }
        note.textContent = bilingual('Stock on hand') + ': ' + stockAnswer.scopeName +
            ' · Checked ' + new Date(stockAnswer.checkedAt).toLocaleString();
        destination.append(note);
        if (!stockAnswer.items.length) {
            const empty = document.createElement('p');
            empty.textContent = bilingual('No matching stock records in your accessible locations.');
            destination.append(empty);
        }
        for (const item of stockAnswer.items) {
            const row = document.createElement('p');
            const name = document.createElement('strong');
            name.textContent = item.productName + (item.sku ? ' [' + item.sku + ']' : '');
            row.append(name, document.createElement('br'), document.createTextNode(
                bilingual('Available quantity') + ': ' + item.availableQuantity +
                ' · ' + bilingual('Stock on hand') + ': ' + item.currentStock));
            if (item.hasDiscrepancy) row.append(document.createElement('br'), document.createTextNode(
                bilingual('Stock count and available serials differ. Ask your administrator to review before billing.')));
            destination.append(row);
        }
        if (stockAnswer.truncated) {
            const more = document.createElement('p');
            more.textContent = bilingual('Showing the first 50 matches. Refine your search or open Stock on hand.');
            destination.append(more);
        }
        const source = document.createElement('a');
        source.href = '/Inventory';
        source.className = 'btn btn-outline-primary btn-sm';
        source.textContent = bilingual('Stock on hand');
        destination.append(source);
    }
    document.getElementById('help-stock-form')?.addEventListener('submit', async event => {
        event.preventDefault();
        const button = document.getElementById('help-stock-check');
        if (button.disabled) return;
        const term = document.getElementById('help-stock-term').value.trim();
        const low = document.getElementById('help-stock-low').checked;
        if ((!low && term.length < 2) || term.length > 150) {
            stockAnswer = {error:'Enter at least 2 characters of a product name, item code or barcode.'};
            showStockAnswer();
            return;
        }
        button.disabled = true;
        try {
            const query = new URLSearchParams({term, lowStockOnly:low});
            const response = await fetch('/api/help/stock?' + query, {cache:'no-store'});
            const body = await response.json().catch(() => null);
            if (!response.ok) throw new Error(body?.message || 'Stock could not be checked. Your access may have changed. Try again or contact your administrator.');
            stockAnswer = body;
        } catch (error) { stockAnswer = {error:error.message}; }
        finally { button.disabled = false; showStockAnswer(); }
    });
    async function loadHelpLanguage(code) {
        const requestRevision = ++revision;
        status.hidden = true;
        try {
            let nextPack = {};
            if (code !== 'en') {
                if (!packs.has(code)) {
                    packs.set(code, fetch('/languages/help/' + encodeURIComponent(code) + '.json').then(response => {
                        if (!response.ok) throw new Error('Help translation could not be loaded. English instructions remain available.');
                        return response.json();
                    }).catch(error => { packs.delete(code); throw error; }));
                }
                nextPack = await packs.get(code);
            }
            if (requestRevision !== revision) return;
            pack = nextPack;
            refreshHelp();
        } catch (error) {
            if (requestRevision !== revision) return;
            pack = {};
            refreshHelp();
            status.textContent = error.message;
            status.hidden = false;
        }
    }
    document.addEventListener('stockflow:language-ready', event => loadHelpLanguage(event.detail.language));
    refreshHelp();
    opener.addEventListener('click', () => { panel.showModal(); showResults(); document.getElementById('help-ai-question').focus(); });
    document.getElementById('help-browse').addEventListener('click', () => {
        panel.querySelector('.sf-assistant-shortcuts').hidden = false;
        document.getElementById('help-library').hidden = false;
        document.getElementById('help-library').open = true;
        showResults();
        search.focus();
    });
    for (const shortcut of panel.querySelectorAll('[data-help-article]')) {
        shortcut.addEventListener('click', () => {
            const article = articles.find(item => item.id === shortcut.dataset.helpArticle);
            if (article) openArticle(article);
        });
    }
    panel.querySelector('[data-help-stock]')?.addEventListener('click', () => {
        panel.querySelector('.sf-help-stock').hidden = false;
        panel.querySelector('.sf-help-stock').open = true;
        document.getElementById('help-stock-term').focus();
    });
    document.getElementById('help-close').addEventListener('click', () => panel.close());
    panel.addEventListener('close', () => opener.focus());
    search.addEventListener('input', showResults);
    document.getElementById('help-back').addEventListener('click', () => { showResults(); search.focus(); });
})();
