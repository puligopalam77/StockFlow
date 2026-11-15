// Approved instructions and browser-local validation only. Never fills or submits a form.
(() => {
    'use strict';
    const data = document.getElementById('help-guidance-data');
    const panel = document.getElementById('help-panel');
    if (!data || !panel) return;
    let settings = JSON.parse(data.textContent);
    let activeGuide = null;
    let step = 0;
    let highlighted = null;
    let banner = null;
    let lastError = null;
    let focusedForm = null;
    let revision = 0;
    let scheduled = false;
    const result = document.getElementById('help-guidance-result');
    const text = value => window.StockFlowLanguage?.text(value) || value;

    // Use textContent for instructions: help text must never become executable HTML.
    function node(tag, value, className) {
        const element = document.createElement(tag);
        if (value != null) {
            element.dataset.i18nSource = value;
            element.textContent = text(value);
        }
        if (className) element.className = className;
        return element;
    }

    function button(label, action) {
        const element = node('button', label);
        element.type = 'button';
        element.addEventListener('click', action);
        return element;
    }
    const normalize = value => value.replace(/\s*\*\s*$/, '').replace(/\s*\(optional\)\s*/ig,'').trim().toLowerCase();
    function open() {
        if (!panel.open) panel.showModal();
        result.hidden = false;
    }

    function show(title, explanation) {
        open();
        result.replaceChildren(node('h4', title), node('p', explanation));
        result.focus({preventScroll:true});
        result.scrollIntoView({block:'nearest'});
        requestAnimationFrame(() => document.dispatchEvent(new Event('stockflow:guidance-answer')));
    }

    function clearHighlight() {
        highlighted?.classList.remove('sf-stockbot-target');
        highlighted = null;
        banner?.remove();
        banner = null;
    }
    function pointAt(target) {
        if(!target?.isConnected || !target.checkVisibility()) { result.append(node('p','That control is not currently visible. Open the relevant form or tab yourself, then try again.'));return; }
        clearHighlight();panel.close();highlighted=target;target.classList.add('sf-stockbot-target');target.scrollIntoView({block:'center',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});
        banner=node('aside',null,'sf-stockbot-guide-banner');banner.setAttribute('aria-label','StockBot guide');
        banner.append(node('span','Highlighted for you. Make any changes yourself.'),button('Back to StockBot',()=>{clearHighlight();open();}),button('Dismiss',clearHighlight));
        (target.closest('dialog[open]')||document.body).append(banner);
        // Do not click, change a value, invoke a save, or open a payment action.
    }
    function renderStep() {
        if(!activeGuide)return;
        step=Math.max(0,Math.min(step,activeGuide.steps.length-1));const current=activeGuide.steps[step];
        show(activeGuide.title,current.instruction);
        result.prepend(node('small',`Step ${step+1} of ${activeGuide.steps.length}`));
        result.append(node('h5',current.title));
        const controls=node('div',null,'sf-stockbot-step-actions');
        const previous=button('Previous',()=>{step--;renderStep();});previous.disabled=step===0;
        const next=button(step===activeGuide.steps.length-1?'Finish guide':'Next Step',()=>{
            if(step===activeGuide.steps.length-1){clearHighlight();show('Guide finished','You have reviewed the instructions. No record was saved or submitted by StockBot.');activeGuide=null;return;}
            step++;renderStep();
        });
        controls.append(previous,next);
        if(current.target && activeGuide.id===settings.screen?.id)controls.append(button('Show on Screen',()=>pointAt(document.querySelector(current.target))));
        if(activeGuide.route && allowsLink(activeGuide.route)){const link=node('a','Open this screen');link.href=activeGuide.route;controls.append(link);}
        result.append(controls,node('small','Guide controls never complete a transaction. Review and confirm actions yourself.'));
    }
    function startGuide(guide) { if(!guide?.steps?.length)return;activeGuide=guide;step=0;renderStep(); }
    function explainScreen() {
        const guide=settings.screen;
        if(!guide){show('Screen help','No approved walkthrough is available for this screen within your permissions. Use Find a Feature or Browse help articles.');return;}
        show(guide.title,guide.purpose);
        result.append(node('p',settings.example),button('Guide Me Step by Step',()=>startGuide(guide)));
    }
    function explainField(field) {
        if(!(settings.fields||[]).some(current=>current.id===field.id))return;
        show(field.title,field.explanation);result.append(node('p',field.example));
        result.append(button('Learn More',()=>{explainScreen();}));
    }
    function fieldFor(label) {
        const copy=label.cloneNode(true);copy.querySelectorAll('button,input,select,textarea,.sf-caption-help,.sf-field-validation').forEach(e=>e.remove());
        const caption=normalize(window.StockFlowLanguage?.originalText(label)||copy.textContent);
        return (settings.fields||[]).find(field=>field.captions.some(value=>normalize(value)===caption));
    }
    function enhanceFields() {
        for(const label of document.querySelectorAll('.sf-main label')){
            if(label.closest('[data-no-translate],.visually-hidden') || label.dataset.stockbotInfo || !label.checkVisibility())continue;
            const field=fieldFor(label);if(!field)continue;
            if(label.control && label.contains(label.control))continue;
            if(label.parentElement.querySelector('.sf-caption-info'))continue; // Existing short field help remains its presentation owner.
            const info=button('?',()=>explainField(field));info.className='sf-stockbot-field-help';info.setAttribute('aria-label','Explain '+field.title);
            label.dataset.stockbotInfo='true';const group=node('span',null,'sf-stockbot-label');label.before(group);group.append(label,info);
        }
    }
    function showFields() {
        show('Explain a Field','Choose a field from the current screen. Existing information icons also provide short explanations.');
        for(const field of settings.fields||[])result.append(button(field.title,()=>explainField(field)));
        if(!settings.fields?.length)result.append(node('p','No field glossary is registered for this screen yet. Browse the approved help articles.'));
    }
    function visibleControl(field) {
        if(field.checkVisibility())return field;
        if(field.sfSelect)return document.getElementById(field.id+'-search');
        return null;
    }
    function checkForm(form) {
        show('Check This Form','This check stays in your browser. No field values, passwords or customer details are sent to StockBot.');
        const fields=[...form.querySelectorAll('input,select,textarea')].filter(e=>!e.disabled&&!e.readOnly&&e.willValidate&&visibleControl(e)?.checkVisibility());
        const issues=fields.map(field=>({field,message:window.StockFlowValidation?.fieldMessage(field)||(field.validity.valid?'':'Check this field\'s required value or format.')})).filter(e=>e.message);
        if(!issues.length){result.append(node('p','No browser-detectable field issues were found. Stock availability, business rules, permissions and server validation are still checked when you save.'));return;}
        const list=node('ul');
        for(const issue of issues.slice(0,12)){const item=node('li');item.append(node('span',issue.message),button('Show field',()=>pointAt(visibleControl(issue.field))));list.append(item);}
        result.append(list);if(issues.length>12)result.append(node('p','Showing the first 12 issues. Recheck after correcting them.'));
    }
    function chooseForm() {
        const forms=[...document.querySelectorAll('.sf-main form,dialog[open]:not(#help-panel) form')].filter(form=>form.checkVisibility()&&[...form.elements].some(e=>e.matches?.('input,select,textarea')&&!e.disabled&&e.willValidate&&visibleControl(e)?.checkVisibility()));
        if(focusedForm && forms.includes(focusedForm)){checkForm(focusedForm);return;}
        if(forms.length===1){checkForm(forms[0]);return;}
        show('Check This Form',forms.length?'Choose the form you want to check.':'No editable form is currently visible. Open the form first.');
        for(const [index,form]of forms.entries())result.append(button(form.getAttribute('aria-label')||form.querySelector('legend,h2,h3')?.textContent?.trim()||`Form ${index+1}`,()=>checkForm(form)));
    }
    function allowsLink(url) { return (settings.features||[]).some(link=>link.url===url); }
    function features() {
        const term=normalize(document.getElementById('help-feature-search').value);
        const terms=term.replace(/employee|user/g,'staff').replace(/generate|print/g,'').trim().split(/\s+/).filter(Boolean);
        const target=document.getElementById('help-feature-links');target.replaceChildren();
        for(const feature of settings.features||[]){if(!terms.every(word=>(feature.label+' '+feature.url).toLowerCase().includes(word)))continue;
            const link=node('a',feature.label);link.href=feature.url;target.append(link);}
        if(!target.children.length)target.append(node('p','No permitted feature matches. Try a different word or ask your Shop Admin.'));
    }
    function intro() {
        const destination=document.getElementById('help-onboarding');
        let skipped=false;try{skipped=sessionStorage.getItem('stockbot.intro.'+settings.introKey)==='skip';}catch{}
        destination.hidden=skipped;destination.replaceChildren();
        if(!skipped)destination.append(node('p','New to StockFlow? Choose a permitted walkthrough below. You can skip this introduction and open it again later.'),button('Skip introduction',()=>{try{sessionStorage.setItem('stockbot.intro.'+settings.introKey,'skip');}catch{}destination.hidden=true;}));
    }
    function renderMenu() {
        const destination=document.getElementById('help-workflows');destination.replaceChildren();
        for(const guide of settings.workflows||[])destination.append(button(guide.title,()=>startGuide(guide)));
        intro();features();enhanceFields();
    }
    async function refresh() {
        // Only the newest refresh may replace the guide menu. Permission checks
        // happen on the server; these arrays are just the resulting presentation.
        const request = ++revision;
        try {
            const screenId = document.body.dataset.stockbotScreen || 'general';
            const response = await fetch('/api/help/assistant/guidance?screen=' + encodeURIComponent(screenId), {cache:'no-store'});
            if (!response.ok) throw new Error('Guidance access could not be refreshed.');
            const next = await response.json();
            if (request !== revision) return;
            if (!next || !Array.isArray(next.features) || !Array.isArray(next.fields) || !Array.isArray(next.workflows))
                throw new Error('STOCKBOT_GUIDANCE_INCOMPLETE');
            const previousGuides = JSON.stringify([settings.screen, settings.fields, settings.features, settings.workflows]);
            const nextGuides = JSON.stringify([next.screen, next.fields, next.features, next.workflows]);
            settings = next;
            if (previousGuides !== nextGuides) {
                activeGuide = null;
                result.hidden = true;
                clearHighlight();
            }
            renderMenu();
        } catch {
            if (request !== revision) return;
            settings = {fields:[], features:[], workflows:[], errors:[]};
            activeGuide = null;
            clearHighlight();
            result.hidden = true;
            renderMenu();
        }
    }
    document.getElementById('help-explain-screen').addEventListener('click',explainScreen);
    document.getElementById('help-start-guide').addEventListener('click',()=>settings.screen?startGuide(settings.screen):explainScreen());
    document.getElementById('help-explain-fields').addEventListener('click',showFields);
    document.getElementById('help-check-form').addEventListener('click',chooseForm);
    document.getElementById('help-feature-search').addEventListener('input',features);
    document.getElementById('help-onboarding-reset').addEventListener('click',()=>{try{sessionStorage.removeItem('stockbot.intro.'+settings.introKey);}catch{}intro();});
    document.getElementById('help-explain-error').addEventListener('click',()=>{const advice=(settings.errors||[]).find(e=>e.code===lastError);if(advice)show(advice.title,advice.explanation);});
    document.addEventListener('focusin',event=>{if(!panel.contains(event.target)&&event.target.form)focusedForm=event.target.form;});
    document.addEventListener('keydown',event=>{if(event.key==='Escape')clearHighlight();});
    document.addEventListener('stockflow:guide-received',event=>{startGuide(event.detail);});
    document.addEventListener('stockflow:help-sending',()=>{result.hidden=true;clearHighlight();});
    document.addEventListener('stockflow:assistant-context',event=>{
        if(!event.detail.changed)return;
        lastError=null;focusedForm=null;activeGuide=null;result.hidden=true;clearHighlight();
        document.getElementById('help-explain-error').hidden=true;
        if(event.detail.unavailable){
            revision++; // Ignore any guidance request started before the access failure.
            settings={fields:[],features:[],workflows:[],errors:[]};
            renderMenu();
        }else refresh();
    });
    document.getElementById('help-open').addEventListener('click',refresh);
    new MutationObserver(()=>{if(!scheduled){scheduled=true;requestAnimationFrame(()=>{scheduled=false;enhanceFields();});}}).observe(document.querySelector('.sf-main')||document.body,{childList:true,subtree:true});
    new MutationObserver(()=>{refresh();}).observe(document.body,{attributes:true,attributeFilter:['data-stockbot-screen']});
    const fetchBeforeGuidance=window.fetch.bind(window);
    window.fetch=async(input,options={})=>{
        const url=new URL(typeof input==='string'||input instanceof URL?input:input.url,location.href);
        const monitored=url.origin===location.origin&&url.pathname.startsWith('/api/')&&!url.pathname.startsWith('/api/help/');
        const record=code=>{if(monitored&&(settings.errors||[]).some(e=>e.code===code)){lastError=code;document.getElementById('help-explain-error').hidden=false;}};
        try{const response=await fetchBeforeGuidance(input,options);if(!response.ok)record(response.headers.get('X-StockFlow-Error-Code'));return response;}
        catch(error){if(error.name!=='AbortError')record('SERVICE_UNAVAILABLE');throw error;}
    };
    for(const element of panel.querySelectorAll('.sf-stockbot-guide-tools button,.sf-stockbot-guide-tools summary,.sf-stockbot-guide-tools label,#help-explain-error'))element.dataset.i18nSource=element.textContent;
    document.addEventListener('stockflow:language-ready',()=>{window.StockFlowLanguage.refresh();});
    window.StockFlowGuide={allowsLink,fieldFor};renderMenu();
})();
